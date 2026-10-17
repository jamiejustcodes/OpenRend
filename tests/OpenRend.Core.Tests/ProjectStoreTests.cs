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
    public async Task RoundTripPreservesDissolveSettingsAndIntegerFadeFrames()
    {
        var project = Create();
        var editor = new TimelineEditor(project);
        var track = project.Tracks[0];
        var outgoing = track.Clips[0];
        var incoming = editor.PlaceClip(project.Media[0].Id, track.Id, 55, 30);
        editor.ApplyDissolve(outgoing.Id, incoming.Id);

        var path = System.IO.Path.Combine(directory, "dissolve.orproj");
        await ProjectStore.SaveAsync(project, path);
        Assert.Equal(ProjectStore.CurrentVersion, (int)JsonNode.Parse(await File.ReadAllTextAsync(path))!["Version"]!);
        var loaded = await ProjectStore.LoadAsync(path);

        Assert.Equal(15, loaded.Tracks[0].Clips[0].FadeOutFrames);
        Assert.Equal(15, loaded.Tracks[0].Clips[1].FadeInFrames);
        Assert.Null(loaded.Tracks[0].Clips[0].FadeOutTransition);
        Assert.Equal("Default", loaded.Tracks[0].Clips[1].FadeInTransition!.Preset);
    }

    [Fact]
    public async Task EarlierProjectsLoadAndNewSavesDeclareTheCurrentVersion()
    {
        var path = System.IO.Path.Combine(directory, "versioned.openrend");
        var project = Create();
        await ProjectStore.SaveAsync(project, path);
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Equal(ProjectStore.CurrentVersion, (int)manifest["Version"]!);
        manifest["Version"] = 1;
        await File.WriteAllTextAsync(path, manifest.ToJsonString());
        var loaded = await ProjectStore.LoadAsync(path);
        Assert.Null(loaded.Tracks[0].Clips[0].Crop);
        var editor = new TimelineEditor(loaded);
        editor.SetCropKeyframe(loaded.Tracks[0].Clips[0].Id, 0, CropState.Identity(320, 180) with { RotationDegrees = 45 });
        await ProjectStore.SaveAsync(loaded, path);
        Assert.Equal(ProjectStore.CurrentVersion, (int)JsonNode.Parse(await File.ReadAllTextAsync(path))!["Version"]!);
        Assert.Equal(45, (await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].Crop!.Keyframes[0].State.RotationDegrees);
        manifest["Version"] = 2;
        await File.WriteAllTextAsync(path, manifest.ToJsonString());
        Assert.Empty((await ProjectStore.LoadAsync(path)).Tracks[0].Clips[0].VideoEffects);
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

    [Fact]
    public async Task ConcurrentReadersKeepCompleteSnapshotsWhileSavesReplaceTheProject()
    {
        var path = System.IO.Path.Combine(directory, "concurrent.openrend");
        var original = Create();
        original.Name = "Original snapshot";
        for (var i = 0; i < 30000; i++)
            original.Media.Add(new MediaAsset
            {
                Path = System.IO.Path.Combine(directory, new string('m', 180) + i + ".mp4"),
                DurationSeconds = 10, HasVideo = true
            });
        await ProjectStore.SaveAsync(original, path);
        var readers = Enumerable.Range(0, 3).Select(_ => ProjectStore.LoadAsync(path)).ToArray();
        Assert.Contains(readers, read => !read.IsCompleted);
        ProjectDocument? latest = null;
        try
        {
            for (var revision = 1; revision <= 6; revision++)
            {
                latest = Create();
                latest.Name = $"Revision {revision}";
                latest.Tracks[0].Clips[0].StartFrame = 10 + revision;
                latest.Tracks[0].Gain = 1 + revision * .1;
                await ProjectStore.SaveAsync(latest, path);
            }
            foreach (var snapshot in await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(15)))
            {
                Assert.Equal("Original snapshot", snapshot.Name);
                Assert.Equal(30001, snapshot.Media.Count);
                Assert.Equal(original.Media[0].Id, snapshot.Tracks[0].Clips[0].MediaId);
                Assert.Equal(10, snapshot.Tracks[0].Clips[0].StartFrame);
                snapshot.Validate();
            }
            var current = await ProjectStore.LoadAsync(path);
            Assert.Equal("Revision 6", current.Name);
            Assert.Single(current.Media);
            Assert.Equal(latest!.Media[0].Id, current.Tracks[0].Clips[0].MediaId);
            Assert.Equal(16, current.Tracks[0].Clips[0].StartFrame);
            Assert.Equal(1.6, current.Tracks[0].Gain);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(15)); }
    }

    [WindowsOnlyFact]
    public async Task DeniedReplacementPreservesTheOriginalProjectAndRemovesItsTemporaryFile()
    {
        var path = System.IO.Path.Combine(directory, "blocked.openrend");
        var original = Create();
        await ProjectStore.SaveAsync(original, path);
        var before = await File.ReadAllBytesAsync(path);
        var edited = original.DeepClone();
        edited.Name = "Must not replace the blocked snapshot";
        await using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = await Record.ExceptionAsync(() => ProjectStore.SaveAsync(edited, path));
            Assert.True(failure is IOException or UnauthorizedAccessException, "An external reader denying replacement must cause a file-access error.");
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        var loaded = await ProjectStore.LoadAsync(path);
        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.Tracks[0].Clips[0].Id, loaded.Tracks[0].Clips[0].Id);
    }

    [Fact]
    public async Task SaveCannotOverwriteSourceMediaThroughNormalizedOrCaseChangedPaths()
    {
        var source = System.IO.Path.Combine(directory, "original.mp4");
        byte[] original = [0, 1, 2, 127, 255];
        await File.WriteAllBytesAsync(source, original);
        var project = Create();
        project.Media[0].Path = System.IO.Path.Combine(directory, "ORIGINAL.MP4");
        var destination = System.IO.Path.Combine(directory, "uncreated", "..", "original.mp4");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProjectStore.SaveAsync(project, destination));
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        Assert.False(Directory.Exists(System.IO.Path.Combine(directory, "uncreated")));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    private sealed class WindowsOnlyFactAttribute : FactAttribute
    {
        public WindowsOnlyFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Requires Windows file-sharing semantics.";
        }
    }
}
