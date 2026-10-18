using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class TranscriptEditorTests
{
    private static (TimelineEditor Editor, TimelineClip Clip) Create()
    {
        var asset = new MediaAsset { Path = "dialogue.mp4", HasVideo = true, HasAudio = true, DurationSeconds = 20, Width = 1920, Height = 1080 };
        var clip = new TimelineClip
        {
            MediaId = asset.Id,
            StartFrame = 0,
            DurationFrames = 300,
            Transcript = new ClipTranscript
            {
                Words =
                [
                    new("Hello", 0, 30),
                    new("world", 35, 30),
                    // Pause of 35 frames (65 to 100)
                    new("this", 100, 25),
                    new("is", 130, 20),
                    new("OpenRend", 155, 45)
                ]
            }
        };
        var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [clip] };
        var project = new ProjectDocument { Media = [asset], Tracks = [track], Width = 1920, Height = 1080 };
        return (new TimelineEditor(project), clip);
    }

    private static TimelineClip GetClip(TimelineEditor editor, Guid clipId) =>
        editor.Project.Tracks.SelectMany(t => t.Clips).First(c => c.Id == clipId);

    [Fact]
    public void SetClipTranscriptSurvivesUndoRedo()
    {
        var (editor, clip) = Create();
        var initial = editor.GetClipTranscript(clip.Id);
        Assert.NotNull(initial);
        Assert.Equal(5, initial.Words.Count);

        var updated = new ClipTranscript([new WordTiming("Updated", 0, 50)]);
        editor.SetClipTranscript(clip.Id, updated);
        var current = editor.GetClipTranscript(clip.Id);
        Assert.NotNull(current);
        Assert.Single(current.Words);
        Assert.Equal("Updated", current.Words[0].Word);

        editor.Undo();
        var undone = editor.GetClipTranscript(clip.Id);
        Assert.NotNull(undone);
        Assert.Equal(5, undone.Words.Count);

        editor.Redo();
        var redone = editor.GetClipTranscript(clip.Id);
        Assert.NotNull(redone);
        Assert.Single(redone.Words);
    }

    [Fact]
    public void SplitClipAtWordPartitionsTranscriptAndTimelineClips()
    {
        var (editor, clip) = Create();

        // Split at word index 2 ("this" at frame 100)
        var rightClip = editor.SplitClipAtWord(clip.Id, 2);

        var left = GetClip(editor, clip.Id);
        var right = GetClip(editor, rightClip.Id);

        Assert.Equal(100, left.DurationFrames);
        Assert.NotNull(left.Transcript);
        Assert.Equal(2, left.Transcript.Words.Count);
        Assert.Equal("Hello", left.Transcript.Words[0].Word);
        Assert.Equal("world", left.Transcript.Words[1].Word);

        Assert.Equal(100, right.StartFrame);
        Assert.Equal(200, right.DurationFrames);
        Assert.NotNull(right.Transcript);
        Assert.Equal(3, right.Transcript.Words.Count);
        Assert.Equal("this", right.Transcript.Words[0].Word);
        Assert.Equal(0, right.Transcript.Words[0].StartFrame); // Offset shifted relative to right clip!
        Assert.Equal("is", right.Transcript.Words[1].Word);
        Assert.Equal(30, right.Transcript.Words[1].StartFrame);
    }

    [Fact]
    public void DeleteTranscriptRangeMiddleWithRippleRemovesSegmentAndShiftsRemaining()
    {
        var (editor, clip) = Create();

        // Add a later clip on the same track to verify ripple
        var laterClip = new TimelineClip
        {
            MediaId = clip.MediaId,
            StartFrame = 350,
            DurationFrames = 50
        };
        editor.Execute(p => p.Tracks[0].Clips.Add(laterClip));

        // Delete from frame 35 to frame 100 (duration = 65 frames) with autoRipple = true
        editor.DeleteTranscriptRange(clip.Id, 35, 100, autoRipple: true);

        var clips = editor.Project.Tracks[0].Clips.OrderBy(c => c.StartFrame).ToList();
        Assert.Equal(3, clips.Count); // Left part, Right part, and Later clip

        var left = clips[0];
        var right = clips[1];
        var later = clips[2];

        Assert.Equal(0, left.StartFrame);
        Assert.Equal(35, left.DurationFrames);

        // Right part rippled directly to left part's end (35)
        Assert.Equal(35, right.StartFrame);
        Assert.Equal(200, right.DurationFrames);

        // Later clip rippled left by 65 frames: 350 - 65 = 285
        Assert.Equal(285, later.StartFrame);

        // Undo restores initial state
        editor.Undo();
        var restoredClips = editor.Project.Tracks[0].Clips.OrderBy(c => c.StartFrame).ToList();
        Assert.Equal(2, restoredClips.Count);
        Assert.Equal(300, restoredClips[0].DurationFrames);
        Assert.Equal(350, restoredClips[1].StartFrame);
    }

    [Fact]
    public void CutTranscriptRangeExtractsWordsAndPerformsCut()
    {
        var (editor, clip) = Create();

        // Cut words between frame 0 and 35 ("Hello")
        var cut = editor.CutTranscriptRange(clip.Id, 0, 35, autoRipple: true);

        Assert.NotNull(cut);
        Assert.Single(cut.Words);
        Assert.Equal("Hello", cut.Words[0].Word);

        var current = GetClip(editor, clip.Id);
        // Trimmed head: duration is now 265
        Assert.Equal(265, current.DurationFrames);
    }

    [Fact]
    public void DeletePausesRemovesGapsAndRipples()
    {
        var (editor, clip) = Create();

        // Clip has words:
        // "Hello" 0-30
        // "world" 35-65
        // Gap of 35 frames: 65 to 100!
        // "this" 100-125
        // "is" 130-150
        // "OpenRend" 155-200
        // Gap of 100 frames: 200 to 300!
        var removedCount = editor.DeletePauses(clip.Id, minPauseFrames: 30, autoRipple: true);
        Assert.True(removedCount >= 1);

        // Total duration is reduced
        var totalDur = editor.Project.Tracks[0].Clips.Sum(c => c.DurationFrames);
        Assert.True(totalDur < 300);
    }
}
