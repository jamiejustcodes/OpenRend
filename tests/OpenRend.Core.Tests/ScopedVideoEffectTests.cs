using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class ScopedVideoEffectTests
{
    private static (TimelineEditor Editor, MediaAsset Media, TimelineTrack Track, TimelineClip Clip) CreateFixture()
    {
        var media = new MediaAsset { Path = "test.mkv", DurationSeconds = 5, HasVideo = true, HasAudio = false, Width = 1920, Height = 1080 };
        var clip = new TimelineClip { MediaId = media.Id, DurationFrames = 90 };
        var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [clip] };
        var project = new ProjectDocument { Media = [media], Tracks = [track] };
        return (new TimelineEditor(project), media, track, clip);
    }

    [Fact]
    public void ProjectValidationEnforcesTrackKindAndUniqueEffectIds()
    {
        var (editor, media, track, _) = CreateFixture();
        var effect = VideoEffect.Create(VideoEffectKind.Invert);

        // Track FX on audio track is invalid
        var audioTrack = new TimelineTrack { Kind = TrackKind.Audio, VideoEffects = [effect] };
        editor.Project.Tracks.Add(audioTrack);
        Assert.Throws<InvalidDataException>(() => editor.Project.Validate());
        editor.Project.Tracks.Remove(audioTrack);

        // Duplicate IDs in Media FX are invalid
        media.VideoEffects = [effect, effect.DeepClone()];
        Assert.Throws<InvalidDataException>(() => editor.Project.Validate());
        media.VideoEffects.Clear();

        // Duplicate IDs in Track FX are invalid
        track.VideoEffects = [effect, effect.DeepClone()];
        Assert.Throws<InvalidDataException>(() => editor.Project.Validate());
        track.VideoEffects.Clear();

        // Duplicate IDs in VideoOutputEffects are invalid
        editor.Project.VideoOutputEffects = [effect, effect.DeepClone()];
        Assert.Throws<InvalidDataException>(() => editor.Project.Validate());
        editor.Project.VideoOutputEffects.Clear();

        // Valid project passes validation
        editor.Project.Validate();
    }

    [Fact]
    public void TimelineEditorManagesTrackScopedEffectsWithUndoRedo()
    {
        var (editor, _, track, _) = CreateFixture();

        // Add Track FX
        var invert = editor.AddScopedVideoEffect(EffectScope.Track, track.Id, VideoEffectKind.Invert);
        var blur = editor.AddScopedVideoEffect(EffectScope.Track, track.Id, VideoEffectKind.GaussianBlur);
        Assert.Equal(2, editor.Project.Tracks[0].VideoEffects.Count);
        Assert.Equal(invert.Id, editor.Project.Tracks[0].VideoEffects[0].Id);
        Assert.Equal(blur.Id, editor.Project.Tracks[0].VideoEffects[1].Id);

        // Update Track FX
        editor.UpdateScopedVideoEffect(EffectScope.Track, track.Id, blur.Id, 0.75, 0, false);
        Assert.Equal(0.75, editor.Project.Tracks[0].VideoEffects[1].Amount);
        Assert.False(editor.Project.Tracks[0].VideoEffects[1].IsEnabled);

        // Move Track FX
        editor.MoveScopedVideoEffect(EffectScope.Track, track.Id, blur.Id, 0);
        Assert.Equal(blur.Id, editor.Project.Tracks[0].VideoEffects[0].Id);

        // Undo move, update, and adds
        Assert.True(editor.Undo());
        Assert.Equal(invert.Id, editor.Project.Tracks[0].VideoEffects[0].Id);
        Assert.True(editor.Undo());
        Assert.True(editor.Project.Tracks[0].VideoEffects[1].IsEnabled);
        Assert.True(editor.Undo());
        Assert.Single(editor.Project.Tracks[0].VideoEffects);
        Assert.True(editor.Undo());
        Assert.Empty(editor.Project.Tracks[0].VideoEffects);

        // Redo restores them
        Assert.True(editor.Redo());
        Assert.Single(editor.Project.Tracks[0].VideoEffects);
    }

    [Fact]
    public void TimelineEditorManagesMediaScopedEffectsWithUndoRedo()
    {
        var (editor, media, _, _) = CreateFixture();

        // Add Media FX
        var brightness = editor.AddScopedVideoEffect(EffectScope.Media, media.Id, VideoEffectKind.BrightnessContrast);
        Assert.Single(editor.Project.Media[0].VideoEffects);

        // Update Media FX parameters
        editor.UpdateScopedVideoEffectParameters(EffectScope.Media, media.Id, brightness.Id,
            new Dictionary<string, double> { ["Amount"] = 0.5, ["SecondaryAmount"] = -0.25 }, true);
        Assert.Equal(0.5, editor.Project.Media[0].VideoEffects[0].Amount);
        Assert.Equal(-0.25, editor.Project.Media[0].VideoEffects[0].SecondaryAmount);

        // Remove Media FX
        editor.RemoveScopedVideoEffect(EffectScope.Media, media.Id, brightness.Id);
        Assert.Empty(editor.Project.Media[0].VideoEffects);

        // Undo removes removal
        Assert.True(editor.Undo());
        Assert.Single(editor.Project.Media[0].VideoEffects);
        Assert.Equal(0.5, editor.Project.Media[0].VideoEffects[0].Amount);
    }

    [Fact]
    public void TimelineEditorManagesOutputScopedEffectsWithUndoRedo()
    {
        var (editor, _, _, _) = CreateFixture();

        // Add Video Output FX
        var vignette = editor.AddScopedVideoEffect(EffectScope.Output, Guid.Empty, VideoEffectKind.Vignette);
        Assert.Single(editor.Project.VideoOutputEffects);

        // Replace Video Output FX
        var sepia = editor.ReplaceScopedVideoEffect(EffectScope.Output, Guid.Empty, vignette.Id, VideoEffectKind.Sepia);
        Assert.Single(editor.Project.VideoOutputEffects);
        Assert.Equal(VideoEffectKind.Sepia, editor.Project.VideoOutputEffects[0].Kind);

        // Set Video Output FX
        var list = new[] { VideoEffect.Create(VideoEffectKind.FilmGrain), VideoEffect.Create(VideoEffectKind.BroadcastColors) };
        editor.SetScopedVideoEffects(EffectScope.Output, Guid.Empty, list);
        Assert.Equal(2, editor.Project.VideoOutputEffects.Count);

        // Undo restores replaced sepia
        Assert.True(editor.Undo());
        Assert.Single(editor.Project.VideoOutputEffects);
        Assert.Equal(VideoEffectKind.Sepia, editor.Project.VideoOutputEffects[0].Kind);
    }

    [Fact]
    public async Task ProjectStorePersistsScopedEffectsAcrossMediaTrackAndOutput()
    {
        var (editor, media, track, _) = CreateFixture();
        var mediaFx = editor.AddScopedVideoEffect(EffectScope.Media, media.Id, VideoEffectKind.BrightnessContrast);
        editor.UpdateScopedVideoEffect(EffectScope.Media, media.Id, mediaFx.Id, 0.3, 0.1, true);

        var trackFx = editor.AddScopedVideoEffect(EffectScope.Track, track.Id, VideoEffectKind.Saturation);
        editor.UpdateScopedVideoEffect(EffectScope.Track, track.Id, trackFx.Id, 1.5, 0, true);

        var outputFx = editor.AddScopedVideoEffect(EffectScope.Output, Guid.Empty, VideoEffectKind.Vignette);
        editor.UpdateScopedVideoEffect(EffectScope.Output, Guid.Empty, outputFx.Id, 0.4, 0, false);

        var tempDir = Path.Combine(Path.GetTempPath(), "OpenRend-scopedfx-" + Guid.NewGuid().ToString("N"));
        var tempFile = Path.Combine(tempDir, "project.openrend");
        try
        {
            await ProjectStore.SaveAsync(editor.Project, tempFile);
            var loaded = await ProjectStore.LoadAsync(tempFile);

            Assert.Single(loaded.Media[0].VideoEffects);
            Assert.Equal(VideoEffectKind.BrightnessContrast, loaded.Media[0].VideoEffects[0].Kind);
            Assert.Equal(0.3, loaded.Media[0].VideoEffects[0].Amount);

            Assert.Single(loaded.Tracks[0].VideoEffects);
            Assert.Equal(VideoEffectKind.Saturation, loaded.Tracks[0].VideoEffects[0].Kind);
            Assert.Equal(1.5, loaded.Tracks[0].VideoEffects[0].Amount);

            Assert.Single(loaded.VideoOutputEffects);
            Assert.Equal(VideoEffectKind.Vignette, loaded.VideoOutputEffects[0].Kind);
            Assert.False(loaded.VideoOutputEffects[0].IsEnabled);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
