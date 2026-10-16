using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenRend.Core;
using OpenRend.Timeline;

namespace OpenRend.App;

public sealed class PanCropWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly TimelineEditor editor;
    private readonly Guid clipId;
    private readonly Action<long> seek;
    private readonly Dictionary<string, TextBox> fields = [];
    private readonly CropWorkspace workspace = new();
    private readonly CropKeyframeLane lane = new();
    private readonly TextBox frameField;
    private readonly TextBox keyframeField;
    private readonly ComboBox interpolation;
    private readonly ComboBox presets;
    private readonly CheckBox lockAspect;
    private readonly TextBlock status;
    private long localFrame;
    private bool refreshing;
    private bool closed;
    private long sourceRequest;
    private bool sourceRunning;
    private bool sourcePending;
    private (string Path, long Frame, int Width, int Height, Rational Rate)? requestedSource;
    private CropState state;

    public PanCropWindow(EditorViewModel viewModel, Guid selectedClip, Action<long> seekPreview)
    {
        vm = viewModel; editor = vm.Editor; clipId = selectedClip; seek = seekPreview;
        Style = (Style)FindResource(typeof(Window));
        Title = "Video event Pan/Crop"; Width = 902; Height = 632;
        MinWidth = 700; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var clip = EventClip;
        localFrame = Math.Clamp(vm.Position - clip.StartFrame, 0, clip.DurationFrames - 1);
        var root = new Grid { Background = (Brush)FindResource("PanelBrush") };
        root.RowDefinitions.Add(new() { Height = new GridLength(28) });
        root.RowDefinitions.Add(new() { Height = new GridLength(30) });
        root.RowDefinitions.Add(new() { Height = new GridLength(28) });
        root.RowDefinitions.Add(new());
        root.RowDefinitions.Add(new() { Height = new GridLength(104) });
        var heading = new TextBlock { Text = "Event Pan/Crop:  " + clip.Name, Margin = new Thickness(10, 0, 0, 0) };
        root.Children.Add(heading);
        var top = new DockPanel { Background = (Brush)FindResource("HeaderBrush"), LastChildFill = false };
        Grid.SetRow(top, 1); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Pan/Crop", Background = (Brush)FindResource("SelectionBrush"), Padding = new Thickness(8, 4, 8, 4) });
        var reset = Button("Reset", "Reset Pan/Crop", () => Edit(() => editor.ResetCrop(clipId)));
        DockPanel.SetDock(reset, Dock.Right); top.Children.Add(reset);
        var undo = Button("Undo", "Undo crop edit", () => Edit(() => editor.Undo()));
        DockPanel.SetDock(undo, Dock.Right); top.Children.Add(undo);
        var redo = Button("Redo", "Redo crop edit", () => Edit(() => editor.Redo()));
        DockPanel.SetDock(redo, Dock.Right); top.Children.Add(redo);
        var presetRow = new DockPanel { LastChildFill = true, Margin = new Thickness(3, 1, 3, 1) };
        Grid.SetRow(presetRow, 2); root.Children.Add(presetRow);
        presetRow.Children.Add(new TextBlock { Text = "Preset:", Width = 72 });
        presets = new ComboBox { ItemsSource = new[] { "Full frame", "Half size", "Square center" } };
        AutomationProperties.SetName(presets, "Crop preset");
        presets.SelectionChanged += (_, _) =>
        {
            if (refreshing || presets.SelectedIndex < 0) return;
            var identity = CropState.Identity(Source.Width, Source.Height);
            var preset = presets.SelectedIndex switch { 1 => identity with { Width = identity.Width / 2, Height = identity.Height / 2 }, 2 => identity with { Width = Math.Min(identity.Width, identity.Height), Height = Math.Min(identity.Width, identity.Height) }, _ => identity };
            Edit(() => editor.SetCropKeyframe(clipId, localFrame, preset, CurrentInterpolation));
        };
        presetRow.Children.Add(presets);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new GridLength(244) }); body.ColumnDefinitions.Add(new());
        Grid.SetRow(body, 3); root.Children.Add(body);
        var properties = new StackPanel { Margin = new Thickness(40, 2, 3, 0) };
        var scroll = new ScrollViewer { Content = properties, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        body.Children.Add(scroll);
        void Section(string label) => properties.Children.Add(new TextBlock { Text = label, Background = new SolidColorBrush(Color.FromRgb(75, 75, 75)), Padding = new Thickness(4, 1, 0, 1), Margin = new Thickness(0, 0, 0, 2) });
        void Field(string key, string label)
        {
            var row = new Grid { Height = 20 }; row.ColumnDefinitions.Add(new() { Width = new GridLength(107) }); row.ColumnDefinitions.Add(new());
            row.Children.Add(new TextBlock { Text = label + ":", Margin = new Thickness(15, 0, 0, 0) });
            var input = new TextBox { MinHeight = 0, Margin = new Thickness(0), Padding = new Thickness(4, 0, 0, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
            AutomationProperties.SetName(input, label); AutomationProperties.SetAutomationId(input, "Crop" + key);
            input.GotKeyboardFocus += (_, _) => input.SelectAll();
            input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyField(key); e.Handled = true; } else if (e.Key == Key.Escape) { Refresh(); e.Handled = true; } };
            input.LostKeyboardFocus += (_, _) => { if (!closed) ApplyField(key); };
            Grid.SetColumn(input, 1); row.Children.Add(input); fields.Add(key, input); properties.Children.Add(row);
        }
        Section("Position"); Field("Width", "Width"); Field("Height", "Height"); Field("CenterX", "X center"); Field("CenterY", "Y center");
        Section("Rotation"); Field("RotationDegrees", "Angle"); Field("PivotX", "Rotation X center"); Field("PivotY", "Rotation Y center");
        Section("Keyframe interpolation");
        interpolation = new ComboBox { ItemsSource = Enum.GetValues<CropInterpolation>(), Margin = new Thickness(12, 2, 2, 4) };
        AutomationProperties.SetName(interpolation, "Keyframe interpolation");
        interpolation.SelectionChanged += (_, _) => { if (!refreshing && interpolation.SelectedItem is CropInterpolation value) Edit(() => editor.SetCropKeyframe(clipId, localFrame, state, value)); };
        properties.Children.Add(interpolation);
        Section("Source");
        lockAspect = new CheckBox { Content = "Lock aspect ratio", IsChecked = true, Margin = new Thickness(12, 4, 0, 4) };
        properties.Children.Add(lockAspect);
        var toolbar = new StackPanel { Width = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        toolbar.Children.Add(Button("+", "Zoom crop workspace in", () => workspace.Zoom = Math.Min(8, workspace.Zoom * 1.25)));
        toolbar.Children.Add(Button("−", "Zoom crop workspace out", () => workspace.Zoom = Math.Max(.1, workspace.Zoom / 1.25)));
        toolbar.Children.Add(Button("Fit", "Fit source in crop workspace", () => workspace.Zoom = 1));
        body.Children.Add(toolbar);
        Grid.SetColumn(workspace, 1); body.Children.Add(workspace);
        workspace.PreviewChanged += value => { state = value; RefreshFields(); };
        workspace.Committed += value => Edit(() => editor.SetCropKeyframe(clipId, localFrame, value, CurrentInterpolation));
        workspace.Cancelled += Refresh;
        workspace.LockAspect = () => lockAspect.IsChecked == true;
        var bottom = new Grid(); bottom.RowDefinitions.Add(new() { Height = new GridLength(62) }); bottom.RowDefinitions.Add(new() { Height = new GridLength(42) });
        Grid.SetRow(bottom, 4); root.Children.Add(bottom);
        lane.Margin = new Thickness(145, 0, 8, 0); bottom.Children.Add(lane);
        lane.Seek += SetFrame;
        lane.MoveKeyframe += MoveKeyframe;
        bottom.Children.Add(new TextBlock { Text = "Position", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(7, 24, 0, 0), Width = 130, HorizontalAlignment = HorizontalAlignment.Left });
        var transport = new DockPanel { LastChildFill = false }; Grid.SetRow(transport, 1); bottom.Children.Add(transport);
        frameField = new TextBox { Width = 105, TextAlignment = TextAlignment.Right, Margin = new Thickness(3, 5, 6, 5) };
        AutomationProperties.SetName(frameField, "Crop frame"); AutomationProperties.SetAutomationId(frameField, "CropFrame");
        frameField.GotKeyboardFocus += (_, _) => frameField.SelectAll();
        frameField.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyFrame(); e.Handled = true; } };
        frameField.LostKeyboardFocus += (_, _) => { if (!closed) ApplyFrame(); };
        DockPanel.SetDock(frameField, Dock.Right); transport.Children.Add(frameField);
        var keyTime = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        keyTime.Children.Add(new TextBlock { Text = "Keyframe:", Margin = new Thickness(4, 0, 3, 0) });
        keyframeField = new TextBox { Width = 70, TextAlignment = TextAlignment.Right };
        AutomationProperties.SetName(keyframeField, "Crop keyframe frame"); AutomationProperties.SetAutomationId(keyframeField, "CropKeyframeFrame");
        keyframeField.GotKeyboardFocus += (_, _) => keyframeField.SelectAll();
        keyframeField.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyKeyframeTime(); e.Handled = true; } else if (e.Key == Key.Escape) { Refresh(); e.Handled = true; } };
        keyframeField.LostKeyboardFocus += (_, _) => { if (!closed) ApplyKeyframeTime(); };
        keyTime.Children.Add(keyframeField); DockPanel.SetDock(keyTime, Dock.Right); transport.Children.Add(keyTime);
        transport.Children.Add(Button("|◀", "Previous crop keyframe", () => SeekKey(-1)));
        transport.Children.Add(Button("◀", "Previous crop frame", () => SetFrame(localFrame - 1)));
        transport.Children.Add(Button("▶", "Next crop frame", () => SetFrame(localFrame + 1)));
        transport.Children.Add(Button("▶|", "Next crop keyframe", () => SeekKey(1)));
        transport.Children.Add(Button("◆+", "Add crop keyframe", () => Edit(() => editor.SetCropKeyframe(clipId, localFrame, state, CurrentInterpolation))));
        transport.Children.Add(Button("◆−", "Delete crop keyframe", () => Edit(() => editor.RemoveCropKeyframe(clipId, localFrame))));
        status = new TextBlock { Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        transport.Children.Add(status);
        Content = root;
        SourceInitialized += (_, _) => { var dark = 1; DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); };
        editor.Changed += EditorChanged;
        Closed += (_, _) => { closed = true; sourceRequest++; editor.Changed -= EditorChanged; };
        Loaded += (_, _) => { seek(checked(EventClip.StartFrame + localFrame)); Refresh(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && Keyboard.FocusedElement is not TextBox && !workspace.IsMouseCaptured && !lane.IsMouseCaptured) { Close(); e.Handled = true; }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.Z or Key.Y && Keyboard.FocusedElement is not TextBox)
            { Edit(() => { if (e.Key == Key.Z) editor.Undo(); else editor.Redo(); }); e.Handled = true; }
        };
    }

    private TimelineClip EventClip => editor.Project.Tracks.SelectMany(t => t.Clips).Single(c => c.Id == clipId);
    private MediaAsset Source => editor.Project.Media.Single(m => m.Id == EventClip.MediaId);
    private CropInterpolation CurrentInterpolation => interpolation.SelectedItem is CropInterpolation value ? value : CropInterpolation.Linear;
    private static Button Button(string text, string label, Action action)
    {
        var button = new Button { Content = text, ToolTip = label, Padding = new Thickness(6, 1, 6, 1), MinHeight = 23, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, label); button.Click += (_, _) => action(); return button;
    }
    private void Edit(Action action)
    {
        try { action(); status.Text = ""; Refresh(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.IO.InvalidDataException or OverflowException) { status.Text = ex.Message; Refresh(); }
    }
    private void EditorChanged(object? sender, EventArgs e)
    {
        if (editor.Project.Tracks.SelectMany(t => t.Clips).All(c => c.Id != clipId)) { Close(); return; }
        Refresh();
    }
    private void Refresh()
    {
        if (closed) return;
        localFrame = Math.Clamp(localFrame, 0, EventClip.DurationFrames - 1);
        state = TimelineEvaluator.CropAt(EventClip, Source, localFrame);
        refreshing = true;
        try
        {
            RefreshFields(); frameField.Text = localFrame.ToString(CultureInfo.InvariantCulture);
            var identity = CropState.Identity(Source.Width, Source.Height);
            presets.SelectedIndex = state == identity ? 0 : state == (identity with { Width = identity.Width / 2, Height = identity.Height / 2 }) ? 1
                : state == (identity with { Width = Math.Min(identity.Width, identity.Height), Height = Math.Min(identity.Width, identity.Height) }) ? 2 : -1;
            var current = checked(localFrame + EventClip.CropOffsetFrames);
            keyframeField.IsEnabled = EventClip.Crop?.Keyframes.Any(key => key.Frame == current) == true;
            keyframeField.Text = keyframeField.IsEnabled ? localFrame.ToString(CultureInfo.InvariantCulture) : "";
            interpolation.SelectedItem = EventClip.Crop?.Keyframes.LastOrDefault(key => key.Frame <= current)?.Interpolation ?? CropInterpolation.Linear;
            workspace.State = state; workspace.SourceWidth = Math.Max(1, Source.Width); workspace.SourceHeight = Math.Max(1, Source.Height); workspace.InvalidateVisual();
            lane.Duration = EventClip.DurationFrames; lane.Position = localFrame;
            lane.CanMove = EventClip.Crop is not null;
            lane.Rate = editor.Project.FrameRate;
            lane.Keys = EventClip.Crop?.Keyframes.Select(key => (Int128)key.Frame - EventClip.CropOffsetFrames).Where(frame => frame >= 0 && frame < EventClip.DurationFrames).Select(frame => (long)frame).ToArray() ?? [0];
            lane.InvalidateVisual();
        }
        finally { refreshing = false; }
        if (IsLoaded) _ = LoadSourceAsync();
    }
    private void RefreshFields()
    {
        var values = new[] { state.Width, state.Height, state.CenterX, state.CenterY, state.RotationDegrees, state.PivotX, state.PivotY };
        var index = 0;
        foreach (var field in fields.Values) field.Text = values[index++].ToString("0.######", CultureInfo.InvariantCulture);
    }
    private void ApplyField(string key)
    {
        if (refreshing) return;
        var displayed = key switch { "Width" => state.Width, "Height" => state.Height, "CenterX" => state.CenterX, "CenterY" => state.CenterY, "RotationDegrees" => state.RotationDegrees, "PivotX" => state.PivotX, _ => state.PivotY };
        if (fields[key].Text == displayed.ToString("0.######", CultureInfo.InvariantCulture)) return;
        if (!double.TryParse(fields[key].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
        { status.Text = "Enter a finite number."; Refresh(); return; }
        var next = key switch
        {
            "Width" => state with { Width = value, Height = lockAspect.IsChecked == true ? state.Height * value / state.Width : state.Height },
            "Height" => state with { Height = value, Width = lockAspect.IsChecked == true ? state.Width * value / state.Height : state.Width },
            "CenterX" => state with { CenterX = value }, "CenterY" => state with { CenterY = value },
            "RotationDegrees" => state with { RotationDegrees = value }, "PivotX" => state with { PivotX = value }, _ => state with { PivotY = value }
        };
        if (next != state) Edit(() => editor.SetCropKeyframe(clipId, localFrame, next, CurrentInterpolation));
    }
    private void ApplyFrame()
    {
        if (refreshing) return;
        if (long.TryParse(frameField.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame)) SetFrame(frame);
        else { status.Text = "Enter a whole project frame."; Refresh(); }
    }
    private void ApplyKeyframeTime()
    {
        if (refreshing || !keyframeField.IsEnabled) return;
        if (!long.TryParse(keyframeField.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame)) { status.Text = "Enter a whole project frame."; Refresh(); return; }
        if (frame != localFrame) MoveKeyframe(localFrame, frame);
    }
    private void MoveKeyframe(long from, long to) => Edit(() =>
    {
        editor.MoveCropKeyframe(clipId, from, to, preserveDestination: true);
        localFrame = to; seek(checked(EventClip.StartFrame + to));
    });
    private void SetFrame(long frame)
    {
        localFrame = Math.Clamp(frame, 0, EventClip.DurationFrames - 1);
        seek(checked(EventClip.StartFrame + localFrame)); Refresh();
    }
    private void SeekKey(int direction)
    {
        var keys = lane.Keys;
        SetFrame(direction < 0 ? keys.Where(frame => frame < localFrame).DefaultIfEmpty(0).Max()
            : keys.Where(frame => frame > localFrame).DefaultIfEmpty(EventClip.DurationFrames - 1).Min());
    }
    private async Task LoadSourceAsync()
    {
        var desired = (Source.Path, checked(EventClip.SourceInFrame + localFrame), Source.Width, Source.Height, editor.Project.FrameRate);
        if (requestedSource == desired) return;
        requestedSource = desired; sourceRequest++; sourcePending = true;
        if (sourceRunning) return;
        sourceRunning = true;
        try
        {
            while (sourcePending && !closed)
            {
                sourcePending = false;
                var request = sourceRequest; var source = requestedSource!.Value;
                try
                {
                    var width = Math.Min(1280, Math.Max(1, source.Width)); var height = Math.Clamp((int)Math.Round(width * Math.Max(1, source.Height) / (double)Math.Max(1, source.Width)), 1, 4096);
                    var seconds = source.Rate.Seconds(source.Frame);
                    var pixels = await Task.Run(() => vm.Media.GetFrame(source.Path, seconds, width, height));
                    if (closed || request != sourceRequest) continue;
                    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); bitmap.Freeze();
                    workspace.Image = bitmap; workspace.InvalidateVisual();
                }
                catch (Exception ex) { if (!closed && request == sourceRequest) { workspace.Image = null; workspace.InvalidateVisual(); status.Text = "Source preview: " + ex.Message; } }
            }
        }
        finally { sourceRunning = false; }
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

internal sealed class CropWorkspace : FrameworkElement
{
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    public ImageSource? Image { get; set; }
    public int SourceWidth { get; set; } = 1;
    public int SourceHeight { get; set; } = 1;
    public CropState State { get; set; }
    public Func<bool>? LockAspect { get; set; }
    public event Action<CropState>? PreviewChanged;
    public event Action<CropState>? Committed;
    public event Action? Cancelled;
    private double zoom = 1;
    public double Zoom { get => zoom; set { zoom = value; InvalidateVisual(); } }
    private CropState? dragState;
    private Point origin;
    private bool resizing;
    private Rect SourceRect => new((ActualWidth - SourceWidth * Scale) / 2, (ActualHeight - SourceHeight * Scale) / 2, SourceWidth * Scale, SourceHeight * Scale);
    private double Scale => Math.Max(.000001, Math.Min(Math.Max(1, ActualWidth - 60) / SourceWidth, Math.Max(1, ActualHeight - 60) / SourceHeight) * zoom);
    public CropWorkspace()
    {
        Focusable = true; ClipToBounds = true;
        AutomationProperties.SetName(this, "Pan/Crop source workspace");
        AutomationProperties.SetHelpText(this, "Drag the crop to pan. Drag its lower right handle to resize. Arrow keys move one source pixel; Shift moves ten. Escape cancels a drag.");
    }
    private Point ToDisplay(Point source) => new(SourceRect.X + source.X * Scale, SourceRect.Y + source.Y * Scale);
    private Point Rotate(Point source)
    {
        var angle = State.RotationDegrees * Math.PI / 180; var x = source.X - State.PivotX; var y = source.Y - State.PivotY;
        return new(State.PivotX + Math.Cos(angle) * x - Math.Sin(angle) * y, State.PivotY + Math.Sin(angle) * x + Math.Cos(angle) * y);
    }
    private Point[] Corners() => new[] { new Point(State.CenterX - State.Width / 2, State.CenterY - State.Height / 2), new Point(State.CenterX + State.Width / 2, State.CenterY - State.Height / 2), new Point(State.CenterX + State.Width / 2, State.CenterY + State.Height / 2), new Point(State.CenterX - State.Width / 2, State.CenterY + State.Height / 2) }.Select(point => ToDisplay(Rotate(point))).ToArray();
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(225, 225, 225)), null, new Rect(RenderSize));
        if (Image is not null) dc.DrawImage(Image, SourceRect);
        var points = Corners(); var pen = new Pen(Brushes.Black, 1) { DashStyle = DashStyles.Dash };
        for (var i = 0; i < points.Length; i++) { dc.DrawLine(new Pen(Brushes.White, 1), points[i], points[(i + 1) % 4]); dc.DrawLine(pen, points[i], points[(i + 1) % 4]); dc.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 1), new Rect(points[i].X - 3, points[i].Y - 3, 6, 6)); }
        var pivot = ToDisplay(new Point(State.PivotX, State.PivotY)); dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 1), pivot, 4, 4);
        // An orientation mark distinguishes a rotated crop from an unchanged rectangle.
        var mark = new[] { new Point(-.20, .35), new Point(-.20, -.35), new Point(.25, -.35), new Point(.25, -.20), new Point(-.04, -.20), new Point(-.04, -.03), new Point(.17, -.03), new Point(.17, .12), new Point(-.04, .12), new Point(-.04, .35), new Point(-.20, .35) };
        var path = mark.Select(p => ToDisplay(Rotate(new Point(State.CenterX + p.X * State.Width, State.CenterY + p.Y * State.Height)))).ToArray();
        for (var i = 1; i < path.Length; i++) { dc.DrawLine(new Pen(Brushes.White, 1), path[i - 1], path[i]); dc.DrawLine(pen, path[i - 1], path[i]); }
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus(); origin = e.GetPosition(this); dragState = State;
        resizing = (Corners()[2] - origin).Length < 12; CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragState is not { } before) return;
        var delta = (e.GetPosition(this) - origin) / Scale;
        if (resizing)
        {
            var angle = -before.RotationDegrees * Math.PI / 180;
            var width = Math.Max(.001, before.Width + 2 * (Math.Cos(angle) * delta.X - Math.Sin(angle) * delta.Y));
            var height = LockAspect?.Invoke() == true ? before.Height * width / before.Width : Math.Max(.001, before.Height + 2 * (Math.Sin(angle) * delta.X + Math.Cos(angle) * delta.Y));
            State = before with { Width = width, Height = height };
        }
        else
        {
            // The displayed delta is converted back through the crop's rotation.
            var angle = -before.RotationDegrees * Math.PI / 180;
            State = before with { CenterX = before.CenterX + Math.Cos(angle) * delta.X - Math.Sin(angle) * delta.Y, CenterY = before.CenterY + Math.Sin(angle) * delta.X + Math.Cos(angle) * delta.Y };
        }
        PreviewChanged?.Invoke(State); InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (dragState is null) return;
        var before = dragState.Value; dragState = null; ReleaseMouseCapture(); if (State != before) Committed?.Invoke(State); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (dragState is not { } before) return;
        dragState = null; State = before; Cancelled?.Invoke(); InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && dragState is not null) { dragState = null; ReleaseMouseCapture(); Cancelled?.Invoke(); e.Handled = true; return; }
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var x = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
        var y = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
        if (x != 0 || y != 0) { Committed?.Invoke(State with { CenterX = State.CenterX + x, CenterY = State.CenterY + y }); e.Handled = true; }
    }
}

