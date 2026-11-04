using System.Windows;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.UI;

public sealed partial class TimelineView
{
    private FrameRange? loopRegion;
    private FrameRange? loopBeforeDrag;
    private long loopAnchor;
    private bool selectingLoop;

    public event Action<FrameRange?>? LoopRegionChanged;

    public FrameRange? LoopRegion
    {
        get => loopRegion;
        set
        {
            value?.Validate(Project.DurationFrames);
            if (loopRegion == value) return;
            loopRegion = value;
            LoopRegionChanged?.Invoke(value);
            InvalidateVisual();
        }
    }

    private bool BeginLoopRegion(Point point)
    {
        if (point.X < HeaderWidth || point.Y < 0 || point.Y >= 24 || Project.DurationFrames == 0) return false;
        loopBeforeDrag = LoopRegion;
        loopAnchor = Math.Clamp(FrameAt(point.X), 0, Project.DurationFrames);
        if (LoopRegion is { } range)
        {
            if (Math.Abs(point.X - X(range.StartFrame)) <= 6) loopAnchor = range.EndFrame;
            else if (Math.Abs(point.X - X(range.EndFrame)) <= 6) loopAnchor = range.StartFrame;
        }
        selectingLoop = true;
        CaptureMouse();
        return true;
    }

    private void UpdateLoopRegion(Point point)
    {
        var other = Math.Clamp(FrameAt(point.X), 0, Project.DurationFrames);
        var start = Math.Min(loopAnchor, other);
        LoopRegion = other == loopAnchor ? null : new FrameRange(start, Math.Abs(other - loopAnchor));
    }

    private void DrawLoopRegion(DrawingContext dc)
    {
        if (LoopRegion is not { } range) return;
        dc.PushClip(new RectangleGeometry(new Rect(HeaderWidth, 0, Math.Max(0, ActualWidth - HeaderWidth), 24)));
        var start = X(range.StartFrame);
        var end = X(range.EndFrame);
        if (end > start)
        {
            var goldBrush = Brush("#FFC107");
            var borderPen = FrozenPen("#181818", 1);
            var goldPen = FrozenPen("#FFC107", 1.5);

            // Translucent gold wash over the ruler selection
            dc.DrawRectangle(Brush("#28FFC107"), null, new Rect(start, 2, end - start, 20));

            // Top connecting bar across ruler
            dc.DrawRectangle(goldBrush, borderPen, new Rect(start, 2, end - start, 3));

            // Start bracket '['
            var inBracket = new StreamGeometry();
            using (var ctx = inBracket.Open())
            {
                ctx.BeginFigure(new Point(start + 6, 2), true, true);
                ctx.LineTo(new Point(start, 2), true, false);
                ctx.LineTo(new Point(start, 22), true, false);
                ctx.LineTo(new Point(start + 6, 22), true, false);
                ctx.LineTo(new Point(start + 6, 18), true, false);
                ctx.LineTo(new Point(start + 3, 18), true, false);
                ctx.LineTo(new Point(start + 3, 6), true, false);
                ctx.LineTo(new Point(start + 6, 6), true, false);
            }
            inBracket.Freeze();
            dc.DrawGeometry(goldBrush, borderPen, inBracket);

            // End bracket ']'
            var outBracket = new StreamGeometry();
            using (var ctx = outBracket.Open())
            {
                ctx.BeginFigure(new Point(end - 6, 2), true, true);
                ctx.LineTo(new Point(end, 2), true, false);
                ctx.LineTo(new Point(end, 22), true, false);
                ctx.LineTo(new Point(end - 6, 22), true, false);
                ctx.LineTo(new Point(end - 6, 18), true, false);
                ctx.LineTo(new Point(end - 3, 18), true, false);
                ctx.LineTo(new Point(end - 3, 6), true, false);
                ctx.LineTo(new Point(end - 6, 6), true, false);
            }
            outBracket.Freeze();
            dc.DrawGeometry(goldBrush, borderPen, outBracket);
        }
        dc.Pop();
    }
}
