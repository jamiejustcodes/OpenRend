using System;
using System.IO;
using System.Text.Json;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class AudioEffectTests
{
    [Fact]
    public void AudioEffectCatalog_CreateTrackEq_HasFourBandsAndValidDefaults()
    {
        var eq = AudioEffectCatalog.CreateTrackEq();
        Assert.NotNull(eq);
        Assert.Equal(AudioEffectKind.TrackEQ, eq.Kind);
        Assert.Equal("Track EQ", eq.Name);
        Assert.False(eq.IsBypassed);

        Assert.Equal(80.0, eq.Parameters["Band1_Freq"]);
        Assert.Equal(0.0, eq.Parameters["Band1_GainDb"]);
        Assert.Equal(1.0, eq.Parameters["Band1_Q"]);

        Assert.Equal(250.0, eq.Parameters["Band2_Freq"]);
        Assert.Equal(2500.0, eq.Parameters["Band3_Freq"]);
        Assert.Equal(8000.0, eq.Parameters["Band4_Freq"]);

        eq.Validate();
    }

    [Fact]
    public void AudioEffectCatalog_CreateTrackCompressor_HasValidDefaults()
    {
        var comp = AudioEffectCatalog.CreateTrackCompressor();
        Assert.NotNull(comp);
        Assert.Equal(AudioEffectKind.TrackCompressor, comp.Kind);
        Assert.Equal(-18.0, comp.Parameters["ThresholdDb"]);
        Assert.Equal(4.0, comp.Parameters["Ratio"]);
        Assert.Equal(15.0, comp.Parameters["AttackMs"]);
        Assert.Equal(100.0, comp.Parameters["ReleaseMs"]);
        Assert.Equal(0.0, comp.Parameters["OutputGainDb"]);

        comp.Validate();
    }

    [Fact]
    public void AudioEffectCatalog_CreateTrackNoiseGate_HasValidDefaults()
    {
        var gate = AudioEffectCatalog.CreateTrackNoiseGate();
        Assert.NotNull(gate);
        Assert.Equal(AudioEffectKind.TrackNoiseGate, gate.Kind);
        Assert.Equal(-45.0, gate.Parameters["ThresholdDb"]);
        Assert.Equal(5.0, gate.Parameters["AttackMs"]);
        Assert.Equal(150.0, gate.Parameters["ReleaseMs"]);

        gate.Validate();
    }

    [Fact]
    public void AudioEffectCatalog_CreateDefaultTrackChain_ReturnsGateEqCompressor()
    {
        var chain = AudioEffectCatalog.CreateDefaultTrackChain();
        Assert.Equal(3, chain.Count);
        Assert.Equal(AudioEffectKind.TrackNoiseGate, chain[0].Kind);
        Assert.True(chain[0].IsBypassed); // Default gate is bypassed in Vegas Pro default chain
        Assert.Equal(AudioEffectKind.TrackEQ, chain[1].Kind);
        Assert.False(chain[1].IsBypassed);
        Assert.Equal(AudioEffectKind.TrackCompressor, chain[2].Kind);
        Assert.False(chain[2].IsBypassed);
    }

    [Fact]
    public void AudioEffect_DeepClone_CreatesIndependentCopy()
    {
        var original = AudioEffectCatalog.CreateTrackEq("Warm Vocal");
        var clone = original.DeepClone();

        Assert.Equal(original.Id, clone.Id);
        Assert.Equal(original.Kind, clone.Kind);
        Assert.Equal(original.Name, clone.Name);
        Assert.Equal(original.Parameters["Band1_GainDb"], clone.Parameters["Band1_GainDb"]);

        clone.Parameters["Band1_GainDb"] = 12.0;
        Assert.NotEqual(clone.Parameters["Band1_GainDb"], original.Parameters["Band1_GainDb"]);
    }

    [Fact]
    public void ProjectDocument_AudioEffects_SerializesAndValidates()
    {
        var doc = new ProjectDocument();
        var audioTrack = new TimelineTrack { Kind = TrackKind.Audio, Name = "Audio 1", InvertPhase = true };
        audioTrack.AudioEffects.Add(AudioEffectCatalog.CreateTrackEq());
        doc.Tracks.Add(audioTrack);
        doc.MasterAudioEffects.Add(AudioEffectCatalog.CreateTrackCompressor());
        doc.MasterAudioGain = 0.8;
        doc.MasterAudioDim = true;

        doc.Validate();

        var json = JsonSerializer.Serialize(doc);
        var roundtrip = JsonSerializer.Deserialize<ProjectDocument>(json);

        Assert.NotNull(roundtrip);
        Assert.True(roundtrip.Tracks[0].InvertPhase);
        Assert.Single(roundtrip.Tracks[0].AudioEffects);
        Assert.Equal(AudioEffectKind.TrackEQ, roundtrip.Tracks[0].AudioEffects[0].Kind);
        Assert.Single(roundtrip.MasterAudioEffects);
        Assert.Equal(AudioEffectKind.TrackCompressor, roundtrip.MasterAudioEffects[0].Kind);
        Assert.Equal(0.8, roundtrip.MasterAudioGain);
        Assert.True(roundtrip.MasterAudioDim);
    }

    [Fact]
    public void ProjectDocument_AudioEffects_RejectsInvalidConfiguration()
    {
        var doc = new ProjectDocument();
        var videoTrack = new TimelineTrack { Kind = TrackKind.Video, Name = "Video 1" };
        videoTrack.AudioEffects.Add(AudioEffectCatalog.CreateTrackEq());
        doc.Tracks.Add(videoTrack);

        Assert.Throws<InvalidDataException>(() => doc.Validate());
    }
}
