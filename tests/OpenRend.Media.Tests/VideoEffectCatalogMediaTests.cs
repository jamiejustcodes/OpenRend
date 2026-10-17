using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class VideoEffectCatalogMediaTests(MediaFixture fixture) : IClassFixture<MediaFixture>
{
    public static IEnumerable<object[]> EffectKinds() => Enum.GetValues<VideoEffectKind>().Select(kind => new object[] { kind });
    private static byte[] Image(int width, int height)
    {
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        { var i = (y * width + x) * 4; data[i] = (byte)((x * 13 + y * 7) % 256); data[i + 1] = (byte)((x * 3 + y * 23) % 256); data[i + 2] = (byte)((x * 19 + y * 11) % 256); data[i + 3] = (byte)(100 + (x + y) % 156); }
        return data;
    }

    [Theory]
    [MemberData(nameof(EffectKinds))]
    public void EveryAdvertisedEffectActuallyProcessesPixelsAndBypassesWithoutMutation(VideoEffectKind kind)
    {
        var effect = VideoEffect.Create(kind);
        switch (kind)
        {
            case VideoEffectKind.BrightnessContrast: effect.Amount = .2; break;
            case VideoEffectKind.Saturation: effect.Amount = .3; break;
            case VideoEffectKind.HslAdjust: effect.SetParameter("Hue", 70); break;
            case VideoEffectKind.ColorBalance: effect.SetParameter("Green", .2); break;
            case VideoEffectKind.WhiteBalance: effect.SetParameter("Temperature", .5); break;
            case VideoEffectKind.Levels: effect.SetParameter("Gamma", 2); break;
            case VideoEffectKind.ChannelBlend: effect.SetParameter("Crossfeed", .7); break;
            case VideoEffectKind.Pixelate: effect.Amount = 25; break;
            case VideoEffectKind.LinearBlur: effect.Amount = .5; effect.SetParameter("Angle", 45); break;
            case VideoEffectKind.RadialBlur: effect.Amount = .5; break;
            case VideoEffectKind.ChromaBlur: effect.SetParameter("Horizontal", 3); effect.SetParameter("Vertical", 3); break;
            case VideoEffectKind.Spherize: effect.Amount = .6; break;
            case VideoEffectKind.PinchPunch: effect.Amount = .6; break;
            case VideoEffectKind.TvSimulator: effect.Amount = .5; effect.SetParameter("Phosphorescence", .3); break;
            case VideoEffectKind.Crop: effect.SetParameter("XCrop", .2); effect.SetParameter("YCrop", .2); break;
            case VideoEffectKind.Newsprint: effect.Amount = 6; break;
            case VideoEffectKind.GradientMap: effect.Amount = .8; break;
            case VideoEffectKind.Defocus: effect.Amount = 4; break;
            case VideoEffectKind.LabAdjust: effect.SetParameter("ChannelA", .3); break;
            case VideoEffectKind.MaskGenerator: effect.SetParameter("Type", 0); effect.SetParameter("HighIn", .5); break;
            case VideoEffectKind.Timecode: effect.Amount = 1; break;
            case VideoEffectKind.PictureInPicture: effect.SetParameter("Scale", .5); break;
            case VideoEffectKind.LightRays: effect.Amount = .6; break;
            case VideoEffectKind.Starburst: effect.Amount = 50; break;
            case VideoEffectKind.TinyPlanet: effect.SetParameter("Latitude", 45); break;
            case VideoEffectKind.AiAutoReframe: effect.SetParameter("Preview", 2); break;
            case VideoEffectKind.AiColorization: effect.SetParameter("ColorShiftA", .5); break;
            case VideoEffectKind.AiZDepth: effect.SetParameter("Visualization", 2); break;
            case VideoEffectKind.AiSmartMask: effect.SetParameter("Visualization", 1); break;
            case VideoEffectKind.Lut3D:
                effect.Lut = CubeLutParser.Parse("LUT_3D_SIZE 2\n0 0 0\n0 0 1\n0 1 0\n0 1 1\n1 0 0\n1 0 1\n1 1 0\n1 1 1\n");
                break;
        }
        var source = Image(32, 24); var copy = (byte[])source.Clone();
        var result = VideoEffectProcessor.Apply(source, 32, 24, [effect], 5);
        Assert.Equal(source.Length, result.Length); Assert.False(source.SequenceEqual(result), $"{kind} did not affect the test image."); Assert.Equal(copy, source);
        if (kind is not (VideoEffectKind.CookieCutter or VideoEffectKind.ChromaKeyer or VideoEffectKind.Mirror or VideoEffectKind.OffsetAndWrap or VideoEffectKind.Swirl or VideoEffectKind.RadialPixelate or VideoEffectKind.LensCorrection or VideoEffectKind.Wave or VideoEffectKind.LinearBlur or VideoEffectKind.RadialBlur or VideoEffectKind.Spherize or VideoEffectKind.PinchPunch or VideoEffectKind.Crop or VideoEffectKind.Defocus or VideoEffectKind.MaskGenerator or VideoEffectKind.PictureInPicture or VideoEffectKind.TinyPlanet or VideoEffectKind.Timecode or VideoEffectKind.AiAutoReframe or VideoEffectKind.AiSmartMask))
            for (var i = 3; i < source.Length; i += 4) Assert.Equal(source[i], result[i]);
        effect.IsEnabled = false; Assert.Same(source, VideoEffectProcessor.Apply(source, 32, 24, [effect]));
        effect.IsEnabled = true;
        Assert.Equal(4, VideoEffectProcessor.Apply([40, 80, 120, 150], 1, 1, [effect]).Length);
    }

    [Fact]
    public void ColorOperationsHaveKnownEndpointsAndHslRotatesPrimaryColors()
    {
        var bw = VideoEffect.Create(VideoEffectKind.BlackAndWhite);
        Assert.Equal(new byte[] { 85, 85, 85, 77 }, VideoEffectProcessor.Apply([0, 0, 255, 77], [bw]));
        var hsl = VideoEffect.Create(VideoEffectKind.HslAdjust); hsl.SetParameter("Hue", 120);
        Assert.Equal(new byte[] { 0, 255, 0, 77 }, VideoEffectProcessor.Apply([0, 0, 255, 77], [hsl]));
        var levels = VideoEffect.Create(VideoEffectKind.Levels); levels.SetParameter("InputBlack", .2); levels.SetParameter("InputWhite", .8);
        Assert.Equal(new byte[] { 0, 128, 255, 77 }, VideoEffectProcessor.Apply([0, 128, 255, 77], [levels]));
        var legal = VideoEffect.Create(VideoEffectKind.BroadcastColors);
        Assert.Equal(new byte[] { 16, 126, 235, 77 }, VideoEffectProcessor.Apply([0, 128, 255, 77], [legal]));
        var threshold = VideoEffect.Create(VideoEffectKind.Threshold);
        Assert.Equal(new byte[] { 0, 0, 0, 77, 255, 255, 255, 80 }, VideoEffectProcessor.Apply([0, 0, 255, 77, 0, 255, 0, 80], [threshold]));
    }

    [Fact]
    public void BlurIgnoresHiddenColorsAndMedianRemovesAnIsolatedImpulse()
    {
        var blur = VideoEffect.Create(VideoEffectKind.QuickBlur); blur.Amount = 2;
        var result = VideoEffectProcessor.Apply([0, 0, 255, 255, 255, 0, 0, 0], 2, 1, [blur]);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0, 255, 0 }, result);
        var median = VideoEffect.Create(VideoEffectKind.Median); var image = new byte[3 * 3 * 4];
        for (var i = 3; i < image.Length; i += 4) image[i] = 255; image[16] = image[17] = image[18] = 255;
        result = VideoEffectProcessor.Apply(image, 3, 3, [median]); Assert.Equal(0, result[16]); Assert.Equal(255, result[19]);
    }

    [Fact]
    public void MirrorWrapAndKeyerHaveExactPixelAndAlphaBehavior()
    {
        byte[] source = [10, 20, 30, 255, 40, 50, 60, 128, 70, 80, 90, 0];
        var mirror = VideoEffect.Create(VideoEffectKind.Mirror);
        var result = VideoEffectProcessor.Apply(source, 3, 1, [mirror]);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 40, 50, 60, 128, 10, 20, 30, 255 }, result);
        var wrap = VideoEffect.Create(VideoEffectKind.OffsetAndWrap); wrap.SetParameter("Horizontal", 1.0 / 3);
        result = VideoEffectProcessor.Apply(source, 3, 1, [wrap]); Assert.Equal(new byte[] { 0, 0, 0, 0, 10, 20, 30, 255, 40, 50, 60, 128 }, result);
        var keyer = VideoEffect.Create(VideoEffectKind.ChromaKeyer);
        Assert.Equal(new byte[] { 0, 255, 0, 0, 0, 0, 255, 120 }, VideoEffectProcessor.Apply([0, 255, 0, 200, 0, 0, 255, 120], [keyer]));
    }

    [Fact]
    public void NoiseReproducesEachProjectFrameAndRejectsMalformedImageBuffers()
    {
        var source = Image(8, 6); var noise = VideoEffect.Create(VideoEffectKind.FilmGrain);
        var first = VideoEffectProcessor.Apply(source, 8, 6, [noise], 100);
        Assert.Equal(first, VideoEffectProcessor.Apply(source, 8, 6, [noise], 100));
        Assert.False(first.SequenceEqual(VideoEffectProcessor.Apply(source, 8, 6, [noise], 101)));
        noise.SetParameter("Seed", 5); Assert.False(first.SequenceEqual(VideoEffectProcessor.Apply(source, 8, 6, [noise], 100)));
        Assert.Throws<ArgumentException>(() => VideoEffectProcessor.Apply(source, 7, 6, [noise]));
        noise.Parameters["Size"] = double.PositiveInfinity; Assert.Throws<InvalidDataException>(() => VideoEffectProcessor.Apply(source, 8, 6, [noise]));
    }

    [Fact]
    public void HiddenKeyColorCannotBleedThroughNeighborhoodConvolutionOrHalfMirror()
    {
        byte[] source = [0, 255, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255];
        var keyer = VideoEffect.Create(VideoEffectKind.ChromaKeyer);
        foreach (var kind in new[] { VideoEffectKind.Median, VideoEffectKind.MinAndMax, VideoEffectKind.ConvolutionKernel })
        {
            var effect = VideoEffect.Create(kind);
            if (kind == VideoEffectKind.ConvolutionKernel)
            {
                foreach (var parameter in VideoEffectCatalog.Get(kind).Parameters) effect.SetParameter(parameter.Key, 0);
                effect.SetParameter("K10", 1);
            }
            var result = VideoEffectProcessor.Apply(source, 3, 1, [keyer, effect]);
            Assert.Equal(0, result[4]); Assert.Equal(0, result[5]); Assert.Equal(0, result[6]); Assert.Equal(255, result[7]);
        }
        var mirror = VideoEffect.Create(VideoEffectKind.Mirror); mirror.SetParameter("Horizontal", .5);
        var mixed = VideoEffectProcessor.Apply([0, 0, 255, 255, 255, 0, 0, 255], 2, 1, [mirror]);
        Assert.Equal(new byte[] { 128, 0, 128, 255, 128, 0, 128, 255 }, mixed);
    }

    [Fact]
    public async Task KeyedSourceResamplingFiltersPremultipliedColorAtTheMaskBoundary()
    {
        using var media = new MediaService(); var asset = await media.ProbeAsync(fixture.Quadrants);
        var keyer = VideoEffect.Create(VideoEffectKind.ChromaKeyer); keyer.SetParameter("Red", 1);
        var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 3, VideoEffects = [keyer] };
        var project = new ProjectDocument { Media = [asset], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] };
        using var renderer = new TimelineRenderService(media, false);
        var image = renderer.RenderFrame(project, 0, 63, 48); var boundary = (12 * 63 + 31) * 4;
        Assert.Equal(0, image[boundary]); Assert.Equal(0, image[boundary + 1]); Assert.InRange(image[boundary + 2], (byte)127, (byte)128);
    }

    [Fact]
    public async Task SpatialAndFrameSeededEffectsMatchCpuGpuAndEncodedExport()
    {
        using var media = new MediaService(); var asset = await media.ProbeAsync(fixture.Quadrants);
        var blur = VideoEffect.Create(VideoEffectKind.GaussianBlur); blur.Amount = 1;
        var noise = VideoEffect.Create(VideoEffectKind.AddNoise); noise.Amount = .025;
        var clip = new TimelineClip { MediaId = asset.Id, DurationFrames = 3, VideoEffects = [blur, noise, VideoEffect.Create(VideoEffectKind.Sepia)] };
        var project = new ProjectDocument { Width = 64, Height = 48, Media = [asset], Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] };
        using var cpu = new TimelineRenderService(media, false); using var gpu = new TimelineRenderService(media);
        var expected = cpu.RenderFrame(project, 1, 64, 48); var actual = gpu.RenderFrame(project, 1, 64, 48);
        for (var i = 0; i < actual.Length; i++) Assert.InRange(Math.Abs(expected[i] - actual[i]), 0, 1);
        Assert.False(expected.SequenceEqual(cpu.RenderFrame(project, 2, 64, 48)));
        var path = Path.Combine(fixture.Directory, "expanded-effects.mp4"); await new ExportService(cpu).ExportAsync(project, path);
        var encoded = media.GetFrame(path, project.FrameRate.Seconds(1), 64, 48);
        foreach (var (x, y) in new[] { (12, 12), (50, 12), (12, 36), (50, 36) })
        { var p = (y * 64 + x) * 4; for (var c = 0; c < 3; c++) Assert.InRange(Math.Abs(encoded[p + c] - expected[p + c]), 0, 12); }
    }
}
