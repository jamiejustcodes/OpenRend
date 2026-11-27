using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class GradingLutExporterTests
{
    [Theory]
    [InlineData(33)]
    [InlineData(65)]
    public void BakeUsesExactCubeCoordinatesAndRedFastestOrder(int size)
    {
        var grade = VideoEffect.Create(VideoEffectKind.ColorGrading);
        var lut = GradingLutExporter.Bake(grade, 0, size);
        var step = 1.0 / (size - 1);
        lut.Validate();
        Assert.Equal(new[] { 0d, 0, 0, step, 0, 0 }, lut.Samples[..6]);
        Assert.Equal(new[] { 0d, step, 0 }, lut.Samples[(3 * size)..(3 * size + 3)]);
        Assert.Equal(new[] { 0d, 0, step }, lut.Samples[(3 * size * size)..(3 * size * size + 3)]);
        Assert.Equal(new[] { 1d, 1, 1 }, lut.Samples[^3..]);
    }

    [Fact]
    public void ExportedGradeReimportsWithTheSameAnimatedLookAtTheChosenFrame()
    {
        var grade = VideoEffect.Create(VideoEffectKind.ColorGrading);
        grade.Lut = BuiltinLutLibrary.CreatePreset(BuiltinLutLibrary.LookLutNames[0]);
        grade.LookLut = BuiltinLutLibrary.CreatePreset(BuiltinLutLibrary.LookLutNames[1]);
        grade.Animation = new()
        {
            IsEnabled = true,
            Keyframes =
            [
                new() { Frame = 0 },
                new() { Frame = 20, Parameters = new() { ["ExposureLnAdjust"] = .5, [TonalRangeGrading.Shadows + "_Y"] = .04 } }
            ]
        };
        var location = Path.Combine(Path.GetTempPath(), "OpenRend-Grade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(location);
        var path = Path.Combine(location, "Grade.cube");
        try
        {
            GradingLutExporter.Save(grade, 12, 65, path);
            var lut = CubeLutParser.Load(path);
            Assert.Equal(65, lut.Size);
            Assert.Single(Directory.GetFiles(location));
            var imported = VideoEffect.Create(VideoEffectKind.Lut3D);
            imported.Lut = lut;
            byte[] pixels = [33, 44, 58, 255, 78, 89, 102, 240, 176, 152, 140, 128, 248, 231, 205, 255];
            var preview = VideoEffectProcessor.Apply(pixels, 4, 1, [grade], 12);
            var reimport = VideoEffectProcessor.Apply(pixels, [imported]);
            for (var i = 0; i < pixels.Length; i++)
            {
                var tolerance = i % 4 == 3 ? 0 : 3;
                Assert.InRange(Math.Abs(preview[i] - reimport[i]), 0, tolerance);
            }

            var later = GradingLutExporter.Bake(grade, 20, 65);
            Assert.False(lut.Samples.AsSpan().SequenceEqual(later.Samples));
            grade.IsEnabled = false;
            Assert.Equal(new[] { 0.5, 0, 0 }, GradingLutExporter.Bake(grade, 12, 33).Samples[48..51]);
        }
        finally { Directory.Delete(location, recursive: true); }
    }
}
