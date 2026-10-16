using System.Text.Json;
using System.Text.Json.Serialization;
using OpenRend.Core;

namespace OpenRend.Project;

public static class ProjectStore
{
    public const int CurrentVersion = 2;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task SaveAsync(ProjectDocument project, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Validate();
        var fullPath = System.IO.Path.GetFullPath(path);
        if (project.Media.Any(media => string.Equals(System.IO.Path.GetFullPath(media.Path), fullPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A project file cannot overwrite source media.");
        var directory = System.IO.Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var saved = project.DeepClone();
        foreach (var media in saved.Media)
            media.Path = System.IO.Path.GetRelativePath(directory, System.IO.Path.GetFullPath(media.Path));
        var manifest = new ProjectManifest { Version = CurrentVersion, SavedUtc = DateTimeOffset.UtcNow, Project = saved };
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullPath)) File.Replace(temporary, fullPath, destinationBackupFileName: null);
            else File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static async Task<ProjectDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        // Readers keep their snapshot handle while a save replaces the directory entry.
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Project manifest exceeds 64 MiB.");
        ProjectManifest? manifest;
        try { manifest = await JsonSerializer.DeserializeAsync<ProjectManifest>(stream, Options, cancellationToken); }
        catch (JsonException error) { throw new InvalidDataException("Project JSON is invalid.", error); }
        catch (ArgumentOutOfRangeException error) { throw new InvalidDataException("Project contains an invalid frame rate.", error); }
        if (manifest == null || manifest.Format != "OpenRend" || manifest.Project == null) throw new InvalidDataException("This is not an OpenRend project.");
        if (manifest.Version is < 1 or > CurrentVersion) throw new InvalidDataException($"Project version {manifest.Version} is not supported. Supported versions: 1–{CurrentVersion}.");
        var project = manifest.Project;
        project.Validate();
        var directory = System.IO.Path.GetDirectoryName(fullPath)!;
        foreach (var media in project.Media) media.Path = System.IO.Path.GetFullPath(media.Path, directory);
        return project;
    }

    public static string GetAutosavePath(string projectPath) => System.IO.Path.GetFullPath(projectPath) + ".autosave";

    public static Task SaveAutosaveAsync(ProjectDocument project, string projectPath, CancellationToken cancellationToken = default) => SaveAsync(project, GetAutosavePath(projectPath), cancellationToken);

    public static Task<ProjectDocument> RecoverAutosaveAsync(string projectPath, CancellationToken cancellationToken = default) => LoadAsync(GetAutosavePath(projectPath), cancellationToken);

    public static bool HasNewerAutosave(string projectPath)
    {
        var autosave = GetAutosavePath(projectPath);
        return File.Exists(autosave) && (!File.Exists(projectPath) || File.GetLastWriteTimeUtc(autosave) > File.GetLastWriteTimeUtc(projectPath));
    }

    public static IReadOnlyList<MediaAsset> FindMissingMedia(ProjectDocument project) => project.Media.Where(m => !File.Exists(m.Path)).ToList();

    public static void RelinkMedia(ProjectDocument project, Guid mediaId, string replacementPath)
    {
        var fullPath = System.IO.Path.GetFullPath(replacementPath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Replacement media was not found.", fullPath);
        var media = project.Media.SingleOrDefault(m => m.Id == mediaId) ?? throw new KeyNotFoundException("Media was not found.");
        media.Path = fullPath;
    }

    private sealed class ProjectManifest
    {
        public string Format { get; set; } = "OpenRend";
        public int Version { get; set; }
        public DateTimeOffset SavedUtc { get; set; }
        public ProjectDocument? Project { get; set; }
    }
}
