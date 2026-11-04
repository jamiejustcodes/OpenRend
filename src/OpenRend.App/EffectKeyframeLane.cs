using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro Video FX Keyframe Timeline Lane.
/// Provides visual display of keyframe diamonds, time ruler, playhead cursor,
/// dragging to adjust keyframe timing, and right-click interpolation curve context menu.
/// </summary>
internal sealed class EffectKeyframeLane : FrameworkElement
{
    public long Duration { get; set; } = 1;
    public long Position { get; set; }

    private List<VideoEffectKeyframe> keyframes = [];
    public List<VideoEffectKeyframe> Keyframes
    {
        get => keyframes;
        set
        {
            keyframes = value ?? [];
            InvalidateVisual();
        }
    }

    public Rational Rate { get; set; } = new(30, 1);

    public event Action<long>? Seek;
    public event Action<long, long>? MoveKeyframe;
    public event Action<long, EffectInterpolation>? InterpolationChanged;
    public event Action<long>? DeleteKeyframeRequested;
    public event Action<long>? AddKeyframeRequested;

    private readonly Dictionary<EffectInterpolation, MenuItem> curveItems = [];
    private readonly MenuItem deleteItem;
    private long? activeMenuKey;
    private long? dragging;
    private long pendingDragFrame;

    private double FrameX(long frame) => 6 + (Duration > 1 ? frame * Math.Max(1.0, ActualWidth - 14) / (Duration - 1) : 0);
    private long FrameAt(double x) => Duration > 1
        ? (long)Math.Clamp(Math.Round((x - 6) / Math.Max(1.0, ActualWidth - 14) * (Duration - 1)), 0, Duration - 1)
        : 0;

    public EffectKeyframeLane()
    {
        ClipToBounds = true;
        Focusable = true;
        Height = 46;
        MinHeight = 46;

        AutomationProperties.SetName(this, "Video FX Keyframe Timeline");
        AutomationProperties.SetHelpText(this, "Click to seek cursor. Drag keyframe diamond to move; right click for curve menu.");

        ContextMenu = new ContextMenu();
        AutomationProperties.SetName(ContextMenu, "Keyframe interpolation");

        foreach (var curve in Enum.GetValues<EffectInterpolation>())
        {
            var item = new MenuItem { Header = curve.ToString(), IsCheckable = true };
            item.Click += (_, _) =>
            {
                if (activeMenuKey.HasValue) InterpolationChanged?.Invoke(activeMenuKey.Value, curve);
            };
            curveItems[curve] = item;
            ContextMenu.Items.Add(item);
        }

        ContextMenu.Items.Add(new Separator());
        deleteItem = new MenuItem { Header = "Delete Keyframe", InputGestureText = "Del" };
        deleteItem.Click += (_, _) =>
        {
            if (activeMenuKey.HasValue) DeleteKeyframeRequested?.Invoke(activeMenuKey.Value);
        };
        ContextMenu.Items.Add(deleteItem);

        ContextMenuOpening += (_, e) =>
        {
            var key = e.CursorLeft < 0 ? (Keyframes.Any(k => k.Frame == Position) ? Position : (long?)null) : KeyAt(new Point(e.CursorLeft, e.CursorTop));
            if (key is null) { e.Handled = true; return; }
            activeMenuKey = key.Value;
            var target = Keyframes.FirstOrDefault(k => k.Frame == key.Value);
            var interp = target?.Interpolation ?? EffectInterpolation.Linear;
            foreach (var (c, item) in curveItems) item.IsChecked = c == interp;
            deleteItem.IsEnabled = Keyframes.Count > 1 || key.Value > 0;
            ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        };
    }

