using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class AudioSourceModeTests
{
    [Fact]
    public async Task SourceModeExtendsOnlyTheSelectedAudioEventAndSurvivesUndoAndSave()
    {
        var media = new MediaAsset { Path = Path.GetFullPath("tone.wav"), HasAudio = true, DurationSeconds = 1 };
        var clip = new TimelineClip { MediaId = media.Id, DurationFrames = 30 };
        var other = new TimelineClip { MediaId = media.Id, StartFrame = 60, DurationFrames = 30 };
        var project = new ProjectDocument { FrameRate = new(30, 1), Media = [media],
            Tracks = [new() { Kind = TrackKind.Audio, Clips = [clip, other] }] };
        var editor = new TimelineEditor(project);
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.TrimClip(clip.Id, 0, 45));

        editor.SetAudioSourceMode(clip.Id, AudioSourceMode.Loop);
        editor.TrimClip(clip.Id, 0, 45);
        editor.SetAudioSourceMode(clip.Id, AudioSourceMode.PadSilence);
        Assert.Equal(45, Current().DurationFrames);
        editor.Undo();
        Assert.Equal(AudioSourceMode.Loop, Current().AudioSourceMode);
        editor.Redo();
        Assert.Equal(AudioSourceMode.PadSilence, Current().AudioSourceMode);

        var directory = Path.Combine(Path.GetTempPath(), $"openrend-audio-mode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "extended-audio.openrend");
            await ProjectStore.SaveAsync(project, path);
            var clips = (await ProjectStore.LoadAsync(path)).Tracks[0].Clips;
            var restored = clips[0];
            Assert.Equal(AudioSourceMode.PadSilence, restored.AudioSourceMode);
            Assert.Equal((0, 0, 45), (restored.StartFrame, restored.SourceInFrame, restored.DurationFrames));
            Assert.Equal(AudioSourceMode.Bounded, clips[1].AudioSourceMode);
        }
        finally { Directory.Delete(directory, recursive: true); }

        TimelineClip Current() => project.Tracks[0].Clips[0];
    }
}
