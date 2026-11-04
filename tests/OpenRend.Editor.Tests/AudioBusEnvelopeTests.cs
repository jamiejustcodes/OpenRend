using System;
using System.IO;
using System.Linq;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class AudioBusEnvelopeTests
{
    private static (EditorViewModel ViewModel, TimelineEditor Editor, ProjectDocument Project) CreateEnvironment()
    {
        var media = new MediaService();
        var renderer = new TimelineRenderService(media);
        var exporter = new ExportService(renderer);
        var vm = new EditorViewModel(media, renderer, exporter);
        vm.Project.Tracks.Clear();
        vm.Project.AudioBuses.Clear();

        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = "test.wav",
            HasAudio = true,
            DurationSeconds = 10.0,
            FrameRate = 30.0
        };
        vm.Project.Media.Add(asset);

        var track = new TimelineTrack
        {
            Kind = TrackKind.Audio,
            Name = "Audio 1"
        };
        var clip = new TimelineClip
        {
            MediaId = asset.Id,
            Name = "Audio Clip",
            StartFrame = 0,
            DurationFrames = 300
        };
        track.Clips.Add(clip);
        vm.Project.Tracks.Add(track);

        return (vm, vm.Editor, vm.Project);
    }

    [Fact]
    public void AudioBusEnvelope_AddToggleAndUndo_OperatesAtomically()
    {
        var (_, editor, project) = CreateEnvironment();
        var bus = editor.CreateAudioBus("Bus A");

        Assert.False(editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Volume));
        Assert.False(editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Pan));
        Assert.False(editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Mute));

        // Add Volume Envelope
        editor.ToggleAudioBusEnvelope(bus.Id, EnvelopeType.Volume);
        Assert.True(editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Volume));
        var volEnv = editor.GetAudioBusEnvelope(bus.Id, EnvelopeType.Volume);
        Assert.NotNull(volEnv);
        Assert.Equal(2, volEnv.Points.Count);
        Assert.Equal(1.0, volEnv.Points[0].Value);

        // Add Pan Envelope
        editor.ToggleAudioBusEnvelope(bus.Id, EnvelopeType.Pan);
        Assert.True(editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Pan));
        var panEnv = editor.GetAudioBusEnvelope(bus.Id, EnvelopeType.Pan);
        Assert.NotNull(panEnv);
        Assert.Equal(2, panEnv.Points.Count);
        Assert.Equal(0.0, panEnv.Points[0].Value);

        // Add Mute Envelope
        editor.ToggleAudioBusEnvelope(bus.Id, EnvelopeType.Mute);
        Assert.True(editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Mute));

        // Validation test
        var busTrack = project.AudioBuses.First(b => b.Id == bus.Id);
        busTrack.Validate();

        // Undo Mute Envelope
        editor.Undo();
        Assert.False(editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Mute));
        Assert.True(editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Pan));
        Assert.True(editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Volume));

        // Redo Mute Envelope
        editor.Redo();
        Assert.True(editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Mute));

        // Toggle off Volume Envelope
        editor.ToggleAudioBusEnvelope(bus.Id, EnvelopeType.Volume);
        Assert.False(editor.HasAudioBusEnvelope(bus.Id, EnvelopeType.Volume));
    }

    [Fact]
    public void MasterAudioEnvelope_AddToggleAndUndo_OperatesAtomically()
    {
        var (_, editor, project) = CreateEnvironment();

        Assert.False(editor.HasMasterAudioEnvelope(EnvelopeType.Volume));
        Assert.False(editor.HasMasterAudioEnvelope(EnvelopeType.Mute));

        // Add Master Volume Envelope
        editor.ToggleMasterAudioEnvelope(EnvelopeType.Volume);
        Assert.True(editor.HasMasterAudioEnvelope(EnvelopeType.Volume));
        var masterVol = editor.GetMasterAudioEnvelope(EnvelopeType.Volume);
        Assert.NotNull(masterVol);
        Assert.Equal(2, masterVol.Points.Count);
        Assert.Equal(1.0, masterVol.Points[0].Value);

        // Add Master Mute Envelope
        editor.ToggleMasterAudioEnvelope(EnvelopeType.Mute);
        Assert.True(editor.HasMasterAudioEnvelope(EnvelopeType.Mute));
        var masterMute = editor.GetMasterAudioEnvelope(EnvelopeType.Mute);
        Assert.NotNull(masterMute);
        Assert.Equal(2, masterMute.Points.Count);
        Assert.Equal(0.0, masterMute.Points[0].Value);

        // Project validation should pass
        project.Validate();

        // Undo Master Mute Envelope
        editor.Undo();
        Assert.False(editor.HasMasterAudioEnvelope(EnvelopeType.Mute));
        Assert.True(editor.HasMasterAudioEnvelope(EnvelopeType.Volume));

        // Redo Master Mute Envelope
        editor.Redo();
        Assert.True(editor.HasMasterAudioEnvelope(EnvelopeType.Mute));

        // Toggle off Master Volume Envelope
        editor.ToggleMasterAudioEnvelope(EnvelopeType.Volume);
        Assert.False(editor.HasMasterAudioEnvelope(EnvelopeType.Volume));
    }

    [Fact]
    public void AudioBusAndMasterEnvelope_PointAndCurveModifications_WorkCorrectly()
    {
        var (_, editor, project) = CreateEnvironment();
        var bus = editor.CreateAudioBus("Bus A");

        editor.AddAudioBusEnvelope(bus.Id, EnvelopeType.Volume);
        editor.SetAudioBusEnvelopePoint(bus.Id, EnvelopeType.Volume, 50, 0.5, EnvelopeCurveType.Smooth);

        var busEnv = editor.GetAudioBusEnvelope(bus.Id, EnvelopeType.Volume);
        Assert.NotNull(busEnv);
        Assert.Equal(3, busEnv.Points.Count);
        Assert.Equal(50, busEnv.Points[1].Frame);
        Assert.Equal(0.5, busEnv.Points[1].Value);
        Assert.Equal(EnvelopeCurveType.Smooth, busEnv.Points[1].Curve);

        // Change curve
        editor.SetAudioBusEnvelopePointCurve(bus.Id, EnvelopeType.Volume, 50, EnvelopeCurveType.Sharp);
        busEnv = editor.GetAudioBusEnvelope(bus.Id, EnvelopeType.Volume);
        Assert.Equal(EnvelopeCurveType.Sharp, busEnv!.Points[1].Curve);

        // Remove point
        editor.RemoveAudioBusEnvelopePoint(bus.Id, EnvelopeType.Volume, 50);
        busEnv = editor.GetAudioBusEnvelope(bus.Id, EnvelopeType.Volume);
        Assert.Equal(2, busEnv!.Points.Count);

        // Master Envelope point manipulation
        editor.AddMasterAudioEnvelope(EnvelopeType.Volume);
        editor.SetMasterAudioEnvelopePoint(EnvelopeType.Volume, 100, 1.5, EnvelopeCurveType.Fast);
        var masterEnv = editor.GetMasterAudioEnvelope(EnvelopeType.Volume);
        Assert.NotNull(masterEnv);
        Assert.Equal(3, masterEnv.Points.Count);
        Assert.Equal(100, masterEnv.Points[1].Frame);
        Assert.Equal(1.5, masterEnv.Points[1].Value);
        Assert.Equal(EnvelopeCurveType.Fast, masterEnv.Points[1].Curve);

        // Change curve
        editor.SetMasterAudioEnvelopePointCurve(EnvelopeType.Volume, 100, EnvelopeCurveType.Slow);
        masterEnv = editor.GetMasterAudioEnvelope(EnvelopeType.Volume);
        Assert.Equal(EnvelopeCurveType.Slow, masterEnv!.Points[1].Curve);

        // Undo
        editor.Undo();
        masterEnv = editor.GetMasterAudioEnvelope(EnvelopeType.Volume);
        Assert.Equal(EnvelopeCurveType.Fast, masterEnv!.Points[1].Curve);
    }

    [Fact]
    public void Validation_RejectsInvalidEnvelopeTypesOnBusAndMaster()
    {
        var (_, _, project) = CreateEnvironment();
        var bus = new AudioBusTrack { Name = "Bus X" };
        project.AudioBuses.Add(bus);

        // Audio Bus does not allow Velocity or Composite
        bus.Envelopes = [new ClipEnvelope { Type = EnvelopeType.Velocity }];
        Assert.Throws<InvalidDataException>(() => bus.Validate());

        bus.Envelopes = [new ClipEnvelope { Type = EnvelopeType.Composite }];
        Assert.Throws<InvalidDataException>(() => bus.Validate());

        // Audio Bus allows Volume, Pan, Mute
        bus.Envelopes = [
            new ClipEnvelope { Type = EnvelopeType.Volume },
            new ClipEnvelope { Type = EnvelopeType.Pan },
            new ClipEnvelope { Type = EnvelopeType.Mute }
        ];
        bus.Validate();

        // Master Bus does not allow Pan or Composite
        project.MasterAudioEnvelopes = [new ClipEnvelope { Type = EnvelopeType.Pan }];
        Assert.Throws<InvalidDataException>(() => project.Validate());

        // Master Bus allows Volume and Mute
        project.MasterAudioEnvelopes = [
            new ClipEnvelope { Type = EnvelopeType.Volume },
            new ClipEnvelope { Type = EnvelopeType.Mute }
        ];
        project.Validate();
    }
}

