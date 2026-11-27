using System.Windows;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.UI;

public sealed partial class TimelineView
{
    private void DrawSourceGuides(DrawingContext dc, TimelineTrack track, TimelineClip clip, MediaAsset asset,
        Rect rect, long sourceIn, long duration, double playbackRate)
    {
        var width = PixelsPerSecond;
        var rate = Project.FrameRate;
        var visibleStart = Math.Max(0, HeaderWidth - rect.Left) / width;
        var visibleEnd = Math.Min(rect.Right, ActualWidth) - rect.Left;
        if (visibleEnd <= 0 || rect.Height < 8) return;
        var repeats = track.Kind == TrackKind.Audio ? clip.AudioSourceMode == AudioSourceMode.Loop
            : clip.VideoSourceMode == VideoSourceMode.Loop;
        var guides = track.Kind == TrackKind.Audio
            ? AudioSourceGuides(rate, clip, asset, sourceIn, duration, visibleStart, visibleEnd / width, 4 / width)
            : VideoSourceGuides(rate, clip, asset, sourceIn, duration, playbackRate, visibleStart, visibleEnd / width, width);
        var pen = repeats ? FrozenPen("#63D6CB", 2) : FrozenPen("#BEC7D0", 2);
        var last = double.NegativeInfinity;
        foreach (var seconds in guides)
        {
            var x = rect.Left + seconds * width;
            if (x - last < 4) continue;
            var top = rect.Top + 1;
            dc.DrawLine(pen, new Point(x, top), new Point(x, top + (repeats ? 10 : 6)));
            if (repeats) dc.DrawLine(pen, new Point(x + 3, top), new Point(x + 3, top + 6));
            last = x;
        }
    }

    internal static IEnumerable<double> AudioSourceGuides(Rational rate, TimelineClip clip, MediaAsset asset,
        long sourceIn, long duration, double visibleStart, double visibleEnd, double minimumSpacing = 0)
    {
        var period = AudioSourceTime.SourceSampleCount(asset);
        if (period < 1) yield break;
        var sampleRate = AudioSourceTime.MixSampleRate;
        var origin = AudioSourceOrigin(rate, clip, sourceIn, duration);
        var maximum = Math.Min(visibleEnd, rate.Seconds(duration));

        if (clip.AudioSourceMode == AudioSourceMode.PadSilence)
        {
            var edge = (clip.IsReversed ? origin - period : period - origin) / sampleRate;
            if (edge > 0 && edge >= visibleStart && edge < maximum) yield return edge;
            yield break;
        }

        if (clip.AudioSourceMode != AudioSourceMode.Loop) yield break;
        var phase = (origin % period + period) % period;
        var first = clip.IsReversed ? phase : period - phase;
        if (first == 0) first = period;
        var sample = first + Math.Max(0, Math.Ceiling((visibleStart * sampleRate - first) / period)) * period;
        var spacing = Math.Max(1, Math.Ceiling(minimumSpacing * sampleRate / period)) * period;
        while (sample < maximum * sampleRate)
        {
            yield return sample / sampleRate;
            var next = sample + spacing;
            if (next <= sample) yield break;
            sample = next;
        }
    }

    private static double AudioSourceOrigin(Rational rate, TimelineClip clip, long sourceIn, long duration)
    {
        var origin = AudioSourceTime.SampleAt(rate.Seconds(sourceIn));
        // A reversed event starts at the exclusive end of its source window.
        if (clip.IsReversed) origin += AudioSourceTime.SampleCount((Int128)duration * rate.Denominator, rate.Numerator);
        return origin;
    }

    internal static IEnumerable<double> VideoSourceGuides(Rational rate, TimelineClip clip, MediaAsset asset,
        long sourceIn, long duration, double playbackRate, double visibleStart, double visibleEnd, double pixelsPerSecond)
    {
        if (clip.VideoSourceMode == VideoSourceMode.Bounded || !asset.HasFiniteVideoSource) yield break;
        var phase = new VideoSourcePhase(clip, sourceIn, duration, playbackRate);
        var first = Math.Max(1, (long)Math.Ceiling(visibleStart * rate.Value));
        var last = Math.Min(duration - 1, (long)Math.Floor(visibleEnd * rate.Value));
        if (last < first) yield break;
        var before = first - 1;
        var region = RegionAt(before);
        if (!phase.HasVelocity && region == RegionAt(last)) yield break;

        // Below one pixel per frame, probe subpixel buckets and find a real frame boundary in each changed bucket.
        var step = Math.Max(1, (long)Math.Ceiling(.75 * rate.Value / pixelsPerSecond));
        while (before < last)
        {
            var after = before + Math.Min(step, last - before);
            var next = RegionAt(after);
            if (region != next)
            {
                var left = before + 1;
                var right = after;
                while (left < right)
                {
                    var middle = left + (right - left) / 2;
                    if (RegionAt(middle) == region) left = middle + 1;
                    else right = middle;
                }
                yield return rate.Seconds(left);
            }
            before = after;
            region = next;
        }

        double RegionAt(long frame) => VideoSourceTime.Region(rate, phase.SourceFrameAtSample(phase.SampledEventFrame(frame)),
            asset.DurationSeconds, clip.VideoSourceMode);
    }
}
