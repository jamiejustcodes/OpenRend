using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace OpenRend.Core;

/// <summary>
/// Vegas Pro 1:1 Audio Effect Plug-In Types.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AudioEffectKind>))]
public enum AudioEffectKind
{
    TrackEQ,
    TrackCompressor,
    TrackNoiseGate,
    MasterVolume
}

/// <summary>
/// Filter shape for individual bands in the 4-band Parametric Track EQ.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EqBandShape>))]
public enum EqBandShape
{
    LowShelf,
    Peaking,
    HighShelf
}

/// <summary>
/// Vegas Pro 1:1 Audio Effect Definition and Parameter Storage.
/// </summary>
public sealed class AudioEffect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AudioEffectKind Kind { get; set; }
    public string Name { get; set; } = "Audio Effect";
    public bool IsBypassed { get; set; }
    public Dictionary<string, double> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AudioEffect DeepClone()
    {
        return new AudioEffect
        {
            Id = Id,
            Kind = Kind,
            Name = Name,
            IsBypassed = IsBypassed,
            Parameters = new Dictionary<string, double>(Parameters, StringComparer.OrdinalIgnoreCase)
        };
    }

    public void Validate()
    {
        if (Id == Guid.Empty) throw new InvalidDataException("Audio effect ID cannot be empty.");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Audio effect name cannot be empty.");
        if (!Enum.IsDefined(Kind)) throw new InvalidDataException("Audio effect kind is invalid.");

        foreach (var (k, v) in Parameters)
        {
            if (string.IsNullOrWhiteSpace(k)) throw new InvalidDataException("Audio effect parameter name cannot be empty.");
            if (!double.IsFinite(v)) throw new InvalidDataException($"Audio effect parameter '{k}' must be finite.");
        }
    }

    public static AudioEffect CreateTrackEq(string preset = "[Sys] Default")
    {
        return AudioEffectCatalog.CreateTrackEq(preset);
    }

    public static AudioEffect CreateTrackCompressor(string preset = "[Sys] Default")
    {
        return AudioEffectCatalog.CreateTrackCompressor(preset);
    }

    public static AudioEffect CreateTrackNoiseGate(string preset = "[Sys] Default")
    {
        return AudioEffectCatalog.CreateTrackNoiseGate(preset);
    }
}

/// <summary>
/// Built-in Vegas Pro Audio Effect Preset Catalog.
/// </summary>
public static class AudioEffectCatalog
{
    public static AudioEffect CreateTrackEq(string preset = "[Sys] Default")
    {
        var fx = new AudioEffect
        {
            Id = Guid.NewGuid(),
            Kind = AudioEffectKind.TrackEQ,
            Name = "Track EQ",
            IsBypassed = false,
            Parameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                // Band 1: Low Shelf (Default 80 Hz, 0 dB, Q 1.0)
                ["Band1_Enabled"] = 1.0,
                ["Band1_Freq"] = 80.0,
                ["Band1_GainDb"] = 0.0,
                ["Band1_Q"] = 1.0,

                // Band 2: Peaking (Default 250 Hz, 0 dB, Q 1.0)
                ["Band2_Enabled"] = 1.0,
                ["Band2_Freq"] = 250.0,
                ["Band2_GainDb"] = 0.0,
                ["Band2_Q"] = 1.0,

                // Band 3: Peaking (Default 2,500 Hz, 0 dB, Q 1.0)
                ["Band3_Enabled"] = 1.0,
                ["Band3_Freq"] = 2500.0,
                ["Band3_GainDb"] = 0.0,
                ["Band3_Q"] = 1.0,

                // Band 4: High Shelf (Default 8,000 Hz, 0 dB, Q 1.0)
                ["Band4_Enabled"] = 1.0,
                ["Band4_Freq"] = 8000.0,
                ["Band4_GainDb"] = 0.0,
                ["Band4_Q"] = 1.0,

                // Master EQ Output Gain
                ["MasterGainDb"] = 0.0
            }
        };

