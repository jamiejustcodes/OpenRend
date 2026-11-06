using System;
using System.Text.Json.Serialization;

namespace OpenRend.Core;

/// <summary>
/// Defines the sidechain audio ducking role for a timeline audio track, matching Vegas Pro 22+ Auto Ducking.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AudioDuckingRole>))]
public enum AudioDuckingRole
{
    /// <summary>
    /// Track does not participate in auto-ducking.
    /// </summary>
    None = 0,

    /// <summary>
    /// Controller / trigger track containing dialogue, voiceover, or narration.
    /// When speech is detected on this track, listener tracks duck in volume.
    /// </summary>
    Controller = 1,

    /// <summary>
    /// Listener track containing background music, ambiance, or sound effects.
    /// Attenuates volume automatically whenever speech is present on controller tracks.
    /// </summary>
    Listener = 2
}

/// <summary>
/// Configuration parameters for sidechain audio ducking on listener tracks.
/// </summary>
public sealed class AudioDuckingSettings : IEquatable<AudioDuckingSettings>
{
    public const double DefaultDuckLevelDb = -12.0;
    public const double DefaultThresholdDb = -30.0;
    public const double DefaultAttackMs = 200.0;
    public const double DefaultHoldMs = 600.0;
    public const double DefaultReleaseMs = 800.0;

    /// <summary>
    /// Whether dynamic realtime ducking is active for this listener track.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Specific controller track ID, or null to listen to all active controller tracks in the project.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ControllerTrackId { get; set; }

    /// <summary>
    /// Attenuation level in decibels when ducking is active (typically -6.0 dB to -30.0 dB, default -12.0 dB).
    /// Value is clamped to [-60.0, 0.0] dB.
    /// </summary>
    public double DuckLevelDb { get; set; } = DefaultDuckLevelDb;

    /// <summary>
    /// Speech detection threshold in decibels (typically -48.0 dB to -12.0 dB, default -30.0 dB).
    /// Signal levels above this threshold trigger ducking.
    /// </summary>
    public double ThresholdDb { get; set; } = DefaultThresholdDb;

    /// <summary>
    /// Ramp-down attack duration in milliseconds when speech begins (default 200 ms).
    /// Clamped to [10.0, 2000.0] ms.
    /// </summary>
    public double AttackMs { get; set; } = DefaultAttackMs;

    /// <summary>
    /// Hold duration in milliseconds to maintain ducked level during brief speech pauses (default 600 ms).
    /// Clamped to [0.0, 5000.0] ms.
    /// </summary>
    public double HoldMs { get; set; } = DefaultHoldMs;

    /// <summary>
    /// Ramp-up release recovery duration in milliseconds when speech ceases (default 800 ms).
    /// Clamped to [50.0, 5000.0] ms.
    /// </summary>
    public double ReleaseMs { get; set; } = DefaultReleaseMs;

    /// <summary>
    /// Calculates the linear gain multiplier corresponding to DuckLevelDb (e.g. -12 dB -> ~0.2511886).
    /// </summary>
    [JsonIgnore]
    public double LinearDuckMultiplier => Math.Pow(10.0, Math.Clamp(DuckLevelDb, -96.0, 0.0) / 20.0);

    public void Validate()
    {
        if (!double.IsFinite(DuckLevelDb)) DuckLevelDb = DefaultDuckLevelDb;
        if (!double.IsFinite(ThresholdDb)) ThresholdDb = DefaultThresholdDb;
        if (!double.IsFinite(AttackMs)) AttackMs = DefaultAttackMs;
        if (!double.IsFinite(HoldMs)) HoldMs = DefaultHoldMs;
        if (!double.IsFinite(ReleaseMs)) ReleaseMs = DefaultReleaseMs;

        DuckLevelDb = Math.Clamp(DuckLevelDb, -60.0, 0.0);
        ThresholdDb = Math.Clamp(ThresholdDb, -60.0, -6.0);
        AttackMs = Math.Clamp(AttackMs, 10.0, 2000.0);
        HoldMs = Math.Clamp(HoldMs, 0.0, 5000.0);
        ReleaseMs = Math.Clamp(ReleaseMs, 50.0, 5000.0);
    }

    public AudioDuckingSettings DeepClone() => new()
    {
        IsEnabled = IsEnabled,
        ControllerTrackId = ControllerTrackId,
        DuckLevelDb = DuckLevelDb,
        ThresholdDb = ThresholdDb,
        AttackMs = AttackMs,
        HoldMs = HoldMs,
        ReleaseMs = ReleaseMs
    };

    public bool Equals(AudioDuckingSettings? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return IsEnabled == other.IsEnabled
            && ControllerTrackId == other.ControllerTrackId
            && Math.Abs(DuckLevelDb - other.DuckLevelDb) < 1e-4
            && Math.Abs(ThresholdDb - other.ThresholdDb) < 1e-4
            && Math.Abs(AttackMs - other.AttackMs) < 1e-4
            && Math.Abs(HoldMs - other.HoldMs) < 1e-4
            && Math.Abs(ReleaseMs - other.ReleaseMs) < 1e-4;
    }

    public override bool Equals(object? obj) => Equals(obj as AudioDuckingSettings);

    public override int GetHashCode() =>
        HashCode.Combine(IsEnabled, ControllerTrackId, Math.Round(DuckLevelDb, 2), Math.Round(ThresholdDb, 2));
}
