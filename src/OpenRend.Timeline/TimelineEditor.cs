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

    public long SnapFrame(long frame, long toleranceFrames, Guid? excludeClipId = null, long? playheadFrame = null)
    {
        if (toleranceFrames < 0) throw new ArgumentOutOfRangeException(nameof(toleranceFrames));
        var candidates = Project.Tracks.SelectMany(t => t.Clips).Where(c => c.Id != excludeClipId).SelectMany(c => new[] { c.StartFrame, c.EndFrame }).Append(0);
        if (playheadFrame.HasValue) candidates = candidates.Append(playheadFrame.Value);
        // Decimal subtraction avoids overflow while comparing user-provided positions.
        return candidates.Select(f => (Frame: f, Distance: Math.Abs((decimal)f - frame))).Where(c => c.Distance <= toleranceFrames).OrderBy(c => c.Distance).ThenBy(c => c.Frame).Select(c => c.Frame).DefaultIfEmpty(Math.Max(0, frame)).First();
    }

    private TimelineTrack FindTrack(Guid id) => Project.Tracks.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException("Track was not found.");
    private (TimelineTrack Track, TimelineClip Clip) FindClip(Guid id)
    {
        foreach (var track in Project.Tracks)
            foreach (var clip in track.Clips)
                if (clip.Id == id) return (track, clip);
        throw new KeyNotFoundException("Clip was not found.");
    }
    private static void EnsureUnlocked(TimelineTrack track) { if (track.IsLocked) throw new InvalidOperationException("Track is locked."); }
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
