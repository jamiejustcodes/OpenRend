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

    public void MoveClip(Guid clipId, Guid trackId, long startFrame)
    {
        var (source, clip) = FindClip(clipId);
        var target = FindTrack(trackId);
        EnsureUnlocked(source);
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
        EnsureUnlocked(track);
        Execute(_ =>
        {
            if (clip.Crop is not null) clip.CropOffsetFrames = checked(clip.CropOffsetFrames + checked(sourceIn - clip.SourceInFrame));
            clip.SourceInFrame = sourceIn;
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
        EnsureUnlocked(track);
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
        EnsureUnlocked(track);
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
        foreach (var item in selected) EnsureUnlocked(item.Track);
        if (selected.Count < 2) throw new ArgumentException("Select at least two clips to link.", nameof(ids));
        if (selected[0].Clip.GroupId.HasValue && selected.All(item => item.Clip.GroupId == selected[0].Clip.GroupId)) return;
        var group = Guid.NewGuid();
        Execute(_ => { foreach (var item in selected) item.Clip.GroupId = group; });
    }

    public void UnlinkClips(IEnumerable<Guid> ids)
    {
        var selected = SelectedClips(ids);
        foreach (var item in selected) EnsureUnlocked(item.Track);
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
            EnsureUnlocked(item.Track);
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
            EnsureUnlocked(item.Track);
            var trimmed = item.Clip.DeepClone();
            trimmed.StartFrame = checked(trimmed.StartFrame + startDelta);
            trimmed.SourceInFrame = checked(trimmed.SourceInFrame + sourceDelta);
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
        foreach (var item in selected) EnsureUnlocked(item.Track);
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
        foreach (var item in selected) EnsureUnlocked(item.Track);
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
        EnsureUnlocked(track);
        Execute(_ =>
        {
            if (gain.HasValue) clip.Gain = gain.Value;
            if (opacity.HasValue) clip.Opacity = opacity.Value;
            if (pan.HasValue) clip.Pan = pan.Value;
            if (fadeInFrames.HasValue) clip.FadeInFrames = fadeInFrames.Value;
            if (fadeOutFrames.HasValue) clip.FadeOutFrames = fadeOutFrames.Value;
        });
    }

    public void SetCropKeyframe(Guid clipId, long localFrame, CropState state, CropInterpolation interpolation = CropInterpolation.Linear)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track);
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
        EnsureUnlocked(track);
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
        EnsureUnlocked(track);
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
        EnsureUnlocked(track);
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
    private static void EnsureVideoCrop(TimelineTrack track) { if (track.Kind != TrackKind.Video) throw new InvalidOperationException("Crop is only available for video clips."); }
    private static void EnsureCompatible(MediaAsset media, TimelineTrack track)
    {
        if (track.Kind == TrackKind.Video && !media.HasVideo || track.Kind == TrackKind.Audio && !media.HasAudio) throw new InvalidOperationException("The media has no stream for this track.");
    }
    private void EnsureSourceBounds(TimelineClip clip, MediaAsset media)
    {
        if (clip.DurationFrames <= 0 || clip.SourceInFrame < 0 || clip.SourceInFrame > long.MaxValue - clip.DurationFrames) throw new ArgumentOutOfRangeException(nameof(clip), "Clip source range is invalid.");
        var available = Project.FrameRate.Frames(media.DurationSeconds);
        if (available > 0 && clip.SourceInFrame + clip.DurationFrames > available) throw new ArgumentOutOfRangeException(nameof(clip), "Clip extends beyond the source media.");
    }
    private void Restore(ProjectDocument snapshot)
    {
        var copy = snapshot.DeepClone();
        Project.Name = copy.Name; Project.Width = copy.Width; Project.Height = copy.Height; Project.FrameRate = copy.FrameRate;
        Project.Media = copy.Media; Project.Tracks = copy.Tracks;
    }
}
