using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenRend.Core;

/// <summary>
/// Determines how individual slide duration is calculated.
/// Matches ScriptPortal.Vegas.Slideshow.SlideshowLengthSetting.
/// </summary>
public enum SlideshowLengthSetting
{
    PerSlide,
    TotalLength
}

/// <summary>
/// Start location on the project timeline.
/// Matches ScriptPortal.Vegas.Slideshow.CreateSlideshowMode.
/// </summary>
public enum SlideshowStartMode
{
    AtCursor,
    AtStart,
    AtTimecode
}

/// <summary>
/// Transition and Pan/Zoom motion effects for generated slideshows.
/// </summary>
public enum SlideshowEffectKind
{
    None,
    Crossfade,
    RandomTransitions,
    PanAndZoom,
    TransitionsAndPanZoom
}

/// <summary>
/// Ken Burns pan/zoom direction for an individual slide.
/// </summary>
public enum SlideshowPanZoom
{
    None,
    ZoomIn,
    ZoomOut,
    PanLeft,
    PanRight
}

/// <summary>
/// Picture item in a slideshow plan with file path, rotation, and Ken Burns motion.
/// Matches ScriptPortal.Vegas.Slideshow.Picture.
/// </summary>
public sealed class SlideshowPicture : IEquatable<SlideshowPicture>
{
    public string Path { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public int OrientationDegrees { get; set; } // 0, 90, 180, 270
    public DateTime? PhotoDate { get; set; }
    public SlideshowPanZoom PanZoom { get; set; } = SlideshowPanZoom.None;

    public SlideshowPicture() { }

    public SlideshowPicture(string filePath)
    {
        Path = filePath ?? string.Empty;
        Filename = System.IO.Path.GetFileName(filePath) ?? string.Empty;
        OrientationDegrees = 0;
    }

    public void RotateClockwise()
    {
        OrientationDegrees = (OrientationDegrees + 90) % 360;
    }

    public void RotateCounterClockwise()
    {
        OrientationDegrees = (OrientationDegrees + 270) % 360;
    }

    public SlideshowPicture DeepClone() => new()
    {
        Path = Path,
        Filename = Filename,
        OrientationDegrees = OrientationDegrees,
        PhotoDate = PhotoDate,
        PanZoom = PanZoom
    };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Path))
            throw new InvalidOperationException("Picture path cannot be empty.");
        if (OrientationDegrees is not (0 or 90 or 180 or 270))
            throw new InvalidOperationException($"Invalid orientation angle: {OrientationDegrees}. Must be 0, 90, 180, or 270 degrees.");
    }

    public bool Equals(SlideshowPicture? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase)
            && OrientationDegrees == other.OrientationDegrees
            && PhotoDate == other.PhotoDate
            && PanZoom == other.PanZoom;
    }

    public override bool Equals(object? obj) => Equals(obj as SlideshowPicture);

    public override int GetHashCode() =>
        HashCode.Combine(Path?.ToLowerInvariant(), OrientationDegrees, PhotoDate, PanZoom);
}

/// <summary>
/// Complete slideshow plan parameters matching Vegas Pro Slideshow Creator.
/// </summary>
public sealed class SlideshowSettings : IEquatable<SlideshowSettings>
{
    public const double DefaultDurationSeconds = 5.0;
    public const double DefaultOverlapSeconds = 1.0;
    public const double DefaultTotalLengthSeconds = 60.0;
    public const double DefaultPanScaleAmount = 0.2; // 20% zoom or pan movement

    public SlideshowStartMode StartMode { get; set; } = SlideshowStartMode.AtCursor;
    public string StartTimecode { get; set; } = "00:00:00:00";
    public int CustomStartFrame { get; set; } = 0;

    public SlideshowLengthSetting LengthSetting { get; set; } = SlideshowLengthSetting.PerSlide;
    public double SecondsPerSlide { get; set; } = DefaultDurationSeconds;
    public double SecondsOfOverlap { get; set; } = DefaultOverlapSeconds;
    public double TotalSlideshowSeconds { get; set; } = DefaultTotalLengthSeconds;

