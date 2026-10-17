using System.Text.Json.Nodes;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class VideoEffectChoiceTests
{
    [Fact]
    public void LevelsChannelDescriptorHasNamedChoicesAndCompatibleDefault()
    {
        var channel = VideoEffectCatalog.Get(VideoEffectKind.Levels).Parameters.Single(parameter => parameter.Key == "Channel");
        Assert.Equal(new[] { "All", "Red", "Green", "Blue", "Alpha" }, channel.Choices);
        Assert.Equal(0, channel.DefaultValue);
        Assert.Equal(0, channel.Minimum);
        Assert.Equal(4, channel.Maximum);
        Assert.Equal(1, channel.Step);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryNamedChannelIsAValidDiscreteValue(int channel)
    {
        var effect = VideoEffect.Create(VideoEffectKind.Levels);
        effect.SetParameter("Channel", channel);
        effect.Validate();
        Assert.Equal(channel, effect.GetParameter("Channel"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FractionalNonfiniteAndUnknownChoicesAreRejected(double channel)
    {
        var effect = VideoEffect.Create(VideoEffectKind.Levels);
        Assert.Throws<InvalidDataException>(() => effect.SetParameter("Channel", channel));
        Assert.Equal(0, effect.GetParameter("Channel"));
        effect.Parameters["Channel"] = channel;
        Assert.Throws<InvalidDataException>(effect.Validate);
    }

    [Fact]
    public void ChannelSnapshotsAndUndoDoNotShareMutableParameters()
    {
        var media = new MediaAsset { Path = "source.mkv", DurationSeconds = 2, HasVideo = true };
        var clip = new TimelineClip { MediaId = media.Id, DurationFrames = 60 };
        var editor = new TimelineEditor(new ProjectDocument { Media = [media], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] });
        var effect = editor.AddVideoEffect(clip.Id, VideoEffectKind.Levels);
        editor.UpdateVideoEffectParameters(clip.Id, effect.Id, new Dictionary<string, double> { ["Channel"] = 1 }, true);
        var snapshot = editor.Project.DeepClone();
        var clonedEffect = snapshot.Tracks[0].Clips[0].VideoEffects[0];
        clonedEffect.SetParameter("Channel", 4);
        Assert.Equal(1, editor.Project.Tracks[0].Clips[0].VideoEffects[0].GetParameter("Channel"));
        Assert.True(editor.Undo());
        Assert.Equal(0, editor.Project.Tracks[0].Clips[0].VideoEffects[0].GetParameter("Channel"));
        Assert.True(editor.Redo());
        Assert.Equal(1, editor.Project.Tracks[0].Clips[0].VideoEffects[0].GetParameter("Channel"));
        Assert.Equal(4, clonedEffect.GetParameter("Channel"));
    }

    [Fact]
    public async Task ChannelRoundTripsAndOlderFormatFourWithoutItDefaultsToAll()
    {
        var media = new MediaAsset { Path = "source.mkv", DurationSeconds = 2, HasVideo = true };
        var effect = VideoEffect.Create(VideoEffectKind.Levels); effect.SetParameter("Channel", 1);
        var clip = new TimelineClip { MediaId = media.Id, DurationFrames = 60, VideoEffects = [effect] };
        var project = new ProjectDocument { Media = [media], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] };
        var path = Path.Combine(Path.GetTempPath(), "OpenRend-fx-choice-" + Guid.NewGuid().ToString("N") + ".openrend");
        try
        {
            await ProjectStore.SaveAsync(project, path);
            var loaded = await ProjectStore.LoadAsync(path);
            Assert.Equal(1, loaded.Tracks[0].Clips[0].VideoEffects[0].GetParameter("Channel"));
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            Assert.Equal(ProjectStore.CurrentVersion, (int)manifest["Version"]!);
            var parameters = (JsonObject)manifest["Project"]!["Tracks"]![0]!["Clips"]![0]!["VideoEffects"]![0]!["Parameters"]!;
            parameters.Remove("Channel");
            await File.WriteAllTextAsync(path, manifest.ToJsonString());
            loaded = await ProjectStore.LoadAsync(path);
            loaded.Validate();
            var legacyEffect = loaded.Tracks[0].Clips[0].VideoEffects[0];
            Assert.False(legacyEffect.Parameters.ContainsKey("Channel"));
            Assert.Equal(0, legacyEffect.GetParameter("Channel"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
