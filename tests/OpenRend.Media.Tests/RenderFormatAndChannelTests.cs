using System.Text.Json;
using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class RenderFormatAndChannelTests
{
    [Fact]
    public void ApplyChannelRemappingTransformsStereoSamplesCorrectly()
    {
        // 4 stereo frames: (L0, R0), (L1, R1), (L2, R2), (L3, R3)
        var original = new float[] { 0.8f, 0.4f, -0.6f, 0.2f, 1.0f, -0.5f, 0.0f, 0.3f };

        // Test None
        var none = (float[])original.Clone();
        ExportService.ApplyChannelRemapping(none, ChannelRemapping.None);
        Assert.Equal(original, none);

        // Test Mono: (L + R) / 2
        var mono = (float[])original.Clone();
        ExportService.ApplyChannelRemapping(mono, ChannelRemapping.Mono);
        Assert.Equal(0.6f, mono[0], precision: 5);
        Assert.Equal(0.6f, mono[1], precision: 5);
        Assert.Equal(-0.2f, mono[2], precision: 5);
        Assert.Equal(-0.2f, mono[3], precision: 5);
        Assert.Equal(0.25f, mono[4], precision: 5);
        Assert.Equal(0.25f, mono[5], precision: 5);

        // Test Swap
        var swap = (float[])original.Clone();
        ExportService.ApplyChannelRemapping(swap, ChannelRemapping.Swap);
        Assert.Equal(0.4f, swap[0], precision: 5);
        Assert.Equal(0.8f, swap[1], precision: 5);
        Assert.Equal(0.2f, swap[2], precision: 5);
        Assert.Equal(-0.6f, swap[3], precision: 5);

        // Test MuteLeft (Left channel muted, Right preserved)
        var muteLeft = (float[])original.Clone();
        ExportService.ApplyChannelRemapping(muteLeft, ChannelRemapping.MuteLeft);
        Assert.Equal(0.0f, muteLeft[0]);
        Assert.Equal(0.4f, muteLeft[1]);
        Assert.Equal(0.0f, muteLeft[2]);
        Assert.Equal(0.2f, muteLeft[3]);

        // Test MuteRight (Right channel muted, Left preserved)
        var muteRight = (float[])original.Clone();
        ExportService.ApplyChannelRemapping(muteRight, ChannelRemapping.MuteRight);
        Assert.Equal(0.8f, muteRight[0]);
        Assert.Equal(0.0f, muteRight[1]);
        Assert.Equal(-0.6f, muteRight[2]);
        Assert.Equal(0.0f, muteRight[3]);
    }

    [Fact]
    public async Task ExportAudioOnlyWavePcmProducesValidWaveFileWithoutVideoStream()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"openrend-wav-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourceAudio = Path.Combine(directory, "tone.wav");
        var outputWav = Path.Combine(directory, "rendered.wav");

        try
        {
            MediaFixture.Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=f=440:r=48000:d=1", "-c:a", "pcm_s24le", sourceAudio);
            using var media = new MediaService();
            var audioAsset = await media.ProbeAsync(sourceAudio);

            var project = new ProjectDocument
            {
                Width = 32,
                Height = 24,
                FrameRate = new Rational(30, 1),
                Media = [audioAsset],
                Tracks = [new TimelineTrack { Kind = TrackKind.Audio, Clips = [new TimelineClip { MediaId = audioAsset.Id, DurationFrames = 30 }] }]
            };

            using var renderer = new TimelineRenderService(media, false);
            var template = RenderTemplateCatalog.Find("project-wav-24");
            var options = new ExportOptions
            {
                TemplateId = template.Id,
                ChannelMapping = ChannelRemapping.Mono,
                GenerateLoudnessLog = true
            };

            await new ExportService(renderer).ExportAsync(project, outputWav, options);

            Assert.True(File.Exists(outputWav), "WAV file must be created.");
            var logPath = Path.Combine(directory, "rendered.wav.loudness.log");
            Assert.True(File.Exists(logPath), "Loudness log file must be created.");
            var logContent = File.ReadAllText(logPath);
            Assert.Contains("Integrated Loudness:", logContent);
            Assert.Contains("LUFS", logContent);
            Assert.Contains("True Peak:", logContent);

            using var inspection = JsonDocument.Parse(MediaFixture.Run("ffprobe", "-v", "error", "-show_streams", "-of", "json", outputWav));
            var streams = inspection.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            Assert.Single(streams);
            var audio = streams[0];
            Assert.Equal("audio", audio.GetProperty("codec_type").GetString());
            Assert.Equal("pcm_s24le", audio.GetProperty("codec_name").GetString());
            Assert.Equal("48000", audio.GetProperty("sample_rate").GetString());
            Assert.Equal(2, audio.GetProperty("channels").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAudioOnlyMp3ProducesValidMp3WithoutVideoStream()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"openrend-mp3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourceAudio = Path.Combine(directory, "tone.wav");
        var outputMp3 = Path.Combine(directory, "rendered.mp3");

        try
        {
            MediaFixture.Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=f=880:r=48000:d=1", "-c:a", "pcm_s16le", sourceAudio);
            using var media = new MediaService();
            var audioAsset = await media.ProbeAsync(sourceAudio);

            var project = new ProjectDocument
            {
                Width = 32,
                Height = 24,
                FrameRate = new Rational(30, 1),
                Media = [audioAsset],
                Tracks = [new TimelineTrack { Kind = TrackKind.Audio, Clips = [new TimelineClip { MediaId = audioAsset.Id, DurationFrames = 30 }] }]
            };

            using var renderer = new TimelineRenderService(media, false);
            var template = RenderTemplateCatalog.Find("project-mp3-320");
            var options = new ExportOptions { TemplateId = template.Id };

            await new ExportService(renderer).ExportAsync(project, outputMp3, options);

            Assert.True(File.Exists(outputMp3), "MP3 file must be created.");
            using var inspection = JsonDocument.Parse(MediaFixture.Run("ffprobe", "-v", "error", "-show_streams", "-of", "json", outputMp3));
            var streams = inspection.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            Assert.Single(streams);
            var audio = streams[0];
            Assert.Equal("audio", audio.GetProperty("codec_type").GetString());
            Assert.Equal("mp3", audio.GetProperty("codec_name").GetString());
            Assert.Equal("48000", audio.GetProperty("sample_rate").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportStretchToFillScalesVideoToTargetResolution()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"openrend-stretch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "stretch.mp4");

        try
        {
            using var media = new MediaService();
            var red = new MediaAsset { Path = "openrend://color?hex=#00FF00", DurationSeconds = 1, HasVideo = true };
            var project = new ProjectDocument
            {
                Width = 64,
                Height = 48, // 4:3
                FrameRate = new Rational(30, 1),
                Media = [red],
                Tracks = [new TimelineTrack { Kind = TrackKind.Video, Clips = [new TimelineClip { MediaId = red.Id, DurationFrames = 15 }] }]
            };

            using var renderer = new TimelineRenderService(media, false);
            // Custom template with 64x32 (2:1 aspect ratio)
            var template = new RenderTemplate("custom-" + Guid.NewGuid().ToString("N"), "Stretch 2:1", 20, 128)
            {
                Width = 64,
                Height = 32
            };
            var options = new ExportOptions
            {
                TemplateId = template.Id,
                CustomTemplate = template,
                StretchToFill = true,
                GenerateLoudnessLog = true
            };

            await new ExportService(renderer).ExportAsync(project, output, options);

            Assert.True(File.Exists(output), "Stretched MP4 must be created.");
            using var inspection = JsonDocument.Parse(MediaFixture.Run("ffprobe", "-v", "error", "-show_streams", "-of", "json", output));
            var streams = inspection.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var video = streams.Single(s => s.GetProperty("codec_type").GetString() == "video");
            Assert.Equal(64, video.GetProperty("width").GetInt32());
            Assert.Equal(32, video.GetProperty("height").GetInt32());
            Assert.True(File.Exists(output + ".loudness.log"), "Loudness log must be generated.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
