using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenRend.Core;

/// <summary>
/// Sidechain audio ducking analytical engine.
/// Evaluates voice presence intervals on controller tracks, calculates dynamic envelope multipliers,
/// and generates frame-accurate volume envelopes matching Vegas Pro Auto Ducking.
/// </summary>
public static class AudioDuckingEngine
{
    /// <summary>
    /// Represents an active speech/narration interval in project integer frames.
    /// </summary>
    public readonly record struct SpeechSpan(long StartFrame, long EndFrame);

    /// <summary>
    /// Identifies and merges all active speech spans across relevant controller tracks in integer frames.
    /// </summary>
    public static List<SpeechSpan> GetActiveSpeechSpans(ProjectDocument project, Guid? controllerTrackId)
    {
        var spans = new List<SpeechSpan>();

        var controllerTracks = project.Tracks.Where(t =>
            t.Kind == TrackKind.Audio &&
            t.DuckingRole == AudioDuckingRole.Controller &&
            !t.IsMuted &&
            (!controllerTrackId.HasValue || t.Id == controllerTrackId.Value));

        foreach (var track in controllerTracks)
        {
            if (track.Gain <= 0) continue;

            foreach (var clip in track.Clips)
            {
                if (clip.IsMuted || clip.Gain <= 0 || clip.DurationFrames <= 0) continue;
                spans.Add(new SpeechSpan(clip.StartFrame, clip.EndFrame));
            }
        }

        if (spans.Count <= 1) return spans;

        // Sort by start frame and merge overlapping or contiguous spans
        spans.Sort((a, b) => a.StartFrame.CompareTo(b.StartFrame));

        var merged = new List<SpeechSpan>(spans.Count);
        var current = spans[0];

        for (var i = 1; i < spans.Count; i++)
        {
            var next = spans[i];
            if (next.StartFrame <= current.EndFrame)
            {
                current = new SpeechSpan(current.StartFrame, Math.Max(current.EndFrame, next.EndFrame));
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);
        return merged;
    }

    /// <summary>
    /// Evaluates the realtime analytical ducking gain multiplier [0.0, 1.0] for a listener track at a given frame.
    /// </summary>
    public static double EvaluateDuckingGain(ProjectDocument project, TimelineTrack listenerTrack, long frame)
    {
        if (listenerTrack.DuckingRole != AudioDuckingRole.Listener) return 1.0;
        var settings = listenerTrack.DuckingSettings;
        if (settings is null || !settings.IsEnabled) return 1.0;

        var spans = GetActiveSpeechSpans(project, settings.ControllerTrackId);
        if (spans.Count == 0) return 1.0;

        return EvaluateDuckingGain(spans, settings, project.FrameRate, frame);
    }

    /// <summary>
    /// Evaluates ducking gain from pre-computed speech spans.
    /// </summary>
    public static double EvaluateDuckingGain(IReadOnlyList<SpeechSpan> spans, AudioDuckingSettings settings, Rational frameRate, long frame)
    {
        if (spans.Count == 0 || !settings.IsEnabled) return 1.0;

        var fps = frameRate.Value;
        var attackFrames = Math.Max(1L, (long)Math.Round(settings.AttackMs / 1000.0 * fps));
        var holdFrames = Math.Max(0L, (long)Math.Round(settings.HoldMs / 1000.0 * fps));
        var releaseFrames = Math.Max(1L, (long)Math.Round(settings.ReleaseMs / 1000.0 * fps));
        var duckMul = settings.LinearDuckMultiplier;

        var minGain = 1.0;

        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];

            // 1. Fully inside speech span: ducked
            if (frame >= span.StartFrame && frame < span.EndFrame)
            {
                return duckMul;
            }

            // 2. In attack ramp before speech span: [start - attackFrames, start]
            if (frame >= span.StartFrame - attackFrames && frame < span.StartFrame)
            {
                var progress = (double)(frame - (span.StartFrame - attackFrames)) / attackFrames;
                // Smooth cosine / S-curve interpolation from 1.0 down to duckMul
                var factor = 0.5 * (1.0 + Math.Cos(Math.PI * progress));
                var gain = duckMul + (1.0 - duckMul) * factor;
                if (gain < minGain) minGain = gain;
            }

            // 3. In hold period after speech span: [end, end + holdFrames]
            if (frame >= span.EndFrame && frame < span.EndFrame + holdFrames)
            {
                if (duckMul < minGain) minGain = duckMul;
            }

            // 4. In release recovery ramp after hold: [end + holdFrames, end + holdFrames + releaseFrames]
            if (frame >= span.EndFrame + holdFrames && frame < span.EndFrame + holdFrames + releaseFrames)
            {
                var progress = (double)(frame - (span.EndFrame + holdFrames)) / releaseFrames;
                // Smooth cosine / S-curve interpolation from duckMul up to 1.0
                var factor = 0.5 * (1.0 - Math.Cos(Math.PI * progress));
                var gain = duckMul + (1.0 - duckMul) * factor;
                if (gain < minGain) minGain = gain;
            }
        }

