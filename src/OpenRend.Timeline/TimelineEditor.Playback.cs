using System;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public partial class TimelineEditor
{
    public void SetClipReverse(Guid clipId, bool isReversed)
    {
        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is null || clip.IsReversed == isReversed) return;

        Execute(_ =>
        {
            clip.IsReversed = isReversed;
            if (clip.Takes != null && clip.ActiveTakeIndex >= 0 && clip.ActiveTakeIndex < clip.Takes.Count)
            {
                clip.Takes[clip.ActiveTakeIndex].IsReversed = isReversed;
            }
        });
    }

    public void ToggleClipReverse(Guid clipId)
    {
        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is null) return;
        SetClipReverse(clipId, !clip.IsReversed);
    }

    public void ToggleClipReverseOnSelection(Guid[]? selection = null)
    {
        var targetClips = Project.Tracks.Where(t => !t.IsLocked)
            .SelectMany(t => t.Clips.Where(c => (selection != null ? selection.Contains(c.Id) : true) && !c.IsLocked))
            .ToArray();

        if (targetClips.Length == 0) return;

        // If any clip is not reversed, reverse all; otherwise unreverse all.
        var targetState = !targetClips.All(c => c.IsReversed);

        Execute(_ =>
        {
            foreach (var clip in targetClips)
            {
                clip.IsReversed = targetState;
                if (clip.Takes != null && clip.ActiveTakeIndex >= 0 && clip.ActiveTakeIndex < clip.Takes.Count)
                {
                    clip.Takes[clip.ActiveTakeIndex].IsReversed = targetState;
                }
            }
        });
    }

    public void SetClipPlaybackRate(Guid clipId, double rate)
    {
        if (!double.IsFinite(rate) || Math.Abs(rate) < 0.05 || Math.Abs(rate) > 10.0)
            throw new ArgumentOutOfRangeException(nameof(rate), "Playback rate must be between 0.05 and 10.0.");

        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is null || Math.Abs(clip.PlaybackRate - rate) < 1e-6) return;

        Execute(_ =>
        {
            clip.PlaybackRate = rate;
            if (clip.Takes != null && clip.ActiveTakeIndex >= 0 && clip.ActiveTakeIndex < clip.Takes.Count)
            {
                clip.Takes[clip.ActiveTakeIndex].PlaybackRate = rate;
            }
        });
    }

    public void SetClipUnderSampleRate(Guid clipId, double rate)
    {
        if (!double.IsFinite(rate) || rate < 0.001 || rate > 1.0)
            throw new ArgumentOutOfRangeException(nameof(rate), "UnderSample rate must be between 0.001 and 1.0.");

        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is null || Math.Abs(clip.UnderSampleRate - rate) < 1e-6) return;

        Execute(_ =>
        {
            clip.UnderSampleRate = rate;
            if (clip.Takes != null && clip.ActiveTakeIndex >= 0 && clip.ActiveTakeIndex < clip.Takes.Count)
            {
                clip.Takes[clip.ActiveTakeIndex].UnderSampleRate = rate;
            }
        });
    }

    public void SetClipResampleMode(Guid clipId, VideoResampleMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));

        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is null || clip.ResampleMode == mode) return;

        Execute(_ =>
        {
            clip.ResampleMode = mode;
            if (clip.Takes != null && clip.ActiveTakeIndex >= 0 && clip.ActiveTakeIndex < clip.Takes.Count)
            {
                clip.Takes[clip.ActiveTakeIndex].ResampleMode = mode;
            }
        });
    }

    public void SetClipMaintainAspectRatio(Guid clipId, bool maintain)
    {
        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is null || clip.MaintainAspectRatio == maintain) return;

        Execute(_ =>
        {
            clip.MaintainAspectRatio = maintain;
            if (clip.Takes != null && clip.ActiveTakeIndex >= 0 && clip.ActiveTakeIndex < clip.Takes.Count)
            {
                clip.Takes[clip.ActiveTakeIndex].MaintainAspectRatio = maintain;
            }
        });
    }

    public void SetClipReduceInterlace(Guid clipId, bool reduce)
    {
        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is null || clip.ReduceInterlace == reduce) return;

        Execute(_ =>
        {
            clip.ReduceInterlace = reduce;
            if (clip.Takes != null && clip.ActiveTakeIndex >= 0 && clip.ActiveTakeIndex < clip.Takes.Count)
            {
                clip.Takes[clip.ActiveTakeIndex].ReduceInterlace = reduce;
            }
        });
    }

    public void SetClipNormalize(Guid clipId, bool normalize)
    {
        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is null || clip.Normalize == normalize) return;

        Execute(_ =>
        {
            clip.Normalize = normalize;
            if (clip.Takes != null && clip.ActiveTakeIndex >= 0 && clip.ActiveTakeIndex < clip.Takes.Count)
            {
                clip.Takes[clip.ActiveTakeIndex].Normalize = normalize;
            }
        });
    }

    public void SetClipInvertPhase(Guid clipId, bool invert)
    {
        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is null || clip.InvertPhase == invert) return;

        Execute(_ =>
        {
            clip.InvertPhase = invert;
            if (clip.Takes != null && clip.ActiveTakeIndex >= 0 && clip.ActiveTakeIndex < clip.Takes.Count)
            {
                clip.Takes[clip.ActiveTakeIndex].InvertPhase = invert;
            }
        });
    }
}
