using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenRend.Core;
using OpenRend.Timeline;

namespace OpenRend.App;

// Keep the effect order visible while adjusting a selected effect and its preview.
public sealed class VideoEffectsWindow : Window
{
    private static readonly VideoEffectKind[] BuiltInEffects = Enum.GetValues<VideoEffectKind>();
    private readonly EditorViewModel vm;
    private readonly TimelineEditor editor;
    private readonly Guid clipId;
    private readonly ListBox chain = new();
    private readonly ComboBox available = new();
    private readonly CheckBox enabled = new() { Content = "Enable selected effect" };
    private readonly TextBlock heading = new(), empty = new(), status = new();
    private readonly StackPanel parameters = new();
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly Button remove, up, down, reset, undo, redo;
    private readonly List<(VideoEffectParameterDefinition Definition, Slider Slider, TextBox Input)> controls = [];
    private Guid? selectedId;
    private Guid? parameterEffectId;
    private VideoEffectKind? parameterKind;
    private bool refreshing, closed, previewRunning, previewPending, adjusting;
    private long previewRevision;

    public VideoEffectsWindow(EditorViewModel viewModel, Guid selectedClip)
    {
        vm = viewModel; editor = vm.Editor; clipId = selectedClip;
        Style = (Style)FindResource(typeof(Window));
        Title = "Video event FX"; Width = 820; Height = 560; MinWidth = 700; MinHeight = 490;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Background = (Brush)FindResource("PanelBrush") };
        root.RowDefinitions.Add(new() { Height = new GridLength(30) });
        root.RowDefinitions.Add(new() { Height = new GridLength(36) });
        root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = new GridLength(36) });
        root.Children.Add(new TextBlock { Text = "Video Event FX:  " + EventClip.Name, Margin = new Thickness(10, 0, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        var toolbar = new DockPanel { LastChildFill = false, Background = (Brush)FindResource("HeaderBrush"), Margin = new Thickness(0, 0, 0, 1) };
        Grid.SetRow(toolbar, 1); root.Children.Add(toolbar);
        toolbar.Children.Add(new TextBlock { Text = "Built-in:", Margin = new Thickness(10, 0, 6, 0) });
        available.Width = 185; available.ItemsSource = BuiltInEffects.Select(EffectName).ToArray(); available.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(available, "AvailableVideoEffect"); AutomationProperties.SetName(available, "Available video effect"); toolbar.Children.Add(available);
        toolbar.Children.Add(MakeButton("Add", "Add video effect", () => Edit(() =>
        {
            if (available.SelectedIndex >= 0) selectedId = editor.AddVideoEffect(clipId, BuiltInEffects[available.SelectedIndex]).Id;
        }), "AddVideoEffect"));
        toolbar.Children.Add(MakeButton("Plug-in chooser…", "Open video FX plug-in chooser", OpenChooser, "OpenFxChooser"));
        undo = MakeButton("Undo", "Undo effect edit", () => Edit(() => editor.Undo()), "UndoVideoEffect");
        redo = MakeButton("Redo", "Redo effect edit", () => Edit(() => editor.Redo()), "RedoVideoEffect");
        DockPanel.SetDock(redo, Dock.Right); toolbar.Children.Add(redo); DockPanel.SetDock(undo, Dock.Right); toolbar.Children.Add(undo);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new GridLength(230) }); body.ColumnDefinitions.Add(new());
        Grid.SetRow(body, 2); root.Children.Add(body);
        var left = new DockPanel { Background = (Brush)FindResource("SurfaceBrush"), Margin = new Thickness(6) };
        body.Children.Add(left);
        var chainLabel = new TextBlock { Text = "Effect chain", Margin = new Thickness(5, 5, 5, 8) }; DockPanel.SetDock(chainLabel, Dock.Top); left.Children.Add(chainLabel);
        var chainTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 5, 2, 2) };
        up = MakeButton("↑", "Move effect earlier", () => Move(-1), "MoveVideoEffectUp");
        down = MakeButton("↓", "Move effect later", () => Move(1), "MoveVideoEffectDown");
        remove = MakeButton("Remove", "Remove video effect", () => Edit(() => { if (selectedId is { } id) editor.RemoveVideoEffect(clipId, id); }), "RemoveVideoEffect");
        chainTools.Children.Add(up); chainTools.Children.Add(down); chainTools.Children.Add(remove); DockPanel.SetDock(chainTools, Dock.Bottom); left.Children.Add(chainTools);
        chain.DisplayMemberPath = nameof(ChainEntry.Name); AutomationProperties.SetAutomationId(chain, "VideoEffectChain"); AutomationProperties.SetName(chain, "Video effect chain");
        chain.SelectionChanged += (_, _) => { if (!refreshing) { selectedId = (chain.SelectedItem as ChainEntry)?.Id; RefreshParameters(); } };
        left.Children.Add(chain);
        var right = new Grid { Margin = new Thickness(8, 6, 10, 6) }; Grid.SetColumn(right, 1); body.Children.Add(right);
        right.RowDefinitions.Add(new() { Height = GridLength.Auto }); right.RowDefinitions.Add(new() { Height = GridLength.Auto }); right.RowDefinitions.Add(new() { Height = GridLength.Auto }); right.RowDefinitions.Add(new());
        heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 3, 0, 8); right.Children.Add(heading);
        enabled.Margin = new Thickness(0, 0, 0, 8); Grid.SetRow(enabled, 1); right.Children.Add(enabled);
        AutomationProperties.SetAutomationId(enabled, "VideoEffectEnabled");
        enabled.Checked += (_, _) => ApplyControls(); enabled.Unchecked += (_, _) => ApplyControls();
        var fieldHost = new StackPanel(); Grid.SetRow(fieldHost, 2); right.Children.Add(fieldHost);
        empty.Text = "Choose a built-in effect and click Add.\nEffects run from top to bottom."; empty.Margin = new Thickness(0, 4, 0, 10); empty.Foreground = (Brush)FindResource("MutedBrush"); fieldHost.Children.Add(empty);
        fieldHost.Children.Add(new ScrollViewer { Content = parameters, MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        reset = MakeButton("Reset effect", "Reset selected effect", () => Edit(() =>
        {
            if (SelectedEffect is not { } effect) return;
            var defaults = VideoEffectCatalog.Get(effect.Kind).Parameters.ToDictionary(parameter => parameter.Key, parameter => parameter.DefaultValue);
            editor.UpdateVideoEffectParameters(clipId, effect.Id, defaults, effect.IsEnabled);
        }), "ResetVideoEffect");
        reset.HorizontalAlignment = HorizontalAlignment.Left; fieldHost.Children.Add(reset);
        var previewHost = new Border { Background = Brushes.Black, Margin = new Thickness(0, 10, 0, 0), Child = preview };
        AutomationProperties.SetName(preview, "Video effect preview"); Grid.SetRow(previewHost, 3); right.Children.Add(previewHost);
        var bottom = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 0, 8, 0) }; Grid.SetRow(bottom, 3); root.Children.Add(bottom);
        var close = MakeButton("Close", "Close video event FX", Close, "CloseVideoEffects"); DockPanel.SetDock(close, Dock.Right); bottom.Children.Add(close);
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
        Refresh();
    }

    private sealed record ChainEntry(Guid Id, string Name);
    private TimelineClip EventClip => editor.Project.Tracks.SelectMany(t => t.Clips).Single(c => c.Id == clipId);
    private VideoEffect? SelectedEffect => EventClip.VideoEffects.SingleOrDefault(effect => effect.Id == selectedId);
    private static string EffectName(VideoEffectKind kind) => VideoEffectCatalog.Get(kind).Name;
    private void OpenChooser()
    {
        var chooser = new VideoEffectChooserWindow(EventClip.VideoEffects) { Owner = this };
        if (chooser.ShowDialog() == true && chooser.Result is { } result)
            Edit(() => editor.SetVideoEffects(clipId, result));
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
        if (editor.Project.Tracks.SelectMany(t => t.Clips).All(c => c.Id != clipId)) { Close(); return; }
        Refresh();
    }
    private void Refresh()
    {
        if (closed) return;
        refreshing = true;
        try
        {
            if (!EventClip.VideoEffects.Any(effect => effect.Id == selectedId)) selectedId = EventClip.VideoEffects.FirstOrDefault()?.Id;
            chain.ItemsSource = EventClip.VideoEffects.Select((effect, index) => new ChainEntry(effect.Id, $"{index + 1}. {EffectName(effect.Kind)}{(effect.IsEnabled ? "" : " (bypassed)")}")).ToArray();
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
            var effect = SelectedEffect; var index = EventClip.VideoEffects.FindIndex(item => item.Id == selectedId);
            heading.Text = effect is null ? "No effects in chain" : EffectName(effect.Kind);
            enabled.Visibility = reset.Visibility = effect is null ? Visibility.Collapsed : Visibility.Visible;
            empty.Visibility = effect is null ? Visibility.Visible : Visibility.Collapsed;
            enabled.IsChecked = effect?.IsEnabled == true;
            remove.IsEnabled = effect is not null; up.IsEnabled = index > 0; down.IsEnabled = index >= 0 && index < EventClip.VideoEffects.Count - 1;
            // Reuse controls during edits so keyboard focus and a slider's capture survive refresh.
            if (effect is not null && parameterEffectId == effect.Id && parameterKind == effect.Kind)
            {
                for (var i = 0; i < controls.Count; i++)
                {
                    var value = effect.GetParameter(controls[i].Definition.Key);
                    controls[i].Slider.Value = value; controls[i].Input.Text = value.ToString("0.###", CultureInfo.InvariantCulture);
                }
                return;
            }
            parameters.Children.Clear(); controls.Clear(); parameterEffectId = effect?.Id; parameterKind = effect?.Kind;
            if (effect is null) return;
            foreach (var parameter in VideoEffectCatalog.Get(effect.Kind).Parameters) AddParameter(parameter, effect.GetParameter(parameter.Key));
        }
        finally { refreshing = false; }
    }
    private void AddParameter(VideoEffectParameterDefinition definition, double value)
    {
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
        return true;
    }
    private void ApplyControls()
    {
        if (refreshing || SelectedEffect is not { } effect || !ReadControls(out var values)) return;
        Edit(() => editor.UpdateVideoEffectParameters(clipId, effect.Id, values, enabled.IsChecked == true));
    }
    private void Move(int direction)
    {
        if (selectedId is not { } id) return;
        var index = EventClip.VideoEffects.FindIndex(effect => effect.Id == id);
        Edit(() => editor.MoveVideoEffect(clipId, id, index + direction));
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
                var project = editor.Project.DeepClone(); var clip = project.Tracks.SelectMany(track => track.Clips).Single(item => item.Id == clipId);
                if (adjusting && selectedId is { } id && ReadControls(out var values))
                {
                    var effect = clip.VideoEffects.Single(item => item.Id == id);
                    foreach (var (key, value) in values) effect.SetParameter(key, value);
                }
                var frame = Math.Clamp(vm.Position, clip.StartFrame, clip.EndFrame - 1);
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
