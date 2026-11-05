using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenRend.Core;

public sealed class TrackMotionKeyframe
{
    public long Frame { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double PositionZ { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Depth { get; set; }
    public double RotationZ { get; set; }
    public double RotationX { get; set; }
    public double RotationY { get; set; }
    public double OrientationX { get; set; }
    public double OrientationY { get; set; }
    public double OrientationZ { get; set; }
    public double RotationOffsetX { get; set; }
    public double RotationOffsetY { get; set; }
    public double RotationOffsetZ { get; set; }
    public double PivotX { get => RotationOffsetX; set => RotationOffsetX = value; }
    public double PivotY { get => RotationOffsetY; set => RotationOffsetY = value; }
    public double PivotZ { get => RotationOffsetZ; set => RotationOffsetZ = value; }
    public CropInterpolation Interpolation { get; set; } = CropInterpolation.Linear;
    public double Smoothness { get; set; } = 100.0;

    public bool Is3D() =>
        Math.Abs(PositionZ) >= 0.001 ||
        Math.Abs(RotationX) >= 0.001 ||
        Math.Abs(RotationY) >= 0.001 ||
        Math.Abs(OrientationX) >= 0.001 ||
        Math.Abs(OrientationY) >= 0.001 ||
        Math.Abs(OrientationZ) >= 0.001 ||
        Math.Abs(RotationOffsetZ) >= 0.001 ||
        Math.Abs(Depth) >= 0.001;

    public static TrackMotionKeyframe Identity(int width, int height) => new()
    {
        Frame = 0,
        PositionX = 0,
        PositionY = 0,
        PositionZ = 0,
        Width = Math.Max(1, width),
        Height = Math.Max(1, height),
        Depth = 0,
        RotationZ = 0,
        RotationX = 0,
        RotationY = 0,
        OrientationX = 0,
        OrientationY = 0,
        OrientationZ = 0,
        RotationOffsetX = 0,
        RotationOffsetY = 0,
        RotationOffsetZ = 0,
        Interpolation = CropInterpolation.Linear,
        Smoothness = 100.0
    };

    public bool IsIdentity(int defaultWidth, int defaultHeight) =>
        Math.Abs(PositionX) < 0.001 &&
        Math.Abs(PositionY) < 0.001 &&
        Math.Abs(PositionZ) < 0.001 &&
        Math.Abs(Width - defaultWidth) < 0.001 &&
        Math.Abs(Height - defaultHeight) < 0.001 &&
        Math.Abs(Depth) < 0.001 &&
        Math.Abs(RotationZ % 360) < 0.001 &&
        Math.Abs(RotationX % 360) < 0.001 &&
        Math.Abs(RotationY % 360) < 0.001 &&
        Math.Abs(OrientationX % 360) < 0.001 &&
        Math.Abs(OrientationY % 360) < 0.001 &&
        Math.Abs(OrientationZ % 360) < 0.001 &&
        Math.Abs(RotationOffsetX) < 0.001 &&
        Math.Abs(RotationOffsetY) < 0.001 &&
        Math.Abs(RotationOffsetZ) < 0.001;

    public void Validate()
    {
        const double limit = 1_000_000_000;
        if (Frame < 0 || !double.IsFinite(Width) || !double.IsFinite(Height) || Width < 0.0001 || Height < 0.0001 || Width > limit || Height > limit)
            throw new InvalidDataException("Track motion dimensions are invalid.");
        if (!double.IsFinite(Depth) || Math.Abs(Depth) > limit)
            throw new InvalidDataException("Track motion depth is invalid.");
        if (!double.IsFinite(PositionX) || Math.Abs(PositionX) > limit ||
            !double.IsFinite(PositionY) || Math.Abs(PositionY) > limit ||
            !double.IsFinite(PositionZ) || Math.Abs(PositionZ) > limit ||
            !double.IsFinite(RotationZ) || !double.IsFinite(RotationX) || !double.IsFinite(RotationY) ||
            !double.IsFinite(OrientationX) || !double.IsFinite(OrientationY) || !double.IsFinite(OrientationZ) ||
            !double.IsFinite(RotationOffsetX) || Math.Abs(RotationOffsetX) > limit ||
            !double.IsFinite(RotationOffsetY) || Math.Abs(RotationOffsetY) > limit ||
            !double.IsFinite(RotationOffsetZ) || Math.Abs(RotationOffsetZ) > limit)
            throw new InvalidDataException("Track motion coordinates are invalid.");
        if (!Enum.IsDefined(Interpolation)) throw new InvalidDataException("Track motion interpolation is undefined.");
        if (!double.IsFinite(Smoothness) || Smoothness is < 0 or > 100) throw new InvalidDataException("Track motion smoothness must be between 0 and 100.");
    }

    public static TrackMotionKeyframe Lerp(TrackMotionKeyframe a, TrackMotionKeyframe b, double amount)
    {
        static double Mix(double x, double y, double t) => x + (y - x) * t;
        return new TrackMotionKeyframe
        {
            Frame = (long)Math.Round(Mix(a.Frame, b.Frame, amount)),
            PositionX = Mix(a.PositionX, b.PositionX, amount),
            PositionY = Mix(a.PositionY, b.PositionY, amount),
            PositionZ = Mix(a.PositionZ, b.PositionZ, amount),
            Width = Mix(a.Width, b.Width, amount),
            Height = Mix(a.Height, b.Height, amount),
            Depth = Mix(a.Depth, b.Depth, amount),
            RotationZ = Mix(a.RotationZ, b.RotationZ, amount),
            RotationX = Mix(a.RotationX, b.RotationX, amount),
            RotationY = Mix(a.RotationY, b.RotationY, amount),
            OrientationX = Mix(a.OrientationX, b.OrientationX, amount),
            OrientationY = Mix(a.OrientationY, b.OrientationY, amount),
            OrientationZ = Mix(a.OrientationZ, b.OrientationZ, amount),
            RotationOffsetX = Mix(a.RotationOffsetX, b.RotationOffsetX, amount),
            RotationOffsetY = Mix(a.RotationOffsetY, b.RotationOffsetY, amount),
            RotationOffsetZ = Mix(a.RotationOffsetZ, b.RotationOffsetZ, amount),
            Interpolation = a.Interpolation,
            Smoothness = a.Smoothness
        };
    }

    public TrackMotionKeyframe DeepClone() => (TrackMotionKeyframe)MemberwiseClone();
}

public sealed class TrackShadowKeyframe
{
    public long Frame { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double Blur { get; set; } = 5.0;
    public double Intensity { get; set; } = 0.5;
    public string Color { get; set; } = "#80000000";
    public TrackShadowKeyframe DeepClone() => (TrackShadowKeyframe)MemberwiseClone();
}

public sealed class TrackGlowKeyframe
{
    public long Frame { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double Blur { get; set; } = 5.0;
    public double Intensity { get; set; } = 0.5;
    public string Color { get; set; } = "#80FFFFFF";
    public TrackGlowKeyframe DeepClone() => (TrackGlowKeyframe)MemberwiseClone();
}

public sealed class TrackMotion
{
    public List<TrackMotionKeyframe> MotionKeyframes { get; set; } = [];
    public bool ShadowEnabled { get; set; }
    public List<TrackShadowKeyframe> ShadowKeyframes { get; set; } = [];
    public bool GlowEnabled { get; set; }
    public List<TrackGlowKeyframe> GlowKeyframes { get; set; } = [];

    public TrackMotion DeepClone() => new()
    {
        MotionKeyframes = MotionKeyframes.Select(k => k.DeepClone()).ToList(),
        ShadowEnabled = ShadowEnabled,
        ShadowKeyframes = ShadowKeyframes.Select(k => k.DeepClone()).ToList(),
        GlowEnabled = GlowEnabled,
        GlowKeyframes = GlowKeyframes.Select(k => k.DeepClone()).ToList()
    };

    public void Validate(int defaultWidth, int defaultHeight)
    {
        long previous = -1;
        foreach (var key in MotionKeyframes)
        {
            if (key is null || key.Frame < 0 || key.Frame <= previous)
                throw new InvalidDataException("Track motion keyframes must be unique and strictly ordered.");
            key.Validate();
            previous = key.Frame;
        }
    }

    public TrackMotionKeyframe Evaluate(long frame, int defaultWidth, int defaultHeight)
    {
        if (MotionKeyframes.Count == 0) return TrackMotionKeyframe.Identity(defaultWidth, defaultHeight);
        if (frame <= MotionKeyframes[0].Frame) return MotionKeyframes[0];
        if (frame >= MotionKeyframes[^1].Frame) return MotionKeyframes[^1];

        var low = 0;
        var high = MotionKeyframes.Count - 1;
        while (high - low > 1)
        {
            var mid = low + (high - low) / 2;
            if (MotionKeyframes[mid].Frame <= frame) low = mid;
            else high = mid;
        }

        var left = MotionKeyframes[low];
        var right = MotionKeyframes[high];
        if (left.Interpolation == CropInterpolation.Hold) return left;

        var progress = (double)(frame - left.Frame) / (right.Frame - left.Frame);
        var centered = progress - 0.5;
        var s = Math.Clamp(left.Smoothness / 100.0, 0.0, 1.0);
        var weight = left.Interpolation switch
        {
            CropInterpolation.Slow => progress * progress,
            CropInterpolation.Fast => 1.0 - (1.0 - progress) * (1.0 - progress),
            CropInterpolation.Smooth => progress * (1.0 - s) + (progress * progress * (3.0 - 2.0 * progress)) * s,
            CropInterpolation.Sharp => 0.5 + 4.0 * centered * centered * centered,
            _ => progress
        };

        return TrackMotionKeyframe.Lerp(left, right, weight);
    }
}

