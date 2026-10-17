using System.Text;
using System.Text.Json;
using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class CustomRenderTemplateTests
{
    [Fact]
    public void CustomTemplateEditsKeepTheirIdentityAndDeleteCleansSavedReferences()
    {
        var original = new RenderPreferences();
        var saved = original.SaveCustomTemplate("Review copy", RenderTemplateCatalog.Find(RenderTemplateCatalog.DefaultId) with
            { VideoCrf = 28, AudioBitrateKbps = 96, EncoderSpeed = EncoderSpeed.Veryfast });
        var id = saved.TemplateId;
        Assert.Empty(original.CustomTemplates);
        Assert.True(saved.FindTemplate(id).IsCustom);
        saved = saved with { Favorites = [id, RenderTemplateCatalog.DefaultId] };
        var renamed = saved.RenameCustomTemplate(id, "Client review");
        Assert.Equal(id, renamed.TemplateId);
        Assert.Equal("Review copy", saved.FindTemplate(id).Name);
        Assert.Equal("Client review", renamed.FindTemplate(id).Name);
        var edited = renamed.SaveCustomTemplate("Client review", renamed.FindTemplate(id) with { VideoCrf = 31 }, id);
        Assert.Equal(31, Assert.Single(edited.CustomTemplates).VideoCrf);
        Assert.Equal([id, RenderTemplateCatalog.DefaultId], edited.Favorites);
        var deleted = edited.DeleteCustomTemplate(id);
        Assert.Empty(deleted.CustomTemplates);
        Assert.Equal(RenderTemplateCatalog.DefaultId, deleted.TemplateId);
        Assert.Equal([RenderTemplateCatalog.DefaultId], deleted.Favorites);
        Assert.Throws<InvalidDataException>(() => edited.DeleteCustomTemplate(RenderTemplateCatalog.DefaultId));
        Assert.Throws<InvalidDataException>(() => edited.RenameCustomTemplate(RenderTemplateCatalog.DefaultId, "Override"));
        Assert.Throws<InvalidDataException>(() => edited.SaveCustomTemplate("client REVIEW", edited.FindTemplate(id)));
        Assert.Throws<InvalidDataException>(() => edited.SaveCustomTemplate("Project settings", edited.FindTemplate(id)));
        Assert.Throws<InvalidDataException>(() => edited.SaveCustomTemplate("Invalid\nname", edited.FindTemplate(id)));
    }

    [Fact]
    public void CustomSettingsRoundTripAndRejectedWritesLeaveTheLastGoodFileIntact()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openrend-custom-render-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new RenderPreferences().SaveCustomTemplate("Delivery", RenderTemplateCatalog.All[0] with
                { VideoCrf = 18, AudioBitrateKbps = 320, EncoderSpeed = EncoderSpeed.Slow });
            settings = settings with { Favorites = [settings.TemplateId] };
            RenderPreferencesStore.Save(settings, path);
            var loaded = RenderPreferencesStore.Load(path);
            Assert.Equal(Assert.Single(settings.CustomTemplates), Assert.Single(loaded.CustomTemplates));
            Assert.Equal(settings.Favorites, loaded.Favorites);
            Assert.Single(RenderTemplateCatalog.Filter(loaded.Templates, new ProjectDocument(), "Delivery;320 kbps;Slow", true, loaded.Favorites));
            var before = File.ReadAllBytes(path);
            Assert.Throws<InvalidDataException>(() => RenderPreferencesStore.Save(loaded with
                { CustomTemplates = [loaded.CustomTemplates[0] with { Id = RenderTemplateCatalog.DefaultId }] }, path));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Throws<InvalidDataException>(() => RenderPreferencesStore.Save(loaded with { CustomTemplates = null! }, path));
            Assert.Throws<InvalidDataException>(() => RenderPreferencesStore.Save(loaded with
                { CustomTemplates = [loaded.CustomTemplates[0], loaded.CustomTemplates[0]] }, path));
            File.WriteAllText(path, "{\"TemplateId\":\"project-mp4\",\"CustomTemplates\":null}");
            Assert.Throws<InvalidDataException>(() => RenderPreferencesStore.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(-1, 192, EncoderSpeed.Fast)]
    [InlineData(52, 192, EncoderSpeed.Fast)]
    [InlineData(20, 31, EncoderSpeed.Fast)]
    [InlineData(20, 513, EncoderSpeed.Fast)]
    [InlineData(20, 192, (EncoderSpeed)999)]
    public void InvalidEncoderSettingsFailBeforeCreatingOrReplacingOutput(int crf, int audioBitrate, EncoderSpeed speed)
    {
        var path = Path.Combine(Path.GetTempPath(), $"openrend-invalid-render-{Guid.NewGuid():N}.mp4");
        var template = new RenderTemplate("custom-" + Guid.NewGuid().ToString("N"), "Invalid", crf, audioBitrate) { EncoderSpeed = speed };
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, false);
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            Assert.Throws<InvalidDataException>(() => { _ = new ExportService(renderer).ExportAsync(CreateProject(), path,
                new ExportOptions { TemplateId = template.Id, CustomTemplate = template }); });
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BuiltinIdentityCannotBeUsedToInjectCustomEncoderSettings()
    {
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, false);
        var service = new ExportService(renderer);
        Assert.Throws<InvalidDataException>(() => { _ = service.ExportAsync(CreateProject(), "unused.mp4", new ExportOptions
            { CustomTemplate = RenderTemplateCatalog.All[0] with { VideoCrf = 40 } }); });
        Assert.Throws<InvalidDataException>(() => { _ = service.ExportAsync(CreateProject(), "unused.mp4", new ExportOptions
            { CustomTemplate = new("custom-" + Guid.NewGuid().ToString("N"), "Other", 20, 192) }); });
    }

    [Theory]
    [InlineData(31, EncoderSpeed.Veryfast, "subme=2")]
    [InlineData(12, EncoderSpeed.Slow, "subme=8")]
    public async Task CustomQualityAndSpeedReachTheEncoderWithoutChangingProjectTiming(int crf, EncoderSpeed speed, string encoderOption)
    {
        var path = Path.Combine(Path.GetTempPath(), $"openrend-custom-encoded-{Guid.NewGuid():N}.mp4");
        var template = new RenderTemplate("custom-" + Guid.NewGuid().ToString("N"), "Custom export", crf, 96) { EncoderSpeed = speed };
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, false);
        try
        {
            var project = CreateProject();
            await new ExportService(renderer).ExportAsync(project, path, new ExportOptions { TemplateId = template.Id, CustomTemplate = template });
            // x264 writes its effective options into the stream, so this checks the encoder rather than a command builder.
            var bytes = Encoding.ASCII.GetString(File.ReadAllBytes(path));
            Assert.Contains($"crf={crf}.0", bytes);
            Assert.Contains(encoderOption, bytes);
            using var inspection = JsonDocument.Parse(MediaFixture.Run("ffprobe", "-v", "error", "-count_frames", "-show_streams", "-of", "json", path));
            var video = inspection.RootElement.GetProperty("streams").EnumerateArray().Single(stream => stream.GetProperty("codec_type").GetString() == "video");
            Assert.Equal("30000/1001", video.GetProperty("r_frame_rate").GetString());
            Assert.Equal("12", video.GetProperty("nb_read_frames").GetString());
            Assert.Equal(32, video.GetProperty("width").GetInt32());
            Assert.Equal(24, video.GetProperty("height").GetInt32());
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(256)]
    public async Task CustomAudioBitrateChangesTheEncodedStreamAndKeepsSourceBytes(int bitrate)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"openrend-custom-audio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "noise.wav");
        var output = Path.Combine(directory, "delivery.mp4");
        try
        {
            MediaFixture.Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "anoisesrc=r=48000:d=1:seed=42", "-c:a", "pcm_f32le", source);
            var original = File.ReadAllBytes(source);
            using var media = new MediaService();
            var audio = await media.ProbeAsync(source);
            var project = CreateProject();
            project.Media.Add(audio);
            project.Tracks.Add(new TimelineTrack { Kind = TrackKind.Audio,
                Clips = [new TimelineClip { MediaId = audio.Id, DurationFrames = 30 }] });
            using var renderer = new TimelineRenderService(media, false);
            var template = new RenderTemplate("custom-" + Guid.NewGuid().ToString("N"), "Audio check", 20, bitrate);
            await new ExportService(renderer).ExportAsync(project, output, new ExportOptions { TemplateId = template.Id, CustomTemplate = template });
            using var inspection = JsonDocument.Parse(MediaFixture.Run("ffprobe", "-v", "error", "-show_streams", "-of", "json", output));
            var stream = inspection.RootElement.GetProperty("streams").EnumerateArray().Single(stream => stream.GetProperty("codec_type").GetString() == "audio");
            var actualBitrate = int.Parse(stream.GetProperty("bit_rate").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(actualBitrate, bitrate * 700, bitrate * 1200);
            Assert.Equal("aac", stream.GetProperty("codec_name").GetString());
            Assert.Equal("48000", stream.GetProperty("sample_rate").GetString());
            Assert.Equal(2, stream.GetProperty("channels").GetInt32());
            Assert.Equal(original, File.ReadAllBytes(source));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static ProjectDocument CreateProject()
    {
        var red = new MediaAsset { Path = "openrend://color?hex=#FF0000", DurationSeconds = 1, HasVideo = true };
        return new ProjectDocument { Width = 32, Height = 24, FrameRate = new Rational(30000, 1001), Media = [red],
            Tracks = [new TimelineTrack { Kind = TrackKind.Video, Clips = [new TimelineClip { MediaId = red.Id, DurationFrames = 12 }] }] };
    }
}
