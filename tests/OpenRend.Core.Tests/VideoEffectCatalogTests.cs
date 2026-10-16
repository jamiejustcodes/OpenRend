using System.Text.Json.Nodes;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class VideoEffectCatalogTests
{
    [Fact]
    public void EveryCatalogEntryHasValidIndependentDefaultsAndUniqueParameters()
    {
        Assert.Equal(Enum.GetValues<VideoEffectKind>().Length, VideoEffectCatalog.Definitions.Count);
        Assert.Equal(VideoEffectCatalog.Definitions.Count, VideoEffectCatalog.Definitions.Select(item => item.Name).Distinct().Count());
        foreach (var definition in VideoEffectCatalog.Definitions)
        {
            var effect = VideoEffect.Create(definition.Kind); effect.Validate();
            Assert.Equal(definition.Parameters.Count, definition.Parameters.Select(parameter => parameter.Key).Distinct().Count());
            foreach (var parameter in definition.Parameters)
            { Assert.Equal(parameter.DefaultValue, effect.GetParameter(parameter.Key)); Assert.True(parameter.Step > 0); Assert.InRange(parameter.DefaultValue, parameter.Minimum, parameter.Maximum); }
            var clone = effect.DeepClone(); Assert.NotSame(effect.Parameters, clone.Parameters);
        }
        Assert.Equal(0, (int)VideoEffectKind.BrightnessContrast); Assert.Equal(1, (int)VideoEffectKind.Saturation); Assert.Equal(2, (int)VideoEffectKind.Invert);
    }

    private static (TimelineEditor Editor, TimelineClip Clip) Create()
    {
        var asset = new MediaAsset { Path = "source.mkv", HasVideo = true, DurationSeconds = 3 };
        var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 90 };
        return (new(new ProjectDocument { Media = [asset], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] }), clip);
    }

    [Fact]
    public void ParameterEditsAndReplacementAreAtomicAndUndoable()
    {
        var (editor, clip) = Create(); var effect = editor.AddVideoEffect(clip.Id, VideoEffectKind.Levels);
        Assert.Throws<InvalidDataException>(() => editor.UpdateVideoEffectParameters(clip.Id, effect.Id, new Dictionary<string, double> { { "InputBlack", .8 }, { "InputWhite", .5 } }, true));
        Assert.Equal(0, clip.VideoEffects[0].GetParameter("InputBlack"));
        editor.UpdateVideoEffectParameters(clip.Id, effect.Id, new Dictionary<string, double> { { "InputBlack", .2 }, { "Gamma", 2 } }, false);
        Assert.Equal(2, clip.VideoEffects[0].GetParameter("Gamma"));
        var replacement = editor.ReplaceVideoEffect(clip.Id, effect.Id, VideoEffectKind.Sepia);
        Assert.False(replacement.IsEnabled); Assert.NotEqual(effect.Id, replacement.Id);
        Assert.True(editor.Undo()); var restored = editor.Project.Tracks[0].Clips[0].VideoEffects[0];
        Assert.Equal(effect.Id, restored.Id); Assert.Equal(2, restored.GetParameter("Gamma"));
        Assert.True(editor.Undo()); Assert.Equal(1, editor.Project.Tracks[0].Clips[0].VideoEffects[0].GetParameter("Gamma"));
        Assert.True(editor.Undo()); Assert.Empty(editor.Project.Tracks[0].Clips[0].VideoEffects);
    }

    [Fact]
    public void AcceptedChooserChainOwnsParametersAndCreatesOnlyOneHistoryStep()
    {
        var (editor, clip) = Create(); var effect = VideoEffect.Create(VideoEffectKind.ColorBalance); effect.SetParameter("Red", .4);
        editor.SetVideoEffects(clip.Id, [effect]); effect.SetParameter("Red", .8);
        Assert.Equal(.4, clip.VideoEffects[0].GetParameter("Red"));
        editor.SetVideoEffects(clip.Id, clip.VideoEffects);
        var snapshot = editor.Project.DeepClone(); var right = editor.SplitClip(clip.Id, 45);
        editor.UpdateVideoEffectParameters(right.Id, effect.Id, new Dictionary<string, double> { { "Red", -.5 } }, true);
        Assert.Equal(.4, clip.VideoEffects[0].GetParameter("Red")); Assert.Equal(.4, snapshot.Tracks[0].Clips[0].VideoEffects[0].GetParameter("Red"));
        Assert.True(editor.Undo()); Assert.True(editor.Undo()); Assert.True(editor.Undo()); Assert.False(editor.CanUndo);
    }

    [Fact]
    public void InvalidDictionariesDuplicateIdsAndLockedChainsAreRejected()
    {
        var (editor, clip) = Create(); var effect = VideoEffect.Create(VideoEffectKind.HslAdjust);
        Assert.Throws<InvalidDataException>(() => editor.SetVideoEffects(clip.Id, [effect, effect.DeepClone()]));
        effect.Parameters["Bogus"] = 1; Assert.Throws<InvalidDataException>(effect.Validate); effect.Parameters.Remove("Bogus");
        effect.Parameters["Hue"] = double.NaN; Assert.Throws<InvalidDataException>(effect.Validate); effect.Parameters["Hue"] = 0;
        effect.Parameters["Amount"] = 0; Assert.Throws<InvalidDataException>(effect.Validate); effect.Parameters.Remove("Amount");
        effect.Parameters = null!; Assert.Throws<InvalidDataException>(effect.Validate);
        editor.SetTrackState(editor.Project.Tracks[0].Id, isLocked: true);
        Assert.Throws<InvalidOperationException>(() => editor.SetVideoEffects(clip.Id, []));
    }

    [Fact]
    public async Task ExtendedParametersRoundTripAndVersionThreeStillLoads()
    {
        var (editor, clip) = Create(); var fx = VideoEffect.Create(VideoEffectKind.Wave); fx.SetParameter("Frequency", 7.5); fx.SetParameter("Phase", 45); editor.SetVideoEffects(clip.Id, [fx]);
        var path = Path.Combine(Path.GetTempPath(), "OpenRend-fx-catalog-" + Guid.NewGuid().ToString("N") + ".openrend");
        try
        {
            await ProjectStore.SaveAsync(editor.Project, path); var manifest = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            Assert.Equal(4, (int)manifest["Version"]!); var loaded = await ProjectStore.LoadAsync(path);
            Assert.Equal(7.5, loaded.Tracks[0].Clips[0].VideoEffects[0].GetParameter("Frequency"));
            manifest["Version"] = 3; manifest["Project"]!["Tracks"]![0]!["Clips"]![0]!["VideoEffects"] = new JsonArray(JsonNode.Parse("{\"Id\":\"" + Guid.NewGuid() + "\",\"Kind\":\"Saturation\",\"Amount\":0.5}"));
            await File.WriteAllTextAsync(path, manifest.ToJsonString()); loaded = await ProjectStore.LoadAsync(path);
            Assert.Equal(.5, loaded.Tracks[0].Clips[0].VideoEffects[0].Amount); Assert.Empty(loaded.Tracks[0].Clips[0].VideoEffects[0].Parameters);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
