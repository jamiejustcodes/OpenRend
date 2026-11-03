using System;
using System.Collections.Generic;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    /// <summary>
    /// Adds a new take to an event/clip, establishing a multi-take collection if one does not already exist.
    /// In 1:1 Vegas Pro architecture, events can hold multiple takes that can be auditioned using 'T' and 'Shift+T'.
    /// </summary>
    public void AddTake(Guid clipId, Guid mediaId, long sourceInFrame = 0, string? name = null, bool setActive = true)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);

        var media = Project.Media.FirstOrDefault(m => m.Id == mediaId)
            ?? throw new InvalidOperationException($"Media {mediaId} does not exist in the project.");

        if (track.Kind == TrackKind.Video && !media.HasVideo)
            throw new InvalidOperationException("Take requires a video media stream for video clips.");
        if (track.Kind == TrackKind.Audio && !media.HasAudio)
            throw new InvalidOperationException("Take requires an audio media stream for audio clips.");

        Execute(_ =>
        {
            if (clip.Takes is null || clip.Takes.Count == 0)
            {
                // Convert single-source clip into a multi-take event with its initial take preserved.
                var initialTake = new TimelineTake
                {
                    Id = Guid.NewGuid(),
                    MediaId = clip.MediaId,
                    Name = clip.Name,
                    SourceInFrame = clip.SourceInFrame,
                    VideoSourceMode = clip.VideoSourceMode,
                    IsReversed = clip.IsReversed,
                    PlaybackRate = clip.PlaybackRate
                };
                clip.Takes = [initialTake];
                clip.ActiveTakeIndex = 0;
            }

            var takeName = !string.IsNullOrWhiteSpace(name) ? name : media.Name;
            var newTake = new TimelineTake
            {
                Id = Guid.NewGuid(),
                MediaId = mediaId,
                Name = takeName,
                SourceInFrame = Math.Max(0, sourceInFrame),
                VideoSourceMode = VideoSourceMode.Bounded
            };

            clip.Takes.Add(newTake);
            if (setActive)
            {
                clip.ActiveTakeIndex = clip.Takes.Count - 1;
                SyncActiveTake(clip, newTake);
            }
        });
    }

    /// <summary>
    /// Switches the specified event to its next take (cyclic), equivalent to pressing 'T' in Vegas Pro.
    /// </summary>
    public void NextTake(Guid clipId)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (clip.Takes is null || clip.Takes.Count <= 1) return;

        Execute(_ =>
        {
            clip.ActiveTakeIndex = (clip.ActiveTakeIndex + 1) % clip.Takes.Count;
            SyncActiveTake(clip, clip.Takes[clip.ActiveTakeIndex]);
        });
    }

    /// <summary>
    /// Switches the specified event to its previous take (cyclic), equivalent to pressing 'Shift+T' in Vegas Pro.
    /// </summary>
    public void PreviousTake(Guid clipId)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (clip.Takes is null || clip.Takes.Count <= 1) return;

        Execute(_ =>
        {
            clip.ActiveTakeIndex = (clip.ActiveTakeIndex - 1 + clip.Takes.Count) % clip.Takes.Count;
            SyncActiveTake(clip, clip.Takes[clip.ActiveTakeIndex]);
        });
    }

    /// <summary>
    /// Sets the active take index on an event.
    /// </summary>
    public void SetActiveTake(Guid clipId, int takeIndex)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (clip.Takes is null || takeIndex < 0 || takeIndex >= clip.Takes.Count)
            throw new ArgumentOutOfRangeException(nameof(takeIndex), "Take index is out of bounds.");

        if (clip.ActiveTakeIndex == takeIndex) return;

        Execute(_ =>
        {
            clip.ActiveTakeIndex = takeIndex;
            SyncActiveTake(clip, clip.Takes[takeIndex]);
        });
    }

    /// <summary>
    /// Deletes the active take from the event. If multiple takes remain, the active index is adjusted.
    /// </summary>
    public void DeleteActiveTake(Guid clipId)
    {
        var (track, clip) = FindClip(clipId);
        EnsureUnlocked(track, clip);
        if (clip.Takes is null || clip.Takes.Count <= 1) return;

        Execute(_ =>
        {
            clip.Takes.RemoveAt(clip.ActiveTakeIndex);
            if (clip.ActiveTakeIndex >= clip.Takes.Count)
                clip.ActiveTakeIndex = clip.Takes.Count - 1;
            SyncActiveTake(clip, clip.Takes[clip.ActiveTakeIndex]);
        });
    }

    /// <summary>
    /// Cycles to the next take on all selected clips that contain multiple takes.
    /// </summary>
    public void NextTakeOnSelection(IEnumerable<Guid> clipIds)
    {
        var ids = clipIds.ToList();
        var targetClips = Project.Tracks.SelectMany(t => t.Clips)
            .Where(c => ids.Contains(c.Id) && c.Takes != null && c.Takes.Count > 1)
            .ToList();

        if (targetClips.Count == 0) return;

        Execute(_ =>
        {
            foreach (var clip in targetClips)
            {
                clip.ActiveTakeIndex = (clip.ActiveTakeIndex + 1) % clip.Takes!.Count;
                SyncActiveTake(clip, clip.Takes[clip.ActiveTakeIndex]);
            }
        });
    }

    /// <summary>
    /// Cycles to the previous take on all selected clips that contain multiple takes.
    /// </summary>
    public void PreviousTakeOnSelection(IEnumerable<Guid> clipIds)
    {
        var ids = clipIds.ToList();
        var targetClips = Project.Tracks.SelectMany(t => t.Clips)
            .Where(c => ids.Contains(c.Id) && c.Takes != null && c.Takes.Count > 1)
            .ToList();

        if (targetClips.Count == 0) return;

        Execute(_ =>
        {
            foreach (var clip in targetClips)
            {
                clip.ActiveTakeIndex = (clip.ActiveTakeIndex - 1 + clip.Takes!.Count) % clip.Takes.Count;
                SyncActiveTake(clip, clip.Takes[clip.ActiveTakeIndex]);
            }
        });
    }

    private static void SyncActiveTake(TimelineClip clip, TimelineTake take)
    {
        clip.MediaId = take.MediaId;
        clip.SourceInFrame = take.SourceInFrame;
        clip.VideoSourceMode = take.VideoSourceMode;
        clip.Name = take.Name;
        clip.IsReversed = take.IsReversed;
        clip.PlaybackRate = take.PlaybackRate;
    }
}