        return Math.Clamp(minGain, duckMul, 1.0);
    }

    /// <summary>
    /// Generates or replaces a frame-accurate volume ClipEnvelope on the listener track with exact ducking keyframes.
    /// This matches Vegas Pro's "Create Ducking Envelope" / "Bake Ducking to Track Envelope" feature.
    /// </summary>
    public static ClipEnvelope BakeDuckingToEnvelope(ProjectDocument project, TimelineTrack listenerTrack)
    {
        if (listenerTrack.DuckingRole != AudioDuckingRole.Listener)
            throw new InvalidOperationException("Track is not configured as an Auto Ducking Listener.");

        var settings = listenerTrack.DuckingSettings ?? new AudioDuckingSettings();
        settings.Validate();

        var spans = GetActiveSpeechSpans(project, settings.ControllerTrackId);
        var fps = project.FrameRate.Value;
        var attackFrames = Math.Max(1L, (long)Math.Round(settings.AttackMs / 1000.0 * fps));
        var holdFrames = Math.Max(0L, (long)Math.Round(settings.HoldMs / 1000.0 * fps));
        var releaseFrames = Math.Max(1L, (long)Math.Round(settings.ReleaseMs / 1000.0 * fps));
        var duckMul = settings.LinearDuckMultiplier;

        listenerTrack.Envelopes ??= [];
        var env = listenerTrack.Envelopes.FirstOrDefault(e => e.Type == EnvelopeType.Volume);
        if (env is null)
        {
            env = new ClipEnvelope
            {
                Type = EnvelopeType.Volume,
                IsEnabled = true
            };
            listenerTrack.Envelopes.Add(env);
        }

        env.Points.Clear();

        if (spans.Count == 0)
        {
            // Flat envelope at unity gain
            env.Points.Add(new EnvelopePoint(0, 1.0, EnvelopeCurveType.Linear));
            return env;
        }

        // Merge intervals that would overlap due to attack / hold / release transitions
        // An interval starts at Math.Max(0, span.StartFrame - attackFrames) and recovers at span.EndFrame + holdFrames + releaseFrames
        var duckIntervals = new List<(long PreAttack, long AttackEnd, long HoldEnd, long ReleaseEnd)>();

        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            var pre = Math.Max(0, span.StartFrame - attackFrames);
            var attEnd = span.StartFrame;
            var hEnd = span.EndFrame + holdFrames;
            var relEnd = hEnd + releaseFrames;

            if (duckIntervals.Count > 0 && pre <= duckIntervals[^1].ReleaseEnd)
            {
                // Merge with previous interval so volume stays ducked without jitter
                var prev = duckIntervals[^1];
                duckIntervals[^1] = (prev.PreAttack, prev.AttackEnd, Math.Max(prev.HoldEnd, hEnd), Math.Max(prev.ReleaseEnd, relEnd));
            }
            else
            {
                duckIntervals.Add((pre, attEnd, hEnd, relEnd));
            }
        }

        // Initial unity point at timeline start if needed
        if (duckIntervals[0].PreAttack > 0)
        {
            env.Points.Add(new EnvelopePoint(0, 1.0, EnvelopeCurveType.Linear));
        }

        foreach (var (preAttack, attackEnd, holdEnd, releaseEnd) in duckIntervals)
        {
            // Point 1: Prior to attack ramp (unity gain 1.0)
            if (env.Points.Count == 0 || env.Points[^1].Frame < preAttack)
            {
                env.Points.Add(new EnvelopePoint(preAttack, 1.0, EnvelopeCurveType.Smooth));
            }

            // Point 2: At speech onset (attenuated duck level)
            if (attackEnd > preAttack)
            {
                env.Points.Add(new EnvelopePoint(attackEnd, duckMul, EnvelopeCurveType.Linear));
            }
            else if (env.Points.Count > 0 && env.Points[^1].Frame == attackEnd)
            {
                env.Points[^1].Value = duckMul;
            }

            // Point 3: At hold completion before release recovery
            if (holdEnd > attackEnd)
            {
                env.Points.Add(new EnvelopePoint(holdEnd, duckMul, EnvelopeCurveType.Smooth));
            }

            // Point 4: At release recovery (restored to unity gain 1.0)
            env.Points.Add(new EnvelopePoint(releaseEnd, 1.0, EnvelopeCurveType.Linear));
        }

        // Final point out to project duration if timeline extends beyond last recovery
        var totalProjectFrames = project.DurationFrames;
        if (totalProjectFrames > env.Points[^1].Frame)
        {
            env.Points.Add(new EnvelopePoint(totalProjectFrames, 1.0, EnvelopeCurveType.Linear));
        }

        return env;
    }
}