        switch (preset)
        {
            case "Warm Vocal":
                fx.Parameters["Band1_GainDb"] = 1.5;
                fx.Parameters["Band2_Freq"] = 400.0;
                fx.Parameters["Band2_GainDb"] = -2.0;
                fx.Parameters["Band3_Freq"] = 3500.0;
                fx.Parameters["Band3_GainDb"] = 2.5;
                fx.Parameters["Band4_GainDb"] = 2.0;
                break;
            case "Bass Boost (+6 dB)":
                fx.Parameters["Band1_Freq"] = 100.0;
                fx.Parameters["Band1_GainDb"] = 6.0;
                break;
            case "Treble Boost (+6 dB)":
                fx.Parameters["Band4_Freq"] = 7000.0;
                fx.Parameters["Band4_GainDb"] = 6.0;
                break;
            case "Mid Cut (-4 dB at 1 kHz)":
                fx.Parameters["Band2_Freq"] = 1000.0;
                fx.Parameters["Band2_GainDb"] = -4.0;
                fx.Parameters["Band2_Q"] = 1.4;
                break;
            case "Telephone / Radio Bandpass":
                fx.Parameters["Band1_Freq"] = 400.0;
                fx.Parameters["Band1_GainDb"] = -18.0;
                fx.Parameters["Band2_Freq"] = 1200.0;
                fx.Parameters["Band2_GainDb"] = 4.0;
                fx.Parameters["Band3_Freq"] = 2400.0;
                fx.Parameters["Band3_GainDb"] = 3.0;
                fx.Parameters["Band4_Freq"] = 3500.0;
                fx.Parameters["Band4_GainDb"] = -18.0;
                break;
        }

        return fx;
    }

    public static AudioEffect CreateTrackCompressor(string preset = "[Sys] Default")
    {
        var fx = new AudioEffect
        {
            Id = Guid.NewGuid(),
            Kind = AudioEffectKind.TrackCompressor,
            Name = "Track Compressor",
            IsBypassed = false,
            Parameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["ThresholdDb"] = -18.0,
                ["Ratio"] = 4.0,
                ["AttackMs"] = 15.0,
                ["ReleaseMs"] = 100.0,
                ["OutputGainDb"] = 0.0
            }
        };

        switch (preset)
        {
            case "Gentle Compression (2:1)":
                fx.Parameters["ThresholdDb"] = -14.0;
                fx.Parameters["Ratio"] = 2.0;
                fx.Parameters["AttackMs"] = 25.0;
                fx.Parameters["ReleaseMs"] = 150.0;
                fx.Parameters["OutputGainDb"] = 1.0;
                break;
            case "Vocal Leveler (4:1, -18 dB)":
                fx.Parameters["ThresholdDb"] = -18.0;
                fx.Parameters["Ratio"] = 4.0;
                fx.Parameters["AttackMs"] = 10.0;
                fx.Parameters["ReleaseMs"] = 120.0;
                fx.Parameters["OutputGainDb"] = 3.0;
                break;
            case "Heavy Limiting (10:1)":
                fx.Parameters["ThresholdDb"] = -10.0;
                fx.Parameters["Ratio"] = 10.0;
                fx.Parameters["AttackMs"] = 2.0;
                fx.Parameters["ReleaseMs"] = 80.0;
                fx.Parameters["OutputGainDb"] = 2.0;
                break;
            case "Snappy Drums (Fast Attack)":
                fx.Parameters["ThresholdDb"] = -16.0;
                fx.Parameters["Ratio"] = 5.0;
                fx.Parameters["AttackMs"] = 1.5;
                fx.Parameters["ReleaseMs"] = 60.0;
                fx.Parameters["OutputGainDb"] = 2.5;
                break;
        }

        return fx;
    }

    public static AudioEffect CreateTrackNoiseGate(string preset = "[Sys] Default")
    {
        var fx = new AudioEffect
        {
            Id = Guid.NewGuid(),
            Kind = AudioEffectKind.TrackNoiseGate,
            Name = "Track Noise Gate",
            IsBypassed = false,
            Parameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["ThresholdDb"] = -45.0,
                ["AttackMs"] = 5.0,
                ["ReleaseMs"] = 150.0,
                ["DampingDb"] = -60.0
            }
        };

        switch (preset)
        {
            case "Strict Gate (-35 dB)":
                fx.Parameters["ThresholdDb"] = -35.0;
                fx.Parameters["AttackMs"] = 2.0;
                fx.Parameters["ReleaseMs"] = 100.0;
                break;
            case "Gentle Ambient Gate (-55 dB)":
                fx.Parameters["ThresholdDb"] = -55.0;
                fx.Parameters["AttackMs"] = 10.0;
                fx.Parameters["ReleaseMs"] = 200.0;
                break;
        }

        return fx;
    }

    /// <summary>
    /// Returns the standard default Vegas Pro audio track FX chain:
    /// 1. Track Noise Gate (bypassed by default)
    /// 2. Track EQ
    /// 3. Track Compressor
    /// </summary>
    public static List<AudioEffect> CreateDefaultTrackChain()
    {
        var gate = CreateTrackNoiseGate();
        gate.IsBypassed = true; // Noise gate is inactive by default in Vegas Pro default chain
        return [gate, CreateTrackEq(), CreateTrackCompressor()];
    }
}
