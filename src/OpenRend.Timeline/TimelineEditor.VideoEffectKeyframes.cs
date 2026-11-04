using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public void SetScopedVideoEffectKeyframe(
        EffectScope scope,
        Guid targetId,
        Guid effectId,
        long frame,
        IReadOnlyDictionary<string, double>? parameters = null,
        double? amount = null,
        double? secondaryAmount = null,
        EffectInterpolation interpolation = EffectInterpolation.Linear,
        double smoothness = 100.0)
    {
        if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame), "Keyframe frame must be non-negative.");

        Execute(p =>
        {
            var effects = GetScopedVideoEffects(scope, targetId);
            var effect = effects.FirstOrDefault(e => e.Id == effectId)
                ?? throw new KeyNotFoundException("Video effect not found.");

            effect.Animation ??= new VideoEffectAnimation { IsEnabled = true };
            effect.Animation.IsEnabled = true;

            var keyframes = effect.Animation.Keyframes;
            var existing = keyframes.FirstOrDefault(k => k.Frame == frame);

            var keyParams = parameters != null
                ? new Dictionary<string, double>(parameters, StringComparer.Ordinal)
                : new Dictionary<string, double>(effect.Parameters, StringComparer.Ordinal);

            if (existing != null)
            {
                existing.Amount = amount ?? effect.Amount;
                existing.SecondaryAmount = secondaryAmount ?? effect.SecondaryAmount;
                existing.Parameters = keyParams;
                existing.Interpolation = interpolation;
                existing.Smoothness = smoothness;
            }
            else
            {
                var newKey = new VideoEffectKeyframe
                {
                    Frame = frame,
                    Amount = amount ?? effect.Amount,
                    SecondaryAmount = secondaryAmount ?? effect.SecondaryAmount,
                    Parameters = keyParams,
                    Interpolation = interpolation,
                    Smoothness = smoothness
                };
                keyframes.Add(newKey);
                keyframes.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            }

            effect.Animation.Validate();
        });
    }

    public void RemoveScopedVideoEffectKeyframe(EffectScope scope, Guid targetId, Guid effectId, long frame)
    {
        Execute(p =>
        {
            var effects = GetScopedVideoEffects(scope, targetId);
            var effect = effects.FirstOrDefault(e => e.Id == effectId)
                ?? throw new KeyNotFoundException("Video effect not found.");

            if (effect.Animation == null) return;
            var idx = effect.Animation.Keyframes.FindIndex(k => k.Frame == frame);
            if (idx >= 0)
            {
                effect.Animation.Keyframes.RemoveAt(idx);
                if (effect.Animation.Keyframes.Count == 0)
                {
                    effect.Animation.IsEnabled = false;
                }
            }
        });
    }

    public void SetScopedVideoEffectKeyframeInterpolation(
        EffectScope scope,
        Guid targetId,
        Guid effectId,
        long frame,
        EffectInterpolation interpolation,
        double smoothness = 100.0)
    {
        Execute(p =>
        {
            var effects = GetScopedVideoEffects(scope, targetId);
            var effect = effects.FirstOrDefault(e => e.Id == effectId)
                ?? throw new KeyNotFoundException("Video effect not found.");

            if (effect.Animation == null) return;
            var kf = effect.Animation.Keyframes.FirstOrDefault(k => k.Frame == frame)
                ?? throw new KeyNotFoundException("Keyframe not found at specified frame.");

            kf.Interpolation = interpolation;
            kf.Smoothness = smoothness;
        });
    }

    public void MoveScopedVideoEffectKeyframe(EffectScope scope, Guid targetId, Guid effectId, long oldFrame, long newFrame)
    {
        if (newFrame < 0) throw new ArgumentOutOfRangeException(nameof(newFrame));
        if (oldFrame == newFrame) return;

        Execute(p =>
        {
            var effects = GetScopedVideoEffects(scope, targetId);
            var effect = effects.FirstOrDefault(e => e.Id == effectId)
                ?? throw new KeyNotFoundException("Video effect not found.");

            if (effect.Animation == null) return;
            var kf = effect.Animation.Keyframes.FirstOrDefault(k => k.Frame == oldFrame)
                ?? throw new KeyNotFoundException("Keyframe not found at source frame.");

            // Remove any existing at newFrame to replace
            effect.Animation.Keyframes.RemoveAll(k => k.Frame == newFrame);
            kf.Frame = newFrame;
            effect.Animation.Keyframes.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            effect.Animation.Validate();
        });
    }

    public void SetScopedVideoEffectAnimationEnabled(EffectScope scope, Guid targetId, Guid effectId, bool enabled)
    {
        Execute(p =>
        {
            var effects = GetScopedVideoEffects(scope, targetId);
            var effect = effects.FirstOrDefault(e => e.Id == effectId)
                ?? throw new KeyNotFoundException("Video effect not found.");

            if (effect.Animation == null)
            {
                if (!enabled) return;
                effect.Animation = new VideoEffectAnimation { IsEnabled = true };
                // Initialize with first keyframe at frame 0 matching current static parameters
                effect.Animation.Keyframes.Add(new VideoEffectKeyframe
                {
                    Frame = 0,
                    Amount = effect.Amount,
                    SecondaryAmount = effect.SecondaryAmount,
                    Parameters = new Dictionary<string, double>(effect.Parameters, StringComparer.Ordinal),
                    Interpolation = EffectInterpolation.Linear
                });
            }
            else
            {
                effect.Animation.IsEnabled = enabled;
            }
        });
    }
}

