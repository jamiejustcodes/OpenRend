using System.Text.Json;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class GroupingTests
{
    private static (TimelineEditor Editor, TimelineClip Video, TimelineClip Audio) CreatePair()
    {
        var media = new MediaAsset { Path = "pair.mp4", DurationSeconds = 20, HasVideo = true, HasAudio = true };
        var video = new TimelineClip { MediaId = media.Id, StartFrame = 10, SourceInFrame = 20, DurationFrames = 90, FadeInFrames = 40, FadeOutFrames = 30 };
        var audio = video.DeepClone(); audio.Id = Guid.NewGuid();
        var group = Guid.NewGuid(); video.GroupId = audio.GroupId = group;
        var project = new ProjectDocument { Media = [media], Tracks = [new() { Kind = TrackKind.Video, Clips = [video] }, new() { Kind = TrackKind.Audio, Clips = [audio] }] };
        return (new TimelineEditor(project), video, audio);
    }
    private static TimelineClip Clip(TimelineEditor editor, Guid id) => editor.Project.Tracks.SelectMany(t => t.Clips).Single(c => c.Id == id);

    [Fact]
    public void GroupMembershipSurvivesCloneAndSerializationAndCanBeBypassed()
    {
        var (editor, video, audio) = CreatePair();
        Assert.Equal(video.GroupId, editor.Project.DeepClone().Tracks[0].Clips[0].GroupId);
        var loaded = JsonSerializer.Deserialize<ProjectDocument>(JsonSerializer.Serialize(editor.Project))!;
        loaded.Validate(); Assert.Equal(video.GroupId, loaded.Tracks[1].Clips[0].GroupId);
        Assert.True(editor.ExpandSelection([video.Id]).SetEquals([video.Id, audio.Id]));
        Assert.True(editor.ExpandSelection([video.Id], ignoreGroups: true).SetEquals([video.Id]));
        Assert.Throws<KeyNotFoundException>(() => editor.ExpandSelection([Guid.NewGuid()]));
    }

    [Fact]
    public void MoveIsAtomicKeepsOffsetsAndOnlyChangesPrimaryTrack()
    {
        var (editor, video, audio) = CreatePair();
        var extra = new TimelineTrack { Kind = TrackKind.Video };
        editor.Project.Tracks.Add(extra);
        audio.StartFrame = 15;
        editor.MoveSelection([video.Id], video.Id, extra.Id, 50);
        Assert.Equal(50, Clip(editor, video.Id).StartFrame); Assert.Equal(55, Clip(editor, audio.Id).StartFrame);
        Assert.Single(extra.Clips); Assert.Empty(editor.Project.Tracks[0].Clips); Assert.Equal(audio.Id, Assert.Single(editor.Project.Tracks[1].Clips).Id);
        Assert.True(editor.Undo()); Assert.Equal(10, Clip(editor, video.Id).StartFrame); Assert.Equal(15, Clip(editor, audio.Id).StartFrame); Assert.False(editor.CanUndo);
        Assert.True(editor.Redo()); Assert.Equal(50, Clip(editor, video.Id).StartFrame); Assert.Equal(55, Clip(editor, audio.Id).StartFrame);
        editor.MoveSelection([video.Id], video.Id, extra.Id, 60, ignoreGroups: true);
        Assert.Equal(60, Clip(editor, video.Id).StartFrame); Assert.Equal(55, Clip(editor, audio.Id).StartFrame);
    }

    [Fact]
    public void TrimAppliesSameDeltasAndResetsSplitFadeAnchors()
    {
        var (editor, video, audio) = CreatePair();
        audio.StartFrame = 15; audio.SourceInFrame = 25;
        video.FadeOffsetFrames = audio.FadeOffsetFrames = 10;
        video.FadeDurationFrames = audio.FadeDurationFrames = 100;
        editor.TrimSelection(video.Id, 20, 30, 30);
        foreach (var id in new[] { video.Id, audio.Id })
        {
            var clip = Clip(editor, id); Assert.Equal(id == video.Id ? 20 : 25, clip.StartFrame); Assert.Equal(id == video.Id ? 30 : 35, clip.SourceInFrame);
            Assert.Equal(30, clip.DurationFrames); Assert.Equal(0, clip.FadeOffsetFrames); Assert.Null(clip.FadeDurationFrames); Assert.Equal(30, clip.FadeInFrames); Assert.Equal(0, clip.FadeOutFrames);
        }
        Assert.True(editor.Undo()); Assert.Equal(90, Clip(editor, audio.Id).DurationFrames); Assert.Equal(10, Clip(editor, video.Id).FadeOffsetFrames);
        Assert.True(editor.Redo()); Assert.Equal(30, Clip(editor, audio.Id).DurationFrames);
    }

    [Fact]
    public void SplitPreservesEveryFrameFadeAndCreatesIndependentRightGroups()
    {
        var (editor, video, audio) = CreatePair();
        var secondVideo = video.DeepClone(); secondVideo.Id = Guid.NewGuid(); secondVideo.GroupId = Guid.NewGuid();
        var secondAudio = audio.DeepClone(); secondAudio.Id = Guid.NewGuid(); secondAudio.GroupId = secondVideo.GroupId;
        editor.Project.Tracks[0].Clips.Add(secondVideo); editor.Project.Tracks[1].Clips.Add(secondAudio);
        var expected = Enumerable.Range(0, 90).Select(f => TimelineEvaluator.FadeAt(video, f)).ToArray();
        var rights = editor.SplitSelection([video.Id, secondVideo.Id], 30);
        Assert.Equal(4, rights.Count);
        var rightVideo = rights.Single(c => c.GroupId == rights[0].GroupId && editor.Project.Tracks[0].Clips.Contains(c));
        Assert.NotEqual(video.GroupId, rightVideo.GroupId); Assert.NotEqual(secondVideo.GroupId, rightVideo.GroupId);
        Assert.Equal(2, rights.Select(c => c.GroupId).Distinct().Count());
        foreach (var group in rights.GroupBy(c => c.GroupId)) Assert.Equal(2, group.Count());
        for (var frame = 10; frame < 100; ++frame)
        {
            var pair = editor.Project.Tracks.Select(t => t.Clips.First(c => c.GroupId == (frame < 30 ? video.GroupId : rightVideo.GroupId) && c.StartFrame <= frame && c.EndFrame > frame));
            foreach (var clip in pair) { Assert.Equal(expected[frame - 10], TimelineEvaluator.FadeAt(clip, frame - clip.StartFrame), 12); Assert.Equal(frame + 10, clip.SourceInFrame + frame - clip.StartFrame); }
        }
        Assert.True(editor.ExpandSelection([rightVideo.Id]).SetEquals(rights.Where(c => c.GroupId == rightVideo.GroupId).Select(c => c.Id)));
        Assert.True(editor.Undo()); Assert.Equal(4, editor.Project.Tracks.Sum(t => t.Clips.Count)); Assert.False(editor.CanUndo);
        Assert.True(editor.Redo()); Assert.Equal(8, editor.Project.Tracks.Sum(t => t.Clips.Count));
    }

    [Fact]
    public void DeleteExpandsGroupsAndRipplesEachEditedTrackByRemovedSpanUnion()
    {
        var (editor, video, audio) = CreatePair();
        video.DurationFrames = audio.DurationFrames = 20; video.FadeInFrames = audio.FadeInFrames = 0; video.FadeOutFrames = audio.FadeOutFrames = 0;
        var overlap = video.DeepClone(); overlap.Id = Guid.NewGuid(); overlap.GroupId = null; overlap.StartFrame = 20;
        var laterVideo = video.DeepClone(); laterVideo.Id = Guid.NewGuid(); laterVideo.GroupId = null; laterVideo.StartFrame = 80;
        var laterAudio = audio.DeepClone(); laterAudio.Id = Guid.NewGuid(); laterAudio.GroupId = null; laterAudio.StartFrame = 80;
        editor.Project.Tracks[0].Clips.AddRange([overlap, laterVideo]); editor.Project.Tracks[1].Clips.Add(laterAudio);
        editor.DeleteSelection([video.Id, overlap.Id], ripple: true);
        Assert.Equal(50, Clip(editor, laterVideo.Id).StartFrame); Assert.Equal(60, Clip(editor, laterAudio.Id).StartFrame);
        Assert.Equal(2, editor.Project.Tracks.Sum(t => t.Clips.Count)); Assert.True(editor.Undo()); Assert.Equal(5, editor.Project.Tracks.Sum(t => t.Clips.Count)); Assert.False(editor.CanUndo);
        Assert.True(editor.Redo()); Assert.Equal(2, editor.Project.Tracks.Sum(t => t.Clips.Count));
    }

    [Fact]
    public void LockedGroupmateRejectsAllMutationsWithoutAddingHistory()
    {
        var (editor, video, audio) = CreatePair(); editor.Project.Tracks[1].IsLocked = true;
        Assert.Throws<InvalidOperationException>(() => editor.MoveSelection([video.Id], video.Id, editor.Project.Tracks[0].Id, 50));
        Assert.Throws<InvalidOperationException>(() => editor.TrimSelection(video.Id, 20, 30, 60));
        Assert.Throws<InvalidOperationException>(() => editor.SplitSelection([video.Id], 40));
        Assert.Throws<InvalidOperationException>(() => editor.DeleteSelection([video.Id], ripple: true));
        Assert.Throws<InvalidOperationException>(() => editor.UnlinkClips([video.Id]));
        Assert.Equal(10, Clip(editor, video.Id).StartFrame); Assert.Equal(90, Clip(editor, audio.Id).DurationFrames); Assert.False(editor.CanUndo);
        editor.MoveSelection([video.Id], video.Id, editor.Project.Tracks[0].Id, 20, ignoreGroups: true);
        Assert.Equal(20, Clip(editor, video.Id).StartFrame); Assert.Equal(10, Clip(editor, audio.Id).StartFrame);
    }

    [Fact]
    public void InvalidSecondaryBoundsAndIncompatibleTargetRejectWholeTransaction()
    {
        var (editor, video, audio) = CreatePair(); audio.SourceInFrame = 510;
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.TrimSelection(video.Id, 20, 30, 90));
        Assert.Equal(10, Clip(editor, video.Id).StartFrame); Assert.Equal(20, Clip(editor, video.Id).SourceInFrame); Assert.False(editor.CanUndo);
        audio.SourceInFrame = 20; audio.StartFrame = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.MoveSelection([video.Id], video.Id, editor.Project.Tracks[0].Id, 5)); Assert.Equal(10, video.StartFrame); Assert.False(editor.CanUndo);
        editor.Project.Media[0].HasAudio = false;
        Assert.Throws<InvalidOperationException>(() => editor.MoveSelection([video.Id], video.Id, editor.Project.Tracks[1].Id, 20)); Assert.Equal(10, video.StartFrame); Assert.False(editor.CanUndo);
    }

    [Fact]
    public void LinkingMergesWholeGroupsAndUnlinkingIsOneUndoUnit()
    {
        var (editor, video, audio) = CreatePair();
        var third = video.DeepClone(); third.Id = Guid.NewGuid(); third.GroupId = null; editor.Project.Tracks[0].Clips.Add(third);
        var original = video.GroupId;
        editor.LinkClips([video.Id, third.Id]); Assert.True(editor.ExpandSelection([audio.Id]).SetEquals([video.Id, audio.Id, third.Id])); Assert.NotEqual(original, video.GroupId);
        Assert.True(editor.Undo()); Assert.Equal(original, Clip(editor, audio.Id).GroupId); Assert.Null(Clip(editor, third.Id).GroupId); Assert.True(editor.Redo());
        editor.UnlinkClips([audio.Id]); Assert.All(editor.Project.Tracks.SelectMany(t => t.Clips), c => Assert.Null(c.GroupId));
        Assert.True(editor.Undo()); Assert.True(editor.ExpandSelection([third.Id]).SetEquals([video.Id, audio.Id, third.Id]));
    }

    [Fact]
    public void ExplicitMultiSelectionIncludesUngroupedClipsAndLockedTargetRejectsMove()
    {
        var (editor, video, audio) = CreatePair();
        var other = video.DeepClone(); other.Id = Guid.NewGuid(); other.GroupId = null; other.StartFrame = 30;
        editor.Project.Tracks[0].Clips.Add(other);
        var target = new TimelineTrack { Kind = TrackKind.Video, IsLocked = true }; editor.Project.Tracks.Add(target);
        Assert.Throws<InvalidOperationException>(() => editor.MoveSelection([video.Id, other.Id], video.Id, target.Id, 50));
        Assert.Equal(30, other.StartFrame); Assert.False(editor.CanUndo);
        target.IsLocked = false;
        editor.MoveSelection([video.Id, other.Id], video.Id, target.Id, 50);
        Assert.Equal(50, video.StartFrame); Assert.Equal(50, audio.StartFrame); Assert.Equal(70, other.StartFrame);
        Assert.Equal(other.Id, Assert.Single(editor.Project.Tracks[0].Clips).Id); Assert.Equal(video.Id, Assert.Single(target.Clips).Id);
    }

    [Fact]
    public void IgnoringGroupsDeletesOnlyExplicitSelectionAndSplitSkipsNoncrossingGroupmate()
    {
        var (editor, video, audio) = CreatePair(); audio.StartFrame = 120;
        var rights = editor.SplitSelection([video.Id], 40);
        Assert.Single(rights); Assert.Equal(90, audio.DurationFrames);
        editor.DeleteSelection([video.Id], ignoreGroups: true);
        Assert.DoesNotContain(editor.Project.Tracks.SelectMany(t => t.Clips), c => c.Id == video.Id);
        Assert.Equal(audio.Id, Assert.Single(editor.Project.Tracks[1].Clips).Id); Assert.Equal(rights[0].Id, Assert.Single(editor.Project.Tracks[0].Clips).Id);
    }
}
