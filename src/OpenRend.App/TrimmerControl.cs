using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenRend.Core;
using OpenRend.UI;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro Trimmer window/dock control for source media preview and subclip marking.
/// Provides dedicated source playback, In/Out point handles, duration calculations,
/// and one-click timeline insertion at the project cursor (A).
/// </summary>
public sealed class TrimmerControl : UserControl
{
    private EditorViewModel? vm;
    private MediaAsset? activeMedia;
    private long totalFrames;
    private long inFrame;
    private long outFrame;
    private long currentFrame;
    private bool isPlaying;
    private bool isLooping = true;
    private readonly DispatcherTimer playbackTimer;

    // Header info controls
    private readonly TextBlock mediaNameLabel;
    private readonly TextBlock formatLabel;
    private readonly TextBlock cursorTimecode;
    private readonly TextBlock inTimecode;
    private readonly TextBlock outTimecode;
    private readonly TextBlock lengthTimecode;

    // Preview area
    private readonly Grid previewGrid;
    private readonly Image previewImage;
    private readonly TextBlock emptyPrompt;

    // Scrub ruler
    private readonly TrimmerRuler ruler;

    // Transport buttons
    private readonly Button markInBtn;
    private readonly Button markOutBtn;
    private readonly Button clearPointsBtn;
    private readonly Button selectAllBtn;
    private readonly Button prevFrameBtn;
    private readonly Button playPauseBtn;
    private readonly Button nextFrameBtn;
    private readonly ToggleButton loopToggle;
    private readonly Button addToTimelineBtn;
    private readonly Button createSubclipBtn;

    public MediaAsset? ActiveMedia => activeMedia;
    public long CurrentFrame => currentFrame;
    public long InFrame => inFrame;
    public long OutFrame => outFrame;

    public event Action<Guid, long, long>? AddToTimelineRequested;

    public TrimmerControl()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        Focusable = true;

        playbackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33.33)
        };
        playbackTimer.Tick += OnPlaybackTick;

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); // Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Preview
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) }); // Scrub ruler
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) }); // Transport toolbar

        // 1. Header Bar
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 0, 8, 0)
        };
        var headerDock = new DockPanel { LastChildFill = true };

        // Timecodes on the right
        var tcStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(tcStack, Dock.Right);

        cursorTimecode = CreateTcBlock("Cursor: 00:00:00:00", "#CCCCCC");
        inTimecode = CreateTcBlock("In: 00:00:00:00", "#448DBD");
        outTimecode = CreateTcBlock("Out: 00:00:00:00", "#448DBD");
        lengthTimecode = CreateTcBlock("Length: 00:00:00:00", "#E0B020");

        tcStack.Children.Add(cursorTimecode);
        tcStack.Children.Add(new Separator { Margin = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)) });
        tcStack.Children.Add(inTimecode);
        tcStack.Children.Add(outTimecode);
        tcStack.Children.Add(lengthTimecode);
        headerDock.Children.Add(tcStack);

        // Media info on the left
        var infoStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        mediaNameLabel = new TextBlock
        {
            Text = "No Media Loaded",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 280
        };
        formatLabel = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xB0, 0xC8)),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        infoStack.Children.Add(mediaNameLabel);
        infoStack.Children.Add(formatLabel);
        headerDock.Children.Add(infoStack);

        headerBorder.Child = headerDock;
        Grid.SetRow(headerBorder, 0);
        rootGrid.Children.Add(headerBorder);

        // 2. Preview Area
        previewGrid = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)) };
        previewImage = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        emptyPrompt = new TextBlock
        {
            Text = "Double-click or right-click media in Project Media to open in Trimmer.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24)
        };
        previewGrid.Children.Add(previewImage);
        previewGrid.Children.Add(emptyPrompt);
        Grid.SetRow(previewGrid, 1);
        rootGrid.Children.Add(previewGrid);

        // 3. Scrub Ruler
        ruler = new TrimmerRuler(this);
        Grid.SetRow(ruler, 2);
        rootGrid.Children.Add(ruler);

        // 4. Transport & Action Toolbar
        var toolbarBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(6, 2, 6, 2)
        };
        var toolbarDock = new DockPanel { LastChildFill = true };

        // Insertion actions on right
        var actionStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(actionStack, Dock.Right);

        addToTimelineBtn = new Button
        {
            Content = "[+] Add to Timeline (A)",
            Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), // Vegas green
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(4, 0, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x8E, 0x3C)),
            ToolTip = "Insert marked subclip onto the active timeline track at cursor (A)"
        };
        addToTimelineBtn.Click += (_, _) => AddSubclipToTimeline();

        createSubclipBtn = new Button
        {
            Content = "Create Subclip",
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(4, 0, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            ToolTip = "Create new subclip asset in Project Media pool"
        };
        createSubclipBtn.Click += (_, _) => CreateSubclip();

        actionStack.Children.Add(createSubclipBtn);
        actionStack.Children.Add(addToTimelineBtn);
        toolbarDock.Children.Add(actionStack);

        // Transport & Trim markers on left
        var transportStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        markInBtn = CreateToolbarBtn("[", "Mark In (I)", (_, _) => MarkIn());
        markOutBtn = CreateToolbarBtn("]", "Mark Out (O)", (_, _) => MarkOut());
        clearPointsBtn = CreateToolbarBtn("Clear", "Clear In/Out Points", (_, _) => ClearPoints());
        selectAllBtn = CreateToolbarBtn("All", "Select Entire Clip (Ctrl+A)", (_, _) => SelectAll());

        prevFrameBtn = CreateToolbarBtn("◀", "Previous Frame (Left)", (_, _) => StepFrame(-1));
        playPauseBtn = CreateToolbarBtn("▶", "Play / Pause (Space)", (_, _) => TogglePlayPause());
        nextFrameBtn = CreateToolbarBtn("▶", "Next Frame (Right)", (_, _) => StepFrame(1));

        loopToggle = new ToggleButton
        {
            Content = "↻",
            IsChecked = isLooping,
            Width = 26,
            Height = 24,
            Margin = new Thickness(2, 0, 2, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            ToolTip = "Loop Playback (Q)"
        };
        loopToggle.Click += (_, _) => isLooping = loopToggle.IsChecked == true;

        transportStack.Children.Add(markInBtn);
        transportStack.Children.Add(markOutBtn);
        transportStack.Children.Add(clearPointsBtn);
        transportStack.Children.Add(selectAllBtn);
        transportStack.Children.Add(new Separator { Margin = new Thickness(4, 4, 4, 4), Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)) });
        transportStack.Children.Add(prevFrameBtn);
        transportStack.Children.Add(playPauseBtn);
        transportStack.Children.Add(nextFrameBtn);
        transportStack.Children.Add(loopToggle);

        toolbarDock.Children.Add(transportStack);
        toolbarBorder.Child = toolbarDock;
        Grid.SetRow(toolbarBorder, 3);
        rootGrid.Children.Add(toolbarBorder);

        Content = rootGrid;

        KeyDown += OnTrimmerKeyDown;
    }

    public void Initialize(EditorViewModel viewModel)
    {
        vm = viewModel;
    }

    public void LoadMedia(MediaAsset media)
    {
        activeMedia = media ?? throw new ArgumentNullException(nameof(media));
        var rate = vm?.Project.FrameRate ?? new Rational(30, 1);
        totalFrames = Math.Max(1, (long)Math.Round(media.DurationSeconds * rate.Value));
        inFrame = 0;
        outFrame = totalFrames;
        currentFrame = 0;
        isPlaying = false;
        playbackTimer.Stop();
        playPauseBtn.Content = "▶";

        mediaNameLabel.Text = media.Name;
        mediaNameLabel.ToolTip = media.Path;

        if (media.HasVideo)
        {
            formatLabel.Text = $"{media.Width}×{media.Height}  {media.DurationSeconds:0.00}s ({totalFrames} frames)";
        }
        else
        {
            formatLabel.Text = $"Audio Only  {media.DurationSeconds:0.00}s ({totalFrames} frames)";
        }

        emptyPrompt.Visibility = Visibility.Collapsed;
        previewImage.Visibility = Visibility.Visible;

        UpdateReadouts();
        RequestCurrentFrameAsync();
        ruler.InvalidateVisual();
    }

    public void SeekTo(long frame)
    {
        if (activeMedia is null) return;
        currentFrame = Math.Clamp(frame, 0, totalFrames);
        UpdateReadouts();
        RequestCurrentFrameAsync();
        ruler.InvalidateVisual();
    }

    public void MarkIn()
    {
        if (activeMedia is null) return;
        inFrame = Math.Clamp(currentFrame, 0, outFrame);
        UpdateReadouts();
        ruler.InvalidateVisual();
    }

    public void MarkOut()
    {
        if (activeMedia is null) return;
        outFrame = Math.Clamp(currentFrame, inFrame, totalFrames);
        UpdateReadouts();
        ruler.InvalidateVisual();
    }

    public void ClearPoints()
    {
        if (activeMedia is null) return;
        inFrame = 0;
        outFrame = totalFrames;
        UpdateReadouts();
        ruler.InvalidateVisual();
    }

    public void SelectAll()
    {
        ClearPoints();
    }

    public void StepFrame(int delta)
    {
        if (activeMedia is null) return;
        SeekTo(currentFrame + delta);
    }

    public void TogglePlayPause()
    {
        if (activeMedia is null) return;
        isPlaying = !isPlaying;
        if (isPlaying)
        {
            if (currentFrame >= outFrame || currentFrame < inFrame)
                currentFrame = inFrame;
            playbackTimer.Start();
            playPauseBtn.Content = "⏸";
        }
        else
        {
            playbackTimer.Stop();
            playPauseBtn.Content = "▶";
        }
    }

    public void AddSubclipToTimeline()
    {
        if (activeMedia is null || vm is null) return;
        var duration = Math.Max(1, outFrame - inFrame);
        AddToTimelineRequested?.Invoke(activeMedia.Id, inFrame, duration);
        vm.PlaceSubclip(activeMedia.Id, inFrame, duration);
        vm.Status = $"Placed subclip '{activeMedia.Name}' [{inFrame}..{outFrame}] on timeline.";
    }

    public void CreateSubclip()
    {
        if (activeMedia is null || vm is null) return;
        var duration = Math.Max(1, outFrame - inFrame);
        var subclipAsset = activeMedia.DeepClone();
        subclipAsset.Id = Guid.NewGuid();
        subclipAsset.DurationSeconds = duration / (double)vm.Project.FrameRate.Value;

        vm.Project.Media.Add(subclipAsset);
        vm.RefreshLibrary();
        vm.Status = $"Created subclip for '{activeMedia.Name}' in Project Media pool.";
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        if (!isPlaying || activeMedia is null) return;
        currentFrame++;
        if (currentFrame >= outFrame)
        {
            if (isLooping)
            {
                currentFrame = inFrame;
            }
            else
            {
                currentFrame = outFrame;
                TogglePlayPause();
                return;
            }
        }
        UpdateReadouts();
        RequestCurrentFrameAsync();
        ruler.InvalidateVisual();
    }

    private async void RequestCurrentFrameAsync()
    {
        if (activeMedia is null || vm is null) return;
        if (!File.Exists(activeMedia.Path)) return;

        try
        {
            var rate = vm.Project.FrameRate;
            var seconds = currentFrame / (double)rate.Value;
            if (activeMedia.HasVideo)
            {
                var bytes = await vm.Media.GetFrameAsync(activeMedia.Path, seconds, 640, 360);
                if (bytes.Length == 640 * 360 * 4)
                {
                    var wb = new WriteableBitmap(640, 360, 96, 96, PixelFormats.Bgra32, null);
                    wb.WritePixels(new Int32Rect(0, 0, 640, 360), bytes, 640 * 4, 0);
                    wb.Freeze();
                    previewImage.Source = wb;
                }
            }
        }
        catch
        {
            // Decoding fallback leaves thumbnail or prior frame intact
        }
    }

    private void UpdateReadouts()
    {
        var rate = vm?.Project.FrameRate ?? new Rational(30, 1);
        cursorTimecode.Text = $"Cursor: {TimelineView.FormatTimecode(currentFrame, rate)}";
        inTimecode.Text = $"In: {TimelineView.FormatTimecode(inFrame, rate)}";
        outTimecode.Text = $"Out: {TimelineView.FormatTimecode(outFrame, rate)}";
        lengthTimecode.Text = $"Length: {TimelineView.FormatTimecode(Math.Max(0, outFrame - inFrame), rate)}";
    }

    private void OnTrimmerKeyDown(object sender, KeyEventArgs e)
    {
        if (activeMedia is null) return;
        if (e.Key == Key.I) { MarkIn(); e.Handled = true; }
        else if (e.Key == Key.O) { MarkOut(); e.Handled = true; }
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.None) { AddSubclipToTimeline(); e.Handled = true; }
        else if (e.Key == Key.Space) { TogglePlayPause(); e.Handled = true; }
        else if (e.Key == Key.Left) { StepFrame(-1); e.Handled = true; }
        else if (e.Key == Key.Right) { StepFrame(1); e.Handled = true; }
        else if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { SelectAll(); e.Handled = true; }
    }

    private static TextBlock CreateTcBlock(string text, string hex) => new()
    {
        Text = text,
        FontSize = 11,
        FontFamily = new FontFamily("Consolas, Segoe UI"),
        Foreground = (Brush)new BrushConverter().ConvertFromString(hex)!,
        Margin = new Thickness(4, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Button CreateToolbarBtn(string text, string tooltip, RoutedEventHandler handler)
    {
        var btn = new Button
        {
            Content = text,
            Width = text.Length > 2 ? double.NaN : 26,
            Height = 24,
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(2, 0, 2, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            ToolTip = tooltip
        };
        btn.Click += handler;
        return btn;
    }

    private sealed class TrimmerRuler : FrameworkElement
    {
        private readonly TrimmerControl owner;
        private string dragMode = "";

        public TrimmerRuler(TrimmerControl trimmer)
        {
            owner = trimmer;
            ClipToBounds = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var rect = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B)), new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), 1), rect);

            if (owner.activeMedia is null || owner.totalFrames <= 0) return;

            var inX = FrameToX(owner.inFrame);
            var outX = FrameToX(owner.outFrame);
            var curX = FrameToX(owner.currentFrame);

            // Shaded selection region between [In, Out]
            var selRect = new Rect(inX, 1, Math.Max(2, outX - inX), ActualHeight - 2);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, 0x2B, 0x6C, 0xB0)), null, selRect);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0x8D, 0xBD)), 1.5), new Point(inX, 1), new Point(outX, 1));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0x8D, 0xBD)), 1.5), new Point(inX, ActualHeight - 1), new Point(outX, ActualHeight - 1));

            // In Bracket handle [
            var inPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)), 2);
            dc.DrawLine(inPen, new Point(inX, 2), new Point(inX, ActualHeight - 2));
            dc.DrawLine(inPen, new Point(inX, 2), new Point(inX + 5, 2));
            dc.DrawLine(inPen, new Point(inX, ActualHeight - 2), new Point(inX + 5, ActualHeight - 2));

            // Out Bracket handle ]
            var outPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)), 2);
            dc.DrawLine(outPen, new Point(outX, 2), new Point(outX, ActualHeight - 2));
            dc.DrawLine(outPen, new Point(outX, 2), new Point(outX - 5, 2));
            dc.DrawLine(outPen, new Point(outX, ActualHeight - 2), new Point(outX - 5, ActualHeight - 2));

            // Ruler tick marks along the ruler
            var tickPen = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1);
            var numTicks = 20;
            for (var i = 0; i <= numTicks; i++)
            {
                var tx = i * ActualWidth / numTicks;
                dc.DrawLine(tickPen, new Point(tx, ActualHeight - 8), new Point(tx, ActualHeight - 1));
            }

            // Playhead Cursor Needle
            var curPen = new Pen(new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)), 1.2);
            dc.DrawLine(curPen, new Point(curX, 0), new Point(curX, ActualHeight));

            // Needle diamond head at top
            var diamond = new StreamGeometry();
            using (var ctx = diamond.Open())
            {
                ctx.BeginFigure(new Point(curX, 12), true, true);
                ctx.LineTo(new Point(curX - 4, 0), true, false);
                ctx.LineTo(new Point(curX + 4, 0), true, false);
            }
            diamond.Freeze();
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xE0, 0xB0, 0x20)), null, diamond);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (owner.activeMedia is null || owner.totalFrames <= 0) return;
            Focus();
            var p = e.GetPosition(this);
            var inX = FrameToX(owner.inFrame);
            var outX = FrameToX(owner.outFrame);

            if (Math.Abs(p.X - inX) <= 6)
            {
                dragMode = "In";
                CaptureMouse();
            }
            else if (Math.Abs(p.X - outX) <= 6)
            {
                dragMode = "Out";
                CaptureMouse();
            }
            else
            {
                dragMode = "Seek";
                owner.SeekTo(XToFrame(p.X));
                CaptureMouse();
            }
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!IsMouseCaptured || owner.activeMedia is null) return;
            var x = e.GetPosition(this).X;
            var frame = XToFrame(x);

            if (dragMode == "In")
            {
                owner.inFrame = Math.Clamp(frame, 0, owner.outFrame);
                owner.UpdateReadouts();
                InvalidateVisual();
            }
            else if (dragMode == "Out")
            {
                owner.outFrame = Math.Clamp(frame, owner.inFrame, owner.totalFrames);
                owner.UpdateReadouts();
                InvalidateVisual();
            }
            else if (dragMode == "Seek")
            {
                owner.SeekTo(frame);
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
                dragMode = "";
            }
        }

        private double FrameToX(long frame) =>
            owner.totalFrames <= 0 ? 0 : (frame / (double)owner.totalFrames) * ActualWidth;

        private long XToFrame(double x) =>
            ActualWidth <= 0 ? 0 : (long)Math.Round((x / ActualWidth) * owner.totalFrames);
    }
}
