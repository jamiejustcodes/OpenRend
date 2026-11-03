using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class ProjectPropertiesEditorTests
{
    [Fact]
    public void UndoAndRedoRestoreProjectPropertiesExactly()
    {
        var project = new ProjectDocument
        {
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(30000, 1001),
            FieldOrder = VideoFieldOrder.ProgressiveScan,
            PixelAspectRatio = 1.0,
            RenderQuality = VideoRenderQuality.Good,
            PixelFormat = ProjectPixelFormat.Int8Bit,
            AudioSampleRate = 48000,
            AudioBitDepth = 24
        };

        var editor = new TimelineEditor(project);

        editor.Execute(p =>
        {
            p.Width = 3840;
            p.Height = 2160;
            p.FrameRate = new Rational(60000, 1001);
            p.FieldOrder = VideoFieldOrder.UpperFieldFirst;
            p.PixelAspectRatio = 1.3333;
            p.RenderQuality = VideoRenderQuality.Best;
            p.PixelFormat = ProjectPixelFormat.Float32BitFullRange;
            p.AudioSampleRate = 96000;
            p.AudioBitDepth = 32;
            p.Author = "Editor In Chief";
        });

        Assert.Equal(3840, editor.Project.Width);
        Assert.Equal(2160, editor.Project.Height);
        Assert.Equal(new Rational(60000, 1001), editor.Project.FrameRate);
        Assert.Equal(VideoFieldOrder.UpperFieldFirst, editor.Project.FieldOrder);
        Assert.Equal(1.3333, editor.Project.PixelAspectRatio);
        Assert.Equal(VideoRenderQuality.Best, editor.Project.RenderQuality);
        Assert.Equal(ProjectPixelFormat.Float32BitFullRange, editor.Project.PixelFormat);
        Assert.Equal(96000, editor.Project.AudioSampleRate);
        Assert.Equal(32, editor.Project.AudioBitDepth);
        Assert.Equal("Editor In Chief", editor.Project.Author);

        // Undo restores original properties
        editor.Undo();
        Assert.Equal(1920, editor.Project.Width);
        Assert.Equal(1080, editor.Project.Height);
        Assert.Equal(new Rational(30000, 1001), editor.Project.FrameRate);
        Assert.Equal(VideoFieldOrder.ProgressiveScan, editor.Project.FieldOrder);
        Assert.Equal(1.0, editor.Project.PixelAspectRatio);
        Assert.Equal(VideoRenderQuality.Good, editor.Project.RenderQuality);
        Assert.Equal(ProjectPixelFormat.Int8Bit, editor.Project.PixelFormat);
        Assert.Equal(48000, editor.Project.AudioSampleRate);
        Assert.Equal(24, editor.Project.AudioBitDepth);
        Assert.Null(editor.Project.Author);

        // Redo reapplies updated properties
        editor.Redo();
        Assert.Equal(3840, editor.Project.Width);
        Assert.Equal(2160, editor.Project.Height);
        Assert.Equal(new Rational(60000, 1001), editor.Project.FrameRate);
        Assert.Equal(VideoFieldOrder.UpperFieldFirst, editor.Project.FieldOrder);
        Assert.Equal(1.3333, editor.Project.PixelAspectRatio);
        Assert.Equal(VideoRenderQuality.Best, editor.Project.RenderQuality);
        Assert.Equal(ProjectPixelFormat.Float32BitFullRange, editor.Project.PixelFormat);
        Assert.Equal(96000, editor.Project.AudioSampleRate);
        Assert.Equal(32, editor.Project.AudioBitDepth);
        Assert.Equal("Editor In Chief", editor.Project.Author);
    }
}
