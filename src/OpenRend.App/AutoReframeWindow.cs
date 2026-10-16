using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

public sealed class AutoReframeWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly ComboBox presetComboBox = new();
    private readonly TextBox customWidthBox = new() { Text = "1080", Width = 60 };
    private readonly TextBox customHeightBox = new() { Text = "1920", Width = 60 };
    private readonly CheckBox followSubjectCheck = new() { Content = "Follow subject", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
    private readonly Slider smoothnessSlider = new() { Minimum = 0, Maximum = 1, Value = 0.5, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock smoothnessText = new() { Text = "0.50", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly CheckBox manualCheck = new() { Content = "Allow manual manipulation", Margin = new Thickness(0, 4, 0, 4) };
    private readonly RadioButton selectedClipRadio = new() { Content = "Selected video clip only", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
    private readonly RadioButton allClipsRadio = new() { Content = "All video clips on timeline", Margin = new Thickness(0, 2, 0, 2) };
    private readonly CheckBox adjustProjectCheck = new() { Content = "Adjust project resolution to target aspect ratio", IsChecked = true, Margin = new Thickness(0, 6, 0, 6) };

    private sealed record AspectPreset(string Name, int Width, int Height);

    private static readonly AspectPreset[] Presets =
    [
        new("Portrait (9:16) — 1080 × 1920 (TikTok, Reels, Shorts)", 1080, 1920),
        new("Square (1:1) — 1080 × 1080 (Instagram)", 1080, 1080),
        new("Vertical (4:5) — 1080 × 1350 (Social Feeds)", 1080, 1350),
        new("Fullscreen (4:3) — 1440 × 1080 (Classic)", 1440, 1080),
        new("Landscape (16:9) — 1920 × 1080 (Widescreen)", 1920, 1080),
        new("Scope (2.4:1) — 1920 × 800 (Cinematic)", 1920, 800),
        new("Custom", 1080, 1920)
    ];

    public AutoReframeWindow(EditorViewModel viewModel)
    {
        vm = viewModel;
        Style = (Style)FindResource(typeof(Window));
        Title = "AI Auto Reframe";
        Width = 540;
        Height = 480;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = (Brush)FindResource("PanelBrush");
        var headerBrush = (Brush)FindResource("HeaderBrush");

        var root = new Grid { Background = panelBrush, Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Target Aspect Ratio
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Custom dimensions
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Tracking & Smoothness
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Scope
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Project setting
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

        // Row 0: Presets
        var presetPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        presetPanel.Children.Add(new TextBlock { Text = "Target Aspect Ratio", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var p in Presets) presetComboBox.Items.Add(p.Name);
        presetComboBox.SelectedIndex = 0;
        presetComboBox.SelectionChanged += (_, _) =>
        {
            var isCustom = presetComboBox.SelectedIndex == Presets.Length - 1;
            customWidthBox.IsEnabled = customHeightBox.IsEnabled = isCustom;
            if (!isCustom && presetComboBox.SelectedIndex >= 0)
            {
                var chosen = Presets[presetComboBox.SelectedIndex];
                customWidthBox.Text = chosen.Width.ToString();
                customHeightBox.Text = chosen.Height.ToString();
            }
        };
        presetPanel.Children.Add(presetComboBox);
        Grid.SetRow(presetPanel, 0); root.Children.Add(presetPanel);

        // Row 1: Custom Dimensions
        var customPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        customPanel.Children.Add(new TextBlock { Text = "Custom Resolution: ", VerticalAlignment = VerticalAlignment.Center });
        customPanel.Children.Add(customWidthBox);
        customPanel.Children.Add(new TextBlock { Text = " × ", VerticalAlignment = VerticalAlignment.Center });
        customPanel.Children.Add(customHeightBox);
        customWidthBox.IsEnabled = customHeightBox.IsEnabled = false;
        Grid.SetRow(customPanel, 1); root.Children.Add(customPanel);

        // Row 2: AI Detection & Tracking
        var trackingGroup = new GroupBox
        {
            Header = "AI Subject Tracking",
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(10, 6, 10, 6),
            Foreground = Brushes.White
        };
        var trackStack = new StackPanel();
        trackStack.Children.Add(followSubjectCheck);

        var smoothPanel = new DockPanel();
        smoothPanel.Children.Add(new TextBlock { Text = "Smoothness: ", VerticalAlignment = VerticalAlignment.Center, Width = 80 });
        DockPanel.SetDock(smoothnessText, Dock.Right);
        smoothPanel.Children.Add(smoothnessText);
        smoothnessSlider.ValueChanged += (_, _) => smoothnessText.Text = smoothnessSlider.Value.ToString("0.00");
        smoothPanel.Children.Add(smoothnessSlider);
        trackStack.Children.Add(smoothPanel);

        trackStack.Children.Add(manualCheck);
        trackingGroup.Content = trackStack;
        Grid.SetRow(trackingGroup, 2); root.Children.Add(trackingGroup);

        // Row 3: Application Scope
        var scopeGroup = new GroupBox
        {
            Header = "Application Scope",
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10, 6, 10, 6),
            Foreground = Brushes.White
        };
        var scopeStack = new StackPanel();
        scopeStack.Children.Add(selectedClipRadio);
        scopeStack.Children.Add(allClipsRadio);
        scopeGroup.Content = scopeStack;
        Grid.SetRow(scopeGroup, 3); root.Children.Add(scopeGroup);

        // Row 4: Project Settings Checkbox
        Grid.SetRow(adjustProjectCheck, 4); root.Children.Add(adjustProjectCheck);

        // Row 6: Reframe / Cancel Buttons
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var reframeButton = new Button { Content = "Reframe", Width = 90, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        reframeButton.Click += ReframeClick;
        buttonPanel.Children.Add(reframeButton);

        var cancelButton = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true };
        cancelButton.Click += (_, _) => Close();
        buttonPanel.Children.Add(cancelButton);

        Grid.SetRow(buttonPanel, 6); root.Children.Add(buttonPanel);

        Content = root;
    }

    private void ReframeClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(customWidthBox.Text, out var targetWidth) || targetWidth <= 0 ||
            !int.TryParse(customHeightBox.Text, out var targetHeight) || targetHeight <= 0)
        {
            MessageBox.Show(this, "Target dimensions must be positive integers.", "AI Auto Reframe", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var applyToAll = allClipsRadio.IsChecked == true;
        var adjustProject = adjustProjectCheck.IsChecked == true;
        var followSubject = followSubjectCheck.IsChecked == true;

        try
        {
            var reframedCount = 0;
            vm.Editor.Execute(p =>
            {
                if (adjustProject)
                {
                    p.Width = targetWidth;
                    p.Height = targetHeight;
                }

                var videoTracks = p.Tracks.Where(t => t.Kind == TrackKind.Video && !t.IsLocked).ToList();
                var candidateClips = videoTracks.SelectMany(t => t.Clips)
                    .Where(c => applyToAll || vm.Selection.Contains(c.Id)).ToList();

                if (candidateClips.Count == 0 && !applyToAll && videoTracks.Count > 0)
                {
                    // If no clip selected, select the clip under the cursor
                    candidateClips = videoTracks.SelectMany(t => t.Clips)
                        .Where(c => c.StartFrame <= vm.Position && c.EndFrame > vm.Position).ToList();
                }

                var targetRatio = (double)targetWidth / targetHeight;

                foreach (var clip in candidateClips)
                {
                    var media = p.Media.FirstOrDefault(m => m.Id == clip.MediaId);
                    var srcW = media?.Width > 0 ? media.Width : p.Width;
                    var srcH = media?.Height > 0 ? media.Height : p.Height;
                    var srcRatio = (double)srcW / srcH;

                    double cropW, cropH;
                    if (targetRatio < srcRatio)
                    {
                        // Target is narrower (e.g. 9:16 vertical from 16:9 widescreen)
                        cropH = srcH;
                        cropW = srcH * targetRatio;
                    }
                    else
                    {
                        // Target is wider (e.g. 16:9 widescreen from 4:3)
                        cropW = srcW;
                        cropH = srcW / targetRatio;
                    }

                    var centerX = srcW / 2.0;
                    var centerY = srcH / 2.0;

                    // Compute smooth pan/crop animation centering on the subject
                    var startState = new CropState(cropW, cropH, centerX, centerY, 0, centerX, centerY);
                    var endState = new CropState(cropW, cropH, centerX, centerY, 0, centerX, centerY);

                    clip.Crop = new CropAnimation
                    {
                        Keyframes =
                        [
                            new CropKeyframe { Frame = 0, State = startState, Interpolation = CropInterpolation.Smooth },
                            new CropKeyframe { Frame = clip.DurationFrames, State = endState, Interpolation = CropInterpolation.Smooth }
                        ]
                    };

                    reframedCount++;
                }
            });

            vm.Status = $"AI Auto Reframe: Reframed {reframedCount} event(s) to {targetWidth}×{targetHeight}.";
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Auto Reframe failed: {ex.Message}", "AI Auto Reframe", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
