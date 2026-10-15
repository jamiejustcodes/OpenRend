using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenRend.Core;

namespace OpenRend.Project;

public sealed record ImportResult(ProjectDocument Project, IReadOnlyList<string> Warnings);

public static class LegacyProjectImporter
{
    public static async Task<ImportResult> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Project manifest exceeds 64 MiB.");
        try
        {
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return Import(json.RootElement, fullPath);
        }
        catch (JsonException error) { throw new InvalidDataException("Project JSON is invalid.", error); }
        catch (OverflowException error) { throw new InvalidDataException("Project contains an out-of-range value.", error); }
        catch (ArgumentOutOfRangeException error) { throw new InvalidDataException("Project contains an out-of-range value.", error); }
    }

    private static ImportResult Import(JsonElement root, string path)
    {
        RequireFields(root, ["version", "width", "height", "fps_num", "fps_den", "media", "tracks", "clips"], ["master_volume", "master_muted"]);
        if (Integer(root, "version") != 1) throw new InvalidDataException("Unsupported project version.");
        var project = new ProjectDocument
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Width = checked((int)Integer(root, "width", minimum: 1)),
            Height = checked((int)Integer(root, "height", minimum: 1)),
            FrameRate = new Rational(checked((int)Integer(root, "fps_num", minimum: 1)), checked((int)Integer(root, "fps_den", minimum: 1)))
        };
        var warnings = new List<string>();
        var masterGain = Number(root, "master_volume", 1);
        if (masterGain < 0) throw new InvalidDataException("Master gain must be nonnegative.");
        if (Boolean(root, "master_muted", false)) masterGain = 0;
        var mediaMap = new Dictionary<string, MediaAsset>(StringComparer.Ordinal);
        var mediaNode = root.GetProperty("media");
        if (mediaNode.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Media must be an object.");
        foreach (var entry in mediaNode.EnumerateObject())
        {
            var source = entry.Value;
            RequireFields(source, ["id", "path", "duration_frames"], ["width", "height", "has_video", "has_audio"]);
            var id = Text(source, "id");
            if (entry.Name != id || mediaMap.ContainsKey(id)) throw new InvalidDataException("Media keys must match unique IDs.");
            var media = new MediaAsset
            {
                Id = Identifier("media", id),
                Path = Path.GetFullPath(Text(source, "path"), Path.GetDirectoryName(path)!),
                DurationSeconds = project.FrameRate.Seconds(Integer(source, "duration_frames", minimum: 1)),
                Width = checked((int)Integer(source, "width", 0)),
                Height = checked((int)Integer(source, "height", 0)),
                HasVideo = Boolean(source, "has_video", true),
                HasAudio = Boolean(source, "has_audio", false)
            };
            if (!media.HasVideo && !media.HasAudio) throw new InvalidDataException("Media has no audio or video stream.");
            mediaMap.Add(id, media);
            project.Media.Add(media);
            if (!File.Exists(media.Path)) warnings.Add($"Media is missing and must be relinked: {media.Path}");
        }

        var trackMap = new Dictionary<string, TimelineTrack>(StringComparer.Ordinal);
        foreach (var source in Array(root, "tracks"))
        {
            RequireFields(source, ["id", "name"], ["kind", "muted"]);
            var id = Text(source, "id");
            if (trackMap.ContainsKey(id)) throw new InvalidDataException("Track IDs must be unique.");
            var kind = OptionalText(source, "kind", "video") switch
            {
                "video" => TrackKind.Video,
                "audio" => TrackKind.Audio,
                _ => throw new InvalidDataException("Track kind must be video or audio.")
            };
            var track = new TimelineTrack { Id = Identifier("track", id), Name = Text(source, "name"), Kind = kind, IsMuted = Boolean(source, "muted", false) };
            trackMap.Add(id, track);
            project.Tracks.Add(track);
        }
        // The source format composites its final track over earlier tracks.
        project.Tracks.Reverse();

        var companionTracks = new Dictionary<Guid, TimelineTrack>();
        var clipIds = new HashSet<string>(StringComparer.Ordinal);
        var hasGroups = false;
        foreach (var source in Array(root, "clips"))
        {
            RequireFields(source, ["id", "media_id", "track_id", "start", "source_in", "duration"], ["volume", "opacity", "fade_in", "fade_out", "fade_offset", "fade_duration", "group_id", "audio_enabled", "pan"]);
            var id = Text(source, "id");
            if (!clipIds.Add(id)) throw new InvalidDataException("Clip IDs must be unique.");
            if (!mediaMap.TryGetValue(Text(source, "media_id"), out var media)) throw new InvalidDataException("A clip references unknown media.");
            if (!trackMap.TryGetValue(Text(source, "track_id"), out var track)) throw new InvalidDataException("A clip references an unknown track.");
            if (source.TryGetProperty("group_id", out var group) && group.ValueKind != JsonValueKind.Null)
            {
                _ = Text(source, "group_id");
                hasGroups = true;
            }
            var gain = Number(source, "volume", 1);
            if (gain < 0) throw new InvalidDataException("Clip gain must be nonnegative.");
            var clip = new TimelineClip
            {
                Id = Identifier("clip", id), MediaId = media.Id, Name = media.Name,
                StartFrame = Integer(source, "start"), SourceInFrame = Integer(source, "source_in"),
                DurationFrames = Integer(source, "duration", minimum: 1),
                Gain = gain, Opacity = Number(source, "opacity", 1), Pan = Number(source, "pan", 0),
                FadeInFrames = Integer(source, "fade_in", 0), FadeOutFrames = Integer(source, "fade_out", 0),
                FadeOffsetFrames = Integer(source, "fade_offset", 0),
                FadeDurationFrames = source.TryGetProperty("fade_duration", out var fadeDuration) && fadeDuration.ValueKind != JsonValueKind.Null ? Integer(source, "fade_duration", minimum: 1) : null
            };
            var audioEnabled = Boolean(source, "audio_enabled", true);
            if (track.Kind == TrackKind.Audio) clip.Gain *= audioEnabled ? masterGain : 0;
            track.Clips.Add(clip);
            if (track.Kind == TrackKind.Video && audioEnabled && media.HasAudio)
            {
                if (!companionTracks.TryGetValue(track.Id, out var companion))
                {
                    companion = new TimelineTrack { Id = Identifier("embedded-track", track.Id.ToString("N"), preserveGuid: false), Name = track.Name + " audio", Kind = TrackKind.Audio, IsMuted = track.IsMuted };
                    companionTracks.Add(track.Id, companion);
                    project.Tracks.Add(companion);
                }
                var audio = clip.DeepClone();
                audio.Id = Identifier("embedded-clip", id, preserveGuid: false);
                audio.Gain *= masterGain;
                companion.Clips.Add(audio);
            }
        }
        if (hasGroups) warnings.Add("Clip groups were imported as independent clips; linked editing must be recreated.");
        if (companionTracks.Count > 0) warnings.Add("Embedded audio was placed on separate audio tracks; subsequent video and audio edits are independent.");
        foreach (var track in project.Tracks)
        {
            var ordered = track.Clips.OrderBy(c => c.StartFrame).ToArray();
            for (var i = 1; i < ordered.Length; i++)
                if (ordered[i].StartFrame < ordered[i - 1].EndFrame) throw new InvalidDataException("Source project clips overlap on one track.");
        }
        project.Validate();
        return new ImportResult(project, warnings);
    }

    private static Guid Identifier(string category, string value, bool preserveGuid = true)
    {
        if (preserveGuid && Guid.TryParse(value, out var parsed) && parsed != Guid.Empty) return parsed;
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(category + ":" + value)).AsSpan(0, 16));
    }

    private static void RequireFields(JsonElement record, string[] required, string[] optional)
    {
        if (record.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Project records must be objects.");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in record.EnumerateObject())
            if (!found.Add(property.Name) || !required.Contains(property.Name) && !optional.Contains(property.Name)) throw new InvalidDataException($"Unsupported or duplicate project field: {property.Name}");
        foreach (var name in required)
            if (!found.Contains(name)) throw new InvalidDataException($"Required project field is missing: {name}");
    }

    private static JsonElement.ArrayEnumerator Array(JsonElement record, string name)
    {
        var value = record.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"{name} must be an array.");
        return value.EnumerateArray();
    }

    private static string Text(JsonElement record, string name)
    {
        var value = record.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw new InvalidDataException($"{name} must be nonempty text.");
        return value.GetString()!;
    }

    private static string OptionalText(JsonElement record, string name, string fallback) => record.TryGetProperty(name, out _) ? Text(record, name) : fallback;

    private static long Integer(JsonElement record, string name, long? fallback = null, long minimum = 0)
    {
        if (!record.TryGetProperty(name, out var value) && fallback.HasValue) return fallback.Value;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) || number < minimum) throw new InvalidDataException($"{name} must be an integer >= {minimum}.");
        return number;
    }

    private static double Number(JsonElement record, string name, double fallback)
    {
        if (!record.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number)) throw new InvalidDataException($"{name} must be finite.");
        return number;
    }

    private static bool Boolean(JsonElement record, string name, bool fallback)
    {
        if (!record.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException($"{name} must be a boolean.");
        return value.GetBoolean();
    }
}