    public SlideshowEffectKind Effect { get; set; } = SlideshowEffectKind.Crossfade;
    public double PanScaleAmount { get; set; } = DefaultPanScaleAmount;

    public Guid? TargetTrackId { get; set; }

    public List<SlideshowPicture> Pictures { get; set; } = [];

    /// <summary>
    /// Computes the effective slide duration in seconds based on current settings and picture count.
    /// In TotalLength mode: effective = (Total + (N - 1) * overlap) / N.
    /// In PerSlide mode: effective = SecondsPerSlide.
    /// </summary>
    public double ComputeEffectiveSecondsPerSlide()
    {
        if (Pictures.Count == 0) return SecondsPerSlide;
        if (LengthSetting == SlideshowLengthSetting.PerSlide) return Math.Max(0.5, SecondsPerSlide);

        int count = Pictures.Count;
        if (count == 1) return Math.Max(0.5, TotalSlideshowSeconds);

        double totalOverlap = (count - 1) * SecondsOfOverlap;
        double grossDuration = TotalSlideshowSeconds + totalOverlap;
        if (grossDuration <= 0) grossDuration = count * 0.5;

        return Math.Max(0.5, grossDuration / count);
    }

    /// <summary>
    /// Computes the total timeline span of the resulting slideshow in seconds.
    /// </summary>
    public double ComputeTotalTimelineSeconds()
    {
        int count = Pictures.Count;
        if (count == 0) return 0.0;
        double perSlide = ComputeEffectiveSecondsPerSlide();
        if (count == 1) return perSlide;
        return (count * perSlide) - ((count - 1) * SecondsOfOverlap);
    }

    public void Validate()
    {
        if (Pictures.Count == 0)
            throw new InvalidOperationException("Slideshow must contain at least one picture.");
        if (SecondsPerSlide <= 0)
            throw new InvalidOperationException("Seconds per slide must be greater than zero.");
        if (SecondsOfOverlap < 0)
            throw new InvalidOperationException("Seconds of overlap cannot be negative.");
        if (SecondsOfOverlap >= SecondsPerSlide)
            throw new InvalidOperationException("Seconds of overlap must be strictly less than seconds per slide.");
        if (CustomStartFrame < 0)
            throw new InvalidOperationException("Custom start frame cannot be negative.");

        foreach (var pic in Pictures)
        {
            pic.Validate();
        }
    }

    public SlideshowSettings DeepClone() => new()
    {
        StartMode = StartMode,
        StartTimecode = StartTimecode,
        CustomStartFrame = CustomStartFrame,
        LengthSetting = LengthSetting,
        SecondsPerSlide = SecondsPerSlide,
        SecondsOfOverlap = SecondsOfOverlap,
        TotalSlideshowSeconds = TotalSlideshowSeconds,
        Effect = Effect,
        PanScaleAmount = PanScaleAmount,
        TargetTrackId = TargetTrackId,
        Pictures = Pictures.Select(p => p.DeepClone()).ToList()
    };

    public bool Equals(SlideshowSettings? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return StartMode == other.StartMode
            && StartTimecode == other.StartTimecode
            && CustomStartFrame == other.CustomStartFrame
            && LengthSetting == other.LengthSetting
            && Math.Abs(SecondsPerSlide - other.SecondsPerSlide) < 1e-6
            && Math.Abs(SecondsOfOverlap - other.SecondsOfOverlap) < 1e-6
            && Math.Abs(TotalSlideshowSeconds - other.TotalSlideshowSeconds) < 1e-6
            && Effect == other.Effect
            && Math.Abs(PanScaleAmount - other.PanScaleAmount) < 1e-6
            && TargetTrackId == other.TargetTrackId
            && Pictures.SequenceEqual(other.Pictures);
    }

    public override bool Equals(object? obj) => Equals(obj as SlideshowSettings);

    public override int GetHashCode() =>
        HashCode.Combine(StartMode, LengthSetting, SecondsPerSlide, SecondsOfOverlap, Effect, Pictures.Count);
}

