using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace OpenRend.Core;

public enum EffectInterpolation
{
    Linear,
    Hold,
    Slow,
    Fast,
    Smooth,
    Sharp
}

/// <summary>
/// 1:1 Vegas Pro Video FX Keyframe.
/// Represents an animated state of video effect parameters at an integer frame.
/// </summary>
public sealed class VideoEffectKeyframe
{
    public long Frame { get; set; }
    public double Amount { get; set; }
    public double SecondaryAmount { get; set; }
    public Dictionary<string, double> Parameters { get; set; } = [];
    public EffectInterpolation Interpolation { get; set; } = EffectInterpolation.Linear;
    public double Smoothness { get; set; } = 100.0;

    public VideoEffectKeyframe DeepClone() => new()
    {
        Frame = Frame,
        Amount = Amount,
        SecondaryAmount = SecondaryAmount,
        Parameters = new Dictionary<string, double>(Parameters, StringComparer.Ordinal),
        Interpolation = Interpolation,
        Smoothness = Smoothness
    };

    public void Validate()
    {
        if (Frame < 0) throw new InvalidDataException("Keyframe frame must be non-negative.");
        if (!double.IsFinite(Amount) || !double.IsFinite(SecondaryAmount) || !double.IsFinite(Smoothness))
            throw new InvalidDataException("Keyframe parameter values must be finite numbers.");
        if (Parameters is null) throw new InvalidDataException("Keyframe parameters dictionary must not be null.");
        foreach (var (key, val) in Parameters)
        {
            if (string.IsNullOrWhiteSpace(key) || !double.IsFinite(val))
                throw new InvalidDataException($"Invalid keyframe parameter '{key}'.");
        }
    }
}

/// <summary>
/// Ordered keyframe track for video effect parameter animation.
/// Evaluates parameter values at any integer frame using Vegas Pro curve models.
/// </summary>
public sealed class VideoEffectAnimation
{
    public bool IsEnabled { get; set; }
    public List<VideoEffectKeyframe> Keyframes { get; set; } = [];

    public VideoEffectAnimation DeepClone() => new()
    {
        IsEnabled = IsEnabled,
        Keyframes = Keyframes.Select(k => k.DeepClone()).ToList()
    };

    public void Validate()
    {
        if (Keyframes is null) throw new InvalidDataException("Keyframes collection cannot be null.");
        long lastFrame = -1;
        foreach (var kf in Keyframes)
        {
            if (kf is null) throw new InvalidDataException("Keyframe cannot be null.");
            kf.Validate();
            if (kf.Frame < lastFrame) throw new InvalidDataException("Keyframes must be sorted in chronological order.");
            lastFrame = kf.Frame;
        }
    }

    /// <summary>
    /// Evaluates the effect parameters at the given frame.
    /// Returns the interpolated Amount, SecondaryAmount, and parameter dictionary.
    /// </summary>
    public (double Amount, double SecondaryAmount, Dictionary<string, double> Parameters) Evaluate(long frame, VideoEffect baseEffect)
    {
        ArgumentNullException.ThrowIfNull(baseEffect);

        if (!IsEnabled || Keyframes.Count == 0)
        {
            return (baseEffect.Amount, baseEffect.SecondaryAmount, new Dictionary<string, double>(baseEffect.Parameters, StringComparer.Ordinal));
        }

        if (Keyframes.Count == 1 || frame <= Keyframes[0].Frame)
        {
            var first = Keyframes[0];
            return (first.Amount, first.SecondaryAmount, new Dictionary<string, double>(first.Parameters, StringComparer.Ordinal));
        }

        if (frame >= Keyframes[^1].Frame)
        {
            var last = Keyframes[^1];
            return (last.Amount, last.SecondaryAmount, new Dictionary<string, double>(last.Parameters, StringComparer.Ordinal));
        }

        // Binary search for surrounding keyframes
        int low = 0;
        int high = Keyframes.Count - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (Keyframes[mid].Frame <= frame) low = mid;
            else high = mid;
        }

        var left = Keyframes[low];
        var right = Keyframes[high];

        if (left.Interpolation == EffectInterpolation.Hold)
        {
            return (left.Amount, left.SecondaryAmount, new Dictionary<string, double>(left.Parameters, StringComparer.Ordinal));
        }

        double span = right.Frame - left.Frame;
        double progress = span > 0 ? (double)(frame - left.Frame) / span : 0.0;
        double centered = progress - 0.5;
        double s = Math.Clamp(left.Smoothness / 100.0, 0.0, 1.0);

        double weight = left.Interpolation switch
        {
            EffectInterpolation.Slow => progress * progress,
            EffectInterpolation.Fast => 1.0 - (1.0 - progress) * (1.0 - progress),
            EffectInterpolation.Smooth => progress * (1.0 - s) + (progress * progress * (3.0 - 2.0 * progress)) * s,
            EffectInterpolation.Sharp => 0.5 + 4.0 * centered * centered * centered,
            _ => progress // Linear
        };

        double evalAmount = Mix(left.Amount, right.Amount, weight);
        double evalSecondary = Mix(left.SecondaryAmount, right.SecondaryAmount, weight);

        var evalParams = new Dictionary<string, double>(StringComparer.Ordinal);
        // Union of keys from base, left, and right
        var allKeys = new HashSet<string>(baseEffect.Parameters.Keys);
        foreach (var k in left.Parameters.Keys) allKeys.Add(k);
        foreach (var k in right.Parameters.Keys) allKeys.Add(k);

        foreach (var key in allKeys)
        {
            double vLeft = left.Parameters.TryGetValue(key, out var vl) ? vl : baseEffect.GetParameter(key);
            double vRight = right.Parameters.TryGetValue(key, out var vr) ? vr : baseEffect.GetParameter(key);
            evalParams[key] = Mix(vLeft, vRight, weight);
        }

        return (evalAmount, evalSecondary, evalParams);
    }

    private static double Mix(double a, double b, double weight) => a + (b - a) * weight;
}

