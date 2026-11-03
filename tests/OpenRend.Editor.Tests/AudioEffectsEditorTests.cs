using System;
using System.Linq;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class AudioEffectsEditorTests
{
    [Fact]
    public void TimelineEditor_SetTrackInvertPhase_UpdatesAndSupportsUndo()
    {
        var doc = new ProjectDocument();
        var track = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Audio };
        doc.Tracks.Add(track);
        var editor = new TimelineEditor(doc);

        Assert.False(editor.Project.Tracks[0].InvertPhase);

        editor.SetTrackInvertPhase(track.Id, true);
        Assert.True(editor.Project.Tracks[0].InvertPhase);

        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.False(editor.Project.Tracks[0].InvertPhase);

        Assert.True(editor.CanRedo);
        editor.Redo();
        Assert.True(editor.Project.Tracks[0].InvertPhase);
    }

    [Fact]
    public void TimelineEditor_TrackAudioEffects_AddBypassUpdateRemove_SupportsUndo()
    {
        var doc = new ProjectDocument();
        var track = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Audio };
        doc.Tracks.Add(track);
        var editor = new TimelineEditor(doc);

        var eq = AudioEffectCatalog.CreateTrackEq();
        editor.AddTrackAudioEffect(track.Id, eq);
        Assert.Single(editor.Project.Tracks[0].AudioEffects);

        // Bypass
        editor.SetTrackAudioEffectBypassed(track.Id, eq.Id, true);
        Assert.True(editor.Project.Tracks[0].AudioEffects[0].IsBypassed);

        // Parameter update
        editor.UpdateTrackAudioEffectParameter(track.Id, eq.Id, "Band1_GainDb", 4.5);
        Assert.Equal(4.5, editor.Project.Tracks[0].AudioEffects[0].Parameters["Band1_GainDb"]);

        // Remove
        editor.RemoveTrackAudioEffect(track.Id, eq.Id);
        Assert.Empty(editor.Project.Tracks[0].AudioEffects);

        // Undo removal
        editor.Undo();
        Assert.Single(editor.Project.Tracks[0].AudioEffects);
        Assert.Equal(4.5, editor.Project.Tracks[0].AudioEffects[0].Parameters["Band1_GainDb"]);
    }

    [Fact]
    public void TimelineEditor_MasterAudio_GainMuteDim_SupportsUndo()
    {
        var doc = new ProjectDocument();
        var editor = new TimelineEditor(doc);

        editor.SetMasterAudioGain(0.75);
        Assert.Equal(0.75, editor.Project.MasterAudioGain);

        editor.SetMasterAudioMute(true);
        Assert.True(editor.Project.MasterAudioMute);

        editor.SetMasterAudioDim(true);
        Assert.True(editor.Project.MasterAudioDim);

        editor.Undo(); // undo dim
        Assert.False(editor.Project.MasterAudioDim);

        editor.Undo(); // undo mute
        Assert.False(editor.Project.MasterAudioMute);

        editor.Undo(); // undo gain
        Assert.Equal(1.0, editor.Project.MasterAudioGain);
    }

    [Fact]
    public void TimelineEditor_MasterAudioEffects_AddAndRemove_SupportsUndo()
    {
        var doc = new ProjectDocument();
        var editor = new TimelineEditor(doc);

        var comp = AudioEffectCatalog.CreateTrackCompressor();
        editor.AddMasterAudioEffect(comp);
        Assert.Single(editor.Project.MasterAudioEffects);

        editor.SetMasterAudioEffectBypassed(comp.Id, true);
        Assert.True(editor.Project.MasterAudioEffects[0].IsBypassed);

        editor.RemoveMasterAudioEffect(comp.Id);
        Assert.Empty(editor.Project.MasterAudioEffects);

        editor.Undo();
        Assert.Single(editor.Project.MasterAudioEffects);
    }

    [Fact]
    public void TimelineEditor_AudioEffects_RejectsVideoTrack()
    {
        var doc = new ProjectDocument();
        var videoTrack = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Video };
        doc.Tracks.Add(videoTrack);
        var editor = new TimelineEditor(doc);

        var eq = AudioEffectCatalog.CreateTrackEq();
        Assert.Throws<InvalidOperationException>(() => editor.AddTrackAudioEffect(videoTrack.Id, eq));
        Assert.Throws<InvalidOperationException>(() => editor.SetTrackInvertPhase(videoTrack.Id, true));
    }
}
