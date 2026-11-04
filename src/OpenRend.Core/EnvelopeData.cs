using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace OpenRend.Core;

[JsonConverter(typeof(JsonStringEnumConverter<EnvelopeType>))]
public enum EnvelopeType
{
    Velocity,
    Composite,
    FadeToColor,
    Mute,
    Volume,
    Pan
}

[JsonConverter(typeof(JsonStringEnumConverter<EnvelopeCurveType>))]
public enum EnvelopeCurveType
{
    Linear,
    Fast,
    Slow,
    Smooth,
    Sharp,
    Hold
}

public sealed class EnvelopePoint : IEquatable<EnvelopePoint>
{
    public long Frame { get; set; }
    public double Value { get; set; }
    public EnvelopeCurveType Curve { get; set; } = EnvelopeCurveType.Linear;

    public EnvelopePoint() { }

    public EnvelopePoint(long frame, double value, EnvelopeCurveType curve = EnvelopeCurveType.Linear)
    {
        Frame = frame;
        Value = value;
        Curve = curve;
    }

    public EnvelopePoint DeepClone() => new()
    {
        Frame = Frame,
        Value = Value,
        Curve = Curve
    };

    public void Validate()
    {
        if (Frame < 0) throw new InvalidDataException("Envelope point frame cannot be negative.");
        if (!double.IsFinite(Value)) throw new InvalidDataException("Envelope point value must be finite.");
        if (!Enum.IsDefined(Curve)) throw new InvalidDataException("Envelope curve type is invalid.");
    }

    public bool Equals(EnvelopePoint? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Frame == other.Frame && Math.Abs(Value - other.Value) < 1e-9 && Curve == other.Curve;
    }

    public override bool Equals(object? obj) => Equals(obj as EnvelopePoint);
    public override int GetHashCode() => HashCode.Combine(Frame, Math.Round(Value, 6), Curve);
}

