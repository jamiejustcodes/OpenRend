using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed class TimelineEditor
{
    private readonly Stack<ProjectDocument> undo = new();
    private readonly Stack<ProjectDocument> redo = new();
    private bool executing;
    public ProjectDocument Project { get; }
    public bool CanUndo => undo.Count != 0;
    public bool CanRedo => redo.Count != 0;
    public event EventHandler? Changed;

    public TimelineEditor(ProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Validate();
        Project = project;
    }

    public void Execute(Action<ProjectDocument> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (executing) throw new InvalidOperationException("Nested timeline transactions are not supported.");
        var before = Project.DeepClone();
        executing = true;
        try
        {
            mutation(Project);
            SynchronizeDissolveOverlaps(Project, before);
            Project.Validate();
            undo.Push(before);
            redo.Clear();
        }
        catch { Restore(before); throw; }
        finally { executing = false; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Execute(string description, Action<ProjectDocument> mutation) => Execute(mutation);

    public bool Undo()
    {
        if (executing) throw new InvalidOperationException("Cannot undo inside a timeline transaction.");
        if (!undo.TryPop(out var previous)) return false;
        redo.Push(Project.DeepClone());
        Restore(previous);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (executing) throw new InvalidOperationException("Cannot redo inside a timeline transaction.");
        if (!redo.TryPop(out var next)) return false;
        undo.Push(Project.DeepClone());
        Restore(next);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public TimelineTrack AddTrack(TrackKind kind)
    {
        var track = new TimelineTrack { Kind = kind, Name = $"{kind} {Project.Tracks.Count(t => t.Kind == kind) + 1}" };
        Execute(p => p.Tracks.Add(track));
        return track;
    }

    public TimelineTrack InsertTrack(int index, TrackKind kind)
    {
        var track = new TimelineTrack { Kind = kind, Name = $"{kind} {Project.Tracks.Count(t => t.Kind == kind) + 1}" };
        Execute(p => p.Tracks.Insert(Math.Clamp(index, 0, p.Tracks.Count), track));
        return track;
    }

    public TimelineTrack DuplicateTrack(Guid trackId)
    {
        var sourceIndex = Project.Tracks.FindIndex(t => t.Id == trackId);
        if (sourceIndex < 0) throw new KeyNotFoundException("Track was not found.");
        var source = Project.Tracks[sourceIndex];
        var clone = source.DeepClone();
        clone.Id = Guid.NewGuid();
        clone.Name = $"{source.Name} (Copy)";
        foreach (var c in clone.Clips)
        {
            c.Id = Guid.NewGuid();
        }
        Execute(p => p.Tracks.Insert(sourceIndex + 1, clone));
        return clone;
    }

    public void DeleteTrack(Guid trackId)
    {
        Execute(p =>
        {
            var idx = p.Tracks.FindIndex(t => t.Id == trackId);
            if (idx >= 0) p.Tracks.RemoveAt(idx);
        });
    }

    public void AddMedia(MediaAsset media)
    {
        ArgumentNullException.ThrowIfNull(media);
        Execute(p => p.Media.Add(media.DeepClone()));
    }

    public TimelineClip PlaceClip(Guid mediaId, Guid trackId, long startFrame, long? durationFrames = null)
    {
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        var media = Project.Media.FirstOrDefault(m => m.Id == mediaId) ?? throw new KeyNotFoundException("Media was not found.");
        EnsureCompatible(media, track);
        var duration = durationFrames ?? Project.FrameRate.Frames(media.DurationSeconds);
        var clip = new TimelineClip { MediaId = mediaId, Name = media.Name, StartFrame = startFrame, DurationFrames = duration };
        EnsureSourceBounds(clip, media);
        Execute(_ => track.Clips.Add(clip));
        return clip;
    }

    public void ApplyDissolve(Guid outgoingClipId, Guid incomingClipId)
    {
        if (outgoingClipId == incomingClipId) throw new ArgumentException("Choose two different video events.");
        var (outgoingTrack, outgoing) = FindClip(outgoingClipId);
        var (incomingTrack, incoming) = FindClip(incomingClipId);
        if (outgoingTrack.Id != incomingTrack.Id || outgoingTrack.Kind != TrackKind.Video)
            throw new InvalidOperationException("A dissolve requires overlapping video events on the same track.");
        EnsureUnlocked(outgoingTrack, outgoing);
        EnsureUnlocked(incomingTrack, incoming);
        if (outgoing.StartFrame >= incoming.StartFrame || outgoing.EndFrame <= incoming.StartFrame || outgoing.EndFrame > incoming.EndFrame)
            throw new InvalidOperationException("The incoming event must overlap the end of the outgoing event.");
        var overlap = outgoing.EndFrame - incoming.StartFrame;
        if (outgoing.FadeInFrames > outgoing.DurationFrames - overlap || incoming.FadeOutFrames > incoming.DurationFrames - overlap)
            throw new InvalidOperationException("The overlap conflicts with an existing fade.");
        if (outgoingTrack.Clips.Any(clip => clip.Id != outgoingClipId && clip.Id != incomingClipId &&
            clip.StartFrame < outgoing.EndFrame && clip.EndFrame > incoming.StartFrame))
            throw new InvalidOperationException("A dissolve requires exactly two events in the overlap.");
        Execute(_ =>
        {
            // The two fade lengths follow the integer-frame overlap, including after save and undo.
            outgoing.FadeOutFrames = overlap;
            incoming.FadeInFrames = overlap;
            incoming.FadeInTransition = new VideoTransition();
            outgoing.FadeOffsetFrames = incoming.FadeOffsetFrames = 0;
            outgoing.FadeDurationFrames = incoming.FadeDurationFrames = null;
        });
    }

    public void RemoveVideoTransition(Guid clipId, bool fadeOut = false)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (track.Kind != TrackKind.Video) throw new InvalidOperationException("Transitions are only available for video clips.");
        if ((fadeOut ? clip.FadeOutTransition : clip.FadeInTransition) is null) return;
        Execute(_ =>
        {
            // Removing the plug-in leaves the event's fade and overlap edit decisions intact.
            if (fadeOut) clip.FadeOutTransition = null;
            else clip.FadeInTransition = null;
        });
    }

    public void SetVideoFadeCurve(Guid clipId, VideoFadeCurve curve, bool fadeOut = false)
        => SetVideoFadeCurve(clipId, curve, null, fadeOut);

    public void SetVideoFadeCurve(Guid clipId, VideoFadeCurve curve, VideoFadeCurve? reciprocalCurve, bool fadeOut = false)
    {
        if (!Enum.IsDefined(curve)) throw new ArgumentOutOfRangeException(nameof(curve));
        if (reciprocalCurve.HasValue && !Enum.IsDefined(reciprocalCurve.Value)) throw new ArgumentOutOfRangeException(nameof(reciprocalCurve));
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (track.Kind != TrackKind.Video) throw new InvalidOperationException("Fade curves are only available for video events.");
        var recip = reciprocalCurve ?? curve;
        if (fadeOut ? (clip.FadeOutCurve == curve && clip.FadeOutReciprocalCurve == recip)
                    : (clip.FadeInCurve == curve && clip.FadeInReciprocalCurve == recip && clip.FadeInTransition is null)) return;
        Execute(_ =>
        {
            var targetClips = clip.FadeDurationFrames is null
                ? [clip]
                : track.Clips.Where(c => c.MediaId == clip.MediaId && c.FadeDurationFrames == clip.FadeDurationFrames &&
                                         c.StartFrame - c.FadeOffsetFrames == clip.StartFrame - clip.FadeOffsetFrames).ToArray();
            foreach (var target in targetClips)
            {
                if (fadeOut)
                {
                    target.FadeOutCurve = curve;
                    target.FadeOutReciprocalCurve = recip;
                    target.FadeOutTransition = null;
                }
                else
                {
                    target.FadeInCurve = curve;
                    target.FadeInReciprocalCurve = recip;
                    target.FadeInTransition = null;
                }
            }
        });
    }

    public void SetVideoSourceMode(Guid clipId, VideoSourceMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (track.Kind != TrackKind.Video) throw new InvalidOperationException("Source playback modes are only available for video events.");
        if (clip.VideoSourceMode == mode) return;
        var changed = clip.DeepClone();
        changed.VideoSourceMode = mode;
        var media = Project.Media.Single(media => media.Id == clip.MediaId);
        if (clip.VideoSourceMode == VideoSourceMode.Loop && mode != VideoSourceMode.Loop)
        {
            var seconds = VideoSourceTime.Seconds(Project.FrameRate, clip.SourceInFrame, media.DurationSeconds, VideoSourceMode.Loop);
            var frame = Project.FrameRate.Frames(seconds);
            if (Math.Abs(Project.FrameRate.Seconds(frame) - seconds) > 1e-9)
                throw new InvalidOperationException("This loop phase requires a fractional project-frame source offset. Changing its source mode is not supported.");
            // Removing whole source cycles must not restart event-relative crop animation.
            changed.SourceInFrame = frame;
        }
        EnsureSourceBounds(changed, media);
        Execute(_ => { clip.VideoSourceMode = mode; clip.SourceInFrame = changed.SourceInFrame; });
    }

    public int ApplyDissolveToSelection(IEnumerable<Guid> selectedClipIds)
    {
        var ids = ExpandSelection(selectedClipIds, ignoreGroups: true);
        var selected = Project.Tracks.Where(track => track.Kind == TrackKind.Video)
            .SelectMany(track => track.Clips.Where(clip => ids.Contains(clip.Id)).Select(clip => (Track: track, Clip: clip)))
            .OrderBy(item => item.Clip.StartFrame).ToArray();
        if (selected.Length == 2)
        {
            var outgoing = selected[0];
            var incoming = selected[1];
            if (outgoing.Track != incoming.Track || outgoing.Clip.EndFrame <= incoming.Clip.StartFrame) return 0;
            ApplyDissolve(outgoing.Clip.Id, incoming.Clip.Id);
            return 1;
        }
        return ApplyDissolvesToAdjacent(selected.Select(item => item.Clip.Id));
    }

    public int ApplyDissolveToSelection(IEnumerable<Guid> selectedClipIds, long automaticOverlapFrames, bool ripple)
    {
        if (automaticOverlapFrames <= 0) throw new ArgumentOutOfRangeException(nameof(automaticOverlapFrames));
        var ids = ExpandSelection(selectedClipIds, ignoreGroups: true);
        var selected = Project.Tracks.Where(track => track.Kind == TrackKind.Video)
            .SelectMany(track => track.Clips.Where(clip => ids.Contains(clip.Id)).Select(clip => (Track: track, Clip: clip))).ToArray();
        if (selected.Length != 1) return ApplyDissolveToSelection(ids);
        var (track, incoming) = selected[0];
        if (incoming.FadeInFrames != 0 || incoming.FadeOutFrames != 0 || track.Clips.Any(clip =>
            clip.Id != incoming.Id && clip.StartFrame < incoming.EndFrame && clip.EndFrame > incoming.StartFrame))
            return ApplyDissolvesToAdjacent(ids);
        var outgoing = track.Clips.Where(clip => clip.Id != incoming.Id && clip.EndFrame <= incoming.StartFrame)
            .OrderByDescending(clip => clip.EndFrame).ThenByDescending(clip => clip.StartFrame).FirstOrDefault();
        // A displacement that only closes the gap cannot create a dissolve.
        if (outgoing is null || incoming.StartFrame - outgoing.EndFrame >= automaticOverlapFrames) return 0;
        EnsureUnlocked(track);
        var start = Math.Max(0, incoming.StartFrame - automaticOverlapFrames);
        if (start >= outgoing.EndFrame) return 0;
        var delta = start - incoming.StartFrame;
        var changes = track.Clips.Where(clip => clip.Id == incoming.Id || ripple && clip.StartFrame >= incoming.StartFrame)
            .ToDictionary(clip => clip.Id, clip => clip.DeepClone());
        foreach (var change in changes.Values)
        {
            change.StartFrame = checked(change.StartFrame + delta);
            EnsureTimelineBounds(change);
        }
        var moved = changes[incoming.Id];
        if (!ripple)
        {
            // Adjusting take edges keeps existing source content and crop keys at their timeline times.
            moved.SourceInFrame = checked(moved.SourceInFrame + delta);
            if (moved.VideoSourceMode == VideoSourceMode.HoldLastFrame) moved.SourceInFrame = Math.Max(0, moved.SourceInFrame);
            if (moved.Crop is not null) moved.CropOffsetFrames = checked(moved.CropOffsetFrames + delta);
            // Legacy bounded events still reject unavailable source handles atomically.
            EnsureSourceBounds(moved, Project.Media.Single(media => media.Id == moved.MediaId));
        }
        var overlap = outgoing.EndFrame - moved.StartFrame;
        if (outgoing.StartFrame >= moved.StartFrame || outgoing.EndFrame > moved.EndFrame ||
            outgoing.FadeInFrames > outgoing.DurationFrames - overlap)
            throw new InvalidOperationException("The automatic overlap conflicts with an existing fade or event boundary.");
        if (track.Clips.Any(clip => clip.Id != incoming.Id && clip.Id != outgoing.Id &&
            (changes.GetValueOrDefault(clip.Id) ?? clip).StartFrame < outgoing.EndFrame &&
            (changes.GetValueOrDefault(clip.Id) ?? clip).EndFrame > moved.StartFrame))
            throw new InvalidOperationException("A dissolve requires exactly two events in the overlap.");
        Execute(_ =>
        {
            foreach (var clip in track.Clips.Where(clip => changes.ContainsKey(clip.Id)))
            {
                var change = changes[clip.Id];
                clip.StartFrame = change.StartFrame;
                clip.SourceInFrame = change.SourceInFrame;
                clip.CropOffsetFrames = change.CropOffsetFrames;
            }
            outgoing.FadeOutFrames = incoming.FadeInFrames = overlap;
            incoming.FadeInTransition = new VideoTransition();
            outgoing.FadeOffsetFrames = incoming.FadeOffsetFrames = 0;
            outgoing.FadeDurationFrames = incoming.FadeDurationFrames = null;
        });
        return 1;
    }

    public void ConvertCutToDissolve(Guid outgoingClipId, Guid incomingClipId, long overlapFrames, CutTransitionAlignment alignment)
    {
        if (overlapFrames <= 0) throw new ArgumentOutOfRangeException(nameof(overlapFrames));
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        var (outgoingTrack, outgoing) = FindClip(outgoingClipId);
        var (incomingTrack, incoming) = FindClip(incomingClipId);
        if (outgoingClipId == incomingClipId || outgoingTrack != incomingTrack || outgoingTrack.Kind != TrackKind.Video ||
            outgoing.EndFrame != incoming.StartFrame)
            throw new InvalidOperationException("Choose two video events that meet at a cut on the same track.");
        EnsureUnlocked(outgoingTrack, outgoing);
        EnsureUnlocked(incomingTrack, incoming);
        var before = alignment switch
        {
            CutTransitionAlignment.BeforeCut => overlapFrames,
            CutTransitionAlignment.CenteredOnCut => overlapFrames / 2,
            _ => 0
        };
        var after = overlapFrames - before;
        var extendedOutgoing = outgoing.DeepClone();
        var extendedIncoming = incoming.DeepClone();
        extendedOutgoing.DurationFrames = checked(outgoing.DurationFrames + after);
        extendedIncoming.StartFrame = checked(incoming.StartFrame - before);
        extendedIncoming.SourceInFrame = checked(incoming.SourceInFrame - before);
        if (extendedIncoming.VideoSourceMode == VideoSourceMode.HoldLastFrame) extendedIncoming.SourceInFrame = Math.Max(0, extendedIncoming.SourceInFrame);
        extendedIncoming.DurationFrames = checked(incoming.DurationFrames + before);
        EnsureTimelineBounds(extendedOutgoing);
        EnsureTimelineBounds(extendedIncoming);
        EnsureSourceBounds(extendedOutgoing, Project.Media.Single(media => media.Id == outgoing.MediaId));
        EnsureSourceBounds(extendedIncoming, Project.Media.Single(media => media.Id == incoming.MediaId));
        if (outgoing.FadeInFrames > extendedOutgoing.DurationFrames - overlapFrames ||
            incoming.FadeOutFrames > extendedIncoming.DurationFrames - overlapFrames)
            throw new InvalidOperationException("The overlap conflicts with an existing fade.");
        if (outgoingTrack.Clips.Any(clip => clip.Id != outgoing.Id && clip.Id != incoming.Id &&
            clip.StartFrame < extendedOutgoing.EndFrame && clip.EndFrame > extendedIncoming.StartFrame))
            throw new InvalidOperationException("A dissolve requires exactly two events in the overlap.");
        Execute(_ =>
        {
            outgoing.DurationFrames = extendedOutgoing.DurationFrames;
            incoming.StartFrame = extendedIncoming.StartFrame;
            incoming.SourceInFrame = extendedIncoming.SourceInFrame;
            incoming.DurationFrames = extendedIncoming.DurationFrames;
            // Extending a take adds handles; existing media and crop keyframes keep their timeline positions.
            if (incoming.Crop is not null) incoming.CropOffsetFrames = checked(incoming.CropOffsetFrames - before);
            outgoing.FadeOutFrames = incoming.FadeInFrames = overlapFrames;
            incoming.FadeInTransition = new VideoTransition();
            outgoing.FadeOffsetFrames = incoming.FadeOffsetFrames = 0;
            outgoing.FadeDurationFrames = incoming.FadeDurationFrames = null;
        });
    }

    public void ConvertOverlapToCut(Guid outgoingClipId, Guid incomingClipId, CutTransitionAlignment alignment)
    {
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        if (outgoingClipId == incomingClipId) throw new ArgumentException("Choose two different video events.");
        var (outgoingTrack, outgoing) = FindClip(outgoingClipId);
        var (incomingTrack, incoming) = FindClip(incomingClipId);
        if (outgoingTrack != incomingTrack || outgoingTrack.Kind != TrackKind.Video)
            throw new InvalidOperationException("A cut conversion requires overlapping video events on the same track.");
        EnsureUnlocked(outgoingTrack, outgoing);
        EnsureUnlocked(incomingTrack, incoming);
        if (outgoing.StartFrame >= incoming.StartFrame || outgoing.EndFrame <= incoming.StartFrame || outgoing.EndFrame >= incoming.EndFrame)
            throw new InvalidOperationException("Both events must retain a positive duration on opposite sides of the overlap.");
        var overlap = outgoing.EndFrame - incoming.StartFrame;
        if (outgoing.FadeOffsetFrames != 0 || incoming.FadeOffsetFrames != 0 ||
            outgoing.FadeDurationFrames is not null || incoming.FadeDurationFrames is not null ||
            outgoing.FadeOutFrames != overlap || incoming.FadeInFrames != overlap)
            throw new InvalidOperationException("Cut conversion requires complete, unsplit fades covering the overlap.");
        if (outgoingTrack.Clips.Any(clip => clip.Id != outgoing.Id && clip.Id != incoming.Id &&
            clip.StartFrame < outgoing.EndFrame && clip.EndFrame > incoming.StartFrame))
            throw new InvalidOperationException("A cut conversion requires exactly two events in the overlap.");
        if (alignment == CutTransitionAlignment.CenteredOnCut && overlap % 2 != 0)
            throw new InvalidOperationException("Centering this overlap would create a half-frame cut. OpenRend stores edits in whole project frames.");
        var cut = alignment switch
        {
            CutTransitionAlignment.BeforeCut => outgoing.EndFrame,
            CutTransitionAlignment.AfterCut => incoming.StartFrame,
            _ => checked(incoming.StartFrame + overlap / 2)
        };
        var trimmedOutgoing = outgoing.DeepClone();
        var trimmedIncoming = incoming.DeepClone();
        var headTrim = cut - incoming.StartFrame;
        trimmedOutgoing.DurationFrames = cut - outgoing.StartFrame;
        trimmedOutgoing.FadeOutFrames = 0;
        trimmedOutgoing.FadeOutTransition = null;
        trimmedIncoming.StartFrame = cut;
        trimmedIncoming.DurationFrames = incoming.EndFrame - cut;
        trimmedIncoming.SourceInFrame = checked(incoming.SourceInFrame + headTrim);
        if (trimmedIncoming.Crop is not null) trimmedIncoming.CropOffsetFrames = checked(incoming.CropOffsetFrames + headTrim);
        trimmedIncoming.FadeInFrames = 0;
        trimmedIncoming.FadeInTransition = null;
        EnsureTimelineBounds(trimmedOutgoing);
        EnsureTimelineBounds(trimmedIncoming);
        EnsureSourceBounds(trimmedOutgoing, Project.Media.Single(media => media.Id == outgoing.MediaId));
        EnsureSourceBounds(trimmedIncoming, Project.Media.Single(media => media.Id == incoming.MediaId));
        if (trimmedOutgoing.FadeInFrames > trimmedOutgoing.DurationFrames || trimmedIncoming.FadeOutFrames > trimmedIncoming.DurationFrames)
            throw new InvalidOperationException("The cut would truncate an outer fade.");
        Execute(_ =>
        {
            outgoing.DurationFrames = trimmedOutgoing.DurationFrames;
            outgoing.FadeOutFrames = 0;
            outgoing.FadeOutTransition = null;
            // Trimming the head advances the take and crop envelope together, preserving surviving content.
            incoming.StartFrame = trimmedIncoming.StartFrame;
            incoming.DurationFrames = trimmedIncoming.DurationFrames;
            incoming.SourceInFrame = trimmedIncoming.SourceInFrame;
            incoming.CropOffsetFrames = trimmedIncoming.CropOffsetFrames;
            incoming.FadeInFrames = 0;
            incoming.FadeInTransition = null;
        });
    }
    public int ApplyDissolvesToAdjacent(IEnumerable<Guid> selectedClipIds)
    {
        var selected = ExpandSelection(selectedClipIds, ignoreGroups: true);
        var pairs = new List<(TimelineClip Outgoing, TimelineClip Incoming)>();
        foreach (var track in Project.Tracks.Where(track => track.Kind == TrackKind.Video))
        {
            var clips = track.Clips.OrderBy(clip => clip.StartFrame).ThenBy(clip => clip.Id).ToArray();
            for (var i = 1; i < clips.Length; i++)
            {
                var outgoing = clips[i - 1];
                var incoming = clips[i];
                if (!selected.Contains(outgoing.Id) && !selected.Contains(incoming.Id)) continue;
                if (outgoing.StartFrame >= incoming.StartFrame || outgoing.EndFrame <= incoming.StartFrame ||
                    outgoing.EndFrame > incoming.EndFrame) continue;
                if (clips.Any(clip => clip.Id != outgoing.Id && clip.Id != incoming.Id &&
                    clip.StartFrame < outgoing.EndFrame && clip.EndFrame > incoming.StartFrame)) continue;
                EnsureUnlocked(track, outgoing);
                EnsureUnlocked(track, incoming);
                pairs.Add((outgoing, incoming));
            }
        }
        if (pairs.Count == 0) return 0;
        var fades = new Dictionary<Guid, (long In, long Out)>();
        foreach (var (outgoing, incoming) in pairs)
        {
            var overlap = outgoing.EndFrame - incoming.StartFrame;
            var outgoingFades = fades.GetValueOrDefault(outgoing.Id, (outgoing.FadeInFrames, outgoing.FadeOutFrames));
            var incomingFades = fades.GetValueOrDefault(incoming.Id, (incoming.FadeInFrames, incoming.FadeOutFrames));
            fades[outgoing.Id] = (outgoingFades.Item1, overlap);
            fades[incoming.Id] = (overlap, incomingFades.Item2);
        }
        foreach (var track in Project.Tracks)
            foreach (var clip in track.Clips.Where(clip => fades.ContainsKey(clip.Id)))
            {
                var fade = fades[clip.Id];
                if (fade.In > clip.DurationFrames - fade.Out)
                    throw new InvalidOperationException("The overlap conflicts with an existing fade.");
            }
        Execute(_ =>
        {
            foreach (var (outgoing, incoming) in pairs)
            {
                var overlap = outgoing.EndFrame - incoming.StartFrame;
                outgoing.FadeOutFrames = incoming.FadeInFrames = overlap;
                incoming.FadeInTransition = new VideoTransition();
                outgoing.FadeOffsetFrames = incoming.FadeOffsetFrames = 0;
                outgoing.FadeDurationFrames = incoming.FadeDurationFrames = null;
            }
        });
        return pairs.Count;
    }

    public void MoveClip(Guid clipId, Guid trackId, long startFrame)
    {
        var (source, clip) = FindClip(clipId);
        var target = FindTrack(trackId);
        EnsureUnlocked(source, clip);
        EnsureUnlocked(target);
        EnsureCompatible(Project.Media.Single(m => m.Id == clip.MediaId), target);
        Execute(_ =>
        {
            source.Clips.Remove(clip);
            clip.StartFrame = startFrame;
            target.Clips.Add(clip);
        });
    }

    public void TrimClip(Guid clipId, long sourceIn, long duration)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        Execute(_ =>
        {
            if (clip.Crop is not null) clip.CropOffsetFrames = checked(clip.CropOffsetFrames + checked(sourceIn - clip.SourceInFrame));
            clip.SourceInFrame = clip.VideoSourceMode == VideoSourceMode.HoldLastFrame ? Math.Max(0, sourceIn) : sourceIn;
            clip.DurationFrames = duration;
            clip.FadeInFrames = Math.Min(clip.FadeInFrames, duration);
            clip.FadeOutFrames = Math.Min(clip.FadeOutFrames, duration - clip.FadeInFrames);
            clip.FadeOffsetFrames = 0;
            clip.FadeDurationFrames = null;
            EnsureSourceBounds(clip, Project.Media.Single(m => m.Id == clip.MediaId));
        });
    }

    public TimelineClip SplitClip(Guid clipId, long frame)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (frame <= clip.StartFrame || frame >= clip.EndFrame) throw new ArgumentOutOfRangeException(nameof(frame), "Split must be inside the clip.");
        var right = clip.DeepClone();
        var leftDuration = frame - clip.StartFrame;
        right.Id = Guid.NewGuid();
        right.StartFrame = frame;
        right.SourceInFrame += leftDuration;
        right.DurationFrames -= leftDuration;
        if (right.Crop is not null) right.CropOffsetFrames = checked(right.CropOffsetFrames + leftDuration);
        var envelope = clip.FadeDurationFrames ?? clip.DurationFrames;
        right.FadeOffsetFrames += leftDuration;
        right.FadeDurationFrames = envelope;
        Execute(_ =>
        {
            clip.DurationFrames = leftDuration;
            clip.FadeDurationFrames = envelope;
            track.Clips.Add(right);
        });
        return right;
    }

    public void DeleteClip(Guid clipId, bool ripple = false)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        Execute(_ =>
        {
            track.Clips.Remove(clip);
            if (ripple)
                foreach (var later in track.Clips.Where(c => c.StartFrame >= clip.EndFrame)) later.StartFrame -= clip.DurationFrames;
        });
    }

    public HashSet<Guid> ExpandSelection(IEnumerable<Guid> ids, bool ignoreGroups = false)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var selected = ids.ToHashSet();
        var clips = Project.Tracks.SelectMany(t => t.Clips).ToDictionary(c => c.Id);
        foreach (var id in selected)
            if (!clips.ContainsKey(id)) throw new KeyNotFoundException("A selected clip was not found.");
        if (ignoreGroups) return selected;
        var groups = selected.Select(id => clips[id].GroupId).OfType<Guid>().ToHashSet();
        selected.UnionWith(clips.Values.Where(c => c.GroupId.HasValue && groups.Contains(c.GroupId.Value)).Select(c => c.Id));
        return selected;
    }

    public void LinkClips(IEnumerable<Guid> ids)
    {
        var selected = SelectedClips(ids);
        foreach (var item in selected) EnsureUnlocked(item.Track, item.Clip);
        if (selected.Count < 2) throw new ArgumentException("Select at least two clips to link.", nameof(ids));
        if (selected[0].Clip.GroupId.HasValue && selected.All(item => item.Clip.GroupId == selected[0].Clip.GroupId)) return;
        var group = Guid.NewGuid();
        Execute(_ => { foreach (var item in selected) item.Clip.GroupId = group; });
    }

    public void UnlinkClips(IEnumerable<Guid> ids)
    {
        var selected = SelectedClips(ids);
        foreach (var item in selected) EnsureUnlocked(item.Track, item.Clip);
        if (selected.All(item => item.Clip.GroupId is null)) return;
        Execute(_ => { foreach (var item in selected) item.Clip.GroupId = null; });
    }

    public void MoveSelection(IEnumerable<Guid> ids, Guid primaryClipId, Guid targetTrackId, long start, bool ignoreGroups = false)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var selected = SelectedClips(ids.Append(primaryClipId), ignoreGroups);
        var (source, primary) = FindClip(primaryClipId);
        var target = FindTrack(targetTrackId);
        EnsureUnlocked(target);
        EnsureCompatible(Project.Media.Single(m => m.Id == primary.MediaId), target);
        var delta = checked(start - primary.StartFrame);
        var positions = new Dictionary<Guid, long>();
        foreach (var item in selected)
        {
            EnsureUnlocked(item.Track, item.Clip);
            var moved = item.Clip.DeepClone();
            moved.StartFrame = checked(moved.StartFrame + delta);
            EnsureTimelineBounds(moved);
            EnsureSourceBounds(moved, Project.Media.Single(m => m.Id == moved.MediaId));
            positions.Add(moved.Id, moved.StartFrame);
        }
        Execute(_ =>
        {
            foreach (var item in selected) item.Clip.StartFrame = positions[item.Clip.Id];
            if (source != target) { source.Clips.Remove(primary); target.Clips.Add(primary); }
        });
    }

    public void TrimSelection(Guid primaryClipId, long newStart, long sourceIn, long duration, bool ignoreGroups = false)
    {
        var selected = SelectedClips([primaryClipId], ignoreGroups);
        var primary = FindClip(primaryClipId).Clip;
        var startDelta = checked(newStart - primary.StartFrame);
        var sourceDelta = checked(sourceIn - primary.SourceInFrame);
        var durationDelta = checked(duration - primary.DurationFrames);
        var changes = new Dictionary<Guid, TimelineClip>();
        foreach (var item in selected)
        {
            EnsureUnlocked(item.Track, item.Clip);
            var trimmed = item.Clip.DeepClone();
            trimmed.StartFrame = checked(trimmed.StartFrame + startDelta);
            trimmed.SourceInFrame = checked(trimmed.SourceInFrame + sourceDelta);
            if (trimmed.VideoSourceMode == VideoSourceMode.HoldLastFrame) trimmed.SourceInFrame = Math.Max(0, trimmed.SourceInFrame);
            trimmed.DurationFrames = checked(trimmed.DurationFrames + durationDelta);
            if (trimmed.Crop is not null) trimmed.CropOffsetFrames = checked(trimmed.CropOffsetFrames + sourceDelta);
            EnsureTimelineBounds(trimmed);
            EnsureSourceBounds(trimmed, Project.Media.Single(m => m.Id == trimmed.MediaId));
            trimmed.FadeInFrames = Math.Min(trimmed.FadeInFrames, trimmed.DurationFrames);
            trimmed.FadeOutFrames = Math.Min(trimmed.FadeOutFrames, trimmed.DurationFrames - trimmed.FadeInFrames);
            trimmed.FadeOffsetFrames = 0;
            trimmed.FadeDurationFrames = null;
            changes.Add(trimmed.Id, trimmed);
        }
        Execute(_ =>
        {
            foreach (var item in selected)
            {
                var change = changes[item.Clip.Id];
                item.Clip.StartFrame = change.StartFrame; item.Clip.SourceInFrame = change.SourceInFrame; item.Clip.DurationFrames = change.DurationFrames;
                item.Clip.FadeInFrames = change.FadeInFrames; item.Clip.FadeOutFrames = change.FadeOutFrames;
                item.Clip.FadeOffsetFrames = 0; item.Clip.FadeDurationFrames = null;
                item.Clip.CropOffsetFrames = change.CropOffsetFrames;
            }
        });
    }

    public IReadOnlyList<TimelineClip> SplitSelection(IEnumerable<Guid> ids, long frame, bool ignoreGroups = false)
    {
        var selected = SelectedClips(ids, ignoreGroups);
        foreach (var item in selected) EnsureUnlocked(item.Track, item.Clip);
        var crossing = selected.Where(item => frame > item.Clip.StartFrame && frame < item.Clip.EndFrame).ToList();
        if (crossing.Count == 0) throw new ArgumentOutOfRangeException(nameof(frame), "Split must be inside a selected clip.");
        var rightGroups = new Dictionary<Guid, Guid>();
        var splits = crossing.Select(item =>
        {
            var leftDuration = checked(frame - item.Clip.StartFrame);
            var envelope = item.Clip.FadeDurationFrames ?? item.Clip.DurationFrames;
            var right = item.Clip.DeepClone(); right.Id = Guid.NewGuid(); right.StartFrame = frame;
            right.SourceInFrame = checked(right.SourceInFrame + leftDuration); right.DurationFrames -= leftDuration;
            if (right.Crop is not null) right.CropOffsetFrames = checked(right.CropOffsetFrames + leftDuration);
            right.FadeOffsetFrames = checked(right.FadeOffsetFrames + leftDuration); right.FadeDurationFrames = envelope;
            if (right.GroupId is Guid original)
            {
                if (!rightGroups.TryGetValue(original, out var group)) rightGroups[original] = group = Guid.NewGuid();
                right.GroupId = group;
            }
            return (item.Track, item.Clip, Right: right, LeftDuration: leftDuration, Envelope: envelope);
        }).ToList();
        Execute(_ =>
        {
            foreach (var split in splits) { split.Clip.DurationFrames = split.LeftDuration; split.Clip.FadeDurationFrames = split.Envelope; split.Track.Clips.Add(split.Right); }
        });
        return splits.Select(split => split.Right).ToArray();
    }

    public void DeleteSelection(IEnumerable<Guid> ids, bool ripple = false, bool ignoreGroups = false)
    {
        var selected = SelectedClips(ids, ignoreGroups);
        if (selected.Count == 0) return;
        foreach (var item in selected) EnsureUnlocked(item.Track, item.Clip);
        var removed = selected.Select(item => item.Clip.Id).ToHashSet();
        var shifts = new Dictionary<Guid, long>();
        if (ripple)
            foreach (var trackGroup in selected.GroupBy(item => item.Track))
            {
                // Collapse the union of removed spans once; overlapping clips must not double the ripple.
                var spans = new List<(long Start, long End)>();
                foreach (var clip in trackGroup.Select(item => item.Clip).OrderBy(c => c.StartFrame))
                    if (spans.Count == 0 || clip.StartFrame > spans[^1].End) spans.Add((clip.StartFrame, clip.EndFrame));
                    else spans[^1] = (spans[^1].Start, Math.Max(spans[^1].End, clip.EndFrame));
                foreach (var remaining in trackGroup.Key.Clips.Where(c => !removed.Contains(c.Id)))
                {
                    var shift = spans.Where(span => span.End <= remaining.StartFrame).Sum(span => span.End - span.Start);
                    shifts.Add(remaining.Id, checked(remaining.StartFrame - shift));
                }
            }
        Execute(_ =>
        {
            foreach (var item in selected) item.Track.Clips.Remove(item.Clip);
            foreach (var track in Project.Tracks)
                foreach (var clip in track.Clips)
                    if (shifts.TryGetValue(clip.Id, out var position)) clip.StartFrame = position;
        });
    }

    public void SetTrackState(Guid trackId, bool? isMuted = null, bool? isSolo = null, bool? isLocked = null, bool? isVisible = null)
    {
        var track = FindTrack(trackId);
        Execute(_ =>
        {
            if (isMuted.HasValue) track.IsMuted = isMuted.Value;
            if (isSolo.HasValue) track.IsSolo = isSolo.Value;
            if (isLocked.HasValue) track.IsLocked = isLocked.Value;
            if (isVisible.HasValue) track.IsVisible = isVisible.Value;
        });
    }

    public void SetTrackLevels(Guid trackId, double? gain = null, double? opacity = null, double? pan = null)
    {
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (gain.HasValue && !double.IsFinite(gain.Value)) throw new ArgumentOutOfRangeException(nameof(gain));
        if (opacity.HasValue && !double.IsFinite(opacity.Value)) throw new ArgumentOutOfRangeException(nameof(opacity));
        if (pan.HasValue && !double.IsFinite(pan.Value)) throw new ArgumentOutOfRangeException(nameof(pan));
        var nextGain = gain.HasValue ? Math.Max(0, gain.Value) : track.Gain;
        var nextOpacity = opacity.HasValue ? Math.Clamp(opacity.Value, 0, 1) : track.Opacity;
        var nextPan = pan.HasValue ? Math.Clamp(pan.Value, -1, 1) : track.Pan;
        if (nextGain == track.Gain && nextOpacity == track.Opacity && nextPan == track.Pan) return;
        Execute(_ => { track.Gain = nextGain; track.Opacity = nextOpacity; track.Pan = nextPan; });
    }

    public void SetClipLevels(Guid clipId, double? gain = null, double? opacity = null, double? pan = null, long? fadeInFrames = null, long? fadeOutFrames = null)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        Execute(_ =>
        {
            if (gain.HasValue) clip.Gain = gain.Value;
            if (opacity.HasValue) clip.Opacity = opacity.Value;
            if (pan.HasValue) clip.Pan = pan.Value;
            if (fadeInFrames.HasValue) clip.FadeInFrames = fadeInFrames.Value;
            if (fadeOutFrames.HasValue) clip.FadeOutFrames = fadeOutFrames.Value;
        });
    }

    public void SetClipLock(Guid clipId, bool isLocked)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track);
        if (clip.IsLocked == isLocked) return;
        Execute(_ =>
        {
            var target = Project.Tracks.SelectMany(t => t.Clips).First(c => c.Id == clipId);
            target.IsLocked = isLocked;
        });
    }

    public void SetClipMute(Guid clipId, bool isMuted)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (clip.IsMuted == isMuted) return;
        Execute(_ =>
        {
            var target = Project.Tracks.SelectMany(t => t.Clips).First(c => c.Id == clipId);
            target.IsMuted = isMuted;
        });
    }

    public void SetSelectionLock(IEnumerable<Guid> ids, bool isLocked, bool ignoreGroups = false)
    {
        var selected = SelectedClips(ids, ignoreGroups);
        foreach (var item in selected) EnsureUnlocked(item.Track);
        var changed = selected.Where(item => item.Clip.IsLocked != isLocked).ToList();
        if (changed.Count == 0) return;
        Execute(_ =>
        {
            foreach (var item in changed) item.Clip.IsLocked = isLocked;
        });
    }

    public void SetSelectionMute(IEnumerable<Guid> ids, bool isMuted, bool ignoreGroups = false)
    {
        var selected = SelectedClips(ids, ignoreGroups);
        foreach (var item in selected) EnsureUnlocked(item.Track, item.Clip);
        var changed = selected.Where(item => item.Clip.IsMuted != isMuted).ToList();
        if (changed.Count == 0) return;
        Execute(_ =>
        {
            foreach (var item in changed) item.Clip.IsMuted = isMuted;
        });
    }

    public void SetCropKeyframe(Guid clipId, long localFrame, CropState state, CropInterpolation interpolation = CropInterpolation.Linear)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        EnsureVideoCrop(track);
        if (localFrame < 0 || localFrame >= clip.DurationFrames) throw new ArgumentOutOfRangeException(nameof(localFrame));
        if (!Enum.IsDefined(interpolation)) throw new ArgumentOutOfRangeException(nameof(interpolation));
        state.Validate();
        var frame = checked(localFrame + clip.CropOffsetFrames);
        var existing = clip.Crop?.Keyframes.FirstOrDefault(key => key.Frame == frame);
        if (existing is not null && existing.State == state && existing.Interpolation == interpolation) return;
        var animation = clip.Crop?.DeepClone() ?? new CropAnimation();
        var offset = clip.CropOffsetFrames;
        if (clip.Crop is null && localFrame > 0)
        {
            var media = Project.Media.Single(item => item.Id == clip.MediaId);
            animation.Keyframes.Add(new CropKeyframe { Frame = 0, State = CropState.Identity(media.Width, media.Height) });
        }
        if (frame < 0)
        {
            // Extending an event before its original start retains the curve and permits new keys.
            var shift = checked(-frame);
            foreach (var key in animation.Keyframes) key.Frame = checked(key.Frame + shift);
            offset = checked(offset + shift);
            frame = 0;
        }
        var keyframe = animation.Keyframes.FirstOrDefault(key => key.Frame == frame);
        if (keyframe is null)
        {
            animation.Keyframes.Add(new CropKeyframe { Frame = frame, State = state, Interpolation = interpolation });
            animation.Keyframes.Sort((left, right) => left.Frame.CompareTo(right.Frame));
        }
        else { keyframe.State = state; keyframe.Interpolation = interpolation; }
        animation.Validate();
        Execute(_ => { clip.Crop = animation; clip.CropOffsetFrames = offset; });
    }

    public void MoveCropKeyframe(Guid clipId, long fromLocalFrame, long toLocalFrame, bool preserveDestination = false)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track);
        EnsureVideoCrop(track);
        if (fromLocalFrame < 0 || fromLocalFrame >= clip.DurationFrames) throw new ArgumentOutOfRangeException(nameof(fromLocalFrame));
        if (toLocalFrame < 0 || toLocalFrame >= clip.DurationFrames) throw new ArgumentOutOfRangeException(nameof(toLocalFrame));
        var from = checked(fromLocalFrame + clip.CropOffsetFrames);
        var to = checked(toLocalFrame + clip.CropOffsetFrames);
        if (clip.Crop is null || clip.Crop.Keyframes.All(key => key.Frame != from)) throw new InvalidOperationException("No crop keyframe exists at the source frame.");
        if (fromLocalFrame == toLocalFrame) return;
        var animation = clip.Crop.DeepClone();
        var moved = animation.Keyframes.Single(key => key.Frame == from);
        animation.Keyframes.Remove(moved);
        if (preserveDestination)
        {
            var visible = animation.Keyframes.Select(key => (Key: key, Local: (Int128)key.Frame - clip.CropOffsetFrames))
                .Where(item => item.Local >= 0 && item.Local < clip.DurationFrames).ToDictionary(item => (long)item.Local, item => item.Key);
            if (visible.ContainsKey(toLocalFrame))
            {
                // Keep both edits on the frame grid. Move the occupied run toward the first gap.
                var gap = toLocalFrame;
                while (gap < clip.DurationFrames - 1 && visible.ContainsKey(gap)) gap++;
                if (!visible.ContainsKey(gap))
                {
                    for (var frame = gap; frame > toLocalFrame; frame--)
                        visible[frame - 1].Frame = checked(visible[frame - 1].Frame + 1);
                }
                else
                {
                    gap = toLocalFrame;
                    while (gap > 0 && visible.ContainsKey(gap)) gap--;
                    for (var frame = gap; frame < toLocalFrame; frame++)
                        visible[frame + 1].Frame = checked(visible[frame + 1].Frame - 1);
                }
            }
        }
        else animation.Keyframes.RemoveAll(key => key.Frame == to);
        moved.Frame = to;
        animation.Keyframes.Add(moved);
        var offset = clip.CropOffsetFrames;
        var earliest = animation.Keyframes.Min(key => key.Frame);
        if (earliest < 0)
        {
            // Shift the retained curve and its origin together, preserving other local key times.
            var shift = checked(-earliest);
            foreach (var key in animation.Keyframes) key.Frame = checked(key.Frame + shift);
            offset = checked(offset + shift);
        }
        animation.Keyframes.Sort((left, right) => left.Frame.CompareTo(right.Frame));
        animation.Validate();
        Execute(_ => { clip.Crop = animation; clip.CropOffsetFrames = offset; });
    }

    public void RemoveCropKeyframe(Guid clipId, long localFrame)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        EnsureVideoCrop(track);
        if (localFrame < 0 || localFrame >= clip.DurationFrames) throw new ArgumentOutOfRangeException(nameof(localFrame));
        if (clip.Crop is null) return;
        var frame = checked(localFrame + clip.CropOffsetFrames);
        if (clip.Crop.Keyframes.All(key => key.Frame != frame)) return;
        Execute(_ =>
        {
            clip.Crop.Keyframes.RemoveAll(key => key.Frame == frame);
            if (clip.Crop.Keyframes.Count == 0) { clip.Crop = null; clip.CropOffsetFrames = 0; }
        });
    }

    public void ResetCrop(Guid clipId)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        EnsureVideoCrop(track);
        if (clip.Crop is null) return;
        Execute(_ => { clip.Crop = null; clip.CropOffsetFrames = 0; });
    }

    public VideoEffect AddVideoEffect(Guid clipId, VideoEffectKind kind)
    {
        var clip = EditableVideoClip(clipId);
        var effect = VideoEffect.Create(kind);
        Execute(_ => clip.VideoEffects.Add(effect));
        return effect;
    }

    public void UpdateVideoEffect(Guid clipId, Guid effectId, double amount, double secondaryAmount, bool isEnabled)
    {
        var clip = EditableVideoClip(clipId);
        var effect = clip.VideoEffects.FirstOrDefault(item => item.Id == effectId) ?? throw new KeyNotFoundException("Video effect was not found.");
        var updated = effect.DeepClone();
        updated.Amount = amount; updated.SecondaryAmount = secondaryAmount; updated.IsEnabled = isEnabled;
        updated.Validate();
        if (effect.Amount == amount && effect.SecondaryAmount == secondaryAmount && effect.IsEnabled == isEnabled) return;
        Execute(_ => { effect.Amount = amount; effect.SecondaryAmount = secondaryAmount; effect.IsEnabled = isEnabled; });
    }

    public void RemoveVideoEffect(Guid clipId, Guid effectId)
    {
        var clip = EditableVideoClip(clipId);
        var effect = clip.VideoEffects.FirstOrDefault(item => item.Id == effectId) ?? throw new KeyNotFoundException("Video effect was not found.");
        Execute(_ => clip.VideoEffects.Remove(effect));
    }

    public void UpdateVideoEffectParameters(Guid clipId, Guid effectId, IReadOnlyDictionary<string, double> parameters, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var clip = EditableVideoClip(clipId);
        var effect = clip.VideoEffects.FirstOrDefault(item => item.Id == effectId) ?? throw new KeyNotFoundException("Video effect was not found.");
        var updated = effect.DeepClone();
        foreach (var (key, value) in parameters) updated.SetParameter(key, value);
        updated.IsEnabled = isEnabled;
        updated.Validate();
        if (effect.IsEnabled == isEnabled && VideoEffectCatalog.Get(effect.Kind).Parameters.All(parameter => effect.GetParameter(parameter.Key) == updated.GetParameter(parameter.Key))) return;
        Execute(_ => clip.VideoEffects[clip.VideoEffects.IndexOf(effect)] = updated);
    }

    public VideoEffect ReplaceVideoEffect(Guid clipId, Guid effectId, VideoEffectKind kind)
    {
        var clip = EditableVideoClip(clipId);
        var effect = clip.VideoEffects.FirstOrDefault(item => item.Id == effectId) ?? throw new KeyNotFoundException("Video effect was not found.");
        var replacement = VideoEffect.Create(kind);
        replacement.IsEnabled = effect.IsEnabled;
        Execute(_ => clip.VideoEffects[clip.VideoEffects.IndexOf(effect)] = replacement);
        return replacement;
    }

    public void SetVideoEffects(Guid clipId, IReadOnlyList<VideoEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        var clip = EditableVideoClip(clipId);
        if (effects.Any(effect => effect is null) || effects.Select(effect => effect.Id).Distinct().Count() != effects.Count)
            throw new InvalidDataException("Video effects require unique identities.");
        foreach (var effect in effects) effect.Validate();
        var snapshot = effects.Select(effect => effect.DeepClone()).ToList();
        var same = clip.VideoEffects.Count == snapshot.Count && clip.VideoEffects.Zip(snapshot).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Kind == pair.Second.Kind && pair.First.IsEnabled == pair.Second.IsEnabled &&
            (pair.First.Lut is { } lut ? lut.ContentEquals(pair.Second.Lut) : pair.Second.Lut is null) &&
            VideoEffectCatalog.Get(pair.First.Kind).Parameters.All(parameter => pair.First.GetParameter(parameter.Key) == pair.Second.GetParameter(parameter.Key)));
        if (same) return;
        // The chooser owns its working copy; a single accepted chain is one undo step.
        Execute(_ => clip.VideoEffects = snapshot);
    }

    public void MoveVideoEffect(Guid clipId, Guid effectId, int destinationIndex)
    {
        var clip = EditableVideoClip(clipId);
        var effect = clip.VideoEffects.FirstOrDefault(item => item.Id == effectId) ?? throw new KeyNotFoundException("Video effect was not found.");
        if (destinationIndex < 0 || destinationIndex >= clip.VideoEffects.Count) throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        if (clip.VideoEffects.IndexOf(effect) == destinationIndex) return;
        Execute(_ => { clip.VideoEffects.Remove(effect); clip.VideoEffects.Insert(destinationIndex, effect); });
    }

    private TimelineClip EditableVideoClip(Guid clipId)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (track.Kind != TrackKind.Video) throw new InvalidOperationException("Video effects are only available for video clips.");
        return clip;
    }

    public long SnapFrame(long frame, long toleranceFrames, Guid? excludeClipId = null, long? playheadFrame = null)
    {
        if (toleranceFrames < 0) throw new ArgumentOutOfRangeException(nameof(toleranceFrames));
        var candidates = Project.Tracks.SelectMany(t => t.Clips).Where(c => c.Id != excludeClipId).SelectMany(c => new[] { c.StartFrame, c.EndFrame }).Append(0);
        if (playheadFrame.HasValue) candidates = candidates.Append(playheadFrame.Value);
        // Decimal subtraction avoids overflow while comparing user-provided positions.
        return candidates.Select(f => (Frame: f, Distance: Math.Abs((decimal)f - frame))).Where(c => c.Distance <= toleranceFrames).OrderBy(c => c.Distance).ThenBy(c => c.Frame).Select(c => c.Frame).DefaultIfEmpty(Math.Max(0, frame)).First();
    }

    private TimelineTrack FindTrack(Guid id) => Project.Tracks.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException("Track was not found.");
    private List<(TimelineTrack Track, TimelineClip Clip)> SelectedClips(IEnumerable<Guid> ids, bool ignoreGroups = false)
    {
        var selected = ExpandSelection(ids, ignoreGroups);
        return Project.Tracks.SelectMany(track => track.Clips.Where(clip => selected.Contains(clip.Id)).Select(clip => (track, clip))).ToList();
    }
    private static void EnsureTimelineBounds(TimelineClip clip)
    {
        if (clip.StartFrame < 0 || clip.DurationFrames <= 0 || clip.StartFrame > long.MaxValue - clip.DurationFrames) throw new ArgumentOutOfRangeException(nameof(clip), "Clip timeline range is invalid.");
    }
    private (TimelineTrack Track, TimelineClip Clip) FindClip(Guid id)
    {
        foreach (var track in Project.Tracks)
            foreach (var clip in track.Clips)
                if (clip.Id == id) return (track, clip);
        throw new KeyNotFoundException("Clip was not found.");
    }
    private static void EnsureUnlocked(TimelineTrack track) { if (track.IsLocked) throw new InvalidOperationException("Track is locked."); }
    private static void EnsureUnlocked(TimelineTrack track, TimelineClip clip)
    {
        if (track.IsLocked) throw new InvalidOperationException("Track is locked.");
        if (clip.IsLocked) throw new InvalidOperationException("Event is locked.");
    }
    private static void EnsureVideoCrop(TimelineTrack track) { if (track.Kind != TrackKind.Video) throw new InvalidOperationException("Crop is only available for video clips."); }
    private static void EnsureCompatible(MediaAsset media, TimelineTrack track)
    {
        if (track.Kind == TrackKind.Video && !media.HasVideo || track.Kind == TrackKind.Audio && !media.HasAudio) throw new InvalidOperationException("The media has no stream for this track.");
    }
    private static void SynchronizeDissolveOverlaps(ProjectDocument project, ProjectDocument before)
    {
        foreach (var track in project.Tracks.Where(track => track.Kind == TrackKind.Video))
        {
            if (track.IsLocked) continue;
            var previousTrack = before.Tracks.FirstOrDefault(previous => previous.Id == track.Id);
            var plainPairs = new Dictionary<Guid, Guid>();
            if (previousTrack is not null)
                foreach (var incoming in previousTrack.Clips.Where(clip => clip.FadeInFrames > 0 &&
                    clip.FadeOffsetFrames == 0 && clip.FadeDurationFrames is null))
                {
                    var outgoing = previousTrack.Clips.FirstOrDefault(clip => clip.Id != incoming.Id &&
                        clip.FadeOffsetFrames == 0 && clip.FadeDurationFrames is null &&
                        clip.StartFrame < incoming.StartFrame && clip.EndFrame <= incoming.EndFrame &&
                        clip.EndFrame - incoming.StartFrame == incoming.FadeInFrames && clip.FadeOutFrames == incoming.FadeInFrames);
                    if (outgoing is not null && !previousTrack.Clips.Any(clip => clip.Id != outgoing.Id && clip.Id != incoming.Id &&
                        clip.StartFrame < outgoing.EndFrame && clip.EndFrame > incoming.StartFrame))
                        plainPairs[incoming.Id] = outgoing.Id;
                }
            // Unsplit fade envelopes follow the edited overlap; split pieces retain their original envelope.
            foreach (var incoming in track.Clips.Where(clip => (clip.FadeInTransition is not null || plainPairs.ContainsKey(clip.Id)) &&
                clip.FadeOffsetFrames == 0 && clip.FadeDurationFrames is null))
            {
                var outgoing = track.Clips.Where(clip => clip.Id != incoming.Id &&
                    (plainPairs.TryGetValue(incoming.Id, out var partner) ? clip.Id == partner : clip.FadeOutFrames > 0) &&
                    clip.FadeOffsetFrames == 0 && clip.FadeDurationFrames is null &&
                    clip.StartFrame < incoming.StartFrame && clip.EndFrame > incoming.StartFrame &&
                    clip.EndFrame <= incoming.EndFrame)
                    .OrderByDescending(clip => clip.StartFrame).FirstOrDefault();
                if (outgoing is null || track.Clips.Any(clip => clip.Id != outgoing.Id && clip.Id != incoming.Id &&
                    clip.StartFrame < outgoing.EndFrame && clip.EndFrame > incoming.StartFrame)) continue;
                var overlap = outgoing.EndFrame - incoming.StartFrame;
                outgoing.FadeOutFrames = overlap;
                incoming.FadeInFrames = overlap;
            }
        }
    }
    private void EnsureSourceBounds(TimelineClip clip, MediaAsset media)
    {
        if (clip.DurationFrames <= 0 || clip.SourceInFrame < 0 && clip.VideoSourceMode != VideoSourceMode.Loop || clip.SourceInFrame > long.MaxValue - clip.DurationFrames) throw new ArgumentOutOfRangeException(nameof(clip), "Clip source range is invalid.");
        if (clip.VideoSourceMode != VideoSourceMode.Bounded &&
            (!media.HasVideo || media.DurationSeconds < 1e-28 || media.Path.StartsWith("openrend://", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Source playback modes require a finite video source.");
        var available = Project.FrameRate.Frames(media.DurationSeconds);
        if (clip.VideoSourceMode == VideoSourceMode.Bounded && available > 0 && clip.SourceInFrame + clip.DurationFrames > available) throw new ArgumentOutOfRangeException(nameof(clip), "Clip extends beyond the source media.");
    }
    private void Restore(ProjectDocument snapshot)
    {
        var copy = snapshot.DeepClone();
        Project.Name = copy.Name; Project.Width = copy.Width; Project.Height = copy.Height; Project.FrameRate = copy.FrameRate;
        Project.Media = copy.Media; Project.Tracks = copy.Tracks;
    }
}
