using System.Text.Json.Nodes;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class LegacyProjectImporterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "OpenRendImportTests", Guid.NewGuid().ToString("N"));
    private readonly string path;
    public LegacyProjectImporterTests()
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "existing.opentrim");
    }
    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static JsonNode Manifest() => JsonNode.Parse("""
        {
          "version": 1, "width": 1920, "height": 1080, "fps_num": 30000, "fps_den": 1001,
          "master_volume": 0.5, "master_muted": false,
          "media": {
            "media-1": {"id":"media-1","path":"media/source.mp4","duration_frames":300,"width":1920,"height":1080,"has_video":true,"has_audio":true}
          },
          "tracks": [
            {"id":"video-1","name":"Lower video","kind":"video","muted":false},
            {"id":"video-2","name":"Upper video","kind":"video","muted":false},
            {"id":"audio-1","name":"Music","kind":"audio","muted":false}
          ],
          "clips": [
            {"id":"clip-1","media_id":"media-1","track_id":"video-1","start":45,"source_in":30,"duration":60,"volume":0.8,"opacity":0.6,"fade_in":40,"fade_out":20,"fade_offset":30,"fade_duration":120,"pan":-0.4,"audio_enabled":true,"group_id":"group-a"},
            {"id":"clip-2","media_id":"media-1","track_id":"video-2","start":45,"source_in":10,"duration":30,"volume":1,"opacity":0.4,"audio_enabled":false},
            {"id":"clip-3","media_id":"media-1","track_id":"audio-1","start":45,"source_in":50,"duration":30,"volume":0.6,"audio_enabled":true,"pan":0.5}
          ]
        }
        """)!;

    private async Task WriteAsync(JsonNode? manifest = null) => await File.WriteAllTextAsync(path, (manifest ?? Manifest()).ToJsonString());

    [Fact]
    public async Task ImportsIntegerEditDecisionsRationalRateAndSplitFadesWithoutChangingSource()
    {
        await WriteAsync();
        var before = await File.ReadAllBytesAsync(path);
        var result = await LegacyProjectImporter.LoadAsync(path);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        var project = result.Project;
        Assert.Equal(new Rational(30000, 1001), project.FrameRate);
        Assert.Equal(1920, project.Width);
        Assert.Equal(1080, project.Height);
        Assert.Equal(105, project.DurationFrames);
        Assert.Equal(Path.Combine(directory, "media", "source.mp4"), Assert.Single(project.Media).Path);
        Assert.Equal(300, project.FrameRate.Frames(project.Media[0].DurationSeconds));
        var clip = Assert.Single(project.Tracks.Single(t => t.Name == "Lower video").Clips);
        Assert.Equal(45, clip.StartFrame);
        Assert.Equal(30, clip.SourceInFrame);
        Assert.Equal(60, clip.DurationFrames);
        Assert.Equal(30, clip.FadeOffsetFrames);
        Assert.Equal(120, clip.FadeDurationFrames);
        Assert.Equal(0.45, TimelineEvaluator.Evaluate(project, 45).Video[0].Opacity, 12);
        Assert.Contains(result.Warnings, w => w.Contains("groups"));
        Assert.Contains(result.Warnings, w => w.Contains("missing"));
        Assert.Contains(result.Warnings, w => w.Contains("Embedded audio"));
    }

    [Fact]
    public async Task EmbeddedAudioIsExtractedOnceAndMasterGainIsApplied()
    {
        await WriteAsync();
        var result = await LegacyProjectImporter.LoadAsync(path);
        var frame = TimelineEvaluator.Evaluate(result.Project, 55);
        Assert.Equal(2, frame.Audio.Count);
        var embedded = frame.Audio.Single(a => a.Track.Name == "Lower video audio");
        Assert.Equal(40, embedded.SourceFrame);
        Assert.Equal(0.4, embedded.Gain, 12);
        Assert.Equal(-0.4, embedded.Pan);
        var music = frame.Audio.Single(a => a.Track.Name == "Music");
        Assert.Equal(0.3, music.Gain, 12);
        Assert.Equal(0.5, music.Pan);
        Assert.DoesNotContain(result.Project.Tracks, t => t.Name == "Upper video audio");
        var second = await LegacyProjectImporter.LoadAsync(path);
        Assert.Equal(result.Project.Media[0].Id, second.Project.Media[0].Id);
        Assert.Equal(embedded.Clip.Id, TimelineEvaluator.Evaluate(second.Project, 55).Audio.Single(a => a.Track.Name == "Lower video audio").Clip.Id);
    }

    [Fact]
    public async Task VideoStackingAndTrackMuteSurviveConversion()
    {
        var manifest = Manifest();
        manifest["tracks"]![0]!["muted"] = true;
        await WriteAsync(manifest);
        var result = await LegacyProjectImporter.LoadAsync(path);
        var frame = TimelineEvaluator.Evaluate(result.Project, 55);
        Assert.Equal("Upper video", Assert.Single(frame.Video).Track.Name);
        Assert.Equal("Music", Assert.Single(frame.Audio).Track.Name);
        Assert.True(result.Project.Tracks.Single(t => t.Name == "Lower video audio").IsMuted);
        manifest["tracks"]![0]!["muted"] = false;
        await WriteAsync(manifest);
        result = await LegacyProjectImporter.LoadAsync(path);
        Assert.Equal(new[] { "Lower video", "Upper video" }, TimelineEvaluator.Evaluate(result.Project, 55).Video.Select(v => v.Track.Name));
    }

    [Fact]
    public async Task MasterMuteAndDisabledAudioRemainSilentAfterRoundTrip()
    {
        var manifest = Manifest();
        manifest["master_muted"] = true;
        manifest["clips"]![2]!["audio_enabled"] = false;
        await WriteAsync(manifest);
        var imported = await LegacyProjectImporter.LoadAsync(path);
        Assert.All(TimelineEvaluator.Evaluate(imported.Project, 55).Audio, a => Assert.Equal(0, a.Gain));
        var saved = Path.Combine(directory, "converted.orproj");
        await ProjectStore.SaveAsync(imported.Project, saved);
        var loaded = await ProjectStore.LoadAsync(saved);
        Assert.Equal(imported.Project.DurationFrames, loaded.DurationFrames);
        Assert.All(TimelineEvaluator.Evaluate(loaded, 55).Audio, a => Assert.Equal(0, a.Gain));
        Assert.Equal(30, loaded.Tracks.Single(t => t.Name == "Lower video").Clips[0].FadeOffsetFrames);
    }

    [Fact]
    public async Task DisabledAudioOnAudioTrackIsRetainedAsSilentDuration()
    {
        var manifest = Manifest();
        manifest["clips"]![2]!["audio_enabled"] = false;
        manifest["clips"]![2]!["start"] = 180;
        await WriteAsync(manifest);
        var result = await LegacyProjectImporter.LoadAsync(path);
        Assert.Equal(210, result.Project.DurationFrames);
        Assert.Equal(0, Assert.Single(TimelineEvaluator.Evaluate(result.Project, 180).Audio).Gain);
    }

    [Fact]
    public async Task RejectsMalformedSourceReferencesRangesVersionsAndTypes()
    {
        foreach (var modify in new Action<JsonNode>[]
        {
            p => p["version"] = 2,
            p => p["fps_den"] = 0,
            p => p["clips"]![0]!["media_id"] = "unknown",
            p => p["clips"]![0]!["source_in"] = 290,
            p => p["clips"]![0]!["duration"] = -1,
            p => p["clips"]![0]!["fade_offset"] = 100,
            p => p["clips"]![0]!["audio_enabled"] = "false",
            p => p["clips"]![0]!["volume"] = -0.5,
            p => p["clips"]![1]!["id"] = "clip-1",
            p => p["unexpected"] = true
        })
        {
            var manifest = Manifest();
            modify(manifest);
            await WriteAsync(manifest);
            await Assert.ThrowsAsync<InvalidDataException>(() => LegacyProjectImporter.LoadAsync(path));
        }
    }
}
