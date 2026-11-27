using System.Windows;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.UI;

public sealed partial class TimelineView
{
    private void DrawTransitionProgress(DrawingContext dc, TimelineClip clip, Rect rect)
    {
        if (clip.FadeInProgress is not { } curve || clip.FadeInFrames <= clip.FadeOffsetFrames || rect.Height < 42) return;
        var frames = Math.Min(clip.DurationFrames, clip.FadeInFrames - clip.FadeOffsetFrames);
        var left = Math.Max(rect.Left, HeaderWidth);
        var end = Math.Min(ActualWidth, X(clip.StartFrame + frames));
        if (end - left < 9) return;
        var startFrame = Math.Clamp(FrameAt(left) - clip.StartFrame, 0, frames);
        var endFrame = Math.Clamp(FrameAt(end) - clip.StartFrame, startFrame, frames);
        var height = Math.Min(17, rect.Height / 4);
        var baseline = rect.Bottom - 6;
        Point At(long frame) => new(X(clip.StartFrame + frame), baseline - height * curve.ValueAt(clip.FadeOffsetFrames + frame, clip.FadeInFrames));

        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            var divisions = (int)Math.Clamp((end - left) / 3, 1, 420);
            drawing.BeginFigure(At(startFrame), false, false);
            for (var index = 1; index <= divisions; index++)
            {
                var frame = startFrame + (long)((decimal)(endFrame - startFrame) * index / divisions);
                drawing.LineTo(At(frame), true, false);
            }
        }
        dc.DrawGeometry(null, FrozenPen("#172629", 3.5), geometry);
        dc.DrawGeometry(null, FrozenPen("#73D4CC", 1.5), geometry);
    }
}