    private long? KeyAt(Point point)
    {
        if (Keyframes.Count == 0 || Math.Abs(point.Y - 30) > 12) return null;
        var nearest = Keyframes.OrderBy(k => Math.Abs(FrameX(k.Frame) - point.X)).First();
        return Math.Abs(FrameX(nearest.Frame) - point.X) <= 8 ? nearest.Frame : null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        var point = e.GetPosition(this);
        var hitKey = KeyAt(point);
        if (hitKey.HasValue)
        {
            dragging = hitKey.Value;
            pendingDragFrame = hitKey.Value;
            Seek?.Invoke(hitKey.Value);
        }
        else
        {
            dragging = null;
            var frame = FrameAt(point.X);
            Seek?.Invoke(frame);
        }
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured)
        {
            var point = e.GetPosition(this);
            var frame = FrameAt(point.X);
            if (dragging.HasValue)
            {
                pendingDragFrame = frame;
                InvalidateVisual();
            }
            else
            {
                Seek?.Invoke(frame);
            }
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            if (dragging.HasValue && pendingDragFrame != dragging.Value)
            {
                MoveKeyframe?.Invoke(dragging.Value, pendingDragFrame);
            }
            dragging = null;
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            var curKey = Keyframes.FirstOrDefault(k => k.Frame == Position);
            if (curKey != null && (Keyframes.Count > 1 || curKey.Frame > 0))
            {
                DeleteKeyframeRequested?.Invoke(curKey.Frame);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Insert)
        {
            AddKeyframeRequested?.Invoke(Position);
            e.Handled = true;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // 1. Background
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 22, 22)), null, new Rect(0, 0, w, h));

        // 2. Ruler Area (Top 16 px)
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(32, 32, 32)), null, new Rect(0, 0, w, 16));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(50, 50, 50)), 1), new Point(0, 16), new Point(w, 16));

        // Ruler ticks & frame numbers
        int step = Math.Max(1, (int)(Duration / 10));
        var tickPen = new Pen(new SolidColorBrush(Color.FromRgb(80, 80, 80)), 1);
        for (long f = 0; f < Duration; f += step)
        {
            double x = FrameX(f);
            dc.DrawLine(tickPen, new Point(x, 10), new Point(x, 16));
            var text = new FormattedText(
                f.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                8,
                Brushes.Gray,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(Math.Max(2, x - text.Width / 2), 1));
        }

        // Center lane line
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(40, 40, 40)), 1), new Point(0, 31), new Point(w, 31));

        // 3. Keyframe Diamonds
        var diamondPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 1.2);
        var unselectedBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
        var selectedBrush = new SolidColorBrush(Color.FromRgb(0, 210, 255)); // Vegas Cyan

        foreach (var kf in Keyframes)
        {
            long frameToDraw = (dragging.HasValue && dragging.Value == kf.Frame) ? pendingDragFrame : kf.Frame;
            double kx = FrameX(frameToDraw);
            double ky = 31;
            bool isCurrent = frameToDraw == Position;

            var diamondGeo = new StreamGeometry();
            using (var ctx = diamondGeo.Open())
            {
                ctx.BeginFigure(new Point(kx, ky - 5.5), true, true);
                ctx.LineTo(new Point(kx + 5.5, ky), true, false);
                ctx.LineTo(new Point(kx, ky + 5.5), true, false);
                ctx.LineTo(new Point(kx - 5.5, ky), true, false);
            }
            diamondGeo.Freeze();

            dc.DrawGeometry(isCurrent ? selectedBrush : unselectedBrush, diamondPen, diamondGeo);
        }

        // 4. Playhead Cursor
        double curX = FrameX(Position);
        var playheadPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 60, 60)), 1.5);
        dc.DrawLine(playheadPen, new Point(curX, 0), new Point(curX, h));

        // Playhead triangle cap
        var capGeo = new StreamGeometry();
        using (var ctx = capGeo.Open())
        {
            ctx.BeginFigure(new Point(curX - 4, 0), true, true);
            ctx.LineTo(new Point(curX + 4, 0), true, false);
            ctx.LineTo(new Point(curX, 6), true, false);
        }
        capGeo.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(255, 60, 60)), null, capGeo);
    }
}

