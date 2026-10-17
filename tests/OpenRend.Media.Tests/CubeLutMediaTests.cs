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
}
