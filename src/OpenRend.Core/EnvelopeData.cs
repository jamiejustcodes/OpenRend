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
    /// and accelerated speeds (+300%).
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

        // Fast numerical integration of velocity envelope curve * playback rate
        double accumulatedDelta = 0.0;
        for (long f = 0; f < targetClipFrame; f++)
        {
            accumulatedDelta += ValueAt(f) * effRate;
        }

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

    public ClipEnvelope DeepClone() => new()
    {
        Id = Id,
        Type = Type,
        IsEnabled = IsEnabled,
        Points = Points.Select(p => p.DeepClone()).ToList()
    };

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

