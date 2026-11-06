using System;
using System.IO;

namespace OpenRend.Core;

/// <summary>
/// Specifies the target action when scene cuts are detected,
/// matching 1:1 Vegas Pro SceneDetectionCmd.Mode enum:
/// SplitOnTimeLine, ApplyAsMediaFXAndGenerateSubclips, OnlyAddRegionMarkers.
/// </summary>
public enum SceneDetectionMode
{
    /// <summary>
    /// Split the video clip and its grouped audio clips at each cut point directly on the timeline.
    /// </summary>
    SplitOnTimeline = 0,

    /// <summary>
    /// Add timeline ruler Region markers covering each detected shot span [start, end].
    /// </summary>
    AddRegionMarkers = 1,

    /// <summary>
    /// Create named subclip references in the media pool (<MediaName>.<0001>, <MediaName>.<0002>).
    /// </summary>
    CreateSubclips = 2
}

/// <summary>
/// Type of detected scene boundary transition.
/// </summary>
public enum SceneCutType
{
    HardCut = 0,
    Fade = 1,
    Dissolve = 2
}

/// <summary>
/// Configuration parameters for scene and cut detection across video assets.
/// Aligned with Vegas Pro ShotDetector OFX parameters (HardCuts, SensitivityHardCuts, MinSceneDuration).
/// </summary>
public sealed class SceneDetectionOptions : IEquatable<SceneDetectionOptions>
{
    /// <summary>
    /// Cut detection sensitivity from 0.0 (low, only drastic cuts) to 1.0 (high, subtle angle changes).
    /// Default is 0.65 (65% sensitivity, corresponding to ~28-32 threshold on proxy frames).
    /// </summary>
    public double Sensitivity { get; set; } = 0.65;

    /// <summary>
    /// Normalized cut threshold between 5.0 (very sensitive) and 80.0 (very strict).
    /// Defaults to (1.0 - Sensitivity) * 55.0 + 10.0 (~29.25 at 0.65 sensitivity).
    /// </summary>
    public double Threshold
    {
        get => Math.Clamp((1.0 - Math.Clamp(Sensitivity, 0.0, 1.0)) * 55.0 + 10.0, 5.0, 80.0);
        set => Sensitivity = Math.Clamp(1.0 - (value - 10.0) / 55.0, 0.0, 1.0);
    }

    /// <summary>
    /// Minimum duration in milliseconds for an individual scene shot before another cut is registered.
    /// Debounces rapid flashes and motion blur; Vegas Pro default is 500.0 ms.
    /// </summary>
    public double MinSceneLengthMs { get; set; } = 500.0;

    /// <summary>
    /// Whether hard cuts (abrupt camera/scene changes) should be detected. Default true.
    /// </summary>
    public bool DetectHardCuts { get; set; } = true;

    /// <summary>
    /// Whether fade-to-black and fade-from-black transitions should be detected. Default false.
    /// </summary>
    public bool DetectFades { get; set; } = false;

    /// <summary>
    /// Whether cross-dissolve transitions should be detected. Default false.
    /// </summary>
    public bool DetectDissolves { get; set; } = false;

    /// <summary>
    /// When splitting on timeline, whether linked and grouped audio tracks are split in lockstep (Vegas Pro SplitGroup).
    /// </summary>
    public bool IncludeGroupedTracks { get; set; } = true;

    /// <summary>
    /// Calculates the minimum frame interval given the project frame rate.
    /// </summary>
    public int CalculateMinFrames(Rational frameRate)
    {
        var fps = frameRate.Denominator > 0 ? (double)frameRate.Numerator / frameRate.Denominator : 30.0;
        return Math.Max(3, (int)Math.Round(MinSceneLengthMs / 1000.0 * fps));
    }

    public SceneDetectionOptions DeepClone() => new()
    {
        Sensitivity = Sensitivity,
        MinSceneLengthMs = MinSceneLengthMs,
        DetectHardCuts = DetectHardCuts,
        DetectFades = DetectFades,
        DetectDissolves = DetectDissolves,
        IncludeGroupedTracks = IncludeGroupedTracks
    };

    public void Validate()
    {
        if (!double.IsFinite(Sensitivity) || Sensitivity < 0.0 || Sensitivity > 1.0)
            throw new InvalidDataException("Sensitivity must be a finite number between 0.0 and 1.0.");
        if (!double.IsFinite(MinSceneLengthMs) || MinSceneLengthMs < 50.0 || MinSceneLengthMs > 60_000.0)
            throw new InvalidDataException("Minimum scene duration must be between 50 ms and 60,000 ms.");
    }

    public bool Equals(SceneDetectionOptions? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Math.Abs(Sensitivity - other.Sensitivity) < 1e-4 &&
               Math.Abs(MinSceneLengthMs - other.MinSceneLengthMs) < 1e-2 &&
               DetectHardCuts == other.DetectHardCuts &&
               DetectFades == other.DetectFades &&
               DetectDissolves == other.DetectDissolves &&
               IncludeGroupedTracks == other.IncludeGroupedTracks;
    }

    public override bool Equals(object? obj) => Equals(obj as SceneDetectionOptions);

    public override int GetHashCode() => HashCode.Combine(Sensitivity, MinSceneLengthMs, DetectHardCuts, DetectFades, DetectDissolves, IncludeGroupedTracks);
}

/// <summary>
/// Represents a detected camera cut or scene boundary.
/// </summary>
public sealed record SceneCut(
    long FrameOffset,
    long AbsoluteFrame,
    double TimestampSeconds,
    double Score,
    SceneCutType CutType
);

/// <summary>
/// Represents a detected continuous scene span [StartOffset, EndOffset].
/// </summary>
public sealed record SceneSpan(
    int Index,
    long StartOffsetFrames,
    long EndOffsetFrames,
    long StartAbsoluteFrame,
    long EndAbsoluteFrame,
    string Name
)
{
    public long DurationFrames => Math.Max(0, EndOffsetFrames - StartOffsetFrames);
}

