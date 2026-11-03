using System;
using System.Collections.Generic;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    /// <summary>
    /// Builds an uncommitted procedural generator MediaAsset matching Vegas Pro specifications.
    /// </summary>
    public MediaAsset BuildGeneratedMedia(MediaGeneratorKind kind, string presetName, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var preset = MediaGeneratorCatalog.Presets.FirstOrDefault(p => p.Kind == kind && string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase))
            ?? MediaGeneratorCatalog.Presets.FirstOrDefault(p => p.Kind == kind)
            ?? new MediaGeneratorPreset(kind, presetName, "Media Generator", new Dictionary<string, string>());

        var paramDict = new Dictionary<string, string>(preset.Parameters);
        if (parameters != null)
        {
            foreach (var (k, v) in parameters) paramDict[k] = v;
        }

        var uri = MediaGeneratorCatalog.CreateUri(kind, preset.Name, paramDict);
        var media = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = uri,
            DurationSeconds = 3600.0, // 1 hour virtual duration for procedural generators
            Width = Project.Width,
            Height = Project.Height,
            FrameRate = Project.FrameRate.Value,
            HasVideo = true,
            HasAudio = false,
            IsGenerated = true,
            Generator = new MediaGeneratorData
            {
                Kind = kind,
                PresetName = preset.Name,
                Title = kind switch
                {
                    MediaGeneratorKind.TestPattern => "Test Pattern",
                    MediaGeneratorKind.SolidColor => "Solid Color",
                    MediaGeneratorKind.Checkerboard => "Checkerboard",
                    MediaGeneratorKind.ColorGradient => "Color Gradient",
                    MediaGeneratorKind.NoiseTexture => "Noise Texture",
                    MediaGeneratorKind.CreditRoll => "Credit Roll",
                    MediaGeneratorKind.TitlesAndText => "Titles & Text",
                    _ => "Generator"
                },
                Parameters = paramDict
            }
        };

        return media;
    }

    /// <summary>
    /// Creates and registers a new generated media asset in Project Media.
    /// </summary>
    public MediaAsset CreateGeneratedMedia(MediaGeneratorKind kind, string presetName, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var media = BuildGeneratedMedia(kind, presetName, parameters);
        AddMedia(media);
        return media;
    }

    /// <summary>
    /// Atomically creates a generator media asset and places it as an event clip on a video track.
    /// Undo/redo removes or restores both asset and event in a single atomic transaction.
    /// </summary>
    public TimelineClip AddGeneratedMediaClip(Guid trackId, long startFrame, MediaGeneratorKind kind, string presetName, long durationFrames = 150, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (track.Kind != TrackKind.Video) throw new InvalidOperationException("Generated media clips can only be placed on video tracks.");

        var media = BuildGeneratedMedia(kind, presetName, parameters);
        var duration = durationFrames > 0 ? durationFrames : Project.FrameRate.Frames(5.0);
        var clip = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaId = media.Id,
            Name = $"{media.Generator?.Title ?? "Generator"} ({presetName})",
            StartFrame = startFrame,
            DurationFrames = duration
        };

        Execute(p =>
        {
            p.Media.Add(media.DeepClone());
            var targetTrack = p.Tracks.First(t => t.Id == trackId);
            targetTrack.Clips.Add(clip.DeepClone());
        });

        return clip;
    }
}
