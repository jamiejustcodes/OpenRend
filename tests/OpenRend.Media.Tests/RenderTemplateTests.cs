using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class RenderTemplateTests
{
    [Fact]
    public void SearchRequiresEverySemicolonTermAcrossTemplateProperties()
    {
        var project = new ProjectDocument { Width = 1920, Height = 1080, FrameRate = new Rational(30000, 1001) };
        Assert.Single(RenderTemplateCatalog.Filter(project, " HIGH ; h.264 ;1920x1080; 29.97", false, []));
        Assert.Empty(RenderTemplateCatalog.Filter(project, "High;HEVC", false, []));
        Assert.Equal(6, RenderTemplateCatalog.Filter(project, "; ; AAC", false, []).Count());
        Assert.Single(RenderTemplateCatalog.Filter(project, "MP4", true, [RenderTemplateCatalog.DefaultId]));
        Assert.Empty(RenderTemplateCatalog.Filter(project, "", true, []));
    }

    [Fact]
    public void FilterByFormatAndMatchProjectSettings()
    {
        var project = new ProjectDocument { Width = 1920, Height = 1080, FrameRate = new Rational(30000, 1001) };
        var avcTemplates = RenderTemplateCatalog.Filter(project, "", false, [], formatId: "mp4-avc").ToList();
        Assert.Equal(4, avcTemplates.Count);
        Assert.All(avcTemplates, t => Assert.Equal("mp4-avc", t.FormatId));

        var wavTemplates = RenderTemplateCatalog.Filter(project, "", false, [], formatId: "audio-wav").ToList();
        Assert.Equal(2, wavTemplates.Count);
        Assert.All(wavTemplates, t => Assert.True(t.IsAudioOnly && t.Extension == ".wav"));

        var matchedTemplates = RenderTemplateCatalog.Filter(project, "", false, [], matchProject: true).ToList();
        Assert.All(matchedTemplates, t =>
        {
            Assert.False(t.IsAudioOnly);
            Assert.Equal(1920, t.Width ?? project.Width);
            Assert.Equal(1080, t.Height ?? project.Height);
        });
    }

    [Fact]
    public void EstimatedBytesCalculatesAccuratelyForVideoAndAudio()
    {
        var project = new ProjectDocument { Width = 1920, Height = 1080, FrameRate = new Rational(30, 1) };
        var template = RenderTemplateCatalog.Find("project-mp4");
        var bytes = template.EstimatedBytes(project, 300); // 10 seconds
        Assert.True(bytes > 500_000 && bytes < 50_000_000);

        var wavTemplate = RenderTemplateCatalog.Find("project-wav-24");
        // 48000 Hz * 2 channels * 3 bytes/sample * 10 seconds = 2,880,000 bytes
        var wavBytes = wavTemplate.EstimatedBytes(project, 300);
        Assert.Equal(2_880_000, wavBytes);
    }

    [Fact]
    public void SavedRenderChoicesAndFavoritesSurviveReloadAndInvalidWritesPreserveThem()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openrend-render-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new RenderPreferences { TemplateId = "project-mp4-high", Favorites = ["project-mp4-high"],
                OutputFolder = Path.GetTempPath(), UseLoopRegion = true };
            RenderPreferencesStore.Save(settings, path);
            var loaded = RenderPreferencesStore.Load(path);
            Assert.Equal(settings.TemplateId, loaded.TemplateId);
            Assert.Equal(settings.Favorites, loaded.Favorites);
            Assert.Equal(settings.OutputFolder, loaded.OutputFolder);
            Assert.True(loaded.UseLoopRegion);
            Assert.Throws<InvalidDataException>(() => RenderPreferencesStore.Save(settings with { TemplateId = "missing" }, path));
            Assert.Equal(settings.TemplateId, RenderPreferencesStore.Load(path).TemplateId);
            File.WriteAllText(path, "{");
            Assert.Throws<InvalidDataException>(() => RenderPreferencesStore.Load(path));
        }
        finally { File.Delete(path); }
    }
}
