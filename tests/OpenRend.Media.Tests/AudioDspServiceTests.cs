using System;
using System.Collections.Generic;
using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class AudioDspServiceTests
{
    [Fact]
    public void AudioDspService_InvertPhase_InvertsBufferSamples()
    {
        var buffer = new float[] { 0.5f, -0.25f, 1.0f, -0.75f };
        AudioDspService.InvertPhase(buffer);

        Assert.Equal(-0.5f, buffer[0], 5);
        Assert.Equal(0.25f, buffer[1], 5);
        Assert.Equal(-1.0f, buffer[2], 5);
        Assert.Equal(0.75f, buffer[3], 5);
    }

    [Fact]
    public void AudioDspService_TrackEq_BassBoost_BoostsLowFrequencies()
    {
        const int sampleRate = 48000;
        const int frames = 4800; // 100ms
        var buffer = new float[frames * 2];

        // Synthesize 40 Hz deep bass tone well within the low shelf band (f < 100 Hz)
        for (int i = 0; i < frames; i++)
        {
            var val = (float)Math.Sin(2.0 * Math.PI * 40.0 * i / sampleRate);
            buffer[i * 2] = val;
            buffer[i * 2 + 1] = val;
        }

        var (_, _, initialRmsL, _) = AudioDspService.MeasureLevels(buffer);

        // Apply +6 dB Bass Boost EQ (Low Shelf at 100 Hz, +6 dB)
        var eq = AudioEffectCatalog.CreateTrackEq("Bass Boost (+6 dB)");
        AudioDspService.ProcessTrackEq(buffer, sampleRate, eq.Parameters);

        var (_, _, boostedRmsL, _) = AudioDspService.MeasureLevels(buffer);

        // +6 dB boost should increase RMS by at least 1.4x (ideal +6 dB is ~2.0x)
        Assert.True(boostedRmsL > initialRmsL * 1.4, $"Expected bass boost RMS > {initialRmsL * 1.4}, got {boostedRmsL}");
    }

    [Fact]
    public void AudioDspService_TrackCompressor_ReducesLoudSignals()
    {
        const int sampleRate = 48000;
        const int frames = 4800;
        var buffer = new float[frames * 2];

        // 0 dBFS sine wave (amplitude = 1.0)
        for (int i = 0; i < frames; i++)
        {
            buffer[i * 2] = 1.0f;
            buffer[i * 2 + 1] = 1.0f;
        }

        // Apply Heavy Limiting (10:1 ratio, -10 dB threshold)
        var comp = AudioEffectCatalog.CreateTrackCompressor("Heavy Limiting (10:1)");
        AudioDspService.ProcessTrackCompressor(buffer, sampleRate, comp.Parameters);

        // Check tail samples after attack has engaged
        var tailSample = buffer[buffer.Length - 2];
        Assert.True(tailSample < 0.85f, $"Expected compressed sample < 0.85, got {tailSample}");
    }

    [Fact]
    public void AudioDspService_TrackNoiseGate_MutesBelowThreshold()
    {
        const int sampleRate = 48000;
        const int frames = 4800;
        var buffer = new float[frames * 2];

        // Quiet signal at -60 dB (amplitude ~ 0.001)
        for (int i = 0; i < frames; i++)
        {
            buffer[i * 2] = 0.001f;
            buffer[i * 2 + 1] = 0.001f;
        }

        // Strict gate threshold at -35 dB (should clamp down)
        var gate = AudioEffectCatalog.CreateTrackNoiseGate("Strict Gate (-35 dB)");
        AudioDspService.ProcessTrackNoiseGate(buffer, sampleRate, gate.Parameters);

        var tailSample = buffer[buffer.Length - 2];
        Assert.True(Math.Abs(tailSample) < 0.0001f, $"Expected gated sample < 0.0001, got {tailSample}");
    }

    [Fact]
    public void AudioDspService_MeasureLevels_CalculatesPeakAndRms()
    {
        var buffer = new float[] { 0.5f, 0.5f, -0.5f, -0.5f };
        var (peakL, peakR, rmsL, rmsR) = AudioDspService.MeasureLevels(buffer);

        Assert.Equal(0.5, peakL, 3);
        Assert.Equal(0.5, peakR, 3);
        Assert.Equal(0.5, rmsL, 3);
        Assert.Equal(0.5, rmsR, 3);
    }
}
