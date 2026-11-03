using System;
using System.Collections.Generic;
using OpenRend.Core;

namespace OpenRend.Media;

/// <summary>
/// Direct Form II Transposed Biquad Filter Coefficients and State for Stereo Channels.
/// </summary>
public struct BiquadFilter
{
    public double B0, B1, B2;
    public double A1, A2;

    // Stereo Direct Form II Transposed internal states
    public double S1L, S2L;
    public double S1R, S2R;

    public void Reset()
    {
        S1L = S2L = S1R = S2R = 0.0;
    }

    public static BiquadFilter PeakingEq(double sampleRate, double freq, double gainDb, double q)
    {
        var filter = new BiquadFilter();
        freq = Math.Clamp(freq, 20.0, sampleRate * 0.49);
        q = Math.Max(0.05, q);

        var w0 = 2.0 * Math.PI * freq / sampleRate;
        var cosw0 = Math.Cos(w0);
        var sinw0 = Math.Sin(w0);
        var alpha = sinw0 / (2.0 * q);
        var a = Math.Pow(10.0, gainDb / 40.0);

        var b0 = 1.0 + alpha * a;
        var b1 = -2.0 * cosw0;
        var b2 = 1.0 - alpha * a;
        var a0 = 1.0 + alpha / a;
        var a1 = -2.0 * cosw0;
        var a2 = 1.0 - alpha / a;

        filter.B0 = b0 / a0;
        filter.B1 = b1 / a0;
        filter.B2 = b2 / a0;
        filter.A1 = a1 / a0;
        filter.A2 = a2 / a0;
        return filter;
    }

    public static BiquadFilter LowShelf(double sampleRate, double freq, double gainDb, double q)
    {
        var filter = new BiquadFilter();
        freq = Math.Clamp(freq, 20.0, sampleRate * 0.49);
        q = Math.Max(0.05, q);

        var w0 = 2.0 * Math.PI * freq / sampleRate;
        var cosw0 = Math.Cos(w0);
        var sinw0 = Math.Sin(w0);
        var alpha = sinw0 / (2.0 * q);
        var a = Math.Pow(10.0, gainDb / 40.0);
        var sqrtA = Math.Sqrt(a);

        var b0 = a * ((a + 1.0) - (a - 1.0) * cosw0 + 2.0 * sqrtA * alpha);
        var b1 = 2.0 * a * ((a - 1.0) - (a + 1.0) * cosw0);
        var b2 = a * ((a + 1.0) - (a - 1.0) * cosw0 - 2.0 * sqrtA * alpha);
        var a0 = (a + 1.0) + (a - 1.0) * cosw0 + 2.0 * sqrtA * alpha;
        var a1 = -2.0 * ((a - 1.0) + (a + 1.0) * cosw0);
        var a2 = (a + 1.0) + (a - 1.0) * cosw0 - 2.0 * sqrtA * alpha;

        filter.B0 = b0 / a0;
        filter.B1 = b1 / a0;
        filter.B2 = b2 / a0;
        filter.A1 = a1 / a0;
        filter.A2 = a2 / a0;
        return filter;
    }

    public static BiquadFilter HighShelf(double sampleRate, double freq, double gainDb, double q)
    {
        var filter = new BiquadFilter();
        freq = Math.Clamp(freq, 20.0, sampleRate * 0.49);
        q = Math.Max(0.05, q);

        var w0 = 2.0 * Math.PI * freq / sampleRate;
        var cosw0 = Math.Cos(w0);
        var sinw0 = Math.Sin(w0);
        var alpha = sinw0 / (2.0 * q);
        var a = Math.Pow(10.0, gainDb / 40.0);
        var sqrtA = Math.Sqrt(a);

        var b0 = a * ((a + 1.0) + (a - 1.0) * cosw0 + 2.0 * sqrtA * alpha);
        var b1 = -2.0 * a * ((a - 1.0) + (a + 1.0) * cosw0);
        var b2 = a * ((a + 1.0) + (a - 1.0) * cosw0 - 2.0 * sqrtA * alpha);
        var a0 = (a + 1.0) - (a - 1.0) * cosw0 + 2.0 * sqrtA * alpha;
        var a1 = 2.0 * ((a - 1.0) - (a + 1.0) * cosw0);
        var a2 = (a + 1.0) - (a - 1.0) * cosw0 - 2.0 * sqrtA * alpha;

        filter.B0 = b0 / a0;
        filter.B1 = b1 / a0;
        filter.B2 = b2 / a0;
        filter.A1 = a1 / a0;
        filter.A2 = a2 / a0;
        return filter;
    }

