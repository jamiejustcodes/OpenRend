using System;
using System.Linq;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class AudioDuckingEditorTests
{
    [Fact]
    public void SetTrackDucking_ConfiguresRoleAndSettings_AndSupportsUndoRedo()
    {
        var project = new ProjectDocument();
        var audioTrack = new TimelineTrack
        {
            Kind = TrackKind.Audio,
            Name = "Dialogue"
        };
        project.Tracks.Add(audioTrack);

        var editor = new TimelineEditor(project);

        // Configure as Controller
        var set1 = editor.SetTrackDucking(audioTrack.Id, AudioDuckingRole.Controller);
        Assert.True(set1);
        Assert.Equal(AudioDuckingRole.Controller, project.Tracks[0].DuckingRole);
        Assert.Null(project.Tracks[0].DuckingSettings);

        // Configure as Listener with custom settings
        var customSettings = new AudioDuckingSettings
        {
            DuckLevelDb = -18.0,
            ThresholdDb = -25.0,
            AttackMs = 150.0,
            HoldMs = 500.0,
            ReleaseMs = 700.0
        };
        var set2 = editor.SetTrackDucking(audioTrack.Id, AudioDuckingRole.Listener, customSettings);
        Assert.True(set2);
        Assert.Equal(AudioDuckingRole.Listener, project.Tracks[0].DuckingRole);
        Assert.NotNull(project.Tracks[0].DuckingSettings);
        Assert.Equal(-18.0, project.Tracks[0].DuckingSettings!.DuckLevelDb);

        // Undo -> reverts to Controller
        editor.Undo();
        Assert.Equal(AudioDuckingRole.Controller, project.Tracks[0].DuckingRole);
        Assert.Null(project.Tracks[0].DuckingSettings);

        // Undo -> reverts to None
        editor.Undo();
        Assert.Equal(AudioDuckingRole.None, project.Tracks[0].DuckingRole);

        // Redo -> Controller
        editor.Redo();
        Assert.Equal(AudioDuckingRole.Controller, project.Tracks[0].DuckingRole);

        // Redo -> Listener
        editor.Redo();
        Assert.Equal(AudioDuckingRole.Listener, project.Tracks[0].DuckingRole);
        Assert.Equal(-18.0, project.Tracks[0].DuckingSettings!.DuckLevelDb);
    }

    [Fact]
    public void BakeTrackDuckingEnvelope_CreatesVolumeEnvelope_AndCanBeCleared()
    {
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = "dummy.wav",
            HasAudio = true
        };

        var project = new ProjectDocument
        {
            FrameRate = new Rational(30, 1),
            Media = [asset]
        };

        var controllerTrack = new TimelineTrack
        {
            Kind = TrackKind.Audio,
            Name = "Voice",
            DuckingRole = AudioDuckingRole.Controller,
            Clips =
            [
                new TimelineClip { MediaId = asset.Id, StartFrame = 30, DurationFrames = 60 }
            ]
        };

        var listenerTrack = new TimelineTrack
        {
            Kind = TrackKind.Audio,
            Name = "Music",
            DuckingRole = AudioDuckingRole.Listener,
            DuckingSettings = new AudioDuckingSettings
            {
                DuckLevelDb = -12.0
            },
            Clips =
            [
                new TimelineClip { MediaId = asset.Id, StartFrame = 0, DurationFrames = 300 }
            ]
        };

        project.Tracks.Add(controllerTrack);
        project.Tracks.Add(listenerTrack);

        var editor = new TimelineEditor(project);

        // Bake envelope
        var baked = editor.BakeTrackDuckingEnvelope(listenerTrack.Id);
        Assert.True(baked);
        var targetTrack = editor.Project.Tracks.First(t => t.Id == listenerTrack.Id);
        Assert.NotNull(targetTrack.Envelopes);
        var volEnv = targetTrack.Envelopes.FirstOrDefault(e => e.Type == EnvelopeType.Volume);
        Assert.NotNull(volEnv);
        Assert.NotEmpty(volEnv.Points);

        // Clear envelope
        var cleared = editor.ClearTrackDuckingEnvelope(listenerTrack.Id);
        Assert.True(cleared);
        Assert.Null(editor.Project.Tracks.First(t => t.Id == listenerTrack.Id).Envelopes);

        // Undo clear
        editor.Undo();
        var restoredTrack = editor.Project.Tracks.First(t => t.Id == listenerTrack.Id);
        Assert.NotNull(restoredTrack.Envelopes);
        Assert.Contains(restoredTrack.Envelopes, e => e.Type == EnvelopeType.Volume);
    }
}
