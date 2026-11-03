using System;
using System.Collections.Generic;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public ClipEnvelope? GetClipEnvelope(Guid clipId, EnvelopeType type)
    {
        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        return clip?.Envelopes?.FirstOrDefault(e => e.Type == type);
    }

    public bool HasClipEnvelope(Guid clipId, EnvelopeType type)
    {
        return GetClipEnvelope(clipId, type) != null;
    }

    public void ToggleClipEnvelope(Guid clipId, EnvelopeType type)
    {
        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip == null) return;

        if (HasClipEnvelope(clipId, type))
        {
            RemoveClipEnvelope(clipId, type);
        }
        else
        {
            AddClipEnvelope(clipId, type);
        }
    }

    public void AddClipEnvelope(Guid clipId, EnvelopeType type)
    {
        Execute(p =>
        {
            var clip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
            if (clip == null) return;

            clip.Envelopes ??= [];
            if (clip.Envelopes.Any(e => e.Type == type)) return;

            var env = new ClipEnvelope
            {
                Type = type,
                IsEnabled = true
            };

            // Initialize with default anchor points across the duration of the event
            var defaultValue = env.DefaultValue;
            env.Points.Add(new EnvelopePoint(0, defaultValue, EnvelopeCurveType.Linear));
            env.Points.Add(new EnvelopePoint(clip.DurationFrames, defaultValue, EnvelopeCurveType.Linear));

            clip.Envelopes.Add(env);
        });
    }

    public void RemoveClipEnvelope(Guid clipId, EnvelopeType type)
    {
        Execute(p =>
        {
            var clip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
            if (clip?.Envelopes == null) return;

            clip.Envelopes.RemoveAll(e => e.Type == type);
            if (clip.Envelopes.Count == 0) clip.Envelopes = null;
        });
    }

    public void SetEnvelopePoint(Guid clipId, EnvelopeType type, long frame, double value, EnvelopeCurveType curve = EnvelopeCurveType.Linear)
    {
        Execute(p =>
        {
            var clip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
            if (clip == null) return;

            clip.Envelopes ??= [];
            var env = clip.Envelopes.FirstOrDefault(e => e.Type == type);
            if (env == null)
            {
                env = new ClipEnvelope { Type = type, IsEnabled = true };
                clip.Envelopes.Add(env);
            }

            var clampedFrame = Math.Clamp(frame, 0, clip.DurationFrames);
            var clampedValue = Math.Clamp(value, env.MinValue, env.MaxValue);

            // Check if a point already exists close to this frame (within 1 frame)
            var existing = env.Points.FirstOrDefault(pt => Math.Abs(pt.Frame - clampedFrame) <= 1);
            if (existing != null)
            {
                existing.Frame = clampedFrame;
                existing.Value = clampedValue;
                existing.Curve = curve;
            }
            else
            {
                env.Points.Add(new EnvelopePoint(clampedFrame, clampedValue, curve));
            }

            env.Points.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        });
    }

    public void RemoveEnvelopePoint(Guid clipId, EnvelopeType type, long frame)
    {
        Execute(p =>
        {
            var clip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
            var env = clip?.Envelopes?.FirstOrDefault(e => e.Type == type);
            if (env == null || env.Points.Count <= 2) return; // Keep boundary points

            // Remove point nearest to specified frame
            var nearest = env.Points.OrderBy(pt => Math.Abs(pt.Frame - frame)).FirstOrDefault();
            if (nearest != null && Math.Abs(nearest.Frame - frame) <= 5)
            {
                // Do not remove outer bounds
                if (nearest.Frame != 0 && nearest.Frame != clip!.DurationFrames)
                {
                    env.Points.Remove(nearest);
                }
            }
        });
    }

    public void SetEnvelopePointCurve(Guid clipId, EnvelopeType type, long frame, EnvelopeCurveType curve)
    {
        Execute(p =>
        {
            var clip = p.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
            var env = clip?.Envelopes?.FirstOrDefault(e => e.Type == type);
            if (env == null) return;

            var nearest = env.Points.OrderBy(pt => Math.Abs(pt.Frame - frame)).FirstOrDefault();
            if (nearest != null)
            {
                nearest.Curve = curve;
            }
        });
    }

    public ClipEnvelope? GetTrackEnvelope(Guid trackId, EnvelopeType type)
    {
        var track = Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        return track?.Envelopes?.FirstOrDefault(e => e.Type == type);
    }

    public bool HasTrackEnvelope(Guid trackId, EnvelopeType type)
    {
        return GetTrackEnvelope(trackId, type) != null;
    }

    public void ToggleTrackEnvelope(Guid trackId, EnvelopeType type)
    {
        var track = Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track == null) return;

        if (HasTrackEnvelope(trackId, type))
        {
            RemoveTrackEnvelope(trackId, type);
        }
        else
        {
            AddTrackEnvelope(trackId, type);
        }
    }

    public void AddTrackEnvelope(Guid trackId, EnvelopeType type)
    {
        Execute(p =>
        {
            var track = p.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track == null) return;

            track.Envelopes ??= [];
            if (track.Envelopes.Any(e => e.Type == type)) return;

            var env = new ClipEnvelope
            {
                Type = type,
                IsEnabled = true
            };

            var defaultValue = env.DefaultValue;
            var duration = Math.Max(p.DurationFrames, 300);
            env.Points.Add(new EnvelopePoint(0, defaultValue, EnvelopeCurveType.Linear));
            env.Points.Add(new EnvelopePoint(duration, defaultValue, EnvelopeCurveType.Linear));

            track.Envelopes.Add(env);
        });
    }

    public void RemoveTrackEnvelope(Guid trackId, EnvelopeType type)
    {
        Execute(p =>
        {
            var track = p.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track?.Envelopes == null) return;

            track.Envelopes.RemoveAll(e => e.Type == type);
            if (track.Envelopes.Count == 0) track.Envelopes = null;
        });
    }

    public void SetTrackEnvelopePoint(Guid trackId, EnvelopeType type, long frame, double value, EnvelopeCurveType curve = EnvelopeCurveType.Linear)
    {
        Execute(p =>
        {
            var track = p.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track == null) return;

            track.Envelopes ??= [];
            var env = track.Envelopes.FirstOrDefault(e => e.Type == type);
            if (env == null)
            {
                env = new ClipEnvelope { Type = type, IsEnabled = true };
                track.Envelopes.Add(env);
            }

            var clampedFrame = Math.Max(0, frame);
            var clampedValue = Math.Clamp(value, env.MinValue, env.MaxValue);

            var existing = env.Points.FirstOrDefault(pt => Math.Abs(pt.Frame - clampedFrame) <= 1);
            if (existing != null)
            {
                existing.Frame = clampedFrame;
                existing.Value = clampedValue;
                existing.Curve = curve;
            }
            else
            {
                env.Points.Add(new EnvelopePoint(clampedFrame, clampedValue, curve));
            }

            env.Points.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        });
    }

    public void RemoveTrackEnvelopePoint(Guid trackId, EnvelopeType type, long frame)
    {
        Execute(p =>
        {
            var track = p.Tracks.FirstOrDefault(t => t.Id == trackId);
            var env = track?.Envelopes?.FirstOrDefault(e => e.Type == type);
            if (env == null || env.Points.Count <= 1) return;

            var nearest = env.Points.OrderBy(pt => Math.Abs(pt.Frame - frame)).FirstOrDefault();
            if (nearest != null)
            {
                env.Points.Remove(nearest);
            }
        });
    }

    public void SetTrackEnvelopePointCurve(Guid trackId, EnvelopeType type, long frame, EnvelopeCurveType curve)
    {
        Execute(p =>
        {
            var track = p.Tracks.FirstOrDefault(t => t.Id == trackId);
            var env = track?.Envelopes?.FirstOrDefault(e => e.Type == type);
            if (env == null) return;

            var nearest = env.Points.OrderBy(pt => Math.Abs(pt.Frame - frame)).FirstOrDefault();
            if (nearest != null)
            {
                nearest.Curve = curve;
            }
        });
    }
}