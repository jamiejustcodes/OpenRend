using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public void SetTrackInvertPhase(Guid trackId, bool invert)
    {
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (track.Kind != TrackKind.Audio) throw new InvalidOperationException("Phase inversion is only available for audio tracks.");
        if (track.InvertPhase == invert) return;

        Execute(p =>
        {
            var t = p.Tracks.First(x => x.Id == trackId);
            t.InvertPhase = invert;
        });
    }

    public void AddTrackAudioEffect(Guid trackId, AudioEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        effect.Validate();
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (track.Kind != TrackKind.Audio) throw new InvalidOperationException("Audio effects can only be added to audio tracks.");

        Execute(p =>
        {
            var t = p.Tracks.First(x => x.Id == trackId);
            t.AudioEffects.Add(effect.DeepClone());
        });
    }

    public void RemoveTrackAudioEffect(Guid trackId, Guid effectId)
    {
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (track.Kind != TrackKind.Audio) throw new InvalidOperationException("Audio effects can only be removed from audio tracks.");

        Execute(p =>
        {
            var t = p.Tracks.First(x => x.Id == trackId);
            var idx = t.AudioEffects.FindIndex(e => e.Id == effectId);
            if (idx >= 0) t.AudioEffects.RemoveAt(idx);
        });
    }

    public void SetTrackAudioEffectBypassed(Guid trackId, Guid effectId, bool bypassed)
    {
        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (track.Kind != TrackKind.Audio) throw new InvalidOperationException("Audio effects can only be modified on audio tracks.");

        Execute(p =>
        {
            var t = p.Tracks.First(x => x.Id == trackId);
            var fx = t.AudioEffects.FirstOrDefault(e => e.Id == effectId) ?? throw new KeyNotFoundException("Audio effect not found.");
            fx.IsBypassed = bypassed;
        });
    }

    public void UpdateTrackAudioEffectParameter(Guid trackId, Guid effectId, string paramName, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramName);
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));

        var track = FindTrack(trackId);
        EnsureUnlocked(track);
        if (track.Kind != TrackKind.Audio) throw new InvalidOperationException("Audio effects can only be modified on audio tracks.");

        Execute(p =>
        {
            var t = p.Tracks.First(x => x.Id == trackId);
            var fx = t.AudioEffects.FirstOrDefault(e => e.Id == effectId) ?? throw new KeyNotFoundException("Audio effect not found.");
            fx.Parameters[paramName] = value;
        });
    }

    public void SetMasterAudioGain(double gain)
    {
        if (!double.IsFinite(gain) || gain < 0) throw new ArgumentOutOfRangeException(nameof(gain));
        if (Math.Abs(Project.MasterAudioGain - gain) < 1e-5) return;

        Execute(p => p.MasterAudioGain = gain);
    }

    public void SetMasterAudioMute(bool mute)
    {
        if (Project.MasterAudioMute == mute) return;
        Execute(p => p.MasterAudioMute = mute);
    }

    public void SetMasterAudioDim(bool dim)
    {
        if (Project.MasterAudioDim == dim) return;
        Execute(p => p.MasterAudioDim = dim);
    }

    public void AddMasterAudioEffect(AudioEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        effect.Validate();

        Execute(p => p.MasterAudioEffects.Add(effect.DeepClone()));
    }

    public void RemoveMasterAudioEffect(Guid effectId)
    {
        Execute(p =>
        {
            var idx = p.MasterAudioEffects.FindIndex(e => e.Id == effectId);
            if (idx >= 0) p.MasterAudioEffects.RemoveAt(idx);
        });
    }

    public void SetMasterAudioEffectBypassed(Guid effectId, bool bypassed)
    {
        Execute(p =>
        {
            var fx = p.MasterAudioEffects.FirstOrDefault(e => e.Id == effectId) ?? throw new KeyNotFoundException("Master audio effect not found.");
            fx.IsBypassed = bypassed;
        });
    }

    public void UpdateMasterAudioEffectParameter(Guid effectId, string paramName, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paramName);
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));

        Execute(p =>
        {
            var fx = p.MasterAudioEffects.FirstOrDefault(e => e.Id == effectId) ?? throw new KeyNotFoundException("Master audio effect not found.");
            fx.Parameters[paramName] = value;
        });
    }
}
