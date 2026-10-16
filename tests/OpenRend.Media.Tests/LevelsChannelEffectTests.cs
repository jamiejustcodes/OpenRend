using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class LevelsChannelEffectTests(MediaFixture fixture) : IClassFixture<MediaFixture>
{
    [Theory]
    [InlineData(0, 127, 64, 32, 192)]
    [InlineData(1, 255, 128, 32, 192)]
    [InlineData(2, 255, 64, 64, 192)]
    [InlineData(3, 127, 128, 64, 192)]
    [InlineData(4, 255, 128, 64, 96)]
    public void OutputRangeAppliesOnlyToTheSelectedChannel(int channel, int blue, int green, int red, int alpha)
    {
        var effect = VideoEffect.Create(VideoEffectKind.Levels);
        effect.SetParameter("Channel", channel);
        effect.SetParameter("OutputWhite", .5);
        byte[] source = [255, 128, 64, 192];
        var original = (byte[])source.Clone();
        var result = VideoEffectProcessor.Apply(source, [effect]);
        Assert.Equal(new byte[] { (byte)blue, (byte)green, (byte)red, (byte)alpha }, result);
        Assert.NotSame(source, result);
        Assert.Equal(original, source);
        effect.IsEnabled = false;
        Assert.Same(source, VideoEffectProcessor.Apply(source, [effect]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void DefaultRangeIsIdentityForEveryChannel(int channel)
    {
        var effect = VideoEffect.Create(VideoEffectKind.Levels);
        effect.SetParameter("Channel", channel);
        byte[] source = [17, 128, 254, 63];
        Assert.Same(source, VideoEffectProcessor.Apply(source, [effect]));
    }

    [Fact]
    public void MissingChannelUsesAllAndSelectedAlphaUsesTheCompleteLevelsTransfer()
    {
        var effect = VideoEffect.Create(VideoEffectKind.Levels);
        effect.Parameters.Remove("Channel");
        effect.SetParameter("OutputWhite", .5);
        Assert.Equal(new byte[] { 127, 127, 127, 73 }, VideoEffectProcessor.Apply([255, 255, 255, 73], [effect]));
        effect.SetParameter("Channel", 4);
        effect.SetParameter("InputBlack", .2);
        effect.SetParameter("InputWhite", .8);
        effect.SetParameter("Gamma", 2);
        effect.SetParameter("OutputBlack", .1);
        effect.SetParameter("OutputWhite", .9);
        var result = VideoEffectProcessor.Apply([11, 22, 33, 128], [effect]);
        var normalized = (128 / 255.0 - .2) / .6;
        var expectedAlpha = (byte)((.1 + Math.Sqrt(normalized) * .8) * 255);
        Assert.Equal(new byte[] { 11, 22, 33, expectedAlpha }, result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(.5)]
    public async Task AlphaChannelRevealsTheLowerLayerOnCpuGpuAndExport(double clipOpacity)
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Quadrants);
        var levels = VideoEffect.Create(VideoEffectKind.Levels);
        levels.SetParameter("Channel", 4);
        levels.SetParameter("OutputWhite", .5);
        var top = new TimelineClip { MediaId = asset.Id, DurationFrames = 3, Opacity = clipOpacity, VideoEffects = [levels] };
        var bottom = new TimelineClip { MediaId = asset.Id, DurationFrames = 3, VideoEffects = [VideoEffect.Create(VideoEffectKind.Invert)] };
        var project = new ProjectDocument { Width = 64, Height = 48, Media = [asset], Tracks =
            [new() { Kind = TrackKind.Video, Clips = [top] }, new() { Kind = TrackKind.Video, Clips = [bottom] }] };
        using var cpu = new TimelineRenderService(media, false);
        using var gpu = new TimelineRenderService(media);
        var expected = cpu.RenderFrame(project, 0, 64, 48);
        var actual = gpu.RenderFrame(project, 0, 64, 48);
        Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
        var p = (12 * 64 + 16) * 4;
        var red = (byte)Math.Round(127 * clipOpacity);
        var cyan = (byte)Math.Round(255 - 127 * clipOpacity);
        Assert.Equal(new byte[] { cyan, cyan, red, 255 }, expected[p..(p + 4)]);
        for (var i = 0; i < actual.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, 1);
        var destination = Path.Combine(fixture.Directory, $"levels-alpha-{clipOpacity}.mp4");
        await new ExportService(cpu).ExportAsync(project, destination);
        var encoded = media.GetFrame(destination, 0, 64, 48);
        for (var c = 0; c < 3; c++) Assert.InRange(Math.Abs(expected[p + c] - encoded[p + c]), 0, 8);
    }
}
