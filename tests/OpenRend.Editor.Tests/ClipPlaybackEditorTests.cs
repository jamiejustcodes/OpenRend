using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public class ClipPlaybackEditorTests
{
    private static (TimelineEditor Editor, TimelineClip Clip) CreateFixture()
    {
        var media = new MediaAsset { Path = "test.mp4", HasVideo = true, DurationSeconds = 10.0 };
        var clip = new TimelineClip
        {
            MediaId = media.Id,
            DurationFrames = 90,
            StartFrame = 0,
            SourceInFrame = 0
        };
        var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [clip] };
        var project = new ProjectDocument { Media = [media], Tracks = [track] };
        var editor = new TimelineEditor(project);
        return (editor, clip);
    }

    [Fact]
    public void SetClipReverseAndUndoRedo()
    {
        var (editor, clip) = CreateFixture();
        Assert.False(clip.IsReversed);

        editor.SetClipReverse(clip.Id, true);
        Assert.True(editor.Project.Tracks[0].Clips[0].IsReversed);

        Assert.True(editor.Undo());
        Assert.False(editor.Project.Tracks[0].Clips[0].IsReversed);

        Assert.True(editor.Redo());
        Assert.True(editor.Project.Tracks[0].Clips[0].IsReversed);
    }

    [Fact]
    public void ToggleClipReverseOnSelection()
    {
        var (editor, clip1) = CreateFixture();
        var clip2 = new TimelineClip { MediaId = editor.Project.Media[0].Id, StartFrame = 100, DurationFrames = 60 };
        editor.Project.Tracks[0].Clips.Add(clip2);

        editor.ToggleClipReverseOnSelection([clip1.Id, clip2.Id]);
        Assert.True(editor.Project.Tracks[0].Clips[0].IsReversed);
        Assert.True(editor.Project.Tracks[0].Clips[1].IsReversed);

        editor.ToggleClipReverseOnSelection([clip1.Id, clip2.Id]);
        Assert.False(editor.Project.Tracks[0].Clips[0].IsReversed);
        Assert.False(editor.Project.Tracks[0].Clips[1].IsReversed);
    }

    [Fact]
    public void SetClipPlaybackRateAndUndoRedo()
    {
        var (editor, clip) = CreateFixture();
        Assert.Equal(1.0, clip.PlaybackRate);

        editor.SetClipPlaybackRate(clip.Id, 2.5);
        Assert.Equal(2.5, editor.Project.Tracks[0].Clips[0].PlaybackRate);

        Assert.True(editor.Undo());
        Assert.Equal(1.0, editor.Project.Tracks[0].Clips[0].PlaybackRate);

        Assert.True(editor.Redo());
        Assert.Equal(2.5, editor.Project.Tracks[0].Clips[0].PlaybackRate);
    }

    [Fact]
    public void SetClipResampleModeAndUnderSampleRate()
    {
        var (editor, clip) = CreateFixture();

        editor.SetClipResampleMode(clip.Id, VideoResampleMode.Disable);
        editor.SetClipUnderSampleRate(clip.Id, 0.5);

        Assert.Equal(VideoResampleMode.Disable, editor.Project.Tracks[0].Clips[0].ResampleMode);
        Assert.Equal(0.5, editor.Project.Tracks[0].Clips[0].UnderSampleRate);

        Assert.True(editor.Undo());
        Assert.Equal(1.0, editor.Project.Tracks[0].Clips[0].UnderSampleRate);

        Assert.True(editor.Undo());
        Assert.Equal(VideoResampleMode.Smart, editor.Project.Tracks[0].Clips[0].ResampleMode);
    }

    [Fact]
    public void SetClipAudioPropertiesAndUndoRedo()
    {
        var (editor, clip) = CreateFixture();

        editor.SetClipInvertPhase(clip.Id, true);
        editor.SetClipNormalize(clip.Id, true);

        Assert.True(editor.Project.Tracks[0].Clips[0].InvertPhase);
        Assert.True(editor.Project.Tracks[0].Clips[0].Normalize);

        Assert.True(editor.Undo());
        Assert.False(editor.Project.Tracks[0].Clips[0].Normalize);

        Assert.True(editor.Undo());
        Assert.False(editor.Project.Tracks[0].Clips[0].InvertPhase);
    }
}
