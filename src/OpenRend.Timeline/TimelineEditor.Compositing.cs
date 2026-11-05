using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public void MakeCompositingChild(Guid trackId)
    {
        var idx = Project.Tracks.FindIndex(t => t.Id == trackId);
        if (idx <= 0) return;
        var tr = Project.Tracks[idx];
        if (tr.Kind != TrackKind.Video) return;
        var prevVid = Project.Tracks.Take(idx).LastOrDefault(t => t.Kind == TrackKind.Video);
        if (prevVid == null || tr.CompositeNestingLevel >= prevVid.CompositeNestingLevel + 1) return;

        Execute(p =>
        {
            var index = p.Tracks.FindIndex(t => t.Id == trackId);
            if (index <= 0) return;
            var track = p.Tracks[index];
            if (track.Kind != TrackKind.Video) return;

            var prevVideo = p.Tracks.Take(index).LastOrDefault(t => t.Kind == TrackKind.Video);
            if (prevVideo == null) return;

            var maxAllowed = prevVideo.CompositeNestingLevel + 1;
            if (track.CompositeNestingLevel < maxAllowed)
            {
                track.CompositeNestingLevel++;
            }
        });
    }

    public void MakeCompositingParent(Guid trackId)
    {
        var idx = Project.Tracks.FindIndex(t => t.Id == trackId);
        if (idx < 0) return;
        var tr = Project.Tracks[idx];
        if (tr.Kind != TrackKind.Video || tr.CompositeNestingLevel <= 0) return;

        Execute(p =>
        {
            var index = p.Tracks.FindIndex(t => t.Id == trackId);
            if (index < 0) return;
            var track = p.Tracks[index];
            if (track.Kind != TrackKind.Video || track.CompositeNestingLevel <= 0) return;

            track.CompositeNestingLevel--;

            // Cascade clamp subsequent video tracks to preserve nesting continuity
            var currentLevel = track.CompositeNestingLevel;
            for (var i = index + 1; i < p.Tracks.Count; i++)
            {
                var subsequent = p.Tracks[i];
                if (subsequent.Kind != TrackKind.Video) continue;
                if (subsequent.CompositeNestingLevel > currentLevel + 1)
                {
                    subsequent.CompositeNestingLevel = currentLevel + 1;
                }
                currentLevel = subsequent.CompositeNestingLevel;
            }
        });
    }

    public void SetTrackAdjustment(Guid trackId, bool isAdjustment)
    {
        var existing = Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (existing == null || existing.Kind != TrackKind.Video || existing.IsAdjustmentTrack == isAdjustment) return;

        Execute(p =>
        {
            var track = p.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track == null || track.Kind != TrackKind.Video) return;
            track.IsAdjustmentTrack = isAdjustment;
        });
    }

    public void ToggleTrackAdjustment(Guid trackId)
    {
        var track = Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track == null || track.Kind != TrackKind.Video) return;
        SetTrackAdjustment(trackId, !track.IsAdjustmentTrack);
    }

    public bool IsCompositingParent(Guid trackId)
    {
        var index = Project.Tracks.FindIndex(t => t.Id == trackId);
        if (index < 0) return false;
        var track = Project.Tracks[index];
        if (track.Kind != TrackKind.Video) return false;

        for (var i = index + 1; i < Project.Tracks.Count; i++)
        {
            var next = Project.Tracks[i];
            if (next.Kind == TrackKind.Video)
            {
                return next.CompositeNestingLevel > track.CompositeNestingLevel;
            }
        }
        return false;
    }

    public TimelineTrack AddAdjustmentTrack(int? index = null)
    {
        var count = Project.Tracks.Count(t => t.IsAdjustmentTrack) + 1;
        var track = new TimelineTrack
        {
            Kind = TrackKind.Video,
            IsAdjustmentTrack = true,
            Name = $"Adjustment Track {count}"
        };
        Execute(p =>
        {
            if (index.HasValue && index.Value >= 0 && index.Value <= p.Tracks.Count)
                p.Tracks.Insert(index.Value, track);
            else
                p.Tracks.Insert(0, track);
        });
        return track;
    }

    public static MediaAsset EnsureAdjustmentMediaAsset(ProjectDocument project)
    {
        var asset = project.Media.FirstOrDefault(m => m.Path.Equals("openrend://adjustment", StringComparison.OrdinalIgnoreCase));
        if (asset == null)
        {
            asset = new MediaAsset
            {
                Id = Guid.NewGuid(),
                Path = "openrend://adjustment",
                DisplayName = "Adjustment Event",
                HasVideo = true,
                HasAudio = false,
                DurationSeconds = 86400, // 24 hours
                Width = Math.Max(1, project.Width),
                Height = Math.Max(1, project.Height)
            };
            project.Media.Add(asset);
        }
        return asset;
    }

    public TimelineClip InsertAdjustmentEvent(Guid trackId, long startFrame, long durationFrames)
    {
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (track.Kind != TrackKind.Video)
            throw new InvalidOperationException("Adjustment events can only be inserted on video tracks.");
        if (durationFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationFrames), "Duration must be positive.");

        var asset = EnsureAdjustmentMediaAsset(Project);
        var clip = new TimelineClip
        {
            MediaId = asset.Id,
            Name = "Adjustment Event",
            StartFrame = Math.Max(0, startFrame),
            DurationFrames = durationFrames,
            IsAdjustmentEvent = true,
            FrameWidth = Project.Width,
            FrameHeight = Project.Height,
            PixelAspectRatio = Project.PixelAspectRatio
        };

        Execute(p =>
        {
            EnsureAdjustmentMediaAsset(p);
            var t = p.Tracks.First(tr => tr.Id == trackId);
            t.Clips.Add(clip);
        });

        return clip;
    }

    public void ResizeAdjustmentEventToProject(Guid clipId)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (!clip.IsAdjustmentEvent)
            throw new InvalidOperationException("Clip is not an adjustment event.");

        Execute(p =>
        {
            var target = p.Tracks.SelectMany(t => t.Clips).First(c => c.Id == clipId);
            target.FrameWidth = p.Width;
            target.FrameHeight = p.Height;
            target.PixelAspectRatio = p.PixelAspectRatio;
            if (target.Crop != null)
            {
                target.Crop = new CropAnimation
                {
                    Keyframes = [new CropKeyframe { Frame = 0, State = CropState.Identity(p.Width, p.Height) }]
                };
            }
        });
    }

    public int ResizeSelectedAdjustmentEventsToProject(IEnumerable<Guid> clipIds)
    {
        var ids = clipIds.ToHashSet();
        var targets = Project.Tracks.Where(t => !t.IsLocked)
            .SelectMany(t => t.Clips)
            .Where(c => ids.Contains(c.Id) && c.IsAdjustmentEvent && !c.IsLocked)
            .ToList();

        if (targets.Count == 0) return 0;

        Execute(p =>
        {
            foreach (var t in p.Tracks.Where(tr => !tr.IsLocked).SelectMany(tr => tr.Clips))
            {
                if (ids.Contains(t.Id) && t.IsAdjustmentEvent && !t.IsLocked)
                {
                    t.FrameWidth = p.Width;
                    t.FrameHeight = p.Height;
                    t.PixelAspectRatio = p.PixelAspectRatio;
                    if (t.Crop != null)
                    {
                        t.Crop = new CropAnimation
                        {
                            Keyframes = [new CropKeyframe { Frame = 0, State = CropState.Identity(p.Width, p.Height) }]
                        };
                    }
                }
            }
        });

        return targets.Count;
    }

    public int ResizeAllAdjustmentEventsToProject()
    {
        var targets = Project.Tracks.Where(t => !t.IsLocked)
            .SelectMany(t => t.Clips)
            .Where(c => c.IsAdjustmentEvent && !c.IsLocked)
            .ToList();

        if (targets.Count == 0) return 0;

        Execute(p =>
        {
            foreach (var t in p.Tracks.Where(tr => !tr.IsLocked).SelectMany(tr => tr.Clips))
            {
                if (t.IsAdjustmentEvent && !t.IsLocked)
                {
                    t.FrameWidth = p.Width;
                    t.FrameHeight = p.Height;
                    t.PixelAspectRatio = p.PixelAspectRatio;
                    if (t.Crop != null)
                    {
                        t.Crop = new CropAnimation
                        {
                            Keyframes = [new CropKeyframe { Frame = 0, State = CropState.Identity(p.Width, p.Height) }]
                        };
                    }
                }
            }
        });

        return targets.Count;
    }
}
