using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

public sealed class TransitionPropertiesWindow : Window
{
    private const double ParameterLabelWidth = 110;
    private readonly EditorViewModel vm;
    private readonly Guid clipId;
    private readonly Action<VideoTransition?>? previewParameter;
    private readonly TextBlock heading = new(), timing = new();
    private readonly ComboBox presetCombo = new();
    private readonly StackPanel paramPanel = new();
    private readonly StackPanel settings = new();
    private readonly TransitionProgressEditor progress;
    private readonly Button remove, undo, redo, switchPanel;
    private bool updatingControls;
    private bool showingProgress;
    private bool committingSlider;
    private ParameterDrag? parameterDrag;

    private sealed record ParameterDrag(Slider Slider, Thumb Thumb, double StartValue);

    public TransitionPropertiesWindow(EditorViewModel viewModel, Guid eventId, bool showProgress = false, Action<long>? seek = null,
        Action<VideoTransition?>? previewParameter = null)
    {
        vm = viewModel; clipId = eventId;
        this.previewParameter = previewParameter;
        showingProgress = showProgress;
        progress = new(curve => vm.Editor.SetTransitionProgress(clipId, curve), offset =>
        {
            if (seek is null) return;
            var (_, clip) = vm.Editor.FindClip(clipId);
            var frame = (decimal)clip.StartFrame - clip.FadeOffsetFrames + Math.Min(offset, clip.FadeInFrames - 1);
            seek((long)Math.Clamp(frame, 0, vm.Project.DurationFrames));
        });
        Style = (Style)FindResource(typeof(Window));
        Title = "Video event FX"; Width = 580; Height = 408; MinWidth = 470; MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Background = (Brush)FindResource("PanelBrush") };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Background = (Brush)FindResource("HeaderBrush") };
        remove = Button("Remove Selected Plug-In", "RemoveTransitionPlugin", () => vm.Editor.RemoveVideoTransition(clipId));
        undo = Button("Undo", "UndoTransitionEdit", () => vm.Editor.Undo());
        redo = Button("Redo", "RedoTransitionEdit", () => vm.Editor.Redo());
        switchPanel = Button("Transition Progress", "TransitionProgressPanel", () =>
        {
            showingProgress = !showingProgress;
            ShowPanel();
        });
        tools.Children.Add(remove); tools.Children.Add(undo); tools.Children.Add(redo); tools.Children.Add(switchPanel);
        DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        var close = Button("Close", "CloseTransitionProperties", Close);
        close.HorizontalAlignment = HorizontalAlignment.Right; close.MinWidth = 78;
        DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var body = new StackPanel { Margin = new Thickness(14) };
        heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 8);
        AutomationProperties.SetAutomationId(heading, "TransitionPluginName");
        timing.Margin = new Thickness(0, 0, 0, 10);
        timing.Foreground = (Brush)FindResource("SubtleTextBrush");

        var presetRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var presetLabel = new TextBlock { Text = "Preset", Width = ParameterLabelWidth, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") };
        presetCombo.Margin = new Thickness(6, 0, 0, 0);
        presetCombo.SelectionChanged += PresetChanged;
        AutomationProperties.SetName(presetCombo, "Preset");
        AutomationProperties.SetAutomationId(presetCombo, "TransitionPresetCombo");
        presetRow.Children.Add(presetLabel);
        presetRow.Children.Add(presetCombo);

        body.Children.Add(heading);
        body.Children.Add(timing);
        settings.Children.Add(presetRow);
        settings.Children.Add(paramPanel);
        body.Children.Add(settings);
        body.Children.Add(progress);

        scroll.Content = body;
        root.Children.Add(scroll);
        Content = root;

        vm.Editor.Changed += EditorChanged;
        Closed += (_, _) =>
        {
            CancelParameterDrag();
            vm.Editor.Changed -= EditorChanged;
        };
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape || parameterDrag is null) return;
            args.Handled = true;
            CancelParameterDrag();
        };
        Refresh();
    }

    private Button Button(string label, string id, Action edit)
    {
        var button = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3) };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) =>
        {
            try { edit(); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Transition", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        return button;
    }

    private void EditorChanged(object? sender, EventArgs e)
    {
        if (committingSlider) UpdateHistoryButtons();
        else Refresh();
    }

    private void Refresh()
    {
        CancelParameterDrag();
        var item = vm.Project.Tracks.SelectMany(track => track.Clips.Select(clip => (Track: track, Clip: clip)))
            .FirstOrDefault(item => item.Clip.Id == clipId);
        var clip = item.Clip;
        var canEditProgress = vm.Editor.CanEditTransitionProgress(clipId);
        switchPanel.IsEnabled = canEditProgress;
        switchPanel.Visibility = clip?.FadeInTransition is null ? Visibility.Collapsed : Visibility.Visible;
        heading.Text = clip?.FadeInTransition is { } effect ? $"{TransitionCatalog.DisplayNameFor(effect.Kind)} · {effect.Preset}"
            : canEditProgress ? "Crossfade" : "No transition plug-in";
        timing.Text = clip is null ? "Event is no longer in the project." : $"Fade in: {clip.FadeInFrames} frames";
        var editable = clip is { IsLocked: false, FadeInTransition: not null } && !item.Track.IsLocked;
        remove.IsEnabled = paramPanel.IsEnabled = presetCombo.IsEnabled = editable;
        UpdateHistoryButtons();
        progress.Bind(clip?.FadeInFrames ?? 1, clip?.FadeInProgress, canEditProgress);
        showingProgress = canEditProgress && (showingProgress || clip?.FadeInTransition is null);
        ShowPanel();

        updatingControls = true;
        try
        {
            presetCombo.Items.Clear();
            paramPanel.Children.Clear();
            if (clip?.FadeInTransition is { } trans)
            {
                var plugin = TransitionCatalog.FindPlugin(trans.Kind);
                if (plugin is not null)
                {
                    foreach (var preset in plugin.Presets)
                        presetCombo.Items.Add(preset.Name);
                    presetCombo.SelectedItem = trans.Preset;
                }
                BuildParameterControls(trans);
            }
        }
        finally
        {
            updatingControls = false;
        }
    }

    private void UpdateHistoryButtons()
    {
        undo.IsEnabled = vm.Editor.CanUndo;
        redo.IsEnabled = vm.Editor.CanRedo;
    }

    private void ShowPanel()
    {
        switchPanel.Content = showingProgress ? "Effect Settings" : "Transition Progress";
        settings.Visibility = showingProgress ? Visibility.Collapsed : Visibility.Visible;
        progress.Visibility = showingProgress ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingControls || presetCombo.SelectedItem is not string selectedPreset) return;
        var item = vm.Project.Tracks.SelectMany(track => track.Clips.Select(clip => (Track: track, Clip: clip)))
            .FirstOrDefault(item => item.Clip.Id == clipId);
        if (item.Clip?.FadeInTransition is not { } current) return;
        var presetInfo = TransitionCatalog.FindPreset(current.Kind, selectedPreset);
        var parameters = presetInfo?.Parameters.ToDictionary(k => k.Key, v => v.Value) ?? new Dictionary<string, string>();
        var updated = new VideoTransition
        {
            Kind = current.Kind,
            Preset = selectedPreset,
            Parameters = parameters
        };
        vm.Editor.SetVideoTransition(clipId, updated);
    }

    private void BuildParameterControls(VideoTransition trans)
    {
        var paramsDict = TransitionCatalog.ResolveParameters(trans);

        switch (trans.Kind)
        {
            case VideoTransitionKind.LinearWipe:
            {
                AddSlider("Angle (°)", "Angle", 0, 360, GetFloat(paramsDict, "Angle", 0), 1);
                AddSlider("Feather", "Feather", 0, 1, GetFloat(paramsDict, "Feather", 0), 2);
                break;
            }
            case VideoTransitionKind.BarnDoor:
            {
                AddChoice("Orientation", ["Horizontal", "Vertical"], (int)GetFloat(paramsDict, "Orientation", 1), idx => SetParam("Orientation", idx.ToString()));
                AddChoice("Direction", ["In", "Out"], (int)GetFloat(paramsDict, "Direction", 1), idx => SetParam("Direction", idx.ToString()));
                AddSlider("Border Size", "BorderSize", 0, 0.2, GetFloat(paramsDict, "BorderSize", 0), 3);
                AddSlider("Border Feather", "BorderFeather", 0, 1, GetFloat(paramsDict, "BorderFeather", 0), 2);
                AddColorChoice("Border Color", paramsDict.GetValueOrDefault("BorderColor", "0 0 0"), col => SetParam("BorderColor", col));
                break;
            }
            case VideoTransitionKind.Push:
            {
                AddChoice("Direction", ["Up", "Down", "Left", "Right"], (int)GetFloat(paramsDict, "Direction", 2), idx => SetParam("Direction", idx.ToString()));
                AddToggle("Push off previous image", GetBool(paramsDict, "PushOffPreviousImage", true), b => SetParam("PushOffPreviousImage", b.ToString().ToLowerInvariant()));
                AddSlider("Border Size", "BorderSize", 0, 0.2, GetFloat(paramsDict, "BorderSize", 0), 3);
                AddColorChoice("Border Color", paramsDict.GetValueOrDefault("BorderColor", "0 0 0"), col => SetParam("BorderColor", col));
                break;
            }
            case VideoTransitionKind.Slide:
            {
                AddSlider("Angle (°)", "Angle", 0, 360, GetFloat(paramsDict, "Angle", 0), 1);
                AddChoice("Direction", ["In", "Out"], (int)GetFloat(paramsDict, "Direction", 0), idx => SetParam("Direction", idx.ToString()));
                AddSlider("Border Size", "BorderSize", 0, 0.2, GetFloat(paramsDict, "BorderSize", 0), 3);
                AddColorChoice("Border Color", paramsDict.GetValueOrDefault("BorderColor", "0 0 0"), col => SetParam("BorderColor", col));
                break;
            }
            case VideoTransitionKind.Zoom:
            {
                AddChoice("Direction", ["In", "Out"], (int)GetFloat(paramsDict, "Direction", 0), idx => SetParam("Direction", idx.ToString()));
                AddSlider("Border Size", "BorderSize", 0, 0.2, GetFloat(paramsDict, "BorderSize", 0), 3);
                AddColorChoice("Border Color", paramsDict.GetValueOrDefault("BorderColor", "0 0 0"), col => SetParam("BorderColor", col));
                break;
            }
            case VideoTransitionKind.Flash:
            {
                AddColorChoice("Flash Tint", paramsDict.GetValueOrDefault("Tint", "1 1 1"), col => SetParam("Tint", col));
                break;
            }
            case VideoTransitionKind.Iris:
            {
                AddChoice("Shape", ["Circle", "Square", "Diamond", "Rectangle", "Triangle"], (int)GetFloat(paramsDict, "Shape", 0), idx => SetParam("Shape", idx.ToString()));
                AddChoice("Direction", ["In", "Out"], (int)GetFloat(paramsDict, "Direction", 1), idx => SetParam("Direction", idx.ToString()));
                AddSlider("Feather", "Feather", 0, 1, GetFloat(paramsDict, "Feather", 0), 2);
                AddSlider("Border", "Border", 0, 0.2, GetFloat(paramsDict, "Border", 0), 3);
                AddColorChoice("Border Color", paramsDict.GetValueOrDefault("Color", "0 0 0"), col => SetParam("Color", col));
                break;
            }
            case VideoTransitionKind.Split:
            {
                AddChoice("Split Mode", ["Push", "Wipe", "Squeeze"], (int)GetFloat(paramsDict, "SplitMode", 0), idx => SetParam("SplitMode", idx.ToString()));
                AddChoice("Direction", ["In", "Out"], (int)GetFloat(paramsDict, "Direction", 1), idx => SetParam("Direction", idx.ToString()));
                AddSlider("Border Size", "BorderSize", 0, 0.2, GetFloat(paramsDict, "BorderSize", 0), 3);
                AddColorChoice("Border Color", paramsDict.GetValueOrDefault("BorderColor", "0 0 0"), col => SetParam("BorderColor", col));
                break;
            }
            case VideoTransitionKind.ClockWipe:
            {
                AddChoice("Direction", ["Clockwise", "Counter Clockwise"], (int)GetFloat(paramsDict, "Direction", 0), idx => SetParam("Direction", idx.ToString()));
                AddSlider("Feather", "Feather", 0, 1, GetFloat(paramsDict, "Feather", 0), 2);
                break;
            }
            case VideoTransitionKind.VenetianBlinds:
            {
                AddSlider("Angle (°)", "Angle", 0, 360, GetFloat(paramsDict, "Angle", 0), 1);
                AddSlider("Number of Blinds", "NumberOfBlinds", 1, 30, GetFloat(paramsDict, "NumberOfBlinds", 5), 0);
                AddSlider("Feather", "Feather", 0, 1, GetFloat(paramsDict, "Feather", 0), 2);
                break;
            }
            case VideoTransitionKind.Squeeze:
            {
                AddChoice("Origin", ["Bottom", "Top", "Right", "Left", "Horizontal center", "Vertical center"],
                    (int)GetFloat(paramsDict, "Direction", 2), index => SetParam("Direction", index.ToString(CultureInfo.InvariantCulture)),
                    "TransitionParameter_Direction");
                AddToggle("Squeeze previous image", GetBool(paramsDict, "SqueezePreviousImage", true),
                    enabled => SetParam("SqueezePreviousImage", enabled ? "true" : "false"));
                AddSlider("Border Size", "BorderSize", 0, .2, GetFloat(paramsDict, "BorderSize", 0), 3);
                AddSlider("Border Feather", "BorderFeather", 0, 1, GetFloat(paramsDict, "BorderFeather", 0), 2);
                AddColorChoice("Border Color", paramsDict.GetValueOrDefault("BorderColor", "0 0 0"), value => SetParam("BorderColor", value));
                break;
            }
            case VideoTransitionKind.Swap:
            {
                AddChoice("Direction", ["Up", "Down", "Left", "Right"], (int)GetFloat(paramsDict, "Direction", 2),
                    index => SetParam("Direction", index.ToString(CultureInfo.InvariantCulture)), "TransitionParameter_Direction");
                AddSlider("Border Size", "BorderSize", 0, .2, GetFloat(paramsDict, "BorderSize", 0), 3);
                AddSlider("Border Feather", "BorderFeather", 0, 1, GetFloat(paramsDict, "BorderFeather", 0), 2);
                AddColorChoice("Border Color", paramsDict.GetValueOrDefault("BorderColor", "0 0 0"), value => SetParam("BorderColor", value));
                break;
            }
            case VideoTransitionKind.CrossEffect:
            {
                var cross = CrossEffectSettings.Read(paramsDict);
                AddChoice("Effect", Enum.GetNames<CrossEffectMode>(), (int)cross.Mode,
                    index => SetParam("EffectType", ((CrossEffectMode)index).ToString()), "TransitionParameter_EffectType");
                if (cross.Mode != CrossEffectMode.None)
                    AddChoice("Apply to", ["Both", "Source", "Destination"], (int)cross.Target,
                        index => SetParam("ApplyTo", ((CrossEffectTarget)index).ToString()), "TransitionParameter_ApplyTo");
                AddSlider("Fade Range", "FadeRange", 0, 1, cross.FadeRange, 2);
                if (cross.Mode == CrossEffectMode.Zoom)
                {
                    if (cross.Target != CrossEffectTarget.Destination)
                    {
                        AddSlider("Source Center X", "SourceCenterX", 0, 1, cross.SourceCenter.X, 2);
                        AddSlider("Source Center Y", "SourceCenterY", 0, 1, cross.SourceCenter.Y, 2);
                    }
                    if (cross.Target != CrossEffectTarget.Source)
                    {
                        AddSlider("Destination X", "DestinationCenterX", 0, 1, cross.DestinationCenter.X, 2);
                        AddSlider("Destination Y", "DestinationCenterY", 0, 1, cross.DestinationCenter.Y, 2);
                    }
                }
                else if (cross.Mode == CrossEffectMode.Pixelate)
                {
                    AddSlider("Maximum Block X", "MaxX", 0, 1, cross.PixelWidth, 2);
                    AddSlider("Maximum Block Y", "MaxY", 0, 1, cross.PixelHeight, 2);
                }
                else if (cross.Mode == CrossEffectMode.Blur)
                    AddSlider("Maximum Blur", "MaxBlur", 0, 1, cross.Blur, 2);
                break;
            }
            case VideoTransitionKind.PageLoop:
            case VideoTransitionKind.PagePeel:
            case VideoTransitionKind.PageRoll:
            {
                var page = PageTransitionSettings.Read(trans.Kind, paramsDict);
                var loop = trans.Kind == VideoTransitionKind.PageLoop;
                AddSlider("Angle (°)", "Angle", 0, 360, page.Angle, 1);
                AddSlider(loop ? "Loop Radius" : "Fold Radius", "Radius", 0, 1, page.Radius, 2);
                if (loop) AddSlider("Loop Position", "LoopPosition", 0, 1, page.LoopPosition, 2);
                else AddSlider("Slide Amount", "SlideAmount", 0, 1, page.SlideAmount, 2);
                AddSlider(loop ? "Paper Opacity" : "Fold Opacity", "Opacity", 0, 1, page.Opacity, 2);
                AddSlider("Perspective", "Perspective", 0, 1, page.Perspective, 2);
                AddColorChoice("Light Color", paramsDict.GetValueOrDefault("LightColor", PageTransitionSettings.DefaultLightColor),
                    value => SetParam("LightColor", value), "TransitionParameter_LightColor");
                break;
            }
            case VideoTransitionKind.Dissolve:
            {
                var types = new[] { "Crossfade", "Additive", "Subtractive", "Color Bleed", "Color Morph", "Threshold Dissolve", "Threshold Appear", "Fade Through Grayscale", "Fade Through Color", "RGB Crossfade" };
                AddChoice("Dissolve Type", types, (int)GetFloat(paramsDict, "Type", 0), idx => SetParam("Type", idx.ToString()));
                if (GetFloat(paramsDict, "Type", 0) == 8)
                {
                    AddColorChoice("Fade Color", paramsDict.GetValueOrDefault("FadeThroughColor", "0 0 0 1"), col => SetParam("FadeThroughColor", col));
                }
                break;
            }
        }
    }

    private void SetParam(string key, string value)
    {
        if (ParameterValue(key, value) is not { } updated) return;
        vm.Editor.SetVideoTransition(clipId, updated);
    }

    private VideoTransition? ParameterValue(string key, string value)
    {
        var current = vm.Project.Tracks.SelectMany(track => track.Clips).FirstOrDefault(clip => clip.Id == clipId)?.FadeInTransition;
        if (current is null) return null;
        var parameters = current.Parameters is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(current.Parameters, StringComparer.OrdinalIgnoreCase);
        if (parameters.TryGetValue(key, out var old) && old == value) return null;
        parameters[key] = value;
        return new VideoTransition { Kind = current.Kind, Preset = current.Preset, Parameters = parameters };
    }

    private void AddSlider(string label, string paramKey, double min, double max, double current, int decimals)
    {
        var format = $"F{decimals}";
        string Format(double value) => value.ToString(format, CultureInfo.InvariantCulture);
        var panel = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var text = new TextBlock { Text = label, Width = ParameterLabelWidth, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") };
        var valText = new TextBlock { Width = 54, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") };
        var slider = new Slider { Minimum = min, Maximum = max, Value = current, Margin = new Thickness(6, 0, 6, 0),
            SmallChange = Math.Pow(10, -decimals), LargeChange = Math.Max(Math.Pow(10, -decimals), (max - min) / 10),
            TickFrequency = 1, IsSnapToTickEnabled = decimals == 0 };
        valText.Text = Format(slider.Value);
        AutomationProperties.SetName(slider, label);
        AutomationProperties.SetAutomationId(slider, "TransitionParameter_" + paramKey);
        slider.ValueChanged += (_, e) =>
        {
            valText.Text = Format(e.NewValue);
            if (updatingControls) return;
            if (parameterDrag?.Slider == slider)
                previewParameter?.Invoke(ParameterValue(paramKey, valText.Text));
            else CommitSlider(valText.Text);
        };
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, args) =>
        {
            if (args.OriginalSource is not Thumb thumb) return;
            slider.Focus();
            parameterDrag = new ParameterDrag(slider, thumb, slider.Value);
        }), true);
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, args) =>
        {
            if (parameterDrag is not { } drag || drag.Slider != slider) return;
            if (args.Canceled) { CancelParameterDrag(); return; }
            parameterDrag = null;
            previewParameter?.Invoke(null);
            if (Format(drag.StartValue) != Format(slider.Value)) CommitSlider(Format(slider.Value));
        }), true);
        panel.Children.Add(text);
        DockPanel.SetDock(valText, Dock.Right);
        panel.Children.Add(valText);
        panel.Children.Add(slider);
        paramPanel.Children.Add(panel);

        void CommitSlider(string value)
        {
            // Keep the focused slider in place for keyboard repeats within the same parameter.
            committingSlider = true;
            try { SetParam(paramKey, value); }
            finally { committingSlider = false; }
        }
    }

    private void CancelParameterDrag()
    {
        if (parameterDrag is not { } drag) return;
        parameterDrag = null;
        drag.Thumb.CancelDrag();
        previewParameter?.Invoke(null);
        var wasUpdating = updatingControls;
        updatingControls = true;
        try { drag.Slider.Value = drag.StartValue; }
        finally { updatingControls = wasUpdating; }
    }

    private void AddChoice(string label, string[] options, int selectedIndex, Action<int> onSelect, string? automationId = null)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var text = new TextBlock { Text = label, Width = ParameterLabelWidth, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") };
        var combo = new ComboBox { Margin = new Thickness(6, 0, 0, 0) };
        AutomationProperties.SetName(combo, label);
        if (automationId is not null) AutomationProperties.SetAutomationId(combo, automationId);
        foreach (var opt in options) combo.Items.Add(opt);
        if (selectedIndex >= 0 && selectedIndex < options.Length) combo.SelectedIndex = selectedIndex;
        combo.SelectionChanged += (_, _) =>
        {
            if (!updatingControls && combo.SelectedIndex >= 0) onSelect(combo.SelectedIndex);
        };
        panel.Children.Add(text);
        panel.Children.Add(combo);
        paramPanel.Children.Add(panel);
    }

    private void AddToggle(string label, bool isChecked, Action<bool> onToggle)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var check = new CheckBox { Content = label, IsChecked = isChecked, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") };
        check.Checked += (_, _) => { if (!updatingControls) onToggle(true); };
        check.Unchecked += (_, _) => { if (!updatingControls) onToggle(false); };
        panel.Children.Add(check);
        paramPanel.Children.Add(panel);
    }

    private void AddColorChoice(string label, string currentColor, Action<string> onColor, string? automationId = null)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var text = new TextBlock { Text = label, Width = ParameterLabelWidth, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") };
        var combo = new ComboBox { Margin = new Thickness(6, 0, 0, 0) };
        AutomationProperties.SetName(combo, label);
        if (automationId is not null) AutomationProperties.SetAutomationId(combo, automationId);
        var colorOptions = new (string Name, string Value)[]
        {
            ("Black", "0 0 0 1"),
            ("White", "1 1 1 1"),
            ("Red", "1 0 0 1"),
            ("Green", "0 1 0 1"),
            ("Blue", "0 0 1 1"),
            ("Yellow", "1 1 0 1"),
            ("Transparent", "0 0 0 0")
        };
        foreach (var (name, _) in colorOptions) combo.Items.Add(name);
        combo.SelectedIndex = Array.FindIndex(colorOptions, color => currentColor == color.Value || currentColor + " 1" == color.Value);
        if (combo.SelectedIndex < 0)
        {
            combo.Items.Add(new ComboBoxItem { Content = "Saved color", IsEnabled = false });
            combo.SelectedIndex = colorOptions.Length;
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (!updatingControls && combo.SelectedIndex >= 0 && combo.SelectedIndex < colorOptions.Length)
                onColor(colorOptions[combo.SelectedIndex].Value);
        };
        panel.Children.Add(text);
        panel.Children.Add(combo);
        paramPanel.Children.Add(panel);
    }

    private static float GetFloat(IReadOnlyDictionary<string, string> dict, string key, float def)
    {
        if (dict.TryGetValue(key, out var val) && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            return result;
        return def;
    }

    private static bool GetBool(IReadOnlyDictionary<string, string> dict, string key, bool def)
    {
        if (dict.TryGetValue(key, out var val) && bool.TryParse(val, out var result))
            return result;
        return def;
    }

}
