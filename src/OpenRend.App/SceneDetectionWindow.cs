using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

/// <summary>
/// Video cut and scene transition detection matching 1:1 Vegas Pro Tools -> Scene Detection (SceneDetectionCmd).
/// Analyzes frame-to-frame luminance and chromatic deltas, identifies camera cut boundaries,
/// and splits the timeline event into individual scenes, creates timeline regions, or generates media pool subclips.
/// </summary>
public sealed class SceneDetectionWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly ComboBox targetClipComboBox = new() { Width = 380 };
    private readonly ComboBox modeComboBox = new() { Width = 380 };
    private readonly Slider sensitivitySlider = new() { Minimum = 10, Maximum = 95, Value = 65, Width = 180, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly TextBlock sensitivityLabel = new() { Text = "Sensitivity: Medium (65%)", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox minLengthTextBox = new() { Width = 60, Text = "500", TextAlignment = TextAlignment.Center };
    private readonly CheckBox splitGroupedCheckBox = new() { Content = "Split linked and grouped audio tracks in lockstep", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
    private readonly CheckBox hardCutsCheckBox = new() { Content = "Detect Hard Cuts (abrupt camera/scene changes)", IsChecked = true, Margin = new Thickness(0, 2, 0, 4) };
    private readonly ProgressBar progressBar = new() { Height = 18, Margin = new Thickness(0, 8, 0, 8), Visibility = Visibility.Collapsed };
    private readonly TextBlock statusText = new() { Text = "Select a video event and click Detect Scenes.", TextWrapping = TextWrapping.Wrap };
    private readonly Button detectButton = new() { Content = "Detect Scenes", Width = 140, Height = 28 };
    private readonly Button cancelButton = new() { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
    private CancellationTokenSource? cts;

    private readonly List<(TimelineTrack Track, TimelineClip Clip, MediaAsset Media)> videoClips = [];

    public SceneDetectionWindow(EditorViewModel viewModel, TimelineClip? initialClip = null)
    {
        vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Style = (Style)FindResource(typeof(Window));
        Title = "Scene Detection";
        Width = 490;
        Height = 440;
        MinWidth = 420;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = (System.Windows.Media.Brush)FindResource("PanelBrush");

        var root = new StackPanel { Margin = new Thickness(16), Background = panelBrush };

        // 1. Target Clip Selection
        root.Children.Add(new TextBlock { Text = "Video Event to Detect:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        PopulateVideoClips(initialClip?.Id);
        root.Children.Add(targetClipComboBox);

        // 2. Operational Mode Selection (Vegas Pro SceneDetectionCmd.Mode)
        root.Children.Add(new TextBlock { Text = "Action on Detection:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
        modeComboBox.Items.Add("Split on Timeline (cut event into individual scenes)");
        modeComboBox.Items.Add("Add Region Markers to Timeline (ruler regions for each scene)");
        modeComboBox.Items.Add("Create Subclips in Media Pool");
        modeComboBox.SelectedIndex = 0;
        modeComboBox.SelectionChanged += (_, _) =>
        {
            splitGroupedCheckBox.IsEnabled = modeComboBox.SelectedIndex == 0;
            detectButton.Content = modeComboBox.SelectedIndex == 0 ? "Detect & Split Scenes" : "Detect Scenes";
        };
        root.Children.Add(modeComboBox);

        // 3. Sensitivity Slider
        var sensPanel = new DockPanel { Margin = new Thickness(0, 12, 0, 4) };
        sensitivitySlider.ValueChanged += (_, e) =>
        {
            var val = (int)e.NewValue;
            var desc = val >= 75 ? "High (find subtle cuts)" : val <= 40 ? "Low (obvious cuts only)" : "Medium (recommended)";
            sensitivityLabel.Text = $"Sensitivity: {desc} ({val}%)";
        };
        sensPanel.Children.Add(sensitivityLabel);
        DockPanel.SetDock(sensitivitySlider, Dock.Right);
        sensPanel.Children.Add(sensitivitySlider);
        root.Children.Add(sensPanel);

        // 4. Min Scene Duration (debouncing)
        var minLenPanel = new WrapPanel { Margin = new Thickness(0, 6, 0, 6), VerticalAlignment = VerticalAlignment.Center };
        minLenPanel.Children.Add(new TextBlock { Text = "Minimum Scene Length (ms):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        minLenPanel.Children.Add(minLengthTextBox);
        minLenPanel.Children.Add(new TextBlock { Text = " (Vegas Pro default: 500 ms)", VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(6, 0, 0, 0) });
        root.Children.Add(minLenPanel);

        // 5. Checkboxes
        root.Children.Add(hardCutsCheckBox);
        root.Children.Add(splitGroupedCheckBox);

        // 6. Progress and Status
        root.Children.Add(progressBar);
        root.Children.Add(statusText);

        // 7. Buttons
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

    private void PopulateVideoClips(Guid? preferClipId = null)
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
                    var isSelected = preferClipId.HasValue ? clip.Id == preferClipId.Value : vm.Selection.Contains(clip.Id);
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
        var sensitivityNorm = sensitivitySlider.Value / 100.0;
        double.TryParse(minLengthTextBox.Text, out var minMs);
        if (minMs < 50.0) minMs = 500.0;

        var options = new SceneDetectionOptions
        {
            Sensitivity = Math.Clamp(sensitivityNorm, 0.0, 1.0),
            MinSceneLengthMs = minMs,
            DetectHardCuts = hardCutsCheckBox.IsChecked ?? true,
            IncludeGroupedTracks = splitGroupedCheckBox.IsChecked ?? true
        };

        var mode = modeComboBox.SelectedIndex switch
        {
            1 => SceneDetectionMode.AddRegionMarkers,
            2 => SceneDetectionMode.CreateSubclips,
            _ => SceneDetectionMode.SplitOnTimeline
        };

        detectButton.IsEnabled = false;
        progressBar.Visibility = Visibility.Visible;
        progressBar.Value = 0;
        statusText.Text = "Scanning frames for scene cut transitions…";

        cts = new CancellationTokenSource();
        var token = cts.Token;

        try
        {
            var service = new SceneDetectionService(vm.Media);
            var progress = new Progress<double>(pct =>
            {
                Dispatcher.Invoke(() =>
                {
                    progressBar.Value = pct;
                    statusText.Text = $"Scanning video frames ({pct:0}%)…";
                });
            });

            var cuts = await service.DetectScenesAsync(
                targetMedia.Path,
                targetClip.SourceInFrame,
                targetClip.DurationFrames,
                vm.Project.FrameRate,
                options,
                targetClip.StartFrame,
                progress,
                token
            );

            if (token.IsCancellationRequested)
            {
                statusText.Text = "Scene detection cancelled.";
                return;
            }

            if (cuts.Count == 0)
            {
                statusText.Text = "No scene cuts detected with current sensitivity.";
                MessageBox.Show(this, "No scene transitions met the threshold criteria. Try increasing the sensitivity or reducing the minimum scene length.", "Scene Detection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (mode == SceneDetectionMode.CreateSubclips)
            {
                // Create named subclips in Project Media pool matching Vegas Pro doSubclipsFromMediaFX
                var spans = SceneDetectionEngine.BuildSceneSpans(cuts, targetClip.DurationFrames, targetClip.StartFrame, targetMedia.Name);
                var subclipCount = 0;
                foreach (var span in spans)
                {
                    var subclipName = $"{targetMedia.Name}.{span.Index:D4}";
                    var subclipMedia = new MediaAsset
                    {
                        Id = Guid.NewGuid(),
                        Path = targetMedia.Path,
                        DisplayName = subclipName,
                        DurationSeconds = vm.Project.FrameRate.Seconds(span.DurationFrames),
                        Width = targetMedia.Width,
                        Height = targetMedia.Height,
                        FrameRate = targetMedia.FrameRate,
                        HasVideo = targetMedia.HasVideo,
                        HasAudio = targetMedia.HasAudio
                    };
                    vm.Editor.AddMedia(subclipMedia);
                    subclipCount++;
                }

                statusText.Text = $"Successfully created {subclipCount} subclips in Project Media pool!";
                MessageBox.Show(this, $"Created {subclipCount} subclips in Project Media pool.", "Scene Detection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // Execute atomic split or region generation in project editor
                var result = vm.Editor.ApplySceneDetection(
                    targetClip.Id,
                    cuts,
                    mode,
                    options,
                    targetClip.Name
                );

                var actionDesc = mode == SceneDetectionMode.SplitOnTimeline
                    ? $"split into {result.SceneCount} scenes on timeline"
                    : $"created {result.ResultRegions.Count} timeline scene regions";

                statusText.Text = $"Successfully detected {result.CutCount} cuts and {actionDesc}!";
                MessageBox.Show(this, $"Detected {result.CutCount} scene transitions and {actionDesc}.", "Scene Detection", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            statusText.Text = "Scene detection cancelled.";
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
