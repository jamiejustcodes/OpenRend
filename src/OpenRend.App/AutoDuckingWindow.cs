using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OpenRend.Core;
using OpenRend.Timeline;

namespace OpenRend.App;

/// <summary>
/// Vegas Pro 1:1 Auto Ducking Controls window.
/// Allows designating audio tracks as Ducking Controllers (Voiceover/Dialogue) or Listeners (Music/Background),
/// tuning sidechain duck levels, speech thresholds, attack, hold, and release dynamics,
/// and baking frame-accurate analytical volume envelopes onto the timeline.
/// </summary>
public sealed class AutoDuckingWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly Guid trackId;
    private readonly Action? onProjectChanged;

    private readonly ComboBox roleCombo = new() { Width = 260 };
    private readonly ComboBox controllerTrackCombo = new() { Width = 260 };
    private readonly StackPanel listenerSettingsPanel = new();

    private readonly Slider duckLevelSlider = new() { Minimum = -48, Maximum = 0, Value = -12, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 160 };
    private readonly TextBlock duckLevelLabel = new() { Text = "Ducking Level: -12.0 dB", VerticalAlignment = VerticalAlignment.Center };

    private readonly Slider thresholdSlider = new() { Minimum = -60, Maximum = -6, Value = -30, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 160 };
    private readonly TextBlock thresholdLabel = new() { Text = "Speech Threshold: -30.0 dB", VerticalAlignment = VerticalAlignment.Center };

    private readonly Slider attackSlider = new() { Minimum = 10, Maximum = 1000, Value = 200, TickFrequency = 10, IsSnapToTickEnabled = true, Width = 160 };
    private readonly TextBlock attackLabel = new() { Text = "Attack (Fade Down): 200 ms", VerticalAlignment = VerticalAlignment.Center };

    private readonly Slider holdSlider = new() { Minimum = 0, Maximum = 3000, Value = 600, TickFrequency = 50, IsSnapToTickEnabled = true, Width = 160 };
    private readonly TextBlock holdLabel = new() { Text = "Hold Time: 600 ms", VerticalAlignment = VerticalAlignment.Center };

    private readonly Slider releaseSlider = new() { Minimum = 50, Maximum = 3000, Value = 800, TickFrequency = 50, IsSnapToTickEnabled = true, Width = 160 };
    private readonly TextBlock releaseLabel = new() { Text = "Release (Fade Up): 800 ms", VerticalAlignment = VerticalAlignment.Center };

    private readonly CheckBox enabledCheck = new() { Content = "Enable Realtime Sidechain Ducking", IsChecked = true, Margin = new Thickness(0, 4, 0, 8) };
    private readonly Button bakeButton = new() { Content = "Bake to Track Volume Envelope", Height = 28, Margin = new Thickness(0, 4, 0, 4) };
    private readonly Button clearEnvelopeButton = new() { Content = "Clear Volume Envelope", Height = 28, Margin = new Thickness(8, 4, 0, 4) };

    public AutoDuckingWindow(EditorViewModel viewModel, Guid targetTrackId, Action? projectChanged = null, Window? owner = null)
    {
        vm = viewModel;
        trackId = targetTrackId;
        onProjectChanged = projectChanged;

        if (owner != null) Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)FindResource(typeof(Window));
        Title = "Auto Ducking Controls";
        Width = 460;
        Height = 490;
        MinWidth = 420;
        MinHeight = 460;

        var targetTrack = vm.Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        var trackName = targetTrack?.Name ?? "Audio Track";

        var panelBrush = (System.Windows.Media.Brush)FindResource("PanelBrush");
        var root = new StackPanel { Margin = new Thickness(16), Background = panelBrush };

        // 1. Header with Track Info
        root.Children.Add(new TextBlock
        {
            Text = $"Track: {trackName}",
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // 2. Track Role Selector
        root.Children.Add(new TextBlock { Text = "Auto Ducking Role:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        roleCombo.Items.Add("None (Standard Audio Track)");
        roleCombo.Items.Add("Controller (Dialogue / Voiceover Trigger)");
        roleCombo.Items.Add("Listener (Music / Ducked Background)");

        var initialRole = targetTrack?.DuckingRole ?? AudioDuckingRole.None;
        roleCombo.SelectedIndex = (int)initialRole;
        roleCombo.SelectionChanged += (_, _) => UpdateVisibility();
        root.Children.Add(roleCombo);

        // 3. Listener Settings Panel
        listenerSettingsPanel.Margin = new Thickness(0, 10, 0, 0);

        // Target Controller Track
        listenerSettingsPanel.Children.Add(new TextBlock { Text = "Trigger Source Track:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) });
        PopulateControllerTracks();
        listenerSettingsPanel.Children.Add(controllerTrackCombo);

        // Ducking Level
        var duckPanel = new DockPanel { Margin = new Thickness(0, 8, 0, 4) };
        duckLevelSlider.ValueChanged += (_, e) => duckLevelLabel.Text = $"Ducking Level: {e.NewValue:F1} dB";
        duckPanel.Children.Add(duckLevelLabel);
        DockPanel.SetDock(duckLevelSlider, Dock.Right);
        duckPanel.Children.Add(duckLevelSlider);
        listenerSettingsPanel.Children.Add(duckPanel);

        // Threshold
        var threshPanel = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
        thresholdSlider.ValueChanged += (_, e) => thresholdLabel.Text = $"Speech Threshold: {e.NewValue:F1} dB";
        threshPanel.Children.Add(thresholdLabel);
        DockPanel.SetDock(thresholdSlider, Dock.Right);
        threshPanel.Children.Add(thresholdSlider);
        listenerSettingsPanel.Children.Add(threshPanel);

        // Attack
        var attackPanel = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
        attackSlider.ValueChanged += (_, e) => attackLabel.Text = $"Attack (Fade Down): {e.NewValue:F0} ms";
        attackPanel.Children.Add(attackLabel);
        DockPanel.SetDock(attackSlider, Dock.Right);
        attackPanel.Children.Add(attackSlider);
        listenerSettingsPanel.Children.Add(attackPanel);

        // Hold
        var holdPanel = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
        holdSlider.ValueChanged += (_, e) => holdLabel.Text = $"Hold Time: {e.NewValue:F0} ms";
        holdPanel.Children.Add(holdLabel);
        DockPanel.SetDock(holdSlider, Dock.Right);
        holdPanel.Children.Add(holdSlider);
        listenerSettingsPanel.Children.Add(holdPanel);

        // Release
        var releasePanel = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
        releaseSlider.ValueChanged += (_, e) => releaseLabel.Text = $"Release (Fade Up): {e.NewValue:F0} ms";
        releasePanel.Children.Add(releaseLabel);
        DockPanel.SetDock(releaseSlider, Dock.Right);
        releasePanel.Children.Add(releaseSlider);
        listenerSettingsPanel.Children.Add(releasePanel);

        // Enable checkbox
        listenerSettingsPanel.Children.Add(enabledCheck);

        // Envelope Action Buttons
        var envButtonsPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        bakeButton.Click += BakeButtonClick;
        clearEnvelopeButton.Click += ClearEnvelopeButtonClick;
        envButtonsPanel.Children.Add(bakeButton);
        envButtonsPanel.Children.Add(clearEnvelopeButton);
        listenerSettingsPanel.Children.Add(envButtonsPanel);

        root.Children.Add(listenerSettingsPanel);

        // Load existing settings if available
        if (targetTrack?.DuckingSettings is { } s)
        {
            duckLevelSlider.Value = s.DuckLevelDb;
            thresholdSlider.Value = s.ThresholdDb;
            attackSlider.Value = s.AttackMs;
            holdSlider.Value = s.HoldMs;
            releaseSlider.Value = s.ReleaseMs;
            enabledCheck.IsChecked = s.IsEnabled;
        }

        // 4. Bottom Dialog Action Buttons
        var actionPanel = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        var okButton = new Button { Content = "OK", Width = 80, Height = 28 };
        okButton.Click += OkButtonClick;
        actionPanel.Children.Add(okButton);

        var cancelButton = new Button { Content = "Cancel", Width = 80, Height = 28, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        actionPanel.Children.Add(cancelButton);

        root.Children.Add(actionPanel);
        Content = root;

        UpdateVisibility();
    }

    private void PopulateControllerTracks()
    {
        controllerTrackCombo.Items.Clear();
        controllerTrackCombo.Items.Add("All Active Controller Tracks");

        var controllers = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Audio && t.Id != trackId).ToList();
        var selectedIdx = 0;

        for (var i = 0; i < controllers.Count; i++)
        {
            var tr = controllers[i];
            var roleTag = tr.DuckingRole == AudioDuckingRole.Controller ? " [Controller]" : "";
            controllerTrackCombo.Items.Add($"{tr.Name}{roleTag}");

            var currentTrack = vm.Project.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (currentTrack?.DuckingSettings?.ControllerTrackId == tr.Id)
            {
                selectedIdx = i + 1;
            }
        }

        controllerTrackCombo.SelectedIndex = selectedIdx;
    }

    private void UpdateVisibility()
    {
        var role = (AudioDuckingRole)roleCombo.SelectedIndex;
        listenerSettingsPanel.Visibility = role == AudioDuckingRole.Listener ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BakeButtonClick(object sender, RoutedEventArgs e)
    {
        var settings = BuildSettings();
        vm.Editor.SetTrackDucking(trackId, AudioDuckingRole.Listener, settings);
        var baked = vm.Editor.BakeTrackDuckingEnvelope(trackId);
        onProjectChanged?.Invoke();

        if (baked)
        {
            MessageBox.Show(this, "Successfully baked speech ducking envelope onto timeline track!", "Auto Ducking", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, "Could not bake envelope. Ensure at least one Controller track with audio clips exists.", "Auto Ducking", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearEnvelopeButtonClick(object sender, RoutedEventArgs e)
    {
        vm.Editor.ClearTrackDuckingEnvelope(trackId);
        onProjectChanged?.Invoke();
        MessageBox.Show(this, "Cleared volume envelope from track.", "Auto Ducking", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private AudioDuckingSettings BuildSettings()
    {
        Guid? selectedControllerId = null;
        if (controllerTrackCombo.SelectedIndex > 0)
        {
            var otherTracks = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Audio && t.Id != trackId).ToList();
            var targetIdx = controllerTrackCombo.SelectedIndex - 1;
            if (targetIdx >= 0 && targetIdx < otherTracks.Count)
            {
                selectedControllerId = otherTracks[targetIdx].Id;
            }
        }

        return new AudioDuckingSettings
        {
            IsEnabled = enabledCheck.IsChecked == true,
            ControllerTrackId = selectedControllerId,
            DuckLevelDb = duckLevelSlider.Value,
            ThresholdDb = thresholdSlider.Value,
            AttackMs = attackSlider.Value,
            HoldMs = holdSlider.Value,
            ReleaseMs = releaseSlider.Value
        };
    }

    private void OkButtonClick(object sender, RoutedEventArgs e)
    {
        var role = (AudioDuckingRole)roleCombo.SelectedIndex;
        var settings = role == AudioDuckingRole.Listener ? BuildSettings() : null;

        vm.Editor.SetTrackDucking(trackId, role, settings);
        onProjectChanged?.Invoke();

        DialogResult = true;
        Close();
    }
}

