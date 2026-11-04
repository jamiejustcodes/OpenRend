using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenRend.Core;
using OpenRend.Timeline;

namespace OpenRend.App;

// Keep the effect order visible while adjusting a selected effect and its preview.
public sealed partial class VideoEffectsWindow : Window
{
    private static readonly VideoEffectKind[] BuiltInEffects = Enum.GetValues<VideoEffectKind>();
    private readonly EditorViewModel vm;
    private readonly TimelineEditor editor;
    private readonly EffectScope scope;
    private readonly Guid targetId;
    private readonly ListBox chain = new();
    private readonly ComboBox available = new();
    private readonly CheckBox enabled = new() { Content = "Enable selected effect" };
    private readonly ToggleButton animateToggle = new() { Content = "Animate", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2), FontSize = 11, ToolTip = "Toggle parameter keyframe animation (Vegas Pro FX keyframes)" };
    private readonly TextBlock heading = new(), empty = new(), status = new();
    private readonly StackPanel parameters = new();
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly Button remove, up, down, reset, undo, redo;
    private readonly List<(VideoEffectParameterDefinition Definition, Slider Slider, TextBox Input)> controls = [];
    private readonly List<(VideoEffectParameterDefinition Definition, ComboBox Choice)> choiceControls = [];

    // Keyframe Animation Controls
    private readonly Border keyframeLaneHost = new()
    {
        Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(48, 48, 48)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2),
        Margin = new Thickness(0, 6, 0, 0),
        Visibility = Visibility.Collapsed
    };
    private readonly EffectKeyframeLane keyframeLane = new();
    private readonly TextBlock keyframeReadout = new() { Text = "Frame: 0", FontSize = 10, Foreground = Brushes.Silver, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private long currentKeyframeCursor;

    private Guid? selectedId;
    private Guid? parameterEffectId;
    private VideoEffectKind? parameterKind;
    private bool refreshing, closed, previewRunning, previewPending, adjusting;
    private long previewRevision;

    public VideoEffectsWindow(EditorViewModel viewModel, Guid selectedClip)
        : this(viewModel, EffectScope.Event, selectedClip)
    {
    }

    public VideoEffectsWindow(EditorViewModel viewModel, EffectScope effectScope, Guid selectedTargetId)
    {
        vm = viewModel; editor = vm.Editor; scope = effectScope; targetId = selectedTargetId;
        Style = (Style)FindResource(typeof(Window));
        Title = WindowTitle; Width = 840; Height = 600; MinWidth = 720; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Background = (Brush)FindResource("PanelBrush") };
        root.RowDefinitions.Add(new() { Height = new GridLength(30) });
        root.RowDefinitions.Add(new() { Height = new GridLength(36) });
        root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = new GridLength(36) });
        root.Children.Add(new TextBlock { Text = HeaderText, Margin = new Thickness(10, 0, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis });

        // 1. Top Toolbar
        var toolbar = new DockPanel { LastChildFill = false, Background = (Brush)FindResource("HeaderBrush"), Margin = new Thickness(0, 0, 0, 1) };
        Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);
        toolbar.Children.Add(new TextBlock { Text = "Built-in:", Margin = new Thickness(10, 0, 6, 0) });
        available.Width = 185; available.ItemsSource = BuiltInEffects.Select(EffectName).ToArray(); available.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(available, "AvailableVideoEffect"); AutomationProperties.SetName(available, "Available video effect"); toolbar.Children.Add(available);
        toolbar.Children.Add(MakeButton("Add", "Add video effect", () => Edit(() =>
        {
            if (available.SelectedIndex >= 0) selectedId = editor.AddScopedVideoEffect(scope, targetId, BuiltInEffects[available.SelectedIndex]).Id;
        }), "AddVideoEffect"));
        toolbar.Children.Add(MakeButton("Plug-in chooser…", "Open video FX plug-in chooser", OpenChooser, "OpenFxChooser"));
        undo = MakeButton("Undo", "Undo effect edit", () => Edit(() => editor.Undo()), "UndoVideoEffect");
        redo = MakeButton("Redo", "Redo effect edit", () => Edit(() => editor.Redo()), "RedoVideoEffect");
        DockPanel.SetDock(redo, Dock.Right); toolbar.Children.Add(redo); DockPanel.SetDock(undo, Dock.Right); toolbar.Children.Add(undo);

        // 2. Main Body Grid
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new GridLength(230) }); body.ColumnDefinitions.Add(new());
        Grid.SetRow(body, 2); root.Children.Add(body);

        // 2.1 Left: Effect Chain
        var left = new DockPanel { Background = (Brush)FindResource("SurfaceBrush"), Margin = new Thickness(6) };
        body.Children.Add(left);
        var chainLabel = new TextBlock { Text = "Effect chain", Margin = new Thickness(5, 5, 5, 8) }; DockPanel.SetDock(chainLabel, Dock.Top); left.Children.Add(chainLabel);
        var chainTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 5, 2, 2) };
        up = MakeButton("↑", "Move effect earlier", () => Move(-1), "MoveVideoEffectUp");
        down = MakeButton("↓", "Move effect later", () => Move(1), "MoveVideoEffectDown");
        remove = MakeButton("Remove", "Remove video effect", () => Edit(() => { if (selectedId is { } id) editor.RemoveScopedVideoEffect(scope, targetId, id); }), "RemoveVideoEffect");
        chainTools.Children.Add(up); chainTools.Children.Add(down); chainTools.Children.Add(remove); DockPanel.SetDock(chainTools, Dock.Bottom); left.Children.Add(chainTools);
        chain.DisplayMemberPath = nameof(ChainEntry.Name); AutomationProperties.SetAutomationId(chain, "VideoEffectChain"); AutomationProperties.SetName(chain, "Video effect chain");
        chain.SelectionChanged += (_, _) => { if (!refreshing) { selectedId = (chain.SelectedItem as ChainEntry)?.Id; RefreshParameters(); } };
        left.Children.Add(chain);

        // 2.2 Right: Effect Parameters & Preview
        var right = new Grid { Margin = new Thickness(8, 6, 10, 6) }; Grid.SetColumn(right, 1); body.Children.Add(right);
        right.RowDefinitions.Add(new() { Height = GridLength.Auto }); // Heading
        right.RowDefinitions.Add(new() { Height = GridLength.Auto }); // Controls bar (Enabled / Reset / Animate)
        right.RowDefinitions.Add(new() { Height = GridLength.Auto }); // Parameter controls scroll
        right.RowDefinitions.Add(new() { Height = GridLength.Auto }); // Keyframe timeline lane
        right.RowDefinitions.Add(new()); // Preview

        heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 3, 0, 8); right.Children.Add(heading);

        // Action row: Enable checkbox, Reset button, and Animate toggle
        var actionRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        actionRow.Children.Add(enabled);
        AutomationProperties.SetAutomationId(enabled, "VideoEffectEnabled");
        enabled.Checked += (_, _) => ApplyControls(); enabled.Unchecked += (_, _) => ApplyControls();

        reset = MakeButton("Reset effect", "Reset selected effect", () => Edit(() =>
        {
            if (SelectedEffect is not { } effect) return;
            var defaults = VideoEffectCatalog.Get(effect.Kind).Parameters.ToDictionary(parameter => parameter.Key, parameter => parameter.DefaultValue);
            editor.UpdateScopedVideoEffectParameters(scope, targetId, effect.Id, defaults, effect.IsEnabled);
        }), "ResetVideoEffect");
        reset.Margin = new Thickness(12, 0, 0, 0);
        actionRow.Children.Add(reset);

        animateToggle.Click += (_, _) =>
        {
            if (SelectedEffect is { } effect)
            {
                bool willEnable = animateToggle.IsChecked == true;
                Edit(() => editor.SetScopedVideoEffectAnimationEnabled(scope, targetId, effect.Id, willEnable));
            }
        };
        DockPanel.SetDock(animateToggle, Dock.Right);
        actionRow.Children.Add(animateToggle);

        Grid.SetRow(actionRow, 1); right.Children.Add(actionRow);

        // Parameter host
        var fieldHost = new StackPanel(); Grid.SetRow(fieldHost, 2); right.Children.Add(fieldHost);
        empty.Text = "Choose a built-in effect and click Add.\nEffects run from top to bottom."; empty.Margin = new Thickness(0, 4, 0, 10); empty.Foreground = (Brush)FindResource("MutedBrush"); fieldHost.Children.Add(empty);
        fieldHost.Children.Add(new ScrollViewer { Content = parameters, MaxHeight = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        // Keyframe Animation Lane Host
        var lanePanel = new Grid();
        lanePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Transport
        lanePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Canvas Lane

        var transport = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 4, 2) };
        var prevBtn = MakeButton("◄|", "Previous keyframe", () =>
        {
            var prev = SelectedEffect?.Animation?.Keyframes.Where(k => k.Frame < currentKeyframeCursor).OrderByDescending(k => k.Frame).FirstOrDefault();
            if (prev != null) SeekToKeyframe(prev.Frame);
        }, "PrevKeyframe");
        prevBtn.Padding = new Thickness(4, 1, 4, 1);
        prevBtn.FontSize = 10;

        var addKeyBtn = MakeButton("◆+", "Add or update keyframe at current frame", () =>
        {
            if (ReadControls(out var values) && SelectedEffect is { } fx)
            {
                Edit(() => editor.SetScopedVideoEffectKeyframe(scope, targetId, fx.Id, currentKeyframeCursor, values));
            }
        }, "AddKeyframe");
        addKeyBtn.Padding = new Thickness(4, 1, 4, 1);
        addKeyBtn.FontSize = 10;

        var delKeyBtn = MakeButton("◆-", "Delete keyframe at current frame", () =>
        {
            if (SelectedEffect is { } fx)
            {
                Edit(() => editor.RemoveScopedVideoEffectKeyframe(scope, targetId, fx.Id, currentKeyframeCursor));
            }
        }, "DelKeyframe");
        delKeyBtn.Padding = new Thickness(4, 1, 4, 1);
        delKeyBtn.FontSize = 10;

        var nextBtn = MakeButton("|►", "Next keyframe", () =>
        {
            var next = SelectedEffect?.Animation?.Keyframes.Where(k => k.Frame > currentKeyframeCursor).OrderBy(k => k.Frame).FirstOrDefault();
            if (next != null) SeekToKeyframe(next.Frame);
        }, "NextKeyframe");
        nextBtn.Padding = new Thickness(4, 1, 4, 1);
        nextBtn.FontSize = 10;

        transport.Children.Add(prevBtn);
        transport.Children.Add(addKeyBtn);
        transport.Children.Add(delKeyBtn);
        transport.Children.Add(nextBtn);
        transport.Children.Add(keyframeReadout);

        Grid.SetRow(transport, 0);
        lanePanel.Children.Add(transport);

        keyframeLane.Seek += SeekToKeyframe;
        keyframeLane.MoveKeyframe += (oldFrame, newFrame) =>
        {
            if (SelectedEffect is { } fx)
            {
                Edit(() => editor.MoveScopedVideoEffectKeyframe(scope, targetId, fx.Id, oldFrame, newFrame));
                SeekToKeyframe(newFrame);
            }
        };
        keyframeLane.InterpolationChanged += (frame, curve) =>
        {
            if (SelectedEffect is { } fx)
            {
                Edit(() => editor.SetScopedVideoEffectKeyframeInterpolation(scope, targetId, fx.Id, frame, curve));
            }
        };
        keyframeLane.DeleteKeyframeRequested += frame =>
        {
            if (SelectedEffect is { } fx)
            {
                Edit(() => editor.RemoveScopedVideoEffectKeyframe(scope, targetId, fx.Id, frame));
            }
        };
        keyframeLane.AddKeyframeRequested += frame =>
        {
            if (ReadControls(out var vals) && SelectedEffect is { } fx)
            {
                Edit(() => editor.SetScopedVideoEffectKeyframe(scope, targetId, fx.Id, frame, vals));
            }
        };

        Grid.SetRow(keyframeLane, 1);
        lanePanel.Children.Add(keyframeLane);
        keyframeLaneHost.Child = lanePanel;

        Grid.SetRow(keyframeLaneHost, 3);
        right.Children.Add(keyframeLaneHost);

        // Preview Host
        var previewHost = new Border { Background = Brushes.Black, Margin = new Thickness(0, 8, 0, 0), Child = preview };
        AutomationProperties.SetName(preview, "Video effect preview"); Grid.SetRow(previewHost, 4); right.Children.Add(previewHost);

        // 3. Bottom Bar
        var bottom = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 0, 8, 0) }; Grid.SetRow(bottom, 3); root.Children.Add(bottom);
        var close = MakeButton("Close", "Close video FX", Close, "CloseVideoEffects"); DockPanel.SetDock(close, Dock.Right); bottom.Children.Add(close);
        status.TextTrimming = TextTrimming.CharacterEllipsis; bottom.Children.Add(status);

        Content = root;
        SourceInitialized += (_, _) => { var dark = 1; DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); };
        editor.Changed += EditorChanged;
        Closed += (_, _) => { closed = true; previewRevision++; editor.Changed -= EditorChanged; };
        Loaded += (_, _) => Refresh();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (choiceControls.FirstOrDefault(control => control.Choice.IsDropDownOpen).Choice is { } openChoice)
                { openChoice.IsDropDownOpen = false; e.Handled = true; return; }
                if (adjusting)
                {
                    adjusting = false; Mouse.Capture(null); RefreshParameters(); RequestPreview();
                }
                else Close();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.Z or Key.Y && Keyboard.FocusedElement is not TextBox)
            { Edit(() => { if (e.Key == Key.Z) editor.Undo(); else editor.Redo(); }); e.Handled = true; }
        };
    }

    private sealed record ChainEntry(Guid Id, string Name);
    private TimelineClip? EventClip => scope == EffectScope.Event ? editor.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == targetId) : null;
    private TimelineTrack? TargetTrack => scope == EffectScope.Track ? editor.Project.Tracks.FirstOrDefault(t => t.Id == targetId) : null;
    private MediaAsset? TargetMedia => scope == EffectScope.Media ? editor.Project.Media.FirstOrDefault(m => m.Id == targetId) : null;

    private List<VideoEffect> TargetEffects => scope switch
    {
        EffectScope.Event => EventClip?.VideoEffects ?? [],
        EffectScope.Track => TargetTrack?.VideoEffects ?? [],
        EffectScope.Media => TargetMedia?.VideoEffects ?? [],
        EffectScope.Output => editor.Project.VideoOutputEffects,
        _ => []
    };

    private string TargetName => scope switch
    {
        EffectScope.Event => EventClip?.Name ?? "",
        EffectScope.Track => TargetTrack is { } track ? $"Track {editor.Project.Tracks.IndexOf(track) + 1}: {track.Name}" : "",
        EffectScope.Media => TargetMedia?.Name ?? "",
        EffectScope.Output => "Master Video Bus",
        _ => ""
    };

    private string HeaderText => scope switch
    {
        EffectScope.Event => "Video Event FX:  " + TargetName,
        EffectScope.Track => "Video Track FX:  " + TargetName,
        EffectScope.Media => "Media FX:  " + TargetName,
        EffectScope.Output => "Video Output FX",
        _ => "Video FX"
    };

    private string WindowTitle => scope switch
    {
        EffectScope.Event => "Video event FX",
        EffectScope.Track => "Video track FX",
        EffectScope.Media => "Media FX",
        EffectScope.Output => "Video output FX",
        _ => "Video FX"
    };

    private VideoEffect? SelectedEffect => TargetEffects.SingleOrDefault(effect => effect.Id == selectedId);
    private static string EffectName(VideoEffectKind kind) => VideoEffectCatalog.Get(kind).Name;

    private void SeekToKeyframe(long frame)
    {
        currentKeyframeCursor = frame;
        keyframeLane.Position = frame;
        keyframeReadout.Text = $"Frame: {frame} / {keyframeLane.Duration}";
        keyframeLane.InvalidateVisual();

        if (SelectedEffect?.Animation is { IsEnabled: true })
        {
            var evaluated = SelectedEffect.EvaluateAt(frame);
            refreshing = true;
            try
            {
                for (var i = 0; i < controls.Count; i++)
                {
                    var val = evaluated.GetParameter(controls[i].Definition.Key);
                    controls[i].Slider.Value = val;
                    controls[i].Input.Text = val.ToString("0.###", CultureInfo.InvariantCulture);
                }
                foreach (var control in choiceControls)
                {
                    control.Choice.SelectedIndex = (int)evaluated.GetParameter(control.Definition.Key);
                }
            }
            finally { refreshing = false; }
        }
        RequestPreview();
    }

    private void OpenChooser()
    {
        var chooser = new VideoEffectChooserWindow(TargetEffects) { Owner = this };
        if (chooser.ShowDialog() == true && chooser.Result is { } result)
            Edit(() => editor.SetScopedVideoEffects(scope, targetId, result));
    }

    private static Button MakeButton(string text, string name, Action action, string id)
    {
        var button = new Button { Content = text, ToolTip = name, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(2), MinHeight = 24, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, name); AutomationProperties.SetAutomationId(button, id); button.Click += (_, _) => action(); return button;
    }

    private void Edit(Action action)
    {
        try { action(); status.Text = ""; Refresh(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or OverflowException) { status.Text = ex.Message; Refresh(); }
    }

    private void EditorChanged(object? sender, EventArgs e)
    {
        var exists = scope switch
        {
            EffectScope.Event => EventClip is not null,
            EffectScope.Track => TargetTrack is not null,
            EffectScope.Media => TargetMedia is not null,
            EffectScope.Output => true,
            _ => false
        };
        if (!exists) { Close(); return; }
        Refresh();
    }

    private void Refresh()
    {
        if (closed) return;
        refreshing = true;
        try
        {
            if (!TargetEffects.Any(effect => effect.Id == selectedId)) selectedId = TargetEffects.FirstOrDefault()?.Id;
            chain.ItemsSource = TargetEffects.Select((effect, index) => new ChainEntry(effect.Id, $"{index + 1}. {EffectName(effect.Kind)}{(effect.IsEnabled ? "" : " (bypassed)")}{(effect.Animation is { IsEnabled: true } ? " [Anim]" : "")}")).ToArray();
            chain.SelectedItem = chain.Items.Cast<ChainEntry>().SingleOrDefault(item => item.Id == selectedId);
            undo.IsEnabled = editor.CanUndo; redo.IsEnabled = editor.CanRedo;
        }
        finally { refreshing = false; }
        RefreshParameters(); RequestPreview();
    }

    private void RefreshParameters()
    {
        refreshing = true;
        try
        {
            var effect = SelectedEffect; var index = TargetEffects.FindIndex(item => item.Id == selectedId);
            UpdateLutDetails();
            heading.Text = effect is null ? "No effects in chain" : EffectName(effect.Kind);
            enabled.Visibility = reset.Visibility = animateToggle.Visibility = effect is null ? Visibility.Collapsed : Visibility.Visible;
            empty.Visibility = effect is null ? Visibility.Visible : Visibility.Collapsed;
            enabled.IsChecked = effect?.IsEnabled == true;
            remove.IsEnabled = effect is not null; up.IsEnabled = index > 0; down.IsEnabled = index >= 0 && index < TargetEffects.Count - 1;

            var anim = effect?.Animation;
            animateToggle.IsChecked = anim?.IsEnabled == true;
            keyframeLaneHost.Visibility = anim?.IsEnabled == true ? Visibility.Visible : Visibility.Collapsed;

            long duration = scope == EffectScope.Event ? (EventClip?.DurationFrames ?? 1) : (editor.Project.DurationFrames > 0 ? editor.Project.DurationFrames : 300);
            keyframeLane.Duration = Math.Max(1, duration);
            keyframeLane.Position = currentKeyframeCursor;
            keyframeLane.Keyframes = anim?.Keyframes ?? [];
            keyframeReadout.Text = $"Frame: {currentKeyframeCursor} / {keyframeLane.Duration}";

            // Reuse controls during edits so keyboard focus and a slider's capture survive refresh.
            if (effect is not null && parameterEffectId == effect.Id && parameterKind == effect.Kind)
            {
                var evaluated = effect.Animation is { IsEnabled: true } ? effect.EvaluateAt(currentKeyframeCursor) : effect;
                for (var i = 0; i < controls.Count; i++)
                {
                    var value = evaluated.GetParameter(controls[i].Definition.Key);
                    controls[i].Slider.Value = value; controls[i].Input.Text = value.ToString("0.###", CultureInfo.InvariantCulture);
                }
                foreach (var control in choiceControls) control.Choice.SelectedIndex = (int)evaluated.GetParameter(control.Definition.Key);
                return;
            }

            parameters.Children.Clear(); controls.Clear(); choiceControls.Clear(); parameterEffectId = effect?.Id; parameterKind = effect?.Kind;
            lutDetails = null; clearLut = null;
            if (effect is null) return;
            if (effect.Kind == VideoEffectKind.Lut3D) AddLutControls();

            var evalSource = effect.Animation is { IsEnabled: true } ? effect.EvaluateAt(currentKeyframeCursor) : effect;
            foreach (var parameter in VideoEffectCatalog.Get(effect.Kind).Parameters) AddParameter(parameter, evalSource.GetParameter(parameter.Key));
        }
        finally { refreshing = false; }
    }

    private void AddParameter(VideoEffectParameterDefinition definition, double value)
    {
        if (definition.Choices is { } choices)
        { AddChoiceParameter(definition, choices, value); return; }
        var label = definition.Name; var minimum = definition.Minimum; var maximum = definition.Maximum;
        var id = definition.Key switch { "Amount" => "VideoEffectAmount", "SecondaryAmount" => "VideoEffectSecondary", _ => "VideoEffect_" + definition.Key };
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(130) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(72) });
        row.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = label });
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, SmallChange = definition.Step, LargeChange = definition.Step * 10, Margin = new Thickness(6, 0, 8, 0) };
        var input = new TextBox { Text = value.ToString("0.###", CultureInfo.InvariantCulture) };
        AutomationProperties.SetName(slider, label); AutomationProperties.SetName(input, label + " value"); AutomationProperties.SetAutomationId(input, id);
        Grid.SetColumn(slider, 1); row.Children.Add(slider); Grid.SetColumn(input, 2); row.Children.Add(input); parameters.Children.Add(row); controls.Add((definition, slider, input));
        input.GotKeyboardFocus += (_, _) => input.SelectAll();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyControls(); e.Handled = true; } };
        input.LostKeyboardFocus += (_, _) => { if (!closed && !refreshing) ApplyControls(); };
        slider.PreviewMouseLeftButtonDown += (_, _) => adjusting = true;
        slider.ValueChanged += (_, _) =>
        {
            if (refreshing) return;
            input.Text = slider.Value.ToString("0.###", CultureInfo.InvariantCulture);
            if (adjusting) RequestPreview(); else ApplyControls();
        };
        // The drag previews a clone; one release creates one undoable edit.
        void Finish() { if (!adjusting) return; adjusting = false; ApplyControls(); }
        slider.PreviewMouseLeftButtonUp += (_, _) => Finish();
        slider.LostMouseCapture += (_, _) => Finish();
    }

    private void AddChoiceParameter(VideoEffectParameterDefinition definition, IReadOnlyList<string> choices, double value)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(130) }); row.ColumnDefinitions.Add(new());
        row.Children.Add(new TextBlock { Text = definition.Name });
        var choice = new ComboBox { ItemsSource = choices, SelectedIndex = (int)value, Margin = new Thickness(6, 0, 0, 0), MinHeight = 23 };
        AutomationProperties.SetName(choice, definition.Name); AutomationProperties.SetAutomationId(choice, "VideoEffect_" + definition.Key);
        Grid.SetColumn(choice, 1); row.Children.Add(choice); parameters.Children.Add(row); choiceControls.Add((definition, choice));
        // Keep this instance through refresh so a selection edit retains keyboard focus.
        choice.SelectionChanged += (_, _) => { if (!refreshing) ApplyControls(); };
    }

    private bool ReadControls(out Dictionary<string, double> values)
    {
        values = [];
        for (var i = 0; i < controls.Count; i++)
        {
            var control = controls[i];
            if (!double.TryParse(control.Input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < control.Slider.Minimum || value > control.Slider.Maximum)
            { status.Text = $"Enter a value from {control.Slider.Minimum} to {control.Slider.Maximum}."; return false; }
            values.Add(control.Definition.Key, value);
        }
        foreach (var control in choiceControls)
        {
            var index = control.Choice.SelectedIndex;
            if (index < 0 || index >= control.Definition.Choices!.Count)
            { status.Text = "Choose a value for " + control.Definition.Name + "."; return false; }
            values.Add(control.Definition.Key, index);
        }
        return true;
    }

    private void ApplyControls()
    {
        if (refreshing || SelectedEffect is not { } effect || !ReadControls(out var values)) return;

        if (effect.Animation is { IsEnabled: true })
        {
            double? amt = values.TryGetValue("Amount", out var a) ? a : (double?)null;
            double? sec = values.TryGetValue("SecondaryAmount", out var s) ? s : (double?)null;
            Edit(() => editor.SetScopedVideoEffectKeyframe(scope, targetId, effect.Id, currentKeyframeCursor, values, amt, sec));
        }
        else
        {
            Edit(() => editor.UpdateScopedVideoEffectParameters(scope, targetId, effect.Id, values, enabled.IsChecked == true));
        }
    }

    private void Move(int direction)
    {
        if (selectedId is not { } id) return;
        var index = TargetEffects.FindIndex(effect => effect.Id == id);
        Edit(() => editor.MoveScopedVideoEffect(scope, targetId, id, index + direction));
    }

    private async void RequestPreview()
    {
        previewRevision++; previewPending = true;
        if (!IsLoaded || previewRunning) return;
        previewRunning = true;
        try
        {
            while (previewPending && !closed)
            {
                previewPending = false; var request = previewRevision;
                var project = editor.Project.DeepClone();
                if (adjusting && selectedId is { } id && ReadControls(out var values))
                {
                    var effects = scope switch
                    {
                        EffectScope.Event => project.Tracks.SelectMany(track => track.Clips).FirstOrDefault(item => item.Id == targetId)?.VideoEffects,
                        EffectScope.Track => project.Tracks.FirstOrDefault(track => track.Id == targetId)?.VideoEffects,
                        EffectScope.Media => project.Media.FirstOrDefault(media => media.Id == targetId)?.VideoEffects,
                        EffectScope.Output => project.VideoOutputEffects,
                        _ => null
                    };
                    if (effects?.SingleOrDefault(item => item.Id == id) is { } effect)
                    {
                        foreach (var (key, value) in values) effect.SetParameter(key, value);
                    }
                }

                long frame = vm.Position;
                if (SelectedEffect?.Animation is { IsEnabled: true })
                {
                    frame = scope == EffectScope.Event && EventClip is { } eventClip
                        ? eventClip.StartFrame + currentKeyframeCursor
                        : currentKeyframeCursor;
                }
                else if (scope == EffectScope.Event && EventClip is { } eventClip2)
                {
                    frame = Math.Clamp(vm.Position, eventClip2.StartFrame, Math.Max(eventClip2.StartFrame, eventClip2.EndFrame - 1));
                }

                var scale = Math.Min(480.0 / project.Width, 270.0 / project.Height);
                var width = Math.Max(1, (int)Math.Round(project.Width * scale)); var height = Math.Max(1, (int)Math.Round(project.Height * scale));
                var pixels = await Task.Run(() => vm.Renderer.RenderFrame(project, frame, width, height));
                if (closed || request != previewRevision) continue;
                var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); preview.Source = image;
            }
        }
        catch (Exception ex) { if (!closed) status.Text = "Preview: " + ex.Message; }
        finally { previewRunning = false; }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
