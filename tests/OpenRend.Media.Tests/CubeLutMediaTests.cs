using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class CubeLutMediaTests
{
    private static CubeLut Lattice(Func<double, double, double, (double R, double G, double B)> transform)
    {
        var values = new List<double>();
        for (var blue = 0; blue < 2; blue++)
        for (var green = 0; green < 2; green++)
        for (var red = 0; red < 2; red++)
        {
            var value = transform(red, green, blue); values.AddRange([value.R, value.G, value.B]);
        }
        return new() { Size = 2, Samples = values.ToArray() };
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void IdentityAndChannelSwapPreserveAlphaAndOriginalBuffer(int interpolation)
    {
        byte[] source = [13, 117, 245, 39, 255, 0, 0, 255, 0, 255, 0, 0]; var original = (byte[])source.Clone();
        var effect = VideoEffect.Create(VideoEffectKind.Lut3D); effect.SetParameter("Interpolation", interpolation);
        effect.Lut = Lattice((r, g, b) => (r, g, b));
        Assert.Equal(source, VideoEffectProcessor.Apply(source, [effect]));
        effect.Lut = Lattice((r, g, b) => (b, r, g));
        Assert.Equal(new byte[] { 117, 245, 13, 39, 0, 0, 255, 255, 255, 0, 0, 0 }, VideoEffectProcessor.Apply(source, [effect]));
        Assert.Equal(original, source);
    }

    [Theory]
    [InlineData(204, 128, 51)]
    [InlineData(204, 51, 128)]
    [InlineData(128, 204, 51)]
    [InlineData(128, 51, 204)]
    [InlineData(51, 204, 128)]
    [InlineData(51, 128, 204)]
    public void DistinguishesTrilinearFromAllSixTetrahedralRegions(byte red, byte green, byte blue)
    {
        var effect = VideoEffect.Create(VideoEffectKind.Lut3D);
        effect.Lut = Lattice((r, g, b) => (r * g * b, r * g * b, r * g * b));
        byte[] source = [blue, green, red, 73];
        var trilinear = VideoEffectProcessor.Apply(source, [effect]);
        Assert.InRange(trilinear[0], (byte)20, (byte)21);
        effect.SetParameter("Interpolation", 1);
        Assert.Equal(new byte[] { 51, 51, 51, 73 }, VideoEffectProcessor.Apply(source, [effect]));
    }

    [Fact]
    public void DomainsClampAndStrengthBlendsWithinTheOrderedEffectChain()
    {
        var effect = VideoEffect.Create(VideoEffectKind.Lut3D);
        effect.Lut = Lattice((r, g, b) => (r, g, b)); effect.Lut.DomainMinimum = [.25, .25, .25]; effect.Lut.DomainMaximum = [.75, .75, .75];
        Assert.Equal(new byte[] { 0, 255, 128, 99 }, VideoEffectProcessor.Apply([0, 255, 128, 99], [effect]));
        effect.Lut = Lattice((r, g, b) => (0, 0, 0)); effect.Amount = .5;
        var invert = VideoEffect.Create(VideoEffectKind.Invert);
        Assert.Equal(new byte[] { 128, 128, 128, 99 }, VideoEffectProcessor.Apply([0, 0, 0, 99], [invert, effect]));
        effect.Amount = 0; byte[] source = [11, 22, 33, 44]; Assert.Same(source, VideoEffectProcessor.Apply(source, [effect]));
        effect.Amount = 1; effect.IsEnabled = false; Assert.Same(source, VideoEffectProcessor.Apply(source, [effect]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ImportedLutMatchesCpuD3DAndEncodedFramesOverALowerTrack(int interpolation)
    {
        var effect = VideoEffect.Create(VideoEffectKind.Lut3D);
        effect.Lut = Lattice((r, g, b) => (b, g, r));
        effect.SetParameter("Interpolation", interpolation);
        var red = new MediaAsset { Path = "openrend://color?hex=#FF0000", HasVideo = true, DurationSeconds = 1 };
        var green = new MediaAsset { Path = "openrend://color?hex=#00FF00", HasVideo = true, DurationSeconds = 1 };
        var project = new ProjectDocument { Width = 32, Height = 24, Media = [red, green], Tracks = [
            new() { Kind = TrackKind.Video, Clips = [new() { MediaId = red.Id, DurationFrames = 3, Opacity = .5, VideoEffects = [effect] }] },
            new() { Kind = TrackKind.Video, Clips = [new() { MediaId = green.Id, DurationFrames = 3 }] }
        ] };
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var media = new MediaService();
            using var cpu = new TimelineRenderService(media, false);
            using var gpu = new TimelineRenderService(media);
            var expected = cpu.RenderFrame(project, 0, 32, 24);
            Assert.InRange((int)expected[0], 127, 128);
            Assert.InRange((int)expected[1], 127, 128);
            Assert.Equal(0, expected[2]); Assert.Equal(255, expected[3]);
            var actual = gpu.RenderFrame(project, 0, 32, 24);
            Assert.True(gpu.IsD3DCompositing, gpu.GpuUnavailableReason);
            for (var i = 0; i < expected.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, 1);
            var path = Path.Combine(directory, "lut.mp4");
            await new ExportService(cpu).ExportAsync(project, path);
            var encoded = media.GetFrame(path, 0, 32, 24);
            for (var channel = 0; channel < 3; channel++) Assert.InRange(Math.Abs(expected[channel] - encoded[channel]), 0, 8);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
