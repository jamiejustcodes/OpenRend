using System.Runtime.InteropServices;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Playback.Tests;

public sealed class MasterAudioMeterTests
{
    [Theory]
    [InlineData(-23)]
    [InlineData(-33)]
    public void StereoCalibrationToneMatchesAllThreeLoudnessReadings(double level)
    {
        var stereo = StereoTone(1000, Math.Pow(10, level / 20), 0, MediaService.SampleRate * 4);
        var meter = new MasterAudioMeter();
        Assert.True(double.IsNaN(meter.Measure(stereo.AsSpan(0, MediaService.SampleRate / 2)).MomentaryLufs));
        var reading = meter.Measure(stereo.AsSpan(MediaService.SampleRate / 2));
        Assert.InRange(reading.MomentaryLufs, level - .1, level + .1);
        Assert.InRange(reading.ShortTermLufs, level - .1, level + .1);
        Assert.InRange(reading.IntegratedLufs, level - .1, level + .1);
    }

    [Fact]
    public void IntegratedLoudnessAppliesBothGatesAcrossAudioBufferBoundaries()
    {
        var audio = StereoTone(1000, Math.Pow(10, -23 / 20.0), 0, MediaService.SampleRate * 8)
            .Concat(StereoTone(1000, Math.Pow(10, -45 / 20.0), 0, MediaService.SampleRate * 2))
            .Concat(StereoTone(1000, Math.Pow(10, -85 / 20.0), 0, MediaService.SampleRate * 6)).ToArray();
        var whole = new MasterAudioMeter();
        var partitioned = new MasterAudioMeter();
        whole.Measure(audio);
        foreach (var buffer in audio.Chunk(4094)) partitioned.Measure(buffer);

        var integrated = whole.ReadIntegratedLoudness();
        Assert.InRange(integrated, -23.15, -22.95);
        Assert.Equal(integrated, partitioned.ReadIntegratedLoudness(), 8);

        var silent = new MasterAudioMeter();
        Assert.True(double.IsNaN(silent.Measure(new float[MediaService.SampleRate / 2]).IntegratedLufs));
        Assert.True(double.IsNegativeInfinity(silent.Measure(new float[MediaService.SampleRate]).IntegratedLufs));
    }

    [Fact]
    public void TruePeakDetectsASineMaximumBetweenSamples()
    {
        var meter = new MasterAudioMeter();
        var reading = meter.Measure(StereoTone(12000, .5, Math.PI / 4, MediaService.SampleRate));
        Assert.InRange(20 * Math.Log10(reading.Left), -9.1, -8.9);
        Assert.InRange(reading.TruePeakDb, -6.4, -5.8);
    }

    [Fact]
    public void QueuedAudioUsesTheCurrentProgramGainDimAndMute()
    {
        var project = new ProjectDocument { MasterAudioGain = 2 };
        var provider = new TimelineAudioBuffer(() => 1, () => TimelineRenderService.MasterOutputGain(project));
        var stereo = new[] { .75f, .5f, .75f, .5f, .75f, .5f, .75f, .5f };
        provider.AddSamples(MemoryMarshal.AsBytes(stereo.AsSpan()).ToArray());
        var bytes = new byte[2 * sizeof(float)];
        (float Left, float Right) Read()
        {
            provider.Read(bytes, 0, bytes.Length);
            var samples = MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
            return (samples[0], samples[1]);
        }

        Assert.Equal((1f, 1f), Read());
        Assert.Equal(1.5, provider.MeterReading.Left);
        project.MasterAudioDim = true;
        Assert.Equal((.15f, .1f), Read());
        project.MasterAudioMute = true;
        Assert.Equal((0f, 0f), Read());
        project.MasterAudioMute = false;
        project.MasterAudioDim = false;
        project.MasterAudioGain = .5;
        Assert.Equal((.375f, .25f), Read());
    }

    [Fact]
    public void MeterHoldsPeaksAndSeesProgramOverloadBeforeTheMonitorAttenuates()
    {
        var provider = new TimelineAudioBuffer(() => .5);
        var frame = new[] { 1.25f, -.5f };
        provider.AddSamples(MemoryMarshal.AsBytes(frame.AsSpan()).ToArray());
        var buffer = new byte[MediaService.Channels * sizeof(float)];
        provider.Read(buffer, 0, buffer.Length);
        Assert.Equal(new[] { .5f, -.25f }, MemoryMarshal.Cast<byte, float>(buffer.AsSpan()).ToArray());
        Assert.True(provider.MeterReading.LeftClipped);
        Assert.False(provider.MeterReading.RightClipped);
        Assert.Equal(1.25, provider.MeterReading.HeldLeft);
        var silence = new byte[MediaService.SampleRate * MediaService.Channels * sizeof(float) / 2];
        provider.Read(silence, 0, silence.Length);
        Assert.Equal(0, provider.MeterReading.Left);
        Assert.Equal(1.25, provider.MeterReading.HeldLeft);
        provider.Read(silence, 0, silence.Length);
        Assert.Equal(0, provider.MeterReading.HeldLeft);
        Assert.True(provider.MeterReading.LeftClipped);
        provider.ClearMeterClips();
        Assert.False(provider.MeterReading.LeftClipped);
    }

    private static float[] StereoTone(double frequency, double amplitude, double phase, int frames)
    {
        var stereo = new float[frames * MediaService.Channels];
        for (var frame = 0; frame < frames; frame++)
            stereo[frame * 2] = stereo[frame * 2 + 1] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * frame / MediaService.SampleRate + phase));
        return stereo;
    }
}