internal sealed class CropKeyframeLane : FrameworkElement
{
    public long Duration { get; set; } = 1;
    public long Position { get; set; }
    public long[] Keys { get; set; } = [0];
    public bool CanMove { get; set; }
    public Rational Rate { get; set; } = new(30, 1);
    public event Action<long>? Seek;
    public event Action<long, long>? MoveKeyframe;
    private long? dragging;
    private long pendingFrame;
    // The ruler reaches the event end; editable frames stop one frame before it.
    private double FrameX(long frame) => 4 + frame * Math.Max(1, ActualWidth - 10) / Math.Max(1.0, Duration);
    private long FrameAt(double x) => (long)Math.Clamp(decimal.Round((decimal)Math.Clamp((x - 4) / Math.Max(1, ActualWidth - 10), 0, 1) * Duration, 0, MidpointRounding.AwayFromZero), 0, Duration - 1);
    public CropKeyframeLane()
    {
        ClipToBounds = true; Focusable = true;
        AutomationProperties.SetName(this, "Crop keyframe timeline");
        AutomationProperties.SetHelpText(this, "Click to seek. Drag a keyframe diamond to change its frame. Arrow keys seek; Escape cancels a drag. Use the keyframe frame field for exact timing.");
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(45, 45, 45)), null, new Rect(RenderSize));
        var pen = new Pen(Brushes.Gray, 1); dc.DrawLine(pen, new Point(0, 19), new Point(ActualWidth, 19)); dc.DrawLine(pen, new Point(0, 43), new Point(ActualWidth, 43));
        for (var i = 0; i <= 6; i++)
        {
            var frame = (long)((Int128)Duration * i / 6); var x = i * Math.Max(1, ActualWidth - 10) / 6;
            var text = new FormattedText(OpenRend.UI.TimelineView.FormatTimecode(frame, Rate), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, Brushes.Silver, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(x + 3, 2));
        }
        foreach (var frame in Keys)
        {
            var x = FrameX(dragging == frame ? pendingFrame : frame);
            dc.PushTransform(new RotateTransform(45, x, 31)); dc.DrawRectangle(frame == Position ? Brushes.White : Brushes.Silver, pen, new Rect(x - 3, 28, 6, 6)); dc.Pop();
        }
        var cursor = FrameX(Position);
        dc.DrawLine(new Pen(Brushes.White, 1), new Point(cursor, 0), new Point(cursor, ActualHeight));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus(); var point = e.GetPosition(this); var frame = FrameAt(point.X);
        var near = Keys.OrderBy(key => Math.Abs((double)key - frame)).FirstOrDefault();
        if (Keys.Length > 0 && Math.Abs(FrameX(near) - e.GetPosition(this).X) < 7)
        {
            frame = near;
            if (CanMove && Math.Abs(point.Y - 31) <= 10) { dragging = near; pendingFrame = near; CaptureMouse(); }
        }
        Seek?.Invoke(frame); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging is null) return;
        pendingFrame = FrameAt(e.GetPosition(this).X); InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (dragging is not { } from) return;
        dragging = null; ReleaseMouseCapture();
        if (pendingFrame != from) MoveKeyframe?.Invoke(from, pendingFrame);
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { dragging = null; InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && dragging is not null) { dragging = null; ReleaseMouseCapture(); InvalidateVisual(); e.Handled = true; return; }
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            Seek?.Invoke(e.Key switch { Key.Home => 0, Key.End => Duration - 1, Key.Left => Math.Max(0, Position - 1), _ => Math.Min(Duration - 1, Position + 1) });
            e.Handled = true;
        }
    }
}
