using System;
using System.IO;
using System.Linq;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class HealAndGapsTests
{
    private static (ProjectDocument Project, TimelineEditor Editor, TimelineTrack VideoTrack, TimelineClip Clip, MediaAsset Media) CreateTestSetup()
    {
        var media = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = "C:\\test\\sample.mp4",
            HasVideo = true,
            HasAudio = true,
            DurationSeconds = 10.0,
            Width = 1920,
            Height = 1080,
            FrameRate = 30.0
        };

        var project = new ProjectDocument
        {
            Name = "Heal and Gaps Test",
            FrameRate = new Rational(30, 1),
            Media = [media]
        };

        var track = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Video, Name = "Video 1" };
        var clip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = media.Id,
            Name = "Sample Clip",
            StartFrame = 0,
            DurationFrames = 90,
            SourceInFrame = 0,
            PlaybackRate = 1.0,
            FadeInFrames = 10,
            FadeOutFrames = 15
        };
        track.Clips.Add(clip);
        project.Tracks.Add(track);

        var editor = new TimelineEditor(project);
        return (project, editor, track, clip, media);
    }

    [Fact]
    public void HealEvents_MergesContiguousSplitSlicesFromSameMedia()
    {
        var (_, editor, track, clip, _) = CreateTestSetup();

        // Split clip at frame 40 into two contiguous pieces
        var right = editor.SplitClip(clip.Id, 40);
        Assert.Equal(2, editor.Project.Tracks[0].Clips.Count);
        Assert.Equal(40, clip.DurationFrames);
        Assert.Equal(40, right.StartFrame);
        Assert.Equal(50, right.DurationFrames);
        Assert.Equal(40, right.SourceInFrame);

        // CanHeal should return true for either single slice or both
        Assert.True(editor.CanHeal([clip.Id, right.Id]));
        Assert.True(editor.CanHeal([clip.Id]));
        Assert.True(editor.CanHeal([right.Id]));

        // Execute Heal
        var healed = editor.HealEvents([clip.Id, right.Id]);
        Assert.Single(healed);
        Assert.Single(editor.Project.Tracks[0].Clips);

        var merged = editor.Project.Tracks[0].Clips[0];
        Assert.Equal(clip.Id, merged.Id);
        Assert.Equal(0, merged.StartFrame);
        Assert.Equal(90, merged.DurationFrames);
        Assert.Equal(0, merged.SourceInFrame);
        Assert.Equal(10, merged.FadeInFrames);
        Assert.Equal(15, merged.FadeOutFrames);

        // Undo restores both slices
        Assert.True(editor.Undo());
        Assert.Equal(2, editor.Project.Tracks[0].Clips.Count);

        // Redo restores healed single event
        Assert.True(editor.Redo());
        Assert.Single(editor.Project.Tracks[0].Clips);
        Assert.Equal(90, editor.Project.Tracks[0].Clips[0].DurationFrames);
    }

    [Fact]
    public void HealEvents_HealsMultiSplitChainIteratively()
    {
        var (_, editor, track, clip, _) = CreateTestSetup();

        // Split into 3 slices: [0..30], [30..60], [60..90]
        var middle = editor.SplitClip(clip.Id, 30);
        var right = editor.SplitClip(middle.Id, 60);

        Assert.Equal(3, editor.Project.Tracks[0].Clips.Count);

        // Heal all 3
        var healed = editor.HealEvents([clip.Id, middle.Id, right.Id]);
        Assert.Single(healed);
        Assert.Single(editor.Project.Tracks[0].Clips);
        Assert.Equal(90, editor.Project.Tracks[0].Clips[0].DurationFrames);
        Assert.Equal(0, editor.Project.Tracks[0].Clips[0].StartFrame);
    }

    [Fact]
    public void HealEvents_SupportsSingleSelectedClipWithNeighbor()
    {
        var (_, editor, track, clip, _) = CreateTestSetup();

        var right = editor.SplitClip(clip.Id, 45);
        Assert.Equal(2, editor.Project.Tracks[0].Clips.Count);

        // Select only the right piece
        var healed = editor.HealEvents([right.Id]);
        Assert.Single(healed);
        Assert.Single(editor.Project.Tracks[0].Clips);
        Assert.Equal(90, editor.Project.Tracks[0].Clips[0].DurationFrames);
    }

    [Fact]
    public void HealEvents_RejectsNonContiguousOrDifferentMedia()
    {
        var (project, editor, track, clip1, media) = CreateTestSetup();

        // Second clip has a gap
        var clip2 = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = media.Id,
            StartFrame = 100, // gap of 10 frames after clip1 ends at 90
            DurationFrames = 50,
            SourceInFrame = 90
        };
        track.Clips.Add(clip2);

        Assert.False(editor.CanHeal([clip1.Id, clip2.Id]));
        var healed = editor.HealEvents([clip1.Id, clip2.Id]);
        Assert.Empty(healed);
        Assert.Equal(2, track.Clips.Count);

        // Different media asset touching
        var otherMedia = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = "C:\\other.mp4",
            HasVideo = true
        };
        project.Media.Add(otherMedia);
        clip2.StartFrame = 90; // now touching
        clip2.MediaId = otherMedia.Id;

        Assert.False(editor.CanHeal([clip1.Id, clip2.Id]));
        healed = editor.HealEvents([clip1.Id, clip2.Id]);
        Assert.Empty(healed);
        Assert.Equal(2, track.Clips.Count);
    }

    [Fact]
    public void HealEvents_AdjustmentEvents_MergesSlicesWithMatchingDimensions()
    {
        var project = new ProjectDocument { FrameRate = new Rational(30, 1), Width = 1920, Height = 1080 };
        var editor = new TimelineEditor(project);
        var track = editor.AddAdjustmentTrack(0);

        var adj1 = editor.InsertAdjustmentEvent(track.Id, 0, 50);
        var adj2 = editor.InsertAdjustmentEvent(track.Id, 50, 50);

        Assert.True(editor.CanHeal([adj1.Id, adj2.Id]));
        var healed = editor.HealEvents([adj1.Id, adj2.Id]);
        Assert.Single(healed);
        Assert.Single(track.Clips);
        Assert.Equal(100, track.Clips[0].DurationFrames);
    }

    [Fact]
    public void CloseGaps_MultiSelectedEvents_ClosesGapsOnTracks()
    {
        var (_, editor, track, _, media) = CreateTestSetup();
        track.Clips.Clear();

        // Create 3 clips with 15-frame gaps: [0..30], [45..75], [90..120]
        var c1 = new TimelineClip { Id = Guid.NewGuid(), MediaId = media.Id, StartFrame = 0, DurationFrames = 30 };
        var c2 = new TimelineClip { Id = Guid.NewGuid(), MediaId = media.Id, StartFrame = 45, DurationFrames = 30 };
        var c3 = new TimelineClip { Id = Guid.NewGuid(), MediaId = media.Id, StartFrame = 90, DurationFrames = 30 };
        track.Clips.Add(c1);
        track.Clips.Add(c2);
        track.Clips.Add(c3);

        editor.CloseGaps([c1.Id, c2.Id, c3.Id]);

        Assert.Equal(0, editor.Project.Tracks[0].Clips[0].StartFrame);
        Assert.Equal(30, editor.Project.Tracks[0].Clips[1].StartFrame);
        Assert.Equal(60, editor.Project.Tracks[0].Clips[2].StartFrame);

        // Undo restores gaps
        Assert.True(editor.Undo());
        Assert.Equal(45, editor.Project.Tracks[0].Clips[1].StartFrame);
        Assert.Equal(90, editor.Project.Tracks[0].Clips[2].StartFrame);

        // Redo reapplies gap closure
        Assert.True(editor.Redo());
        Assert.Equal(30, editor.Project.Tracks[0].Clips[1].StartFrame);
        Assert.Equal(60, editor.Project.Tracks[0].Clips[2].StartFrame);
    }

    [Fact]
    public void CloseGaps_SingleTrackMode_ClosesAllGapsWhenNoneSelected()
    {
        var (_, editor, track, _, media) = CreateTestSetup();
        track.Clips.Clear();

        var c1 = new TimelineClip { Id = Guid.NewGuid(), MediaId = media.Id, StartFrame = 10, DurationFrames = 20 };
        var c2 = new TimelineClip { Id = Guid.NewGuid(), MediaId = media.Id, StartFrame = 50, DurationFrames = 20 };
        var c3 = new TimelineClip { Id = Guid.NewGuid(), MediaId = media.Id, StartFrame = 100, DurationFrames = 20 };
        track.Clips.Add(c1);
        track.Clips.Add(c2);
        track.Clips.Add(c3);

        // Single-track mode: passing null/empty clipIds and singleTrackIndex 0
        editor.CloseGaps(null, 0);

        Assert.Equal(10, editor.Project.Tracks[0].Clips[0].StartFrame);
        Assert.Equal(30, editor.Project.Tracks[0].Clips[1].StartFrame);
        Assert.Equal(50, editor.Project.Tracks[0].Clips[2].StartFrame);
    }

    [Fact]
    public void AddMissingStream_VideoEvent_AddsPairedAudioStreamInGroup()
    {
        var (project, editor, track, clip, media) = CreateTestSetup();
        // media has HasVideo = true, HasAudio = true
        // currently only video track exists

        Assert.True(editor.CanAddMissingStream([clip.Id]));

        var created = editor.AddMissingStream([clip.Id]);
        Assert.Single(created);

        var audioClip = created[0];
        Assert.Equal(clip.MediaId, audioClip.MediaId);
        Assert.Equal(clip.StartFrame, audioClip.StartFrame);
        Assert.Equal(clip.DurationFrames, audioClip.DurationFrames);
        Assert.Equal(clip.SourceInFrame, audioClip.SourceInFrame);

        // Both clips should now share a GroupId
        Assert.NotNull(clip.GroupId);
        Assert.Equal(clip.GroupId, audioClip.GroupId);

        // Audio track should be created
        Assert.Equal(2, editor.Project.Tracks.Count);
        var audioTrack = editor.Project.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Audio);
        Assert.NotNull(audioTrack);
        Assert.Contains(audioClip, audioTrack.Clips);

        // Calling CanAddMissingStream now returns false since stream is present
        Assert.False(editor.CanAddMissingStream([clip.Id]));

        // Undo removes the missing stream and added audio track
        Assert.True(editor.Undo());
        Assert.Single(editor.Project.Tracks);
        Assert.Null(editor.Project.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Audio));

        // Redo restores it
        Assert.True(editor.Redo());
        Assert.Equal(2, editor.Project.Tracks.Count);
    }

    [Fact]
    public void AddMissingStream_AudioEvent_AddsPairedVideoStreamInGroup()
    {
        var media = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = "C:\\test\\clip.mp4",
            HasVideo = true,
            HasAudio = true
        };
        var project = new ProjectDocument
        {
            Name = "Missing Video Stream Test",
            FrameRate = new Rational(30, 1),
            Media = [media]
        };
        var audioTrack = new TimelineTrack { Id = Guid.NewGuid(), Kind = TrackKind.Audio, Name = "Audio 1" };
        var audioClip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = media.Id,
            Name = "Audio Event",
            StartFrame = 15,
            DurationFrames = 75,
            SourceInFrame = 15
        };
        audioTrack.Clips.Add(audioClip);
        project.Tracks.Add(audioTrack);

        var editor = new TimelineEditor(project);
        Assert.True(editor.CanAddMissingStream([audioClip.Id]));

        var created = editor.AddMissingStream([audioClip.Id]);
        Assert.Single(created);

        var videoClip = created[0];
        Assert.Equal(audioClip.MediaId, videoClip.MediaId);
        Assert.Equal(audioClip.StartFrame, videoClip.StartFrame);
        Assert.Equal(audioClip.DurationFrames, videoClip.DurationFrames);
        Assert.Equal(audioClip.SourceInFrame, videoClip.SourceInFrame);
        Assert.NotNull(audioClip.GroupId);
        Assert.Equal(audioClip.GroupId, videoClip.GroupId);

        var videoTrack = editor.Project.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Video);
        Assert.NotNull(videoTrack);
        Assert.Contains(videoClip, videoTrack.Clips);
    }
}