public sealed class ClipEnvelope : IEquatable<ClipEnvelope>
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public EnvelopeType Type { get; set; } = EnvelopeType.Velocity;
    public bool IsEnabled { get; set; } = true;
    public List<EnvelopePoint> Points { get; set; } = [];

    [JsonIgnore]
    public double DefaultValue => Type switch
    {
        EnvelopeType.Velocity => 1.0,
        EnvelopeType.Composite => 1.0,
        EnvelopeType.Volume => 1.0,
        EnvelopeType.Pan => 0.0,
        EnvelopeType.Mute => 0.0,
        _ => 1.0
    };

    [JsonIgnore]
    public double MinValue => Type switch
    {
        EnvelopeType.Velocity => -1.0, // -100% reverse speed
        EnvelopeType.Pan => -1.0,      // 100% Left
        _ => 0.0
    };

    [JsonIgnore]
    public double MaxValue => Type switch
    {
        EnvelopeType.Velocity => 3.0,  // +300% forward speed
        EnvelopeType.Volume => 2.0,    // +6 dB
        _ => 1.0
    };

    public double ValueAt(long frame)
    {
        if (!IsEnabled || Points.Count == 0) return DefaultValue;

        // If frame is before or at the first point
        if (frame <= Points[0].Frame) return Math.Clamp(Points[0].Value, MinValue, MaxValue);

        // If frame is after or at the last point
        if (frame >= Points[^1].Frame) return Math.Clamp(Points[^1].Value, MinValue, MaxValue);

        // Binary search to find segment
        int low = 0;
        int high = Points.Count - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (Points[mid].Frame == frame) return Math.Clamp(Points[mid].Value, MinValue, MaxValue);
            if (Points[mid].Frame < frame) low = mid + 1;
            else high = mid - 1;
        }

        var p0 = Points[high];
        var p1 = Points[low];
        var frameDelta = p1.Frame - p0.Frame;
        if (frameDelta <= 0) return Math.Clamp(p0.Value, MinValue, MaxValue);

        double t = (double)(frame - p0.Frame) / frameDelta;
        t = Math.Clamp(t, 0.0, 1.0);

        double w = p0.Curve switch
        {
            EnvelopeCurveType.Linear => t,
            EnvelopeCurveType.Fast => 1.0 - (1.0 - t) * (1.0 - t),
            EnvelopeCurveType.Slow => t * t,
            EnvelopeCurveType.Smooth => t * t * (3.0 - 2.0 * t),
            EnvelopeCurveType.Sharp => t * t * t,
            EnvelopeCurveType.Hold => t >= 1.0 ? 1.0 : 0.0,
            _ => t
        };

        double interpolated = p0.Value + w * (p1.Value - p0.Value);
        return Math.Clamp(interpolated, MinValue, MaxValue);
    }

    /// <summary>
    /// Computes the accumulated source frame position given velocity envelope integration,
    /// event playback rate, and reverse playback switch.
    /// Supports forward playback, slow motion, freeze frame (0%), reverse speeds (-100%),
    /// and accelerated speeds (+300%) with analytical piecewise curve integration.
    /// </summary>
    public long CalculateAccumulatedSourceFrame(long startSourceFrame, long targetClipFrame, double playbackRate = 1.0, bool isReversed = false, long durationFrames = 0)
    {
        var effRate = double.IsFinite(playbackRate) && playbackRate != 0 ? Math.Abs(playbackRate) : 1.0;
        if (targetClipFrame <= 0 && !isReversed) return startSourceFrame;

        if (!IsEnabled || Points.Count == 0)
        {
            if (isReversed)
            {
                var revFrames = Math.Max(0, durationFrames - 1 - targetClipFrame);
                return Math.Max(0, startSourceFrame + (long)Math.Round(revFrames * effRate));
            }
            return Math.Max(0, startSourceFrame + (long)Math.Round(targetClipFrame * effRate));
        }

        // Fast piecewise analytical integration of velocity envelope curve * playback rate
        double accumulatedDelta = 0.0;
        long currentF = 0;

        if (targetClipFrame <= Points[0].Frame)
        {
            accumulatedDelta = targetClipFrame * Math.Clamp(Points[0].Value, MinValue, MaxValue);
        }
        else
        {
            // From 0 to Points[0].Frame
            if (Points[0].Frame > 0)
            {
                var fEnd = Math.Min(targetClipFrame, Points[0].Frame);
                accumulatedDelta += fEnd * Math.Clamp(Points[0].Value, MinValue, MaxValue);
                currentF = fEnd;
            }

            // Iterate through curve segments
            for (int i = 0; i < Points.Count - 1 && currentF < targetClipFrame; i++)
            {
                var p0 = Points[i];
                var p1 = Points[i + 1];
                if (targetClipFrame <= p0.Frame) break;

                var segDuration = p1.Frame - p0.Frame;
                if (segDuration <= 0) continue;

                var v0 = Math.Clamp(p0.Value, MinValue, MaxValue);
                var v1 = Math.Clamp(p1.Value, MinValue, MaxValue);

                if (targetClipFrame >= p1.Frame)
                {
                    // Full segment integral
                    double segIntegral = p0.Curve switch
                    {
                        EnvelopeCurveType.Linear => segDuration * (v0 + v1) * 0.5,
                        EnvelopeCurveType.Fast => segDuration * (v0 + (2.0 / 3.0) * (v1 - v0)),
                        EnvelopeCurveType.Slow => segDuration * (v0 + (1.0 / 3.0) * (v1 - v0)),
                        EnvelopeCurveType.Smooth => segDuration * (v0 + v1) * 0.5,
                        EnvelopeCurveType.Sharp => segDuration * (v0 + 0.25 * (v1 - v0)),
                        EnvelopeCurveType.Hold => segDuration * v0,
                        _ => segDuration * (v0 + v1) * 0.5
                    };
                    accumulatedDelta += segIntegral;
                    currentF = p1.Frame;
                }
                else
                {
                    // Partial segment integral up to targetClipFrame
                    var localF = targetClipFrame - p0.Frame;
                    double t = (double)localF / segDuration;
                    t = Math.Clamp(t, 0.0, 1.0);

                    double wt = p0.Curve switch
                    {
                        EnvelopeCurveType.Linear => 0.5 * t * t,
                        EnvelopeCurveType.Fast => t * t - (1.0 / 3.0) * t * t * t,
                        EnvelopeCurveType.Slow => (1.0 / 3.0) * t * t * t,
                        EnvelopeCurveType.Smooth => t * t * t - 0.5 * t * t * t * t,
                        EnvelopeCurveType.Sharp => 0.25 * t * t * t * t,
                        EnvelopeCurveType.Hold => 0.0,
                        _ => 0.5 * t * t
                    };
                    double partialIntegral = segDuration * (v0 * t + (v1 - v0) * wt);
                    accumulatedDelta += partialIntegral;
                    currentF = targetClipFrame;
                    break;
                }
            }

            // If targetClipFrame extends past the last point
            if (currentF < targetClipFrame)
            {
                var remaining = targetClipFrame - currentF;
                accumulatedDelta += remaining * Math.Clamp(Points[^1].Value, MinValue, MaxValue);
            }
        }

        accumulatedDelta *= effRate;

        if (isReversed)
        {
            long baseEnd = startSourceFrame + (long)Math.Round(Math.Max(0, durationFrames - 1) * effRate);
            long result = baseEnd - (long)Math.Round(accumulatedDelta);
            return Math.Max(0, result);
        }
        else
        {
            long result = startSourceFrame + (long)Math.Round(accumulatedDelta);
            return Math.Max(0, result);
        }
    }

    /// <summary>
    /// Splits the envelope at the specified frame boundary into two independent continuous halves.
    /// Preserves exact curve interpolation value at the split boundary and shifts right points leftward.
    /// Aligned with 1:1 Vegas Pro event splitting semantics for Velocity and Composite envelopes.
    /// </summary>
    public (ClipEnvelope Left, ClipEnvelope Right) SplitAt(long splitFrame)
    {
        var splitVal = ValueAt(splitFrame);

        // Find the curve type of the segment that crosses or ends at splitFrame
        var segCurve = EnvelopeCurveType.Linear;
        if (Points.Count > 0)
        {
            var prevPoint = Points.LastOrDefault(pt => pt.Frame <= splitFrame);
            if (prevPoint != null)
            {
                segCurve = prevPoint.Curve;
            }
        }

        var left = new ClipEnvelope
        {
            Id = Guid.NewGuid(),
            Type = Type,
            IsEnabled = IsEnabled
        };

        var right = new ClipEnvelope
        {
            Id = Guid.NewGuid(),
            Type = Type,
            IsEnabled = IsEnabled
        };

        // 1. Build Left Envelope
        foreach (var pt in Points.Where(pt => pt.Frame < splitFrame))
        {
            left.Points.Add(pt.DeepClone());
        }
        left.Points.Add(new EnvelopePoint(splitFrame, splitVal, segCurve));
        left.Points.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        for (int i = left.Points.Count - 1; i > 0; i--)
        {
            if (left.Points[i].Frame == left.Points[i - 1].Frame)
            {
                left.Points.RemoveAt(i - 1);
            }
        }

        // 2. Build Right Envelope
        right.Points.Add(new EnvelopePoint(0, splitVal, segCurve));
        foreach (var pt in Points.Where(pt => pt.Frame > splitFrame))
        {
            var shiftedFrame = checked(pt.Frame - splitFrame);
            right.Points.Add(new EnvelopePoint(shiftedFrame, pt.Value, pt.Curve));
        }
        right.Points.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        for (int i = right.Points.Count - 1; i > 0; i--)
        {
            if (right.Points[i].Frame == right.Points[i - 1].Frame)
            {
                right.Points.RemoveAt(i - 1);
            }
        }

        return (left, right);
    }

    public ClipEnvelope DeepClone() => new()
    {
        Id = Id,
        Type = Type,
        IsEnabled = IsEnabled,
        Points = Points.Select(p => p.DeepClone()).ToList()
    };

    /// <summary>
    /// Shifts envelope points downstream or upstream of a threshold frame by delta frames.
    /// Used for timeline event trimming, moving, and ripple insertion.
    /// </summary>
    public void ShiftPoints(long thresholdFrame, long delta)
    {
        if (Points == null || Points.Count == 0 || delta == 0) return;
        if (delta < 0)
        {
            var deleteSpan = (Start: Math.Max(0, checked(thresholdFrame + delta)), End: thresholdFrame);
            RippleForSpans([(deleteSpan.Start, deleteSpan.End)]);
            return;
        }

        foreach (var pt in Points)
        {
            if (pt.Frame >= thresholdFrame)
            {
                pt.Frame = Math.Max(0, checked(pt.Frame + delta));
            }
        }
        Points.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        for (int i = Points.Count - 1; i > 0; i--)
        {
            if (Points[i].Frame == Points[i - 1].Frame)
            {
                Points.RemoveAt(i - 1);
            }
        }
    }

    /// <summary>
    /// Deletes points falling strictly inside collapsed deletion spans and shifts later points left to close the gap.
    /// Used for timeline event deletion and ripple editing.
    /// </summary>
    public void RippleForSpans(IEnumerable<(long Start, long End)> spans)
    {
        if (Points == null || Points.Count == 0) return;
        var spanList = spans.Where(s => s.End > s.Start).OrderBy(s => s.Start).ToList();
        if (spanList.Count == 0) return;

        Points.RemoveAll(pt => spanList.Any(s => pt.Frame > s.Start && pt.Frame < s.End));

        foreach (var pt in Points)
        {
            var shift = spanList.Where(s => s.End <= pt.Frame).Sum(s => s.End - s.Start);
            if (shift > 0)
            {
                pt.Frame = Math.Max(0, checked(pt.Frame - shift));
            }
        }

        Points.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        for (int i = Points.Count - 1; i > 0; i--)
        {
            if (Points[i].Frame == Points[i - 1].Frame)
            {
                Points.RemoveAt(i - 1);
            }
        }
    }

    public void Validate()
    {
        if (Id == Guid.Empty) throw new InvalidDataException("Envelope ID cannot be empty.");
        if (!Enum.IsDefined(Type)) throw new InvalidDataException("Envelope type is invalid.");
        if (Points == null) throw new InvalidDataException("Envelope points collection cannot be null.");

        long lastFrame = -1;
        foreach (var point in Points)
        {
            if (point == null) throw new InvalidDataException("Envelope point cannot be null.");
            point.Validate();
            if (point.Frame < lastFrame) throw new InvalidDataException("Envelope points must be sorted in non-decreasing frame order.");
            lastFrame = point.Frame;
        }
    }

    public bool Equals(ClipEnvelope? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id == other.Id && Type == other.Type && IsEnabled == other.IsEnabled &&
               Points.SequenceEqual(other.Points);
    }

    public override bool Equals(object? obj) => Equals(obj as ClipEnvelope);
    public override int GetHashCode() => HashCode.Combine(Id, Type, IsEnabled, Points.Count);
}
