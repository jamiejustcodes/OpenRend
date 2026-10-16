using System.Text.Json;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class VideoEffectTests
{
    private static (TimelineEditor Editor, Guid ClipId) Create()
    {
        var media = new MediaAsset { Path = "source.mkv", DurationSeconds = 2, HasVideo = true, HasAudio = true, Width = 64, Height = 48 };
        var clip = new TimelineClip { MediaId = media.Id, DurationFrames = 60 };
        return (new(new ProjectDocument { Media = [media], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] }), clip.Id);
    }

    private static TimelineClip Clip(TimelineEditor editor) => editor.Project.Tracks[0].Clips[0];

    [Fact]
    public void OrderedEffectEditsAreUndoableAndDoNotKeepStaleReferences()
    {
        var (editor, clipId) = Create();
        var brightness = editor.AddVideoEffect(clipId, VideoEffectKind.BrightnessContrast);
        var invert = editor.AddVideoEffect(clipId, VideoEffectKind.Invert);
        editor.UpdateVideoEffect(clipId, brightness.Id, .25, -.5, false);
        editor.MoveVideoEffect(clipId, invert.Id, 0);
        Assert.Equal(new[] { invert.Id, brightness.Id }, Clip(editor).VideoEffects.Select(effect => effect.Id));
        editor.RemoveVideoEffect(clipId, brightness.Id);
        Assert.Single(Clip(editor).VideoEffects);
        Assert.True(editor.Undo());
        var restored = Clip(editor).VideoEffects[1];
        Assert.Equal(.25, restored.Amount);
        Assert.Equal(-.5, restored.SecondaryAmount);
        Assert.False(restored.IsEnabled);
        Assert.True(editor.Undo());
        Assert.Equal(brightness.Id, Clip(editor).VideoEffects[0].Id);
        Assert.True(editor.Redo());
        Assert.Equal(invert.Id, Clip(editor).VideoEffects[0].Id);
        editor.UpdateVideoEffect(clipId, brightness.Id, .5, 0, true);
        Assert.False(editor.CanRedo);
        Assert.Equal(.5, Clip(editor).VideoEffects[1].Amount);
    }

    [Fact]
    public void SplitAndProjectSnapshotsOwnIndependentEffectChains()
    {
        var (editor, clipId) = Create();
        var effect = editor.AddVideoEffect(clipId, VideoEffectKind.Saturation);
        editor.UpdateVideoEffect(clipId, effect.Id, .2, 0, true);
        var snapshot = editor.Project.DeepClone();
        var right = editor.SplitClip(clipId, 30);
        editor.UpdateVideoEffect(right.Id, effect.Id, 1.5, 0, true);
        Assert.Equal(.2, Clip(editor).VideoEffects[0].Amount);
        Assert.Equal(.2, snapshot.Tracks[0].Clips[0].VideoEffects[0].Amount);
        Assert.Equal(1.5, right.VideoEffects[0].Amount);
        Assert.NotSame(Clip(editor).VideoEffects, right.VideoEffects);
        Assert.NotSame(Clip(editor).VideoEffects[0], right.VideoEffects[0]);
    }

    [Theory]
    [InlineData(VideoEffectKind.BrightnessContrast, -1.01, 0)]
    [InlineData(VideoEffectKind.BrightnessContrast, 0, 1.01)]
    [InlineData(VideoEffectKind.Saturation, 2.01, 0)]
    [InlineData(VideoEffectKind.Saturation, 1, .1)]
    [InlineData(VideoEffectKind.Invert, -.01, 0)]
    [InlineData(VideoEffectKind.Invert, double.NaN, 0)]
    public void InvalidParametersCannotChangeEffectsOrHistory(VideoEffectKind kind, double amount, double secondary)
    {
        var (editor, clipId) = Create();
        var effect = editor.AddVideoEffect(clipId, kind);
        var initial = effect.Amount;
        Assert.Throws<InvalidDataException>(() => editor.UpdateVideoEffect(clipId, effect.Id, amount, secondary, true));
        Assert.Equal(initial, Clip(editor).VideoEffects[0].Amount);
        Assert.True(editor.Undo());
        Assert.Empty(Clip(editor).VideoEffects);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void LocksAudioAndInvalidReordersRejectMutations()
    {
        var (editor, clipId) = Create();
        var effect = editor.AddVideoEffect(clipId, VideoEffectKind.Invert);
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.MoveVideoEffect(clipId, effect.Id, 1));
        Assert.Throws<KeyNotFoundException>(() => editor.RemoveVideoEffect(clipId, Guid.NewGuid()));
        editor.SetTrackState(editor.Project.Tracks[0].Id, isLocked: true);
        Assert.Throws<InvalidOperationException>(() => editor.AddVideoEffect(clipId, VideoEffectKind.Invert));
        Assert.Throws<InvalidOperationException>(() => editor.UpdateVideoEffect(clipId, effect.Id, .5, 0, true));
        Assert.Throws<InvalidOperationException>(() => editor.MoveVideoEffect(clipId, effect.Id, 0));
        Assert.Throws<InvalidOperationException>(() => editor.RemoveVideoEffect(clipId, effect.Id));
        var audio = editor.AddTrack(TrackKind.Audio);
        var audioClip = editor.PlaceClip(editor.Project.Media[0].Id, audio.Id, 0, 30);
        Assert.Throws<InvalidOperationException>(() => editor.AddVideoEffect(audioClip.Id, VideoEffectKind.Invert));
        audioClip.VideoEffects.Add(VideoEffect.Create(VideoEffectKind.Invert));
        Assert.Throws<InvalidDataException>(() => editor.Project.Validate());
    }

    [Fact]
    public void MalformedChainsAreRejectedAndLegacyClipsHaveNoEffects()
    {
        var (editor, _) = Create();
        var clip = Clip(editor);
        var effect = VideoEffect.Create(VideoEffectKind.Invert);
        clip.VideoEffects = [effect, effect.DeepClone()];
        Assert.Throws<InvalidDataException>(() => editor.Project.Validate());
        clip.VideoEffects = [new() { Kind = (VideoEffectKind)99 }];
        Assert.Throws<InvalidDataException>(() => editor.Project.Validate());
        clip.VideoEffects = null!;
        Assert.Throws<InvalidDataException>(() => editor.Project.Validate());
        Assert.Empty(JsonSerializer.Deserialize<TimelineClip>("{\"DurationFrames\":30}")!.VideoEffects);
    }

    [Fact]
    public void UnchangedUpdatesAndReordersDoNotAddUndoSteps()
    {
        var (editor, clipId) = Create();
        var effect = editor.AddVideoEffect(clipId, VideoEffectKind.Saturation);
        editor.UpdateVideoEffect(clipId, effect.Id, 1, 0, true);
        editor.MoveVideoEffect(clipId, effect.Id, 0);
        Assert.True(editor.Undo());
        Assert.Empty(Clip(editor).VideoEffects);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public async Task SaveAndRecoveryRetainEffectOrderIdentityParametersAndBypass()
    {
        var (editor, clipId) = Create();
        var invert = editor.AddVideoEffect(clipId, VideoEffectKind.Invert);
        editor.UpdateVideoEffect(clipId, invert.Id, .25, 0, false);
        var brightness = editor.AddVideoEffect(clipId, VideoEffectKind.BrightnessContrast);
        editor.UpdateVideoEffect(clipId, brightness.Id, -.25, .75, true);
        var directory = Path.Combine(Path.GetTempPath(), "OpenRend-effects-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "effects.openrend");
        try
        {
            await ProjectStore.SaveAsync(editor.Project, path);
            await ProjectStore.SaveAutosaveAsync(editor.Project, path);
            foreach (var loaded in new[] { await ProjectStore.LoadAsync(path), await ProjectStore.RecoverAutosaveAsync(path) })
            {
                var effects = loaded.Tracks[0].Clips[0].VideoEffects;
                Assert.Equal(new[] { invert.Id, brightness.Id }, effects.Select(effect => effect.Id));
                Assert.False(effects[0].IsEnabled);
                Assert.Equal(.25, effects[0].Amount);
                Assert.Equal(-.25, effects[1].Amount);
                Assert.Equal(.75, effects[1].SecondaryAmount);
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
