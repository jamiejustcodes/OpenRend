using System;
using System.Collections.Generic;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class AudioDuckingTests
{
    [Fact]
    public void AudioDuckingSettings_Defaults_And_Cloning()
    {
        var settings = new AudioDuckingSettings();
        Assert.True(settings.IsEnabled);
        Assert.Null(settings.ControllerTrackId);
        Assert.Equal(-12.0, settings.DuckLevelDb);
        Assert.Equal(-30.0, settings.ThresholdDb);
        Assert.Equal(200.0, settings.AttackMs);
        Assert.Equal(600.0, settings.HoldMs);
        Assert.Equal(800.0, settings.ReleaseMs);

        // -12 dB is 10^(-12/20) ~= 0.2511886
        Assert.InRange(settings.LinearDuckMultiplier, 0.25, 0.252);

        var clone = settings.DeepClone();
        Assert.Equal(settings, clone);
        Assert.Equal(settings.GetHashCode(), clone.GetHashCode());

        clone.DuckLevelDb = -18.0;
        Assert.NotEqual(settings, clone);
    }

    [Fact]
    public void AudioDuckingSettings_Validation_ClampsExtremes()
    {
        var settings = new AudioDuckingSettings
        {
            DuckLevelDb = -100.0,
            ThresholdDb = 20.0,
            AttackMs = -50.0,
            HoldMs = -10.0,
            ReleaseMs = 10000.0
        };

        settings.Validate();

        Assert.Equal(-60.0, settings.DuckLevelDb);
        Assert.Equal(-6.0, settings.ThresholdDb);
        Assert.Equal(10.0, settings.AttackMs);
        Assert.Equal(0.0, settings.HoldMs);
        Assert.Equal(5000.0, settings.ReleaseMs);
    }

    [Fact]
    public void AudioDuckingEngine_GetActiveSpeechSpans_MergesOverlappingClips()
    {
        var project = new ProjectDocument();

        var controllerTrack = new TimelineTrack
        {
            Kind = TrackKind.Audio,
            Name = "Voiceover",
            DuckingRole = AudioDuckingRole.Controller,
            Clips =
            [
                new TimelineClip { StartFrame = 30, DurationFrames = 50 },  // 30 to 80
                new TimelineClip { StartFrame = 70, DurationFrames = 40 },  // 70 to 110 (overlaps)
                new TimelineClip { StartFrame = 200, DurationFrames = 60 }, // 200 to 260
                new TimelineClip { StartFrame = 400, DurationFrames = 30, IsMuted = true } // Muted clip ignored
            ]
        };

        var musicTrack = new TimelineTrack
        {
            Kind = TrackKind.Audio,
            Name = "Background Music",
            DuckingRole = AudioDuckingRole.Listener,
            Clips =
            [
                new TimelineClip { StartFrame = 0, DurationFrames = 1000 }
            ]
        };

        project.Tracks.Add(controllerTrack);
        project.Tracks.Add(musicTrack);

        var spans = AudioDuckingEngine.GetActiveSpeechSpans(project, null);
        Assert.Equal(2, spans.Count);
        Assert.Equal(30, spans[0].StartFrame);
        Assert.Equal(110, spans[0].EndFrame);
        Assert.Equal(200, spans[1].StartFrame);
        Assert.Equal(260, spans[1].EndFrame);
    }

    [Fact]
    public void AudioDuckingEngine_EvaluateDuckingGain_InsideAndOutsideSpeech()
    {
        var project = new ProjectDocument
        {
            FrameRate = new Rational(30, 1)
        };

        var controllerTrack = new TimelineTrack
        {
            Kind = TrackKind.Audio,
            Name = "Voiceover",
            DuckingRole = AudioDuckingRole.Controller,
            Clips =
            [
                new TimelineClip { StartFrame = 90, DurationFrames = 90 } // 90 to 180 (3.0s to 6.0s at 30fps)
            ]
        };

        var listenerTrack = new TimelineTrack
        {
            Kind = TrackKind.Audio,
            Name = "Music",
            DuckingRole = AudioDuckingRole.Listener,
            DuckingSettings = new AudioDuckingSettings
            {
                DuckLevelDb = -12.0,
                AttackMs = 200.0, // 6 frames at 30fps
                HoldMs = 600.0,   // 18 frames at 30fps
                ReleaseMs = 800.0 // 24 frames at 30fps
            }
        };

        project.Tracks.Add(controllerTrack);
        project.Tracks.Add(listenerTrack);

        var duckMul = listenerTrack.DuckingSettings.LinearDuckMultiplier;

        // 1. Far before attack ramp: unity gain 1.0
        Assert.Equal(1.0, AudioDuckingEngine.EvaluateDuckingGain(project, listenerTrack, 50));

        // 2. In attack ramp: frame 87 (mid-attack ramp down)
        var rampGain = AudioDuckingEngine.EvaluateDuckingGain(project, listenerTrack, 87);
        Assert.True(rampGain < 1.0 && rampGain > duckMul);

        // 3. Exactly at speech start (frame 90): ducked
        Assert.InRange(AudioDuckingEngine.EvaluateDuckingGain(project, listenerTrack, 90), duckMul - 1e-4, duckMul + 1e-4);

        // 4. Middle of speech (frame 135): ducked
        Assert.InRange(AudioDuckingEngine.EvaluateDuckingGain(project, listenerTrack, 135), duckMul - 1e-4, duckMul + 1e-4);

        // 5. In hold phase (frame 190): speech ends at 180, hold is 18 frames (up to frame 198)
        Assert.InRange(AudioDuckingEngine.EvaluateDuckingGain(project, listenerTrack, 190), duckMul - 1e-4, duckMul + 1e-4);

        // 6. In release recovery ramp (frame 210): release runs from 198 to 222
        var releaseGain = AudioDuckingEngine.EvaluateDuckingGain(project, listenerTrack, 210);
        Assert.True(releaseGain > duckMul && releaseGain < 1.0);

        // 7. Well after release (frame 250): unity gain 1.0
        Assert.Equal(1.0, AudioDuckingEngine.EvaluateDuckingGain(project, listenerTrack, 250));
    }

    [Fact]
    public void AudioDuckingEngine_BakeDuckingToEnvelope_GeneratesAccurateVolumeEnvelope()
    {
        var project = new ProjectDocument
        {
            FrameRate = new Rational(30, 1)
        };

        var controllerTrack = new TimelineTrack
        {
            Kind = TrackKind.Audio,
            Name = "Voice",
            DuckingRole = AudioDuckingRole.Controller,
            Clips =
            [
                new TimelineClip { StartFrame = 60, DurationFrames = 60 } // 60 to 120 (2s to 4s)
            ]
        };

        var listenerTrack = new TimelineTrack
        {
            Kind = TrackKind.Audio,
            Name = "Music",
            DuckingRole = AudioDuckingRole.Listener,
            DuckingSettings = new AudioDuckingSettings
            {
                DuckLevelDb = -12.0,
                AttackMs = 200.0, // 6 frames
                HoldMs = 600.0,   // 18 frames
                ReleaseMs = 800.0 // 24 frames
            },
            Clips =
            [
                new TimelineClip { StartFrame = 0, DurationFrames = 300 }
            ]
        };

        project.Tracks.Add(controllerTrack);
        project.Tracks.Add(listenerTrack);

        var env = AudioDuckingEngine.BakeDuckingToEnvelope(project, listenerTrack);

        Assert.NotNull(env);
        Assert.Equal(EnvelopeType.Volume, env.Type);
        Assert.True(env.IsEnabled);
        Assert.NotEmpty(env.Points);

        // Point at timeline start: 1.0
        Assert.Equal(0, env.Points[0].Frame);
        Assert.Equal(1.0, env.Points[0].Value);

        // Speech starts at frame 60, attack is 6 frames -> preAttack is 54
        var preAttackPt = env.Points.Find(p => p.Frame == 54);
        Assert.NotNull(preAttackPt);
        Assert.Equal(1.0, preAttackPt.Value);

        // Attack end at frame 60 -> ducked
        var attackEndPt = env.Points.Find(p => p.Frame == 60);
        Assert.NotNull(attackEndPt);
        Assert.InRange(attackEndPt.Value, 0.25, 0.252);

        // Hold ends at 120 + 18 = 138 -> still ducked
        var holdEndPt = env.Points.Find(p => p.Frame == 138);
        Assert.NotNull(holdEndPt);
        Assert.InRange(holdEndPt.Value, 0.25, 0.252);

        // Release ends at 138 + 24 = 162 -> restored to 1.0
        var releaseEndPt = env.Points.Find(p => p.Frame == 162);
        Assert.NotNull(releaseEndPt);
        Assert.Equal(1.0, releaseEndPt.Value);
    }
}
