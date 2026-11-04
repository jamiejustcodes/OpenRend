using System;
using System.IO;
using System.Linq;
using System.Threading;
using OpenRend.Core;
using OpenRend.Timeline;
using OpenRend.UI;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class GroupAndRippleEditorTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }

    [Fact]
    public void TimelineEditor_GroupSelection_And_RemoveFromGroup_MaintainsIntegrityAndUndo()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 10, HasVideo = true };
        doc.Media.Add(media);
        var track = new TimelineTrack { Kind = TrackKind.Video };
        var clipA = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var clipB = new TimelineClip { MediaId = media.Id, StartFrame = 30, DurationFrames = 30 };
        var clipC = new TimelineClip { MediaId = media.Id, StartFrame = 60, DurationFrames = 30 };
        track.Clips.Add(clipA);
        track.Clips.Add(clipB);
        track.Clips.Add(clipC);
        doc.Tracks.Add(track);

        var editor = new TimelineEditor(doc);

        // Group all three
        editor.LinkClips([clipA.Id, clipB.Id, clipC.Id]);
        var cA = editor.Project.Tracks[0].Clips[0];
        var cB = editor.Project.Tracks[0].Clips[1];
        var cC = editor.Project.Tracks[0].Clips[2];
        Assert.NotNull(cA.GroupId);
        Assert.Equal(cA.GroupId, cB.GroupId);
        Assert.Equal(cA.GroupId, cC.GroupId);
        var initialGroup = cA.GroupId.Value;

        // Remove clip B from group: clip A and clip C remain grouped
        editor.RemoveFromGroup([cB.Id]);
        cA = editor.Project.Tracks[0].Clips[0];
        cB = editor.Project.Tracks[0].Clips[1];
        cC = editor.Project.Tracks[0].Clips[2];
        Assert.Null(cB.GroupId);
        Assert.Equal(initialGroup, cA.GroupId);
        Assert.Equal(initialGroup, cC.GroupId);

        // Remove clip A: now only clip C would be left, so clip C is automatically dissolved
        editor.RemoveFromGroup([cA.Id]);
        cA = editor.Project.Tracks[0].Clips[0];
        cB = editor.Project.Tracks[0].Clips[1];
        cC = editor.Project.Tracks[0].Clips[2];
        Assert.Null(cA.GroupId);
        Assert.Null(cC.GroupId);

        // Undo restores clip A and C grouping
        Assert.True(editor.CanUndo);
        editor.Undo();
        cA = editor.Project.Tracks[0].Clips[0];
        cB = editor.Project.Tracks[0].Clips[1];
        cC = editor.Project.Tracks[0].Clips[2];
        Assert.Equal(initialGroup, cA.GroupId);
        Assert.Equal(initialGroup, cC.GroupId);
        Assert.Null(cB.GroupId);

        // Undo restores 3-clip group
        editor.Undo();
        cA = editor.Project.Tracks[0].Clips[0];
        cB = editor.Project.Tracks[0].Clips[1];
        cC = editor.Project.Tracks[0].Clips[2];
        Assert.Equal(initialGroup, cA.GroupId);
        Assert.Equal(initialGroup, cB.GroupId);
        Assert.Equal(initialGroup, cC.GroupId);
    }

    [Fact]
    public void TimelineEditor_ClearGroup_RemovesGroupFromAllMembers()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 10, HasVideo = true };
        doc.Media.Add(media);
        var track = new TimelineTrack { Kind = TrackKind.Video };
        var clipA = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var clipB = new TimelineClip { MediaId = media.Id, StartFrame = 30, DurationFrames = 30 };
        track.Clips.Add(clipA);
        track.Clips.Add(clipB);
        doc.Tracks.Add(track);

        var editor = new TimelineEditor(doc);
        editor.LinkClips([clipA.Id, clipB.Id]);
        var cA = editor.Project.Tracks[0].Clips[0];
        var cB = editor.Project.Tracks[0].Clips[1];
        Assert.NotNull(cA.GroupId);

        // ClearGroup on just clipA dissolves the entire group
        editor.ClearGroup([cA.Id]);
        cA = editor.Project.Tracks[0].Clips[0];
        cB = editor.Project.Tracks[0].Clips[1];
        Assert.Null(cA.GroupId);
        Assert.Null(cB.GroupId);

        // Undo restores group
        Assert.True(editor.CanUndo);
        editor.Undo();
        cA = editor.Project.Tracks[0].Clips[0];
        cB = editor.Project.Tracks[0].Clips[1];
        Assert.NotNull(cA.GroupId);
        Assert.Equal(cA.GroupId, cB.GroupId);
    }

    [Fact]
    public void TimelineEditor_DeleteSelection_AffectedTracks_OnlyRipplesSameTrack()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 10, HasVideo = true };
        doc.Media.Add(media);
        var track1 = new TimelineTrack { Kind = TrackKind.Video };
        var track2 = new TimelineTrack { Kind = TrackKind.Video };
        var clip1A = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var clip1B = new TimelineClip { MediaId = media.Id, StartFrame = 50, DurationFrames = 30 };
        var clip2A = new TimelineClip { MediaId = media.Id, StartFrame = 60, DurationFrames = 30 };
        track1.Clips.Add(clip1A);
        track1.Clips.Add(clip1B);
        track2.Clips.Add(clip2A);
        doc.Tracks.Add(track1);
        doc.Tracks.Add(track2);
        doc.Markers.Add(new TimelineMarker { Frame = 70, Label = "M1" });

        var editor = new TimelineEditor(doc);

        // Delete clip1A with AffectedTracks
        editor.DeleteSelection([clip1A.Id], ripple: true, ignoreGroups: true, rippleMode: AutoRippleMode.AffectedTracks);

        Assert.Single(editor.Project.Tracks[0].Clips);
        // clip1B shifted left by 30 frames: 50 - 30 = 20
        Assert.Equal(20, editor.Project.Tracks[0].Clips[0].StartFrame);
        // Track 2 clip2A unaffected: stays at 60
        Assert.Equal(60, editor.Project.Tracks[1].Clips[0].StartFrame);
        // Marker unaffected: stays at 70
        Assert.Equal(70, editor.Project.Markers[0].Frame);

        // Undo restores state
        editor.Undo();
        Assert.Equal(2, editor.Project.Tracks[0].Clips.Count);
        Assert.Equal(50, editor.Project.Tracks[0].Clips[1].StartFrame);
    }

    [Fact]
    public void TimelineEditor_DeleteSelection_AffectedTracksAndMarkers_RipplesTrackAndMarkers()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 10, HasVideo = true };
        doc.Media.Add(media);
        var track1 = new TimelineTrack { Kind = TrackKind.Video };
        var track2 = new TimelineTrack { Kind = TrackKind.Video };
        var clip1A = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var clip1B = new TimelineClip { MediaId = media.Id, StartFrame = 50, DurationFrames = 30 };
        var clip2A = new TimelineClip { MediaId = media.Id, StartFrame = 60, DurationFrames = 30 };
        track1.Clips.Add(clip1A);
        track1.Clips.Add(clip1B);
        track2.Clips.Add(clip2A);
        doc.Tracks.Add(track1);
        doc.Tracks.Add(track2);
        doc.Markers.Add(new TimelineMarker { Frame = 70, Label = "M1" });

        var editor = new TimelineEditor(doc);

        // Delete clip1A with AffectedTracksAndMarkers
        editor.DeleteSelection([clip1A.Id], ripple: true, ignoreGroups: true, rippleMode: AutoRippleMode.AffectedTracksAndMarkers);

        // clip1B shifted left: 50 - 30 = 20
        Assert.Equal(20, editor.Project.Tracks[0].Clips[0].StartFrame);
        // Track 2 unaffected: 60
        Assert.Equal(60, editor.Project.Tracks[1].Clips[0].StartFrame);
        // Marker shifted left: 70 - 30 = 40
        Assert.Equal(40, editor.Project.Markers[0].Frame);
    }

    [Fact]
    public void TimelineEditor_DeleteSelection_AllTracksAndMarkers_RipplesAllTracksAndMarkers()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 10, HasVideo = true };
        doc.Media.Add(media);
        var track1 = new TimelineTrack { Kind = TrackKind.Video };
        var track2 = new TimelineTrack { Kind = TrackKind.Video };
        var clip1A = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var clip1B = new TimelineClip { MediaId = media.Id, StartFrame = 50, DurationFrames = 30 };
        var clip2A = new TimelineClip { MediaId = media.Id, StartFrame = 60, DurationFrames = 30 };
        track1.Clips.Add(clip1A);
        track1.Clips.Add(clip1B);
        track2.Clips.Add(clip2A);
        doc.Tracks.Add(track1);
        doc.Tracks.Add(track2);
        doc.Markers.Add(new TimelineMarker { Frame = 70, Label = "M1" });

        var editor = new TimelineEditor(doc);

        // Delete clip1A with AllTracksAndMarkers
        editor.DeleteSelection([clip1A.Id], ripple: true, ignoreGroups: true, rippleMode: AutoRippleMode.AllTracksAndMarkers);

        // clip1B shifted left: 50 - 30 = 20
        Assert.Equal(20, editor.Project.Tracks[0].Clips[0].StartFrame);
        // Track 2 clip2A shifted left: 60 - 30 = 30
        Assert.Equal(30, editor.Project.Tracks[1].Clips[0].StartFrame);
        // Marker shifted left: 70 - 30 = 40
        Assert.Equal(40, editor.Project.Markers[0].Frame);

        // Undo
        editor.Undo();
        Assert.Equal(50, editor.Project.Tracks[0].Clips[1].StartFrame);
        Assert.Equal(60, editor.Project.Tracks[1].Clips[0].StartFrame);
        Assert.Equal(70, editor.Project.Markers[0].Frame);
    }

    [Fact]
    public void TimelineEditor_TrimSelection_RipplesDownstreamClipsAndMarkers()
    {
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 20, HasVideo = true };
        doc.Media.Add(media);
        var track = new TimelineTrack { Kind = TrackKind.Video };
        var clipA = new TimelineClip { MediaId = media.Id, StartFrame = 0, SourceInFrame = 0, DurationFrames = 30 };
        var clipB = new TimelineClip { MediaId = media.Id, StartFrame = 30, SourceInFrame = 0, DurationFrames = 30 };
        track.Clips.Add(clipA);
        track.Clips.Add(clipB);
        doc.Tracks.Add(track);
        doc.Markers.Add(new TimelineMarker { Frame = 40, Label = "M1" });

        var editor = new TimelineEditor(doc);

        // Trim clipA duration from 30 to 45 (+15 frames extension) with AffectedTracksAndMarkers
        editor.TrimSelection(clipA.Id, newStart: 0, sourceIn: 0, duration: 45, ignoreGroups: true, ripple: true, rippleMode: AutoRippleMode.AffectedTracksAndMarkers);

        Assert.Equal(45, editor.Project.Tracks[0].Clips[0].DurationFrames);
        // clipB pushed right by 15 frames: 30 + 15 = 45
        Assert.Equal(45, editor.Project.Tracks[0].Clips[1].StartFrame);
        // Marker pushed right by 15 frames: 40 + 15 = 55
        Assert.Equal(55, editor.Project.Markers[0].Frame);

        // Undo restores
        editor.Undo();
        Assert.Equal(30, editor.Project.Tracks[0].Clips[0].DurationFrames);
        Assert.Equal(30, editor.Project.Tracks[0].Clips[1].StartFrame);
        Assert.Equal(40, editor.Project.Markers[0].Frame);
    }

    [Fact]
    public void TimelineView_SelectClip_GroupSelectionBehavior() => RunSta(() =>
    {
        var view = new TimelineView();
        var doc = new ProjectDocument();
        var media = new MediaAsset { Path = "sample.mp4", DurationSeconds = 10, HasVideo = true };
        doc.Media.Add(media);
        var track1 = new TimelineTrack { Kind = TrackKind.Video };
        var track2 = new TimelineTrack { Kind = TrackKind.Audio };
        var group = Guid.NewGuid();
        var videoClip = new TimelineClip { Id = Guid.NewGuid(), MediaId = media.Id, StartFrame = 0, DurationFrames = 30, GroupId = group };
        var audioClip = new TimelineClip { Id = Guid.NewGuid(), MediaId = media.Id, StartFrame = 0, DurationFrames = 30, GroupId = group };
        track1.Clips.Add(videoClip);
        track2.Clips.Add(audioClip);
        doc.Tracks.Add(track1);
        doc.Tracks.Add(track2);
        view.Project = doc;

        // Normal mode (!ShouldIgnoreGroups): selecting video clip also selects audio clip
        view.SelectClip(videoClip.Id, add: false);
        Assert.Contains(videoClip.Id, view.Selection);
        Assert.Contains(audioClip.Id, view.Selection);

        // Deselecting video clip removes entire group
        view.RemoveClipSelection(videoClip.Id);
        Assert.DoesNotContain(videoClip.Id, view.Selection);
        Assert.DoesNotContain(audioClip.Id, view.Selection);

        // IgnoreGroups = true: selecting video clip selects ONLY video clip
        view.IgnoreGroups = true;
        view.SelectClip(videoClip.Id, add: false);
        Assert.Contains(videoClip.Id, view.Selection);
        Assert.DoesNotContain(audioClip.Id, view.Selection);
    });
}