    public void ProcessStereo(ref float left, ref float right)
    {
        // Left channel Direct Form II Transposed
        var yL = B0 * left + S1L;
        S1L = B1 * left - A1 * yL + S2L;
        S2L = B2 * left - A2 * yL;
        left = (float)yL;

        // Right channel Direct Form II Transposed
        var yR = B0 * right + S1R;
        S1R = B1 * right - A1 * yR + S2R;
        S2R = B2 * right - A2 * yR;
        right = (float)yR;
    }
}

/// <summary>
/// High performance Vegas Pro 1:1 Audio DSP processing engine.
/// Provides Track EQ, Track Compressor, Track Noise Gate, Phase Inversion, and Peak/RMS metering.
/// </summary>
public static class AudioDspService
{
    public const int DefaultSampleRate = 48000;

    /// <summary>
    /// Processes an audio buffer through a list of AudioEffects in-place.
    /// Buffer layout is interleaved stereo: [L0, R0, L1, R1, ...].
    /// </summary>
    public static void ProcessAudioChain(float[] buffer, int sampleRate, IReadOnlyList<AudioEffect> effects)
    {
        if (buffer == null || buffer.Length == 0 || effects == null || effects.Count == 0) return;

        foreach (var fx in effects)
        {
            if (fx == null || fx.IsBypassed) continue;

            switch (fx.Kind)
            {
                case AudioEffectKind.TrackEQ:
                    ProcessTrackEq(buffer, sampleRate, fx.Parameters);
                    break;
                case AudioEffectKind.TrackCompressor:
                    ProcessTrackCompressor(buffer, sampleRate, fx.Parameters);
                    break;
                case AudioEffectKind.TrackNoiseGate:
                    ProcessTrackNoiseGate(buffer, sampleRate, fx.Parameters);
                    break;
                case AudioEffectKind.MasterVolume:
                    var gainDb = fx.Parameters.GetValueOrDefault("GainDb", 0.0);
                    var factor = (float)Math.Pow(10.0, gainDb / 20.0);
                    if (Math.Abs(factor - 1.0f) > 1e-4f)
                    {
                        for (int i = 0; i < buffer.Length; i++) buffer[i] *= factor;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Inverts the audio phase in-place: x[n] = -x[n].
    /// </summary>
    public static void InvertPhase(float[] buffer)
    {
        if (buffer == null) return;
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = -buffer[i];
        }
    }

    /// <summary>
    /// Evaluates 4-band Parametric Track EQ across the interleaved stereo buffer.
    /// </summary>
    public static void ProcessTrackEq(float[] buffer, int sampleRate, IReadOnlyDictionary<string, double> parameters)
    {
        var b1Enabled = parameters.GetValueOrDefault("Band1_Enabled", 1.0) > 0.5;
        var b1Gain = parameters.GetValueOrDefault("Band1_GainDb", 0.0);
        var b1Freq = parameters.GetValueOrDefault("Band1_Freq", 80.0);
        var b1Q = parameters.GetValueOrDefault("Band1_Q", 1.0);

        var b2Enabled = parameters.GetValueOrDefault("Band2_Enabled", 1.0) > 0.5;
        var b2Gain = parameters.GetValueOrDefault("Band2_GainDb", 0.0);
        var b2Freq = parameters.GetValueOrDefault("Band2_Freq", 250.0);
        var b2Q = parameters.GetValueOrDefault("Band2_Q", 1.0);

        var b3Enabled = parameters.GetValueOrDefault("Band3_Enabled", 1.0) > 0.5;
        var b3Gain = parameters.GetValueOrDefault("Band3_GainDb", 0.0);
        var b3Freq = parameters.GetValueOrDefault("Band3_Freq", 2500.0);
        var b3Q = parameters.GetValueOrDefault("Band3_Q", 1.0);

        var b4Enabled = parameters.GetValueOrDefault("Band4_Enabled", 1.0) > 0.5;
        var b4Gain = parameters.GetValueOrDefault("Band4_GainDb", 0.0);
        var b4Freq = parameters.GetValueOrDefault("Band4_Freq", 8000.0);
        var b4Q = parameters.GetValueOrDefault("Band4_Q", 1.0);

        var masterGainDb = parameters.GetValueOrDefault("MasterGainDb", 0.0);
        var masterGain = (float)Math.Pow(10.0, masterGainDb / 20.0);

        var filter1 = b1Enabled && Math.Abs(b1Gain) > 0.01 ? BiquadFilter.LowShelf(sampleRate, b1Freq, b1Gain, b1Q) : default;
        var filter2 = b2Enabled && Math.Abs(b2Gain) > 0.01 ? BiquadFilter.PeakingEq(sampleRate, b2Freq, b2Gain, b2Q) : default;
        var filter3 = b3Enabled && Math.Abs(b3Gain) > 0.01 ? BiquadFilter.PeakingEq(sampleRate, b3Freq, b3Gain, b3Q) : default;
        var filter4 = b4Enabled && Math.Abs(b4Gain) > 0.01 ? BiquadFilter.HighShelf(sampleRate, b4Freq, b4Gain, b4Q) : default;

        var use1 = b1Enabled && Math.Abs(b1Gain) > 0.01;
        var use2 = b2Enabled && Math.Abs(b2Gain) > 0.01;
        var use3 = b3Enabled && Math.Abs(b3Gain) > 0.01;
        var use4 = b4Enabled && Math.Abs(b4Gain) > 0.01;

        if (!use1 && !use2 && !use3 && !use4 && Math.Abs(masterGain - 1.0f) < 1e-4f)
        {
            return; // No-op bypass
        }

        int frameCount = buffer.Length / 2;
        for (int i = 0; i < frameCount; i++)
        {
            int idx = i * 2;
            float left = buffer[idx];
            float right = buffer[idx + 1];

            if (use1) filter1.ProcessStereo(ref left, ref right);
            if (use2) filter2.ProcessStereo(ref left, ref right);
            if (use3) filter3.ProcessStereo(ref left, ref right);
            if (use4) filter4.ProcessStereo(ref left, ref right);

            buffer[idx] = left * masterGain;
            buffer[idx + 1] = right * masterGain;
        }
    }

    /// <summary>
    /// Evaluates Vegas Pro Track Dynamic Compressor across the interleaved stereo buffer.
    /// </summary>
    public static void ProcessTrackCompressor(float[] buffer, int sampleRate, IReadOnlyDictionary<string, double> parameters)
    {
        var thresholdDb = parameters.GetValueOrDefault("ThresholdDb", -18.0);
        var ratio = Math.Max(1.0, parameters.GetValueOrDefault("Ratio", 4.0));
        var attackMs = Math.Max(0.1, parameters.GetValueOrDefault("AttackMs", 15.0));
        var releaseMs = Math.Max(1.0, parameters.GetValueOrDefault("ReleaseMs", 100.0));
        var outputGainDb = parameters.GetValueOrDefault("OutputGainDb", 0.0);
        var makeupLinear = (float)Math.Pow(10.0, outputGainDb / 20.0);

        var attackCoeff = Math.Exp(-1.0 / (sampleRate * (attackMs * 0.001)));
        var releaseCoeff = Math.Exp(-1.0 / (sampleRate * (releaseMs * 0.001)));

        double currentGainDb = 0.0;
        int frameCount = buffer.Length / 2;

        for (int i = 0; i < frameCount; i++)
        {
            int idx = i * 2;
            float left = buffer[idx];
            float right = buffer[idx + 1];

            // Linked stereo peak detector
            double peak = Math.Max(Math.Abs(left), Math.Abs(right));
            double peakDb = peak > 1e-5 ? 20.0 * Math.Log10(peak) : -100.0;

            // Target gain reduction in dB
            double targetGainDb = 0.0;
            if (peakDb > thresholdDb)
            {
                targetGainDb = (thresholdDb + (peakDb - thresholdDb) / ratio) - peakDb; // negative dB
            }

            // Attack/Release smoothing
            if (targetGainDb < currentGainDb)
            {
                currentGainDb = targetGainDb + (currentGainDb - targetGainDb) * attackCoeff;
            }
            else
            {
                currentGainDb = targetGainDb + (currentGainDb - targetGainDb) * releaseCoeff;
            }

            float gainLinear = (float)Math.Pow(10.0, currentGainDb / 20.0) * makeupLinear;
            buffer[idx] = left * gainLinear;
            buffer[idx + 1] = right * gainLinear;
        }
    }

    /// <summary>
    /// Evaluates Vegas Pro Track Noise Gate across the interleaved stereo buffer.
    /// </summary>
    public static void ProcessTrackNoiseGate(float[] buffer, int sampleRate, IReadOnlyDictionary<string, double> parameters)
    {
        var thresholdDb = parameters.GetValueOrDefault("ThresholdDb", -45.0);
        var attackMs = Math.Max(0.1, parameters.GetValueOrDefault("AttackMs", 5.0));
        var releaseMs = Math.Max(1.0, parameters.GetValueOrDefault("ReleaseMs", 150.0));
        var dampingDb = parameters.GetValueOrDefault("DampingDb", -60.0);
        var dampingLinear = (float)Math.Pow(10.0, dampingDb / 20.0);

        var attackCoeff = Math.Exp(-1.0 / (sampleRate * (attackMs * 0.001)));
        var releaseCoeff = Math.Exp(-1.0 / (sampleRate * (releaseMs * 0.001)));

        double gateEnvelope = 0.0; // 0 = closed (damping), 1 = open
        int frameCount = buffer.Length / 2;

        for (int i = 0; i < frameCount; i++)
        {
            int idx = i * 2;
            float left = buffer[idx];
            float right = buffer[idx + 1];

            double peak = Math.Max(Math.Abs(left), Math.Abs(right));
            double peakDb = peak > 1e-5 ? 20.0 * Math.Log10(peak) : -100.0;

            double target = peakDb >= thresholdDb ? 1.0 : 0.0;

            if (target > gateEnvelope)
            {
                gateEnvelope = target + (gateEnvelope - target) * attackCoeff;
            }
            else
            {
                gateEnvelope = target + (gateEnvelope - target) * releaseCoeff;
            }

            float gain = (float)(dampingLinear + (1.0 - dampingLinear) * gateEnvelope);
            buffer[idx] = left * gain;
            buffer[idx + 1] = right * gain;
        }
    }

    /// <summary>
    /// Calculates peak (0.0 to 1.0+) and RMS levels for stereo audio buffer.
    /// </summary>
    public static (double PeakLeft, double PeakRight, double RmsLeft, double RmsRight) MeasureLevels(float[] buffer)
    {
        if (buffer == null || buffer.Length == 0) return (0, 0, 0, 0);

        double peakL = 0, peakR = 0;
        double sumSqL = 0, sumSqR = 0;
        int frames = buffer.Length / 2;

        for (int i = 0; i < frames; i++)
        {
            double l = Math.Abs(buffer[i * 2]);
            double r = Math.Abs(buffer[i * 2 + 1]);

            if (l > peakL) peakL = l;
            if (r > peakR) peakR = r;

            sumSqL += l * l;
            sumSqR += r * r;
        }

        double rmsL = Math.Sqrt(sumSqL / Math.Max(1, frames));
        double rmsR = Math.Sqrt(sumSqR / Math.Max(1, frames));

        return (peakL, peakR, rmsL, rmsR);
    }
}
