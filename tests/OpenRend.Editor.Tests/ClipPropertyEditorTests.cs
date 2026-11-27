using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class ClipPropertyEditorTests
{
    [Fact]
    public void LockedGroupMemberRejectsTimingBeforeMakingAnyChanges()
    {
        var asset = new MediaAsset { Path = "source.mp4", HasVideo = true, HasAudio = true, DurationSeconds = 3 };
        var clip = new TimelineClip { MediaId = asset.Id, StartFrame = 8, DurationFrames = 30, GroupId = Guid.NewGuid() };
        var linked = new TimelineClip
        {
            MediaId = asset.Id, StartFrame = 8, DurationFrames = 30, GroupId = clip.GroupId, IsLocked = true
        };
        var project = new ProjectDocument
        {
            Media = [asset],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }, new() { Kind = TrackKind.Audio, Clips = [linked] }]
        };
        var editor = new TimelineEditor(project);
        var edit = new ClipPropertyValues(12, 0, 30, 0.5, 1, 0, 0, 0);
        Assert.Throws<InvalidOperationException>(() => editor.SetClipProperties(clip.Id, edit, ignoreGroups: false));
        Assert.Equal((8L, 1d), (clip.StartFrame, clip.Opacity));
        Assert.Equal(8, linked.StartFrame);
        Assert.False(editor.CanUndo);

        Assert.True(editor.SetClipProperties(clip.Id, edit, ignoreGroups: true));
        Assert.Equal((12L, .5), (clip.StartFrame, clip.Opacity));
        Assert.Equal(8, linked.StartFrame);
        Assert.True(editor.Undo());
        Assert.False(editor.CanUndo);
    }
}
