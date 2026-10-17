using System.Text.Json;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class CubeLutTests
{
    private const string Identity = "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1";

    [Fact]
    public void ReplacingOnlyTheLutPayloadIsAnUndoableEditAndEqualCopiesAreNoOps()
    {
        var effect = VideoEffect.Create(VideoEffectKind.Lut3D);
        var media = new MediaAsset { Path = "source.mp4", HasVideo = true, DurationSeconds = 3 };
        var clip = new TimelineClip { MediaId = media.Id, DurationFrames = 90, VideoEffects = [effect] };
        var project = new ProjectDocument { Media = [media], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] };
        var editor = new TimelineEditor(project);
        VideoEffect Current() => project.Tracks[0].Clips[0].VideoEffects[0];
        var imported = effect.DeepClone(); imported.Lut = CubeLutParser.Parse(Identity);
        editor.SetVideoEffects(clip.Id, [imported]);
        Assert.NotNull(Current().Lut);
        editor.SetVideoEffects(clip.Id, [imported.DeepClone()]);
        Assert.True(editor.Undo()); Assert.Null(Current().Lut); Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        var changed = imported.DeepClone(); changed.Lut!.Samples[0] = .25; changed.Lut.DomainMinimum[0] = -.1;
        editor.SetVideoEffects(clip.Id, [changed]);
        Assert.Equal(.25, Current().Lut!.Samples[0]);
        Assert.True(editor.Undo()); Assert.Equal(0, Current().Lut!.Samples[0]); Assert.Equal(0, Current().Lut!.DomainMinimum[0]);
        var cleared = Current().DeepClone(); cleared.Lut = null;
        editor.SetVideoEffects(clip.Id, [cleared]); Assert.Null(Current().Lut);
        Assert.True(editor.Undo()); Assert.NotNull(Current().Lut);
    }

    [Fact]
    public void ParsesTitleCommentsDomainsAndIndependentClones()
    {
        var lut = CubeLutParser.Parse("TITLE \"My # LUT\" # comment\nDOMAIN_MIN -1 -2 -3\nDOMAIN_MAX 1 2 3\n" + Identity);
        Assert.Equal("My # LUT", lut.Title);
        Assert.Equal(new double[] { -1, -2, -3 }, lut.DomainMinimum);
        var effect = VideoEffect.Create(VideoEffectKind.Lut3D); effect.Lut = lut;
        var clone = effect.DeepClone(); clone.Lut!.Samples[0] = .5; clone.Lut.DomainMinimum[0] = -5;
        Assert.Equal(0, lut.Samples[0]); Assert.Equal(-1, lut.DomainMinimum[0]);
        Assert.Throws<InvalidDataException>(() => { var wrong = VideoEffect.Create(VideoEffectKind.Invert); wrong.Lut = lut; wrong.Validate(); });
    }

    [Theory]
    [InlineData("LUT_1D_SIZE 2\n0 0 0\n1 1 1")]
    [InlineData("LUT_3D_SIZE 66")]
    [InlineData("LUT_3D_SIZE 2\nNaN 0 0")]
    [InlineData("LUT_3D_SIZE 2\n0 0 0")]
    [InlineData("LUT_3D_SIZE 2\nLUT_3D_SIZE 2")]
    [InlineData("LUT_3D_SIZE 2\nDOMAIN_MIN 1 0 0\nDOMAIN_MAX 0 1 1")]
    public void RejectsUnsupportedMalformedOrTruncatedData(string text) => Assert.Throws<InvalidDataException>(() => CubeLutParser.Parse(text));

    [Fact]
    public void RejectsExtraSamplesAndInvalidPersistedPayload()
    {
        Assert.Throws<InvalidDataException>(() => CubeLutParser.Parse(Identity + "\n0 0 0"));
        var lut = CubeLutParser.Parse(Identity); lut.Samples[0] = double.PositiveInfinity;
        Assert.Throws<InvalidDataException>(lut.Validate);
        lut.Samples[0] = 0; lut.DomainMinimum[0] = double.NaN;
        Assert.Throws<InvalidDataException>(lut.Validate);
    }

    [Fact]
    public async Task ProjectPersistsSamplesWithoutDependingOnTheImportedFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var cubePath = Path.Combine(folder, "test.cube"); File.WriteAllText(cubePath, Identity);
            var effect = VideoEffect.Create(VideoEffectKind.Lut3D); effect.Lut = CubeLutParser.Load(cubePath); effect.Amount = .5; effect.SetParameter("Interpolation", 1);
            var media = new MediaAsset { Path = Path.Combine(folder, "source.mp4"), HasVideo = true, DurationSeconds = 1 };
            var project = new ProjectDocument { Media = [media], Tracks = [new() { Kind = TrackKind.Video, Clips = [new() { MediaId = media.Id, DurationFrames = 30, VideoEffects = [effect] }] }] };
            var path = Path.Combine(folder, "project.openrend"); await ProjectStore.SaveAsync(project, path); File.Delete(cubePath);
            var loaded = await ProjectStore.LoadAsync(path); var savedEffect = loaded.Tracks[0].Clips[0].VideoEffects[0];
            Assert.Equal(effect.Lut.Samples, savedEffect.Lut!.Samples); Assert.Equal(.5, savedEffect.Amount); Assert.Equal(1, savedEffect.GetParameter("Interpolation"));
            using var json = JsonDocument.Parse(File.ReadAllText(path)); Assert.Equal(ProjectStore.CurrentVersion, json.RootElement.GetProperty("Version").GetInt32());
        }
        finally { Directory.Delete(folder, true); }
    }
}
