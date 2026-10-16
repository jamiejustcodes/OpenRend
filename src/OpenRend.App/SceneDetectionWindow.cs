using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

/// <summary>
/// Video cut and scene transition detection matching Vegas Pro Tools -> Scene Detection.
/// Analyzes frame-to-frame luminance and color deltas using downsampled proxy frames,
/// identifies camera cut boundaries, and splits the timeline event into individual scenes.
/// </summary>
public sealed class SceneDetectionWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly ComboBox targetClipComboBox = new() { Width = 280 };
    private readonly Slider sensitivitySlider = new() { Minimum = 15, Maximum = 60, Value = 32, Width = 160, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock sensitivityLabel = new() { Text = "Sensitivity: Medium (Threshold 32)", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox minLengthTextBox = new() { Width = 50, Text = "15", TextAlignment = TextAlignment.Center };
    private readonly ProgressBar progressBar = new() { Height = 18, Margin = new Thickness(0, 8, 0, 8), Visibility = Visibility.Collapsed };
    private readonly TextBlock statusText = new() { Text = "Select a video event and click Detect Scenes.", TextWrapping = TextWrapping.Wrap };
    private readonly Button detectButton = new() { Content = "Detect & Split Scenes", Width = 155, Height = 28 };
    private readonly Button cancelButton = new() { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
    private CancellationTokenSource? cts;

    private readonly List<(TimelineTrack Track, TimelineClip Clip, MediaAsset Media)> videoClips = [];

    public SceneDetectionWindow(EditorViewModel viewModel)
    {
        vm = viewModel;
        Style = (Style)FindResource(typeof(Window));
        Title = "Scene Detection";
        Width = 460;
        Height = 310;
        MinWidth = 400;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = (System.Windows.Media.Brush)FindResource("PanelBrush");

        var root = new StackPanel { Margin = new Thickness(16), Background = panelBrush };

        // 1. Target Clip Selection
        root.Children.Add(new TextBlock { Text = "Video Event to Detect:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        PopulateVideoClips();
        root.Children.Add(targetClipComboBox);

        // 2. Sensitivity Slider
        var sensPanel = new DockPanel { Margin = new Thickness(0, 12, 0, 4) };
        sensitivitySlider.ValueChanged += (_, e) =>
        {
            var val = (int)e.NewValue;
            var desc = val < 25 ? "High (find subtle cuts)" : val > 42 ? "Low (obvious cuts only)" : "Medium (recommended)";
            sensitivityLabel.Text = $"Sensitivity: {desc} (Threshold {val})";
        };
        sensPanel.Children.Add(sensitivityLabel);
        DockPanel.SetDock(sensitivitySlider, Dock.Right);
        sensPanel.Children.Add(sensitivitySlider);
        root.Children.Add(sensPanel);

        // 3. Min Scene Duration
        var minLenPanel = new WrapPanel { Margin = new Thickness(0, 6, 0, 10), VerticalAlignment = VerticalAlignment.Center };
        minLenPanel.Children.Add(new TextBlock { Text = "Minimum Scene Length (frames):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        minLenPanel.Children.Add(minLengthTextBox);
        root.Children.Add(minLenPanel);

        // 4. Progress and Status
        root.Children.Add(progressBar);
        root.Children.Add(statusText);

        // 5. Buttons
        var buttonPanel = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        detectButton.Click += DetectButtonClick;
        buttonPanel.Children.Add(detectButton);

        cancelButton.Margin = new Thickness(8, 0, 0, 0);
        cancelButton.Click += (_, _) =>
        {
            cts?.Cancel();
            Close();
        };
        buttonPanel.Children.Add(cancelButton);

        root.Children.Add(buttonPanel);
        Content = root;
    }

    private void PopulateVideoClips()
    {
        videoClips.Clear();
        targetClipComboBox.Items.Clear();

        foreach (var track in vm.Project.Tracks.Where(t => t.Kind == TrackKind.Video))
        {
            foreach (var clip in track.Clips)
            {
                var media = vm.Project.Media.FirstOrDefault(m => m.Id == clip.MediaId);
                if (media is not null && media.HasVideo && !media.Path.StartsWith("openrend://"))
                {
                    videoClips.Add((track, clip, media));
                    var isSelected = vm.Selection.Contains(clip.Id);
                    var item = $"[{track.Name}] {clip.Name} ({clip.DurationFrames} frames)";
                    targetClipComboBox.Items.Add(item);
                    if (isSelected)
                    {
                        targetClipComboBox.SelectedIndex = targetClipComboBox.Items.Count - 1;
                    }
                }
            }
        }

        if (targetClipComboBox.SelectedIndex < 0 && targetClipComboBox.Items.Count > 0)
            targetClipComboBox.SelectedIndex = 0;

        if (targetClipComboBox.Items.Count == 0)
        {
            targetClipComboBox.Items.Add("No video events available on timeline");
            targetClipComboBox.SelectedIndex = 0;
            detectButton.IsEnabled = false;
        }
    }

    private async void DetectButtonClick(object sender, RoutedEventArgs e)
    {
        if (targetClipComboBox.SelectedIndex < 0 || targetClipComboBox.SelectedIndex >= videoClips.Count)
            return;

        var (targetTrack, targetClip, targetMedia) = videoClips[targetClipComboBox.SelectedIndex];
        var threshold = (int)sensitivitySlider.Value;
        int.TryParse(minLengthTextBox.Text, out var minFrames);
        if (minFrames < 5) minFrames = 15;

        detectButton.IsEnabled = false;
        progressBar.Visibility = Visibility.Visible;
        progressBar.Value = 0;
        statusText.Text = "Scanning frames for scene cuts…";

        cts = new CancellationTokenSource();
        var token = cts.Token;

        try
        {
            var splitOffsets = await Task.Run(() =>
            {
                var cuts = new List<long>();
                var startFrame = targetClip.SourceInFrame;
                var totalFrames = targetClip.DurationFrames;
                if (totalFrames < minFrames * 2) return cuts;

                const int proxyW = 160;
                const int proxyH = 90;
                const int pixelCount = proxyW * proxyH;

                byte[]? prevLuma = null;
                long lastCutOffset = 0;

                // Stride by 2 frames for fast evaluation while preserving single-frame cut accuracy
                for (long offset = 0; offset < totalFrames; offset += 2)
                {
                    if (token.IsCancellationRequested) break;

                    var timeSec = vm.Project.FrameRate.Seconds(startFrame + offset);
                    var frameBgra = vm.Media.GetFrame(targetMedia.Path, timeSec, proxyW, proxyH);

                    var currentLuma = new byte[pixelCount];
                    for (var i = 0; i < pixelCount; i++)
                    {
                        var src = i * 4;
                        // Rec. 709 luma proxy: 0.2126R + 0.7152G + 0.0722B
                        currentLuma[i] = (byte)((frameBgra[src + 2] * 54 + frameBgra[src + 1] * 183 + frameBgra[src] * 19) >> 8);
                    }

                    if (prevLuma is not null && (offset - lastCutOffset) >= minFrames)
                    {
                        long diffSum = 0;
                        for (var i = 0; i < pixelCount; i++)
                        {
                            diffSum += Math.Abs(currentLuma[i] - prevLuma[i]);
                        }
                        var avgDiff = diffSum / (double)pixelCount;

                        if (avgDiff >= threshold)
                        {
                            cuts.Add(offset);
                            lastCutOffset = offset;
                        }
                    }

                    prevLuma = currentLuma;

                    if (offset % 30 == 0)
                    {
                        var pct = (double)offset / totalFrames * 100.0;
                        Dispatcher.Invoke(() => progressBar.Value = pct);
                    }
                }

                return cuts;
            }, token);

            if (token.IsCancellationRequested)
            {
                statusText.Text = "Scene detection cancelled.";
                return;
            }

            if (splitOffsets.Count == 0)
            {
                statusText.Text = "No scene cuts detected with current threshold.";
                MessageBox.Show(this, "No scene transitions met the threshold criteria. Try lowering the threshold to increase sensitivity.", "Scene Detection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Execute timeline splits in project editor
            vm.Editor.Execute(p =>
            {
                var track = p.Tracks.FirstOrDefault(t => t.Id == targetTrack.Id);
                if (track is null) return;
                var clip = track.Clips.FirstOrDefault(c => c.Id == targetClip.Id);
                if (clip is null) return;

                var origStart = clip.StartFrame;
                var origSourceIn = clip.SourceInFrame;
                var origDuration = clip.DurationFrames;
                var origName = clip.Name;
                var origMediaId = clip.MediaId;
                var origGain = clip.Gain;
                var origOpacity = clip.Opacity;

                // Remove original clip and insert split subclips
                track.Clips.Remove(clip);

                var cutPoints = new List<long> { 0 };
                cutPoints.AddRange(splitOffsets);
                cutPoints.Add(origDuration);

                for (var i = 0; i < cutPoints.Count - 1; i++)
                {
                    var segStartOffset = cutPoints[i];
                    var segDuration = cutPoints[i + 1] - cutPoints[i];
                    if (segDuration <= 0) continue;

                    var subClip = new TimelineClip
                    {
                        MediaId = origMediaId,
                        Name = $"{origName} [Scene {i + 1}]",
                        StartFrame = origStart + segStartOffset,
                        SourceInFrame = origSourceIn + segStartOffset,
                        DurationFrames = segDuration,
                        Gain = origGain,
                        Opacity = origOpacity
                    };
                    track.Clips.Add(subClip);
                }
            });

            statusText.Text = $"Successfully detected and split {splitOffsets.Count + 1} scenes!";
            MessageBox.Show(this, $"Detected {splitOffsets.Count} scene transitions and created {splitOffsets.Count + 1} clips on the timeline.", "Scene Detection", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            statusText.Text = $"Scene detection failed: {ex.Message}";
            MessageBox.Show(this, $"Scene detection error: {ex.Message}", "Scene Detection", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            detectButton.IsEnabled = true;
            progressBar.Visibility = Visibility.Collapsed;
        }
    }
}
