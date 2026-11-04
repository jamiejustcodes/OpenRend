using System.Globalization;
using System.IO;
using System.Text.Json;
using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class RenderTemplateCustomizationTests
{
    [Fact]
    public void ValidExtendedRenderTemplatePassesValidation()
    {
        var template = new RenderTemplate("custom-" + Guid.NewGuid().ToString("N"), "Vegas Pro YouTube 4K", 18, 384)
        {
            FormatId = "mp4-avc",
            FormatName = "MAGIX AVC/AAC MP4",
            Extension = ".mp4",
            VideoCodec = "libx264",
            AudioCodec = "aac",
            Width = 3840,
            Height = 2160,
            BitrateMode = BitrateMode.VBR_2Pass,
            AverageBitrateBps = 48_000_000,
            MaxBitrateBps = 60_000_000,
            Profile = VideoProfile.High,
            FieldOrder = FieldOrder.Progressive,
            PixelAspectRatio = 1.0,
            FrameRate = new Rational(60, 1),
            HardwareAcceleration = HardwareAcceleration.NVENC,
            EncoderSpeed = EncoderSpeed.Slow,
            AudioChannels = 2,
            AudioSampleRate = 48000,
            FastStart = true,
            Quality = RenderQuality.Best,
            ColorSpace = ColorSpaceOption.Rec709,
            ResampleMode = ResampleMode.DisableResample,
            DescriptionText = "Vegas Pro 1:1 4K 60fps Delivery"
        };

        template.Validate(requireCustom: true);
        Assert.True(template.IsCustom);
        Assert.Equal("Vegas Pro 1:1 4K 60fps Delivery", template.Description(new ProjectDocument()));
    }

    [Theory]
    [InlineData(0, 20_000_000, "Average bitrate must be greater than zero.")]
    [InlineData(-5000, 20_000_000, "Average bitrate must be greater than zero.")]
    [InlineData(25_000_000, 20_000_000, "Maximum bitrate cannot be less than average bitrate.")]
    public void InvalidVbrBitrateThrowsException(long avgBps, long maxBps, string expectedError)
    {
        var template = new RenderTemplate("custom-" + Guid.NewGuid().ToString("N"), "VBR Test", 20, 192)
        {
            BitrateMode = BitrateMode.VBR_1Pass,
            AverageBitrateBps = avgBps,
            MaxBitrateBps = maxBps
        };

        var ex = Assert.Throws<InvalidDataException>(() => template.Validate(requireCustom: true));
        Assert.Equal(expectedError, ex.Message);
    }

    [Fact]
    public void InvalidCbrBitrateThrowsException()
    {
        var template = new RenderTemplate("custom-" + Guid.NewGuid().ToString("N"), "CBR Test", 20, 192)
        {
            BitrateMode = BitrateMode.CBR,
            AverageBitrateBps = 0
        };

        var ex = Assert.Throws<InvalidDataException>(() => template.Validate(requireCustom: true));
        Assert.Equal("Constant bitrate must be greater than zero.", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(8)]
    public void InvalidAudioChannelsThrowsException(int channels)
    {
        var template = new RenderTemplate("custom-" + Guid.NewGuid().ToString("N"), "Channels Test", 20, 192)
        {
            AudioChannels = channels
        };

        var ex = Assert.Throws<InvalidDataException>(() => template.Validate(requireCustom: true));
        Assert.Contains("Audio channels", ex.Message);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void InvalidPixelAspectRatioThrowsException(double aspect)
    {
        var template = new RenderTemplate("custom-" + Guid.NewGuid().ToString("N"), "Aspect Test", 20, 192)
        {
            PixelAspectRatio = aspect
        };

        var ex = Assert.Throws<InvalidDataException>(() => template.Validate(requireCustom: true));
        Assert.Equal("Pixel aspect ratio must be a positive number.", ex.Message);
    }

    [Fact]
    public void ExtendedTemplateSerializesAndDeserializesRoundtrip()
    {
        var template = new RenderTemplate("custom-" + Guid.NewGuid().ToString("N"), "Roundtrip Master", 22, 256)
        {
            Width = 1920,
            Height = 1080,
            BitrateMode = BitrateMode.CBR,
            AverageBitrateBps = 20_000_000,
            MaxBitrateBps = 20_000_000,
            Profile = VideoProfile.High,
            HardwareAcceleration = HardwareAcceleration.NVENC,
            EncoderSpeed = EncoderSpeed.Medium,
            Quality = RenderQuality.Good,
            ColorSpace = ColorSpaceOption.Rec709,
            ResampleMode = ResampleMode.SmartResample,
            AudioChannels = 6,
            AudioSampleRate = 48000,
            FastStart = true
        };

        var json = JsonSerializer.Serialize(template);
        var restored = JsonSerializer.Deserialize<RenderTemplate>(json);

        Assert.NotNull(restored);
        Assert.Equal(template, restored);
        Assert.Equal(BitrateMode.CBR, restored.BitrateMode);
        Assert.Equal(20_000_000, restored.AverageBitrateBps);
        Assert.Equal(VideoProfile.High, restored.Profile);
        Assert.Equal(HardwareAcceleration.NVENC, restored.HardwareAcceleration);
        Assert.Equal(RenderQuality.Good, restored.Quality);
        Assert.Equal(6, restored.AudioChannels);
        Assert.True(restored.FastStart);
    }

    [Fact]
    public void EstimatedBytesCalculatesAccuratelyForBitrateModes()
    {
        var project = new ProjectDocument
        {
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(30, 1)
        };

        // 300 frames at 30 fps = 10.0 seconds
        var templateCbr = new RenderTemplate("cbr-test", "CBR Test", 20, 200)
        {
            BitrateMode = BitrateMode.CBR,
            AverageBitrateBps = 8_000_000 // 8 Mbps video + 200 kbps audio = 8,200,000 bps
        };

        var bytes = templateCbr.EstimatedBytes(project, 300);
        // 8,200,000 bps / 8 = 1,025,000 bytes per sec * 10 sec = 10,250,000 bytes
        Assert.Equal(10_250_000, bytes);
    }

    [Fact]
    public void HardwareAccelerationHelperReportsEncoderStatus()
    {
        var support = HardwareAccelerationHelper.Current;
        Assert.NotNull(support);
        Assert.False(string.IsNullOrWhiteSpace(support.SummaryText));

        // When probed again with forceRefresh, should return valid support
        var refreshed = HardwareAccelerationHelper.Check(forceRefresh: true);
        Assert.NotNull(refreshed);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.SummaryText));
    }
}

