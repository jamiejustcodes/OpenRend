using System;
using System.Linq;
using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class AudioDuckingMediaTests : IClassFixture<MediaFixture>
{
    private readonly MediaFixture fixture;

    public AudioDuckingMediaTests(MediaFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task RenderAudio_DucksListenerTrack_WhenControllerHasSpeech()
    {
        using var mediaService = new MediaService();
        using var renderService = new TimelineRenderService(mediaService);

        var asset = await mediaService.ProbeAsync(fixture.Source);

        var project = new ProjectDocument
        {
            FrameRate = new Rational(30, 1),
            Media = [asset]
        };

        // Voice track (Controller): Active between frame 30 (1.0s) and frame 60 (2.0s)
        var voiceTrack = new TimelineTrack
        {
            Kind = TrackKind.Audio,
            Name = "Voice",
            DuckingRole = AudioDuckingRole.Controller,
            Gain = 1.0,
            Clips =
            [
                new TimelineClip
                {
                    MediaId = asset.Id,
                    StartFrame = 30,
                    DurationFrames = 30,
                    Gain = 1.0
                }
            ]
        };

        // Music track (Listener): Constant background music from frame 0 to 120
        var musicTrack = new TimelineTrack
        {
            Kind = TrackKind.Audio,
            Name = "Music",
            DuckingRole = AudioDuckingRole.Listener,
            Gain = 1.0,
            DuckingSettings = new AudioDuckingSettings
            {
                IsEnabled = true,
                DuckLevelDb = -12.0, // ~0.251 gain
                AttackMs = 100.0,
                HoldMs = 200.0,
                ReleaseMs = 200.0
            },
            Clips =
            [
                new TimelineClip
                {
                    MediaId = asset.Id,
                    StartFrame = 0,
                    DurationFrames = 120,
                    Gain = 1.0
                }
            ]
        };

        project.Tracks.Add(voiceTrack);
        project.Tracks.Add(musicTrack);

        // Measure pure music baseline without ducking:
        voiceTrack.Gain = 0.0001;
        musicTrack.DuckingSettings.IsEnabled = false;
        var unduckedSamples = renderService.RenderAudio(project, 1.5, 480);
        var (_, _, unduckedRmsL, unduckedRmsR) = AudioDspService.MeasureLevels(unduckedSamples);
        var unduckedRms = (unduckedRmsL + unduckedRmsR) / 2.0;

        // Enable ducking and test at frame 45 (1.5s, voice is active):
        musicTrack.DuckingSettings.IsEnabled = true;
        var duckedSamples = renderService.RenderAudio(project, 1.5, 480);
        var (_, _, duckedRmsL, duckedRmsR) = AudioDspService.MeasureLevels(duckedSamples);
        var duckedRms = (duckedRmsL + duckedRmsR) / 2.0;

        // Ducked RMS should be substantially lower than unducked RMS (around 25% of baseline)
        Assert.True(unduckedRms > 0.05, "Baseline audio must have signal.");
        Assert.True(duckedRms < unduckedRms * 0.5, "Ducked audio must be noticeably quieter than baseline.");
    }

    [Fact]
    public async Task RenderAudio_BakedEnvelope_EvaluatesDirectly()
    {
        using var mediaService = new MediaService();
        using var renderService = new TimelineRenderService(mediaService);

        var asset = await mediaService.ProbeAsync(fixture.Source);

        var project = new ProjectDocument
        {
            FrameRate = new Rational(30, 1),
            Media = [asset]
        };

        var musicTrack = new TimelineTrack
        {
            Kind = TrackKind.Audio,
            Name = "Music",
            DuckingRole = AudioDuckingRole.Listener,
            Gain = 1.0,
            DuckingSettings = new AudioDuckingSettings { IsEnabled = true },
            Clips =
            [
                new TimelineClip
                {
                    MediaId = asset.Id,
                    StartFrame = 0,
                    DurationFrames = 120,
                    Gain = 1.0
                }
            ],
            Envelopes =
            [
                new ClipEnvelope
                {
                    Type = EnvelopeType.Volume,
                    IsEnabled = true,
                    Points =
                    [
                        new EnvelopePoint(0, 1.0),
                        new EnvelopePoint(30, 0.1),
                        new EnvelopePoint(60, 0.1),
                        new EnvelopePoint(90, 1.0)
                    ]
                }
            ]
        };

        project.Tracks.Add(musicTrack);

        // At frame 0: volume is 1.0
        var samples0 = renderService.RenderAudio(project, 0.0, 480);
        var (_, _, rms0L, rms0R) = AudioDspService.MeasureLevels(samples0);
        var rms0 = (rms0L + rms0R) / 2.0;

        // At frame 45 (1.5s): envelope is at 0.1
        var samples45 = renderService.RenderAudio(project, 1.5, 480);
        var (_, _, rms45L, rms45R) = AudioDspService.MeasureLevels(samples45);
        var rms45 = (rms45L + rms45R) / 2.0;

        Assert.True(rms0 > 0.05);
        Assert.True(rms45 < rms0 * 0.3);
    }
}
