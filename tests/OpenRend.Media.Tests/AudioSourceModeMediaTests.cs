using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class AudioSourceModeMediaTests
{
    [Fact]
    public async Task AudioLoopUsesTheSamplePeriodAcrossRenderBlocksAndWaveExport()
    {
        const int period = 2401;
        const int sampleRate = MediaService.SampleRate;
        const int sourceOffset = 1602;
        const int frames = 7000;
        const int block = 977;
        var directory = Path.Combine(Path.GetTempPath(), $"openrend-loop-audio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "tone.wav");
            MediaFixture.Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=f=440:r=48000",
                "-af", $"atrim=end_sample={period}", "-c:a", "pcm_s24le", source);
            using var media = new MediaService();
            var asset = await media.ProbeAsync(source);
            var original = media.GetAudio(asset, 0, period);
            Assert.Contains(original, sample => Math.Abs(sample) > .01f);
            var clip = new TimelineClip { MediaId = asset.Id, SourceInFrame = 1, DurationFrames = 8, AudioSourceMode = AudioSourceMode.Loop };
            var project = new ProjectDocument { Width = 32, Height = 24, FrameRate = new(30000, 1001), Media = [asset],
                Tracks = [new() { Kind = TrackKind.Audio, Clips = [clip] }] };
            using var renderer = new TimelineRenderService(media, false);
            var whole = renderer.RenderAudio(project, 0, frames);
            var first = renderer.RenderAudio(project, 0, block);
            var second = renderer.RenderAudio(project, (double)block / sampleRate, frames - block);
            var output = Path.Combine(directory, "loop.wav");
            await new ExportService(renderer).ExportAsync(project, output, new ExportOptions { TemplateId = "project-wav-24" });
            var encoded = media.GetAudio(output, 0, frames);

            for (var i = 0; i < frames; i++)
            {
                var expected = original[((sourceOffset + i) % period) * 2];
                var divided = i < block ? first[i * 2] : second[(i - block) * 2];
                Assert.InRange(Math.Abs(whole[i * 2] - expected), 0, 1e-6);
                Assert.InRange(Math.Abs(divided - expected), 0, 1e-6);
                Assert.InRange(Math.Abs(encoded[i * 2] - expected), 0, 2e-6);
            }

            clip.AudioSourceMode = AudioSourceMode.PadSilence;
            var padded = renderer.RenderAudio(project, 0, frames);
            Assert.Equal(whole.Take((period - sourceOffset) * 2), padded.Take((period - sourceOffset) * 2));
            Assert.All(padded.Skip((period - sourceOffset) * 2), sample => Assert.Equal(0f, sample));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
