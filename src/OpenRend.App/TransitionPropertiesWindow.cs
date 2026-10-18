using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

public sealed class TransitionPropertiesWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly Guid clipId;
    private readonly TextBlock heading = new(), timing = new();
    private readonly ComboBox presetCombo = new();
    private readonly StackPanel paramPanel = new();
    private readonly Button remove, undo, redo;
    private bool updatingControls;

    public TransitionPropertiesWindow(EditorViewModel viewModel, Guid eventId)
    {
        vm = viewModel; clipId = eventId;
        Style = (Style)FindResource(typeof(Window));
        Title = "Video event FX"; Width = 580; Height = 360; MinWidth = 470; MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Background = (Brush)FindResource("PanelBrush") };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Background = (Brush)FindResource("HeaderBrush") };
        remove = Button("Remove Selected Plug-In", "RemoveTransitionPlugin", () => vm.Editor.RemoveVideoTransition(clipId));
        undo = Button("Undo", "UndoTransitionEdit", () => vm.Editor.Undo());
        redo = Button("Redo", "RedoTransitionEdit", () => vm.Editor.Redo());
        tools.Children.Add(remove); tools.Children.Add(undo); tools.Children.Add(redo);
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
        var presetLabel = new TextBlock { Text = "Preset:", Width = 60, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") };
        presetCombo.Margin = new Thickness(4, 0, 0, 0);
        presetCombo.SelectionChanged += PresetChanged;
        AutomationProperties.SetAutomationId(presetCombo, "TransitionPresetCombo");
        presetRow.Children.Add(presetLabel);
        presetRow.Children.Add(presetCombo);

        body.Children.Add(heading);
        body.Children.Add(timing);
        body.Children.Add(presetRow);
        body.Children.Add(paramPanel);

        scroll.Content = body;
        root.Children.Add(scroll);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
        vm.Editor.Changed += EditorChanged;
        Closed += (_, _) => vm.Editor.Changed -= EditorChanged;
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

    private void EditorChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var item = vm.Project.Tracks.SelectMany(track => track.Clips.Select(clip => (Track: track, Clip: clip)))
            .FirstOrDefault(item => item.Clip.Id == clipId);
        var clip = item.Clip;
        heading.Text = clip?.FadeInTransition is { } effect ? $"{effect.Kind} — {effect.Preset}" : "No transition plug-in";
        timing.Text = clip is null ? "Event is no longer in the project." : $"Fade in: {clip.FadeInFrames} frames";
        remove.IsEnabled = clip?.FadeInTransition is not null && !item.Track.IsLocked;
        undo.IsEnabled = vm.Editor.CanUndo; redo.IsEnabled = vm.Editor.CanRedo;

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
            presetCombo.IsEnabled = clip?.FadeInTransition is not null && !item.Track.IsLocked;
        }
        finally
        {
            updatingControls = false;
        }
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
        var paramsDict = trans.Parameters ?? new Dictionary<string, string>();

        switch (trans.Kind)
        {
            case VideoTransitionKind.LinearWipe:
            {
                AddSlider("Angle (°)", "Angle", 0, 360, GetFloat(paramsDict, "Angle", 0), val => SetParam("Angle", val.ToString("F1", CultureInfo.InvariantCulture)));
                AddSlider("Feather", "Feather", 0, 1, GetFloat(paramsDict, "Feather", 0), val => SetParam("Feather", val.ToString("F2", CultureInfo.InvariantCulture)));
                break;
            }
            case VideoTransitionKind.BarnDoor:
            {
                AddChoice("Orientation", ["Horizontal", "Vertical"], (int)GetFloat(paramsDict, "Orientation", 1), idx => SetParam("Orientation", idx.ToString()));
                AddChoice("Direction", ["In", "Out"], (int)GetFloat(paramsDict, "Direction", 1), idx => SetParam("Direction", idx.ToString()));
                AddSlider("Border Size", "BorderSize", 0, 0.2, GetFloat(paramsDict, "BorderSize", 0), val => SetParam("BorderSize", val.ToString("F3", CultureInfo.InvariantCulture)));
                AddSlider("Border Feather", "BorderFeather", 0, 1, GetFloat(paramsDict, "BorderFeather", 0), val => SetParam("BorderFeather", val.ToString("F2", CultureInfo.InvariantCulture)));
                AddColorChoice("Border Color", paramsDict.GetValueOrDefault("BorderColor", "0 0 0"), col => SetParam("BorderColor", col));
                break;
            }
            case VideoTransitionKind.Push:
            {
                AddChoice("Direction", ["Up", "Down", "Left", "Right"], (int)GetFloat(paramsDict, "Direction", 2), idx => SetParam("Direction", idx.ToString()));
                AddToggle("Push off previous image", GetBool(paramsDict, "PushOffPreviousImage", true), b => SetParam("PushOffPreviousImage", b.ToString().ToLowerInvariant()));
                AddSlider("Border Size", "BorderSize", 0, 0.2, GetFloat(paramsDict, "BorderSize", 0), val => SetParam("BorderSize", val.ToString("F3", CultureInfo.InvariantCulture)));
                AddColorChoice("Border Color", paramsDict.GetValueOrDefault("BorderColor", "0 0 0"), col => SetParam("BorderColor", col));
                break;
            }
            case VideoTransitionKind.Slide:
            {
                AddSlider("Angle (°)", "Angle", 0, 360, GetFloat(paramsDict, "Angle", 0), val => SetParam("Angle", val.ToString("F1", CultureInfo.InvariantCulture)));
                AddChoice("Direction", ["In", "Out"], (int)GetFloat(paramsDict, "Direction", 0), idx => SetParam("Direction", idx.ToString()));
                AddSlider("Border Size", "BorderSize", 0, 0.2, GetFloat(paramsDict, "BorderSize", 0), val => SetParam("BorderSize", val.ToString("F3", CultureInfo.InvariantCulture)));
                AddColorChoice("Border Color", paramsDict.GetValueOrDefault("BorderColor", "0 0 0"), col => SetParam("BorderColor", col));
                break;
            }
            case VideoTransitionKind.Zoom:
            {
                AddChoice("Direction", ["In", "Out"], (int)GetFloat(paramsDict, "Direction", 0), idx => SetParam("Direction", idx.ToString()));
                AddSlider("Border Size", "BorderSize", 0, 0.2, GetFloat(paramsDict, "BorderSize", 0), val => SetParam("BorderSize", val.ToString("F3", CultureInfo.InvariantCulture)));
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
                AddSlider("Feather", "Feather", 0, 1, GetFloat(paramsDict, "Feather", 0), val => SetParam("Feather", val.ToString("F2", CultureInfo.InvariantCulture)));
                AddSlider("Border", "Border", 0, 0.2, GetFloat(paramsDict, "Border", 0), val => SetParam("Border", val.ToString("F3", CultureInfo.InvariantCulture)));
                AddColorChoice("Border Color", paramsDict.GetValueOrDefault("Color", "0 0 0"), col => SetParam("Color", col));
                break;
            }
            case VideoTransitionKind.Split:
            {
                AddChoice("Split Mode", ["Push", "Wipe", "Squeeze"], (int)GetFloat(paramsDict, "SplitMode", 0), idx => SetParam("SplitMode", idx.ToString()));
                AddChoice("Direction", ["In", "Out"], (int)GetFloat(paramsDict, "Direction", 1), idx => SetParam("Direction", idx.ToString()));
                AddSlider("Border Size", "BorderSize", 0, 0.2, GetFloat(paramsDict, "BorderSize", 0), val => SetParam("BorderSize", val.ToString("F3", CultureInfo.InvariantCulture)));
                AddColorChoice("Border Color", paramsDict.GetValueOrDefault("BorderColor", "0 0 0"), col => SetParam("BorderColor", col));
                break;
            }
            case VideoTransitionKind.ClockWipe:
            {
                AddChoice("Direction", ["Clockwise", "Counter Clockwise"], (int)GetFloat(paramsDict, "Direction", 0), idx => SetParam("Direction", idx.ToString()));
                AddSlider("Feather", "Feather", 0, 1, GetFloat(paramsDict, "Feather", 0), val => SetParam("Feather", val.ToString("F2", CultureInfo.InvariantCulture)));
                break;
            }
            case VideoTransitionKind.VenetianBlinds:
            {
                AddSlider("Angle (°)", "Angle", 0, 360, GetFloat(paramsDict, "Angle", 0), val => SetParam("Angle", val.ToString("F1", CultureInfo.InvariantCulture)));
                AddSlider("Number of Blinds", "NumberOfBlinds", 1, 30, GetFloat(paramsDict, "NumberOfBlinds", 5), val => SetParam("NumberOfBlinds", ((int)val).ToString()));
                AddSlider("Feather", "Feather", 0, 1, GetFloat(paramsDict, "Feather", 0), val => SetParam("Feather", val.ToString("F2", CultureInfo.InvariantCulture)));
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
        var item = vm.Project.Tracks.SelectMany(track => track.Clips.Select(clip => (Track: track, Clip: clip)))
            .FirstOrDefault(item => item.Clip.Id == clipId);
        if (item.Clip?.FadeInTransition is not { } current) return;
        var parameters = current.Parameters != null
            ? new Dictionary<string, string>(current.Parameters, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        parameters[key] = value;
        var updated = new VideoTransition
        {
            Kind = current.Kind,
            Preset = current.Preset,
            Parameters = parameters
        };
        vm.Editor.SetVideoTransition(clipId, updated);
    }

    private void AddSlider(string label, string paramKey, double min, double max, double current, Action<double> onChange)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var text = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") };
        var valText = new TextBlock { Text = current.ToString("F2", CultureInfo.InvariantCulture), Width = 45, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") };
        var slider = new Slider { Minimum = min, Maximum = max, Value = current, Margin = new Thickness(6, 0, 6, 0) };
        slider.ValueChanged += (_, e) =>
        {
            valText.Text = e.NewValue.ToString("F2", CultureInfo.InvariantCulture);
            if (!updatingControls) onChange(e.NewValue);
        };
        panel.Children.Add(text);
        DockPanel.SetDock(valText, Dock.Right);
        panel.Children.Add(valText);
        panel.Children.Add(slider);
        paramPanel.Children.Add(panel);
    }

    private void AddChoice(string label, string[] options, int selectedIndex, Action<int> onSelect)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var text = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") };
        var combo = new ComboBox { Margin = new Thickness(6, 0, 0, 0) };
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

    private void AddColorChoice(string label, string currentColor, Action<string> onColor)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var text = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") };
        var combo = new ComboBox { Margin = new Thickness(6, 0, 0, 0) };
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
        for (var i = 0; i < colorOptions.Length; i++)
        {
            if (currentColor.StartsWith(colorOptions[i].Value[..3], StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                break;
            }
        }
        if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) =>
        {
            if (!updatingControls && combo.SelectedIndex >= 0)
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
