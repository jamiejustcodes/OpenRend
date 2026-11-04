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

    [Fact]
    public void AdvancedProjectMetadataSurvivesExecutionUndoRedoAndCloning()
    {
        var project = new ProjectDocument
        {
            Name = "Vegas Style Project",
            Width = 1920,
            Height = 1080,
            FrameRate = new Rational(30000, 1001),
            Author = "Lead Editor",
            Engineer = "Lead Audio Engineer",
            Copyright = "© 2026 Studio",
            Comments = "Master edit session",
            UniversalAlbumId = "US-S1Z-26-00001",
            RecordedFilesFolder = @"D:\AudioTakes"
        };

        var clone = project.DeepClone();
        Assert.Equal(project.Name, clone.Name);
        Assert.Equal(project.Author, clone.Author);
        Assert.Equal(project.Engineer, clone.Engineer);
        Assert.Equal(project.Copyright, clone.Copyright);
        Assert.Equal(project.Comments, clone.Comments);
        Assert.Equal(project.UniversalAlbumId, clone.UniversalAlbumId);
        Assert.Equal(project.RecordedFilesFolder, clone.RecordedFilesFolder);

        var editor = new TimelineEditor(project);
        editor.Execute(p =>
        {
            p.Engineer = "Senior Sound Designer";
            p.UniversalAlbumId = "US-S1Z-26-00002";
            p.RecordedFilesFolder = @"E:\RecordedAudio";
        });

        Assert.Equal("Senior Sound Designer", editor.Project.Engineer);
        Assert.Equal("US-S1Z-26-00002", editor.Project.UniversalAlbumId);
        Assert.Equal(@"E:\RecordedAudio", editor.Project.RecordedFilesFolder);

        editor.Undo();
        Assert.Equal("Lead Audio Engineer", editor.Project.Engineer);
        Assert.Equal("US-S1Z-26-00001", editor.Project.UniversalAlbumId);
        Assert.Equal(@"D:\AudioTakes", editor.Project.RecordedFilesFolder);

        editor.Redo();
        Assert.Equal("Senior Sound Designer", editor.Project.Engineer);
        Assert.Equal("US-S1Z-26-00002", editor.Project.UniversalAlbumId);
        Assert.Equal(@"E:\RecordedAudio", editor.Project.RecordedFilesFolder);
    }

    [Fact]
    public void ProjectDocumentWithAdvancedMetadataRoundtripsJsonCleanly()
    {
        var original = new ProjectDocument
        {
            Name = "Roundtrip Test",
            Width = 3840,
            Height = 2160,
            FrameRate = new Rational(60, 1),
            Author = "Colorist",
            Engineer = "Sound Mixer",
            Copyright = "2026 Production",
            Comments = "Delivered in ACEScg",
            UniversalAlbumId = "ALBUM-98765",
            RecordedFilesFolder = @"C:\OpenRend\Takes"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ProjectDocument>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        Assert.Equal(original.FrameRate, restored.FrameRate);
        Assert.Equal(original.Author, restored.Author);
        Assert.Equal(original.Engineer, restored.Engineer);
        Assert.Equal(original.Copyright, restored.Copyright);
        Assert.Equal(original.Comments, restored.Comments);
        Assert.Equal(original.UniversalAlbumId, restored.UniversalAlbumId);
        Assert.Equal(original.RecordedFilesFolder, restored.RecordedFilesFolder);
    }
}
