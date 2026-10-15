using System.Text.Json.Nodes;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class ProjectStoreTests : IDisposable
{
    private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N"));
    public ProjectStoreTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);

    private ProjectDocument Create()
    {
        var editor = new TimelineEditor(new ProjectDocument { Name = "Round trip", FrameRate = new Rational(30000, 1001) });
        var media = new MediaAsset { Path = System.IO.Path.Combine(directory, "source.mp4"), DurationSeconds = 3, HasVideo = true };
        editor.AddMedia(media);
        var track = editor.AddTrack(TrackKind.Video);
        editor.PlaceClip(media.Id, track.Id, 10, 60);
        return editor.Project;
    }

    [Fact]
    public async Task RoundTripPreservesTimelineAndUsesRelativePaths()
    {
        var project = Create();
        var path = System.IO.Path.Combine(directory, "edit.orproj");
        await ProjectStore.SaveAsync(project, path);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Equal("source.mp4", (string?)json["Project"]!["Media"]![0]!["Path"]);
        var loaded = await ProjectStore.LoadAsync(path);
        Assert.Equal(project.FrameRate, loaded.FrameRate);
        Assert.Equal(project.DurationFrames, loaded.DurationFrames);
        Assert.Equal(project.Media[0].Path, loaded.Media[0].Path);
        Assert.Equal(project.Tracks[0].Clips[0].Id, loaded.Tracks[0].Clips[0].Id);
    }

    [Fact]
    public async Task CancelledSavePreservesExistingProjectAndCleansTemporaryFiles()
    {
        var path = System.IO.Path.Combine(directory, "edit.orproj");
        await ProjectStore.SaveAsync(Create(), path);
        var original = await File.ReadAllTextAsync(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProjectStore.SaveAsync(Create(), path, cancellation.Token));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task UnsupportedVersionsAndInvalidManifestsAreRejected()
    {
        var path = System.IO.Path.Combine(directory, "edit.orproj");
        await ProjectStore.SaveAsync(Create(), path);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["Version"] = 999;
        await File.WriteAllTextAsync(path, json.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectStore.LoadAsync(path));
        await File.WriteAllTextAsync(path, "{}");
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectStore.LoadAsync(path));
    }

    [Fact]
    public async Task RecoveryAndRelinkingKeepMediaReferences()
    {
        var project = Create();
        var path = System.IO.Path.Combine(directory, "edit.orproj");
        await ProjectStore.SaveAutosaveAsync(project, path);
        Assert.True(ProjectStore.HasNewerAutosave(path));
        var recovered = await ProjectStore.RecoverAutosaveAsync(path);
        Assert.Single(ProjectStore.FindMissingMedia(recovered));
        var replacement = System.IO.Path.Combine(directory, "replacement.mp4");
        await File.WriteAllBytesAsync(replacement, [1, 2, 3]);
        var id = recovered.Media[0].Id;
        ProjectStore.RelinkMedia(recovered, id, replacement);
        Assert.Empty(ProjectStore.FindMissingMedia(recovered));
        Assert.Equal(id, recovered.Tracks[0].Clips[0].MediaId);
    }
}
