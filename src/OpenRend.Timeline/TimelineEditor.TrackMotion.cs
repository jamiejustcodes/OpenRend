using System;
using System.IO;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public TrackMotion? GetTrackMotion(Guid trackId)
    {
        var track = FindTrack(trackId);
        return track.Motion?.DeepClone();
    }

    public void SetTrackMotion(Guid trackId, TrackMotion? motion)
    {
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (track.Kind != TrackKind.Video) throw new InvalidOperationException("Track motion is only available on video tracks.");

        if (motion is not null)
        {
            motion.Validate(Project.Width, Project.Height);
        }

        Execute(_ => track.Motion = motion?.DeepClone());
    }

    public void SetTrackMotionKeyframe(Guid trackId, TrackMotionKeyframe keyframe)
    {
        ArgumentNullException.ThrowIfNull(keyframe);
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (track.Kind != TrackKind.Video) throw new InvalidOperationException("Track motion is only available on video tracks.");

        keyframe.Validate();
        var motion = track.Motion?.DeepClone() ?? new TrackMotion();
        var existing = motion.MotionKeyframes.FirstOrDefault(k => k.Frame == keyframe.Frame);
        if (existing is null)
        {
            motion.MotionKeyframes.Add(keyframe.DeepClone());
            motion.MotionKeyframes.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        }
        else
        {
            var idx = motion.MotionKeyframes.IndexOf(existing);
            motion.MotionKeyframes[idx] = keyframe.DeepClone();
        }

        motion.Validate(Project.Width, Project.Height);
        Execute(_ => track.Motion = motion);
    }

    public void RemoveTrackMotionKeyframe(Guid trackId, long frame)
    {
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (track.Kind != TrackKind.Video || track.Motion is null) return;

        if (track.Motion.MotionKeyframes.All(k => k.Frame != frame)) return;

        var motion = track.Motion.DeepClone();
        motion.MotionKeyframes.RemoveAll(k => k.Frame == frame);
        if (motion.MotionKeyframes.Count == 0 && !motion.ShadowEnabled && !motion.GlowEnabled)
        {
            Execute(_ => track.Motion = null);
        }
        else
        {
            Execute(_ => track.Motion = motion);
        }
    }

    public void ResetTrackMotion(Guid trackId)
    {
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (track.Kind != TrackKind.Video || track.Motion is null) return;

        Execute(_ => track.Motion = null);
    }

    public void SetTrackShadowEnabled(Guid trackId, bool enabled)
    {
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (track.Kind != TrackKind.Video) throw new InvalidOperationException("Track motion is only available on video tracks.");

        var motion = track.Motion?.DeepClone() ?? new TrackMotion();
        if (motion.ShadowEnabled == enabled) return;
        motion.ShadowEnabled = enabled;
        Execute(_ => track.Motion = motion);
    }

    public void SetTrackGlowEnabled(Guid trackId, bool enabled)
    {
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (track.Kind != TrackKind.Video) throw new InvalidOperationException("Track motion is only available on video tracks.");

        var motion = track.Motion?.DeepClone() ?? new TrackMotion();
        if (motion.GlowEnabled == enabled) return;
        motion.GlowEnabled = enabled;
        Execute(_ => track.Motion = motion);
    }
}

