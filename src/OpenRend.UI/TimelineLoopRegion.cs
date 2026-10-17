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

    public FrameRange? LoopRegion
    {
        get => loopRegion;
        set
        {
            value?.Validate(Project.DurationFrames);
            loopRegion = value;
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
        dc.DrawRectangle(Brush("#60606060"), new Pen(Brush("#AAAAAA"), 1), new Rect(start, 3, end - start, 18));
        dc.DrawRectangle(Brush("#AAAAAA"), null, new Rect(start, 3, 4, 18));
        dc.DrawRectangle(Brush("#AAAAAA"), null, new Rect(end - 4, 3, 4, 18));
        dc.Pop();
    }
}
