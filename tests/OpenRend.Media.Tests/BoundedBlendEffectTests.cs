using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class BoundedBlendEffectTests(MediaFixture fixture) : IClassFixture<MediaFixture>
{
    [Theory]
    [InlineData(255, 0, 0, 85, 170, 42, 42)]
    [InlineData(0, 255, 0, 85, 42, 170, 42)]
    [InlineData(0, 0, 255, 85, 42, 42, 170)]
    [InlineData(255, 255, 0, 170, 212, 212, 85)]
    [InlineData(0, 255, 255, 170, 85, 212, 212)]
    [InlineData(255, 0, 255, 170, 212, 85, 212)]
    public void BlackAndWhiteUsesEqualChannelsAndTruncatesTheBlendedResult(int red, int green, int blue, int gray, int halfRed, int halfGreen, int halfBlue)
    {
        byte[] source = [(byte)blue, (byte)green, (byte)red, 77];
        var copy = (byte[])source.Clone();
        var effect = VideoEffect.Create(VideoEffectKind.BlackAndWhite);
        Assert.Equal(new byte[] { (byte)gray, (byte)gray, (byte)gray, 77 }, VideoEffectProcessor.Apply(source, [effect]));
        effect.Amount = .5;
        Assert.Equal(new byte[] { (byte)halfBlue, (byte)halfGreen, (byte)halfRed, 77 }, VideoEffectProcessor.Apply(source, [effect]));
        effect.Amount = 0;
        Assert.Same(source, VideoEffectProcessor.Apply(source, [effect]));
        Assert.Equal(copy, source);
    }

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(255, 255, 0)]
    [InlineData(0, 255, 255)]
    [InlineData(255, 0, 255)]
    [InlineData(37, 128, 241)]
    public void InvertComplementsEveryByteAndItsHalfBlendTruncatesTo127(int red, int green, int blue)
    {
        byte[] source = [(byte)blue, (byte)green, (byte)red, 0];
        var copy = (byte[])source.Clone();
        var effect = VideoEffect.Create(VideoEffectKind.Invert);
        Assert.Equal(new byte[] { (byte)(255 - blue), (byte)(255 - green), (byte)(255 - red), 0 }, VideoEffectProcessor.Apply(source, [effect]));
        effect.Amount = .5;
        Assert.Equal(new byte[] { 127, 127, 127, 0 }, VideoEffectProcessor.Apply(source, [effect]));
        effect.Amount = 0;
        Assert.Same(source, VideoEffectProcessor.Apply(source, [effect]));
        Assert.Equal(copy, source);
    }

    [Fact]
    public void BlackAndWhiteDoesNotChangeTheSeparateSaturationLumaOperation()
    {
        var effect = VideoEffect.Create(VideoEffectKind.Saturation);
        effect.Amount = 0;
        Assert.Equal(new byte[] { 54, 54, 54, 128 }, VideoEffectProcessor.Apply([0, 0, 255, 128], [effect]));
        var blackAndWhite = VideoEffect.Create(VideoEffectKind.BlackAndWhite);
        Assert.Equal(new byte[] { 66, 66, 66, 128 }, VideoEffectProcessor.Apply([34, 66, 100, 128], [blackAndWhite]));
        blackAndWhite.Amount = .5;
        Assert.Equal(new byte[] { 50, 66, 83, 128 }, VideoEffectProcessor.Apply([34, 66, 100, 128], [blackAndWhite]));
    }

    [Theory]
    [InlineData(VideoEffectKind.BlackAndWhite, 1, 85, 85, 85)]
    [InlineData(VideoEffectKind.BlackAndWhite, .5, 170, 42, 42)]
    [InlineData(VideoEffectKind.Invert, 1, 0, 255, 255)]
    [InlineData(VideoEffectKind.Invert, .5, 127, 127, 127)]
    public async Task ByteBlendPrecedesClipOpacityInBothCompositionPaths(VideoEffectKind kind, double amount, int red, int green, int blue)
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Quadrants);
        var effect = VideoEffect.Create(kind);
        effect.Amount = amount;
        var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 3, Opacity = .5, VideoEffects = [effect] };
        var project = new ProjectDocument { Width = 64, Height = 48, Media = [asset], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] };
        using var cpu = new TimelineRenderService(media, false);
        using var gpu = new TimelineRenderService(media);
        var expected = cpu.RenderFrame(project, 0, 64, 48);
        var actual = gpu.RenderFrame(project, 0, 64, 48);
        Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
        var p = (12 * 64 + 16) * 4;
        Assert.Equal((byte)Math.Round(blue * .5), expected[p]);
        Assert.Equal((byte)Math.Round(green * .5), expected[p + 1]);
        Assert.Equal((byte)Math.Round(red * .5), expected[p + 2]);
        Assert.Equal(255, expected[p + 3]);
        for (var i = 0; i < actual.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, 1);
    }
}
