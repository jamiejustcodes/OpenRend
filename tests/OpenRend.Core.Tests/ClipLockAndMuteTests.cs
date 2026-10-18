using System.IO;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class ClipLockAndMuteTests
{
    private static (TimelineEditor Editor, TimelineClip Clip) Create(bool isVideo = true)
    {
        var media = new MediaAsset { Path = Path.GetFullPath("test.mp4"), HasVideo = isVideo, HasAudio = !isVideo, DurationSeconds = 10 };
        var clip = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 90 };
        var project = new ProjectDocument
        {
            Media = [media],
            Tracks = [new() { Kind = isVideo ? TrackKind.Video : TrackKind.Audio, Clips = [clip] }]
        };
        project.Validate();
        return (new TimelineEditor(project), clip);
    }

    [Fact]
    public void LockedClipRejectsModifyingOperations()
    {
        var (editor, clip) = Create(isVideo: true);
        editor.SetClipLock(clip.Id, true);
        Assert.True(clip.IsLocked);

        var targetTrack = new TimelineTrack { Kind = TrackKind.Video };
        editor.Project.Tracks.Add(targetTrack);

        Assert.Throws<InvalidOperationException>(() => editor.MoveClip(clip.Id, targetTrack.Id, 10));
        Assert.Throws<InvalidOperationException>(() => editor.TrimClip(clip.Id, 10, 60));
        Assert.Throws<InvalidOperationException>(() => editor.SplitClip(clip.Id, 30));
        Assert.Throws<InvalidOperationException>(() => editor.DeleteClip(clip.Id));
        Assert.Throws<InvalidOperationException>(() => editor.SetClipLevels(clip.Id, opacity: 0.5));
        Assert.Throws<InvalidOperationException>(() => editor.SetVideoFadeCurve(clip.Id, VideoFadeCurve.Fast));
        Assert.Throws<InvalidOperationException>(() => editor.SetVideoSourceMode(clip.Id, VideoSourceMode.Loop));
        Assert.Throws<InvalidOperationException>(() => editor.SetClipMute(clip.Id, true));

        // Unlock works and allows editing again
        editor.SetClipLock(clip.Id, false);
        Assert.False(clip.IsLocked);
        editor.TrimClip(clip.Id, 0, 80);
        Assert.Equal(80, clip.DurationFrames);
    }

    [Fact]
    public void DeleteSelectionRejectsLockedClips()
    {
        var (editor, clip1) = Create(isVideo: true);
        var media = editor.Project.Media[0];
        var clip2 = new TimelineClip { MediaId = media.Id, StartFrame = 100, DurationFrames = 50, IsLocked = true };
        editor.Project.Tracks[0].Clips.Add(clip2);

        Assert.Throws<InvalidOperationException>(() => editor.DeleteSelection([clip1.Id, clip2.Id]));
        Assert.Contains(clip1, editor.Project.Tracks[0].Clips);
        Assert.Contains(clip2, editor.Project.Tracks[0].Clips);

        // Deleting only unlocked clip succeeds
        editor.DeleteSelection([clip1.Id]);
        Assert.DoesNotContain(clip1, editor.Project.Tracks[0].Clips);
        Assert.Contains(clip2, editor.Project.Tracks[0].Clips);
    }

    [Fact]
    public void SplitSelectionRejectsLockedClips()
    {
        var (editor, clip1) = Create(isVideo: true);
        var media = editor.Project.Media[0];
        var clip2 = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 90, IsLocked = true };
        editor.Project.Tracks.Add(new TimelineTrack { Kind = TrackKind.Video, Clips = [clip2] });

        Assert.Throws<InvalidOperationException>(() => editor.SplitSelection([clip1.Id, clip2.Id], 30));

        var splits = editor.SplitSelection([clip1.Id], 30);
        Assert.Single(splits);
        Assert.Equal(30, splits[0].StartFrame);
        Assert.Equal(30, clip1.DurationFrames);
        Assert.Equal(90, clip2.DurationFrames);
    }

    [Fact]
    public void MutedClipIsExcludedFromTimelineEvaluation()
    {
        var (editor, clip) = Create(isVideo: true);
        var evaluated = TimelineEvaluator.Evaluate(editor.Project, 10);
        Assert.Single(evaluated.Video);

        editor.SetClipMute(clip.Id, true);
        Assert.True(clip.IsMuted);

        evaluated = TimelineEvaluator.Evaluate(editor.Project, 10);
        Assert.Empty(evaluated.Video);

        Assert.True(editor.Undo());
        Assert.False(editor.Project.Tracks[0].Clips[0].IsMuted);
        evaluated = TimelineEvaluator.Evaluate(editor.Project, 10);
        Assert.Single(evaluated.Video);
    }

    [Fact]
    public async Task Schema9PreservesLockAndMuteRoundTrip()
    {
        var (editor, clip) = Create(isVideo: true);
        editor.SetClipMute(clip.Id, true);
        editor.SetClipLock(clip.Id, true);

        var tempPath = Path.Combine(Path.GetTempPath(), "openrend_schema9_test_" + Guid.NewGuid().ToString("N") + ".openrend");
        try
        {
            await ProjectStore.SaveAsync(editor.Project, tempPath);
            var json = await File.ReadAllTextAsync(tempPath);
            Assert.Contains("\"Version\": 9", json);
            Assert.Contains("\"IsLocked\": true", json);
            Assert.Contains("\"IsMuted\": true", json);

            var loaded = await ProjectStore.LoadAsync(tempPath);
            var reloadedClip = loaded.Tracks[0].Clips[0];
            Assert.True(reloadedClip.IsLocked);
            Assert.True(reloadedClip.IsMuted);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MutingAudioWithIgnoreGroupsLeavesLinkedVideoUnmutedAndEvaluated()
    {
        var media = new MediaAsset { Path = Path.GetFullPath("test_av.mp4"), HasVideo = true, HasAudio = true, DurationSeconds = 10 };
        var videoClip = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 90 };
        var audioClip = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 90 };
        var videoTrack = new TimelineTrack { Kind = TrackKind.Video, Clips = [videoClip] };
        var audioTrack = new TimelineTrack { Kind = TrackKind.Audio, Clips = [audioClip] };
        var project = new ProjectDocument
        {
            Media = [media],
            Tracks = [videoTrack, audioTrack]
        };
        project.Validate();
        var editor = new TimelineEditor(project);
        editor.LinkClips([videoClip.Id, audioClip.Id]);
        Assert.NotNull(videoClip.GroupId);
        Assert.Equal(videoClip.GroupId, audioClip.GroupId);

        // Mute audio with ignoreGroups: true (the Ctrl+Shift+M / MuteAudio button workflow)
        editor.SetSelectionMute([audioClip.Id], true, ignoreGroups: true);
        Assert.True(audioClip.IsMuted);
        Assert.False(videoClip.IsMuted);

        // Timeline evaluation: video is present, audio is empty
        var evaluated = TimelineEvaluator.Evaluate(project, 15);
        Assert.Single(evaluated.Video);
        Assert.Equal(videoClip.Id, evaluated.Video[0].Clip.Id);
        Assert.Empty(evaluated.Audio);
    }
}
