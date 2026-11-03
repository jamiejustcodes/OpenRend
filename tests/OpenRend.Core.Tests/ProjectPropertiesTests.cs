using System.Text.Json;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class ProjectPropertiesTests
{
    [Fact]
    public void DefaultProjectPropertiesMatchIndustryStandard()
    {
        var project = new ProjectDocument();
        Assert.Equal(VideoFieldOrder.ProgressiveScan, project.FieldOrder);
        Assert.Equal(1.0, project.PixelAspectRatio);
        Assert.Equal(VideoRenderQuality.Good, project.RenderQuality);
        Assert.Equal(MotionBlurType.Gaussian, project.MotionBlur);
        Assert.Equal(VideoDeinterlaceMethod.BlendFields, project.DeinterlaceMethod);
        Assert.Equal(ProjectPixelFormat.Int8Bit, project.PixelFormat);
        Assert.Equal(HDRMode.Off, project.HDRMode);
        Assert.Equal(48000, project.AudioSampleRate);
        Assert.Equal(24, project.AudioBitDepth);
    }

    [Fact]
    public void DeepCloneCreatesIndependentCopyOfAllProjectProperties()
    {
        var original = new ProjectDocument
        {
            Name = "Feature Film",
            Width = 3840,
            Height = 2160,
            FrameRate = new Rational(24000, 1001),
            FieldOrder = VideoFieldOrder.UpperFieldFirst,
            PixelAspectRatio = 1.3333,
            RenderQuality = VideoRenderQuality.Best,
            MotionBlur = MotionBlurType.Pyramid,
            DeinterlaceMethod = VideoDeinterlaceMethod.InterpolateFields,
            PixelFormat = ProjectPixelFormat.Float32BitFullRange,
            HDRMode = HDRMode.HDR10,
            AudioSampleRate = 96000,
            AudioBitDepth = 32,
            PrerenderFolder = @"D:\Scratch\Prerenders",
            RulerTimeFormat = "Timecode (SMPTE)",
            Author = "Director Cut",
            Copyright = "© 2027 Studios",
            Comments = "Master edit"
        };

        var clone = original.DeepClone();
        Assert.Equal(original.Name, clone.Name);
        Assert.Equal(original.Width, clone.Width);
        Assert.Equal(original.Height, clone.Height);
        Assert.Equal(original.FrameRate, clone.FrameRate);
        Assert.Equal(original.FieldOrder, clone.FieldOrder);
        Assert.Equal(original.PixelAspectRatio, clone.PixelAspectRatio);
        Assert.Equal(original.RenderQuality, clone.RenderQuality);
        Assert.Equal(original.MotionBlur, clone.MotionBlur);
        Assert.Equal(original.DeinterlaceMethod, clone.DeinterlaceMethod);
        Assert.Equal(original.PixelFormat, clone.PixelFormat);
        Assert.Equal(original.HDRMode, clone.HDRMode);
        Assert.Equal(original.AudioSampleRate, clone.AudioSampleRate);
        Assert.Equal(original.AudioBitDepth, clone.AudioBitDepth);
        Assert.Equal(original.PrerenderFolder, clone.PrerenderFolder);
        Assert.Equal(original.RulerTimeFormat, clone.RulerTimeFormat);
        Assert.Equal(original.Author, clone.Author);
        Assert.Equal(original.Copyright, clone.Copyright);
        Assert.Equal(original.Comments, clone.Comments);

        // Mutating original does not mutate clone
        original.PixelFormat = ProjectPixelFormat.Int8Bit;
        original.AudioSampleRate = 44100;
        original.FieldOrder = VideoFieldOrder.LowerFieldFirst;
        Assert.Equal(ProjectPixelFormat.Float32BitFullRange, clone.PixelFormat);
        Assert.Equal(96000, clone.AudioSampleRate);
        Assert.Equal(VideoFieldOrder.UpperFieldFirst, clone.FieldOrder);
    }

    [Fact]
    public void ValidationEnforcesAspectAndAudioBounds()
    {
        var project = new ProjectDocument();
        project.Validate(); // Valid default

        project.PixelAspectRatio = 0.0;
        Assert.Throws<InvalidDataException>(() => project.Validate());

        project.PixelAspectRatio = 5.0;
        Assert.Throws<InvalidDataException>(() => project.Validate());

        project.PixelAspectRatio = 1.0;
        project.AudioSampleRate = 4000;
        Assert.Throws<InvalidDataException>(() => project.Validate());

        project.AudioSampleRate = 200000;
        Assert.Throws<InvalidDataException>(() => project.Validate());

        project.AudioSampleRate = 48000;
        project.AudioBitDepth = 20; // Only 16, 24, 32 allowed
        Assert.Throws<InvalidDataException>(() => project.Validate());

        project.AudioBitDepth = 16;
        project.Validate(); // Ok
    }

    [Fact]
    public void SerializationRoundTripPreservesProjectProperties()
    {
        var project = new ProjectDocument
        {
            Name = "RoundTrip",
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(60000, 1001),
            FieldOrder = VideoFieldOrder.ProgressiveScan,
            PixelAspectRatio = 1.0,
            RenderQuality = VideoRenderQuality.Good,
            MotionBlur = MotionBlurType.Box,
            DeinterlaceMethod = VideoDeinterlaceMethod.BlendFields,
            PixelFormat = ProjectPixelFormat.Float32Bit,
            HDRMode = HDRMode.HLG,
            AudioSampleRate = 48000,
            AudioBitDepth = 24,
            Author = "Lead Editor",
            Copyright = "© 2027 OpenRend"
        };

        var json = JsonSerializer.Serialize(project);
        var restored = JsonSerializer.Deserialize<ProjectDocument>(json)!;

        Assert.Equal(project.FieldOrder, restored.FieldOrder);
        Assert.Equal(project.PixelAspectRatio, restored.PixelAspectRatio);
        Assert.Equal(project.RenderQuality, restored.RenderQuality);
        Assert.Equal(project.MotionBlur, restored.MotionBlur);
        Assert.Equal(project.DeinterlaceMethod, restored.DeinterlaceMethod);
        Assert.Equal(project.PixelFormat, restored.PixelFormat);
        Assert.Equal(project.HDRMode, restored.HDRMode);
        Assert.Equal(project.AudioSampleRate, restored.AudioSampleRate);
        Assert.Equal(project.AudioBitDepth, restored.AudioBitDepth);
        Assert.Equal(project.Author, restored.Author);
        Assert.Equal(project.Copyright, restored.Copyright);
    }
}
