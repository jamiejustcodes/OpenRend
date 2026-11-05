using System.Text.Json;
using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class AudioChannelsAndPitchEditorTests
{
    private static (ProjectDocument project, TimelineEditor editor, TimelineClip clip) CreateProjectWithAudioClip()
    {
        var mediaId = Guid.NewGuid();
        var clip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = mediaId,
            Name = "Voiceover.wav",
            StartFrame = 0,
            DurationFrames = 120,
            ChannelMode = AudioChannelMode.Both,
            PitchSemis = 0.0,
            InvertPhase = false,
            Normalize = false
        };
        var track = new TimelineTrack
        {
            Id = Guid.NewGuid(),
            Kind = TrackKind.Audio,
            Clips = [clip]
        };
        var media = new MediaAsset
        {
            Id = mediaId,
            Path = "C:/Media/Voiceover.wav",
            HasAudio = true,
            HasVideo = false,
            DurationSeconds = 4.0
        };
        var project = new ProjectDocument
        {
            Name = "AudioTest",
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(30, 1),
            Media = [media],
            Tracks = [track]
        };
        var editor = new TimelineEditor(project);
        return (project, editor, clip);
    }

    [Fact]
    public void SetClipChannelMode_ChangesMode_AndSupportsUndoRedo()
    {
        var (_, editor, clip) = CreateProjectWithAudioClip();

        Assert.Equal(AudioChannelMode.Both, clip.ChannelMode);

        editor.SetClipChannelMode(clip.Id, AudioChannelMode.LeftOnly);
        Assert.Equal(AudioChannelMode.LeftOnly, clip.ChannelMode);

        editor.SetClipChannelMode(clip.Id, AudioChannelMode.Swap);
        Assert.Equal(AudioChannelMode.Swap, clip.ChannelMode);

        // Undo
        Assert.True(editor.Undo());
        Assert.Equal(AudioChannelMode.LeftOnly, editor.Project.Tracks[0].Clips[0].ChannelMode);

        Assert.True(editor.Undo());
        Assert.Equal(AudioChannelMode.Both, editor.Project.Tracks[0].Clips[0].ChannelMode);

        // Redo
        Assert.True(editor.Redo());
        Assert.Equal(AudioChannelMode.LeftOnly, editor.Project.Tracks[0].Clips[0].ChannelMode);
    }

    [Fact]
    public void SetClipPitchSemis_AdjustsPitch_ClampsBounds_AndSupportsUndo()
    {
        var (_, editor, clip) = CreateProjectWithAudioClip();

        Assert.Equal(0.0, clip.PitchSemis);

        editor.SetClipPitchSemis(clip.Id, 2.0);
        Assert.Equal(2.0, clip.PitchSemis);

        // Adjust by +1 semitone
        editor.AdjustClipPitchSemis([clip.Id], 1.0);
        Assert.Equal(3.0, clip.PitchSemis);

        // Adjust by -5 semitones
        editor.AdjustClipPitchSemis([clip.Id], -5.0);
        Assert.Equal(-2.0, clip.PitchSemis);

        // Reset
        editor.ResetClipPitchSemis([clip.Id]);
        Assert.Equal(0.0, editor.Project.Tracks[0].Clips[0].PitchSemis);

        // Undo reset
        Assert.True(editor.Undo());
        Assert.Equal(-2.0, editor.Project.Tracks[0].Clips[0].PitchSemis);

        // Test clamping to [-24, 24]
        editor.SetClipPitchSemis(clip.Id, 30.0);
        Assert.Equal(24.0, editor.Project.Tracks[0].Clips[0].PitchSemis);

        editor.SetClipPitchSemis(clip.Id, -50.0);
        Assert.Equal(-24.0, editor.Project.Tracks[0].Clips[0].PitchSemis);
    }

    [Fact]
    public void ClipAudioProperties_JsonSerializationAndDeepClone_PreservesState()
    {
        var (project, _, clip) = CreateProjectWithAudioClip();
        clip.ChannelMode = AudioChannelMode.RightOnly;
        clip.PitchSemis = -3.5;
        clip.InvertPhase = true;
        clip.Normalize = true;

        var cloned = clip.DeepClone();
        Assert.Equal(AudioChannelMode.RightOnly, cloned.ChannelMode);
        Assert.Equal(-3.5, cloned.PitchSemis);
        Assert.True(cloned.InvertPhase);
        Assert.True(cloned.Normalize);

        var json = JsonSerializer.Serialize(project);
        var restored = JsonSerializer.Deserialize<ProjectDocument>(json);
        Assert.NotNull(restored);

        var restoredClip = restored.Tracks[0].Clips[0];
        Assert.Equal(AudioChannelMode.RightOnly, restoredClip.ChannelMode);
        Assert.Equal(-3.5, restoredClip.PitchSemis);
        Assert.True(restoredClip.InvertPhase);
        Assert.True(restoredClip.Normalize);
    }
}
