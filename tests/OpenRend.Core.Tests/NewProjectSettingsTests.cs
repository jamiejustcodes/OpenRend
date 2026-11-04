using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class NewProjectSettingsTests
{
    [Theory]
    [InlineData(ProjectAspect.Widescreen, 1280, 720, 1920, 1080, 3840, 2160)]
    [InlineData(ProjectAspect.Fullscreen, 1280, 960, 1920, 1440, 4096, 3072)]
    [InlineData(ProjectAspect.Portrait, 720, 1280, 1080, 1920, 2160, 3840)]
    [InlineData(ProjectAspect.Square, 720, 720, 1080, 1080, 2160, 2160)]
    [InlineData(ProjectAspect.Scope, 1280, 536, 2048, 858, 4096, 1716)]
    public void ProjectPresetsHaveTheVerifiedDimensions(ProjectAspect aspect, int hdWidth, int hdHeight, int fullWidth, int fullHeight, int uhdWidth, int uhdHeight)
    {
        var settings = new NewProjectSettings { Aspect = aspect };
        Assert.Equal((hdWidth, hdHeight), (settings with { Resolution = ProjectResolution.HD }).Dimensions);
        Assert.Equal((fullWidth, fullHeight), (settings with { Resolution = ProjectResolution.FullHD }).Dimensions);
        Assert.Equal((uhdWidth, uhdHeight), (settings with { Resolution = ProjectResolution.UHD }).Dimensions);
    }

    [Theory]
    [InlineData(24)] [InlineData(25)] [InlineData(30)] [InlineData(48)] [InlineData(50)] [InlineData(60)] [InlineData(120)]
    public void WholeAndFractionalRatesRemainRationalWhenProjectsAreCreated(int nominal)
    {
        var settings = new NewProjectSettings { FramesPerSecond = nominal, FractionalRate = true };
        Assert.Equal(new Rational(nominal * 1000, 1001), settings.CreateProject().FrameRate);
        Assert.Equal(new Rational(nominal, 1), (settings with { FractionalRate = false }).CreateProject().FrameRate);
        Assert.Empty(settings.CreateProject().Tracks);
        Assert.Empty(settings.CreateProject().Media);
    }

    [Fact]
    public void WelcomePreferencesPreserveValidSettingsAndRejectCorruptReplacements()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openrend-welcome-{Guid.NewGuid():N}.json");
        try
        {
            var preferences = new WelcomePreferences { ShowAtStartup = false,
                NewProject = new() { Aspect = ProjectAspect.Portrait, Resolution = ProjectResolution.UHD, FramesPerSecond = 60 } };
            preferences = preferences.WithRecentProject(Path.GetFullPath("one.openrend"))
                .WithRecentProject(Path.GetFullPath("two.openrend")).WithRecentProject(Path.GetFullPath("one.openrend"));
            WelcomePreferencesStore.Save(preferences, path);
            var loaded = WelcomePreferencesStore.Load(path);
            Assert.False(loaded.ShowAtStartup);
            Assert.Equal(preferences.NewProject, loaded.NewProject);
            Assert.Equal(preferences.RecentProjects, loaded.RecentProjects);
            Assert.Equal(2, loaded.RecentProjects.Length);
            Assert.Throws<InvalidDataException>(() => WelcomePreferencesStore.Save(preferences with
                { NewProject = preferences.NewProject with { FramesPerSecond = 59 } }, path));
            Assert.Equal(preferences.NewProject, WelcomePreferencesStore.Load(path).NewProject);
            File.WriteAllText(path, "{");
            Assert.Throws<InvalidDataException>(() => WelcomePreferencesStore.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WelcomePreferencesPreserveDefaultProjectTemplate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openrend-welcome-default-{Guid.NewGuid():N}.json");
        try
        {
            var defaultTemplate = new ProjectDocument
            {
                Name = "Default Template",
                Width = 3840,
                Height = 2160,
                FrameRate = new Rational(60, 1),
                FieldOrder = VideoFieldOrder.ProgressiveScan,
                PixelAspectRatio = 1.0,
                PixelFormat = ProjectPixelFormat.Float32Bit,
                RenderQuality = VideoRenderQuality.Best,
                MotionBlur = MotionBlurType.Gaussian,
                DeinterlaceMethod = VideoDeinterlaceMethod.InterpolateFields,
                AudioSampleRate = 96000,
                AudioBitDepth = 32,
                PrerenderFolder = @"C:\OpenRend\Prerender",
                RecordedFilesFolder = @"C:\OpenRend\RecordedAudio",
                RulerFormat = RulerFormat.SmpteFilmSync,
                RulerTimeFormat = RulerFormat.SmpteFilmSync.ToDisplayName(),
                BeatsPerMinute = 128.0,
                BeatsPerMeasure = 4,
                Author = "Template Author",
                Engineer = "Template Engineer",
                Copyright = "© 2026",
                UniversalAlbumId = "ALBUM-DEFAULT"
            };

            var preferences = new WelcomePreferences
            {
                ShowAtStartup = true,
                DefaultProjectTemplate = defaultTemplate
            };

            WelcomePreferencesStore.Save(preferences, path);
            var loaded = WelcomePreferencesStore.Load(path);

            Assert.NotNull(loaded.DefaultProjectTemplate);
            Assert.Equal(3840, loaded.DefaultProjectTemplate.Width);
            Assert.Equal(2160, loaded.DefaultProjectTemplate.Height);
            Assert.Equal(new Rational(60, 1), loaded.DefaultProjectTemplate.FrameRate);
            Assert.Equal(ProjectPixelFormat.Float32Bit, loaded.DefaultProjectTemplate.PixelFormat);
            Assert.Equal(96000, loaded.DefaultProjectTemplate.AudioSampleRate);
            Assert.Equal(32, loaded.DefaultProjectTemplate.AudioBitDepth);
            Assert.Equal(@"C:\OpenRend\RecordedAudio", loaded.DefaultProjectTemplate.RecordedFilesFolder);
            Assert.Equal("Template Engineer", loaded.DefaultProjectTemplate.Engineer);
            Assert.Equal("ALBUM-DEFAULT", loaded.DefaultProjectTemplate.UniversalAlbumId);
        }
        finally { File.Delete(path); }
    }
}
