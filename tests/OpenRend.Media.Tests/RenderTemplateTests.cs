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
        Assert.Equal(2, RenderTemplateCatalog.Filter(project, "; ; AAC", false, []).Count());
        Assert.Single(RenderTemplateCatalog.Filter(project, "MP4", true, [RenderTemplateCatalog.DefaultId]));
        Assert.Empty(RenderTemplateCatalog.Filter(project, "", true, []));
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
