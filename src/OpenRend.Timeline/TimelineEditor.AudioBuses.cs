using System;
using System.Collections.Generic;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public AudioBusTrack CreateAudioBus(string? name = null)
    {
        AudioBusTrack created = null!;
        Execute(p =>
        {
            var busName = name;
            if (string.IsNullOrWhiteSpace(busName))
            {
                var letter = 'A';
                while (letter <= 'Z' && p.AudioBuses.Any(b => b.Name.Equals($"Bus {letter}", StringComparison.OrdinalIgnoreCase)))
                {
                    letter++;
                }
                busName = $"Bus {letter}";
            }

            created = new AudioBusTrack
            {
                Name = busName
            };
            p.AudioBuses.Add(created);
        });
        return created;
    }

    public void DeleteAudioBus(Guid busId)
    {
        Execute(p =>
        {
            p.AudioBuses.RemoveAll(b => b.Id == busId);
            foreach (var track in p.Tracks.Where(t => t.Kind == TrackKind.Audio && t.BusTrackId == busId))
            {
                track.BusTrackId = null;
            }
        });
    }

    public void SetTrackAudioBus(Guid trackId, Guid? busId)
    {
        Execute(p =>
        {
            var track = p.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (track == null || track.Kind != TrackKind.Audio) return;
            if (busId.HasValue && !p.AudioBuses.Any(b => b.Id == busId.Value)) return;
            track.BusTrackId = busId;
        });
    }

    public void SetAudioBusVolume(Guid busId, double gain)
    {
        Execute(p =>
        {
            var bus = p.AudioBuses.FirstOrDefault(b => b.Id == busId);
            if (bus == null) return;
            bus.Gain = Math.Max(0, gain);
        });
    }

    public void SetAudioBusPan(Guid busId, double pan)
    {
        Execute(p =>
        {
            var bus = p.AudioBuses.FirstOrDefault(b => b.Id == busId);
            if (bus == null) return;
            bus.Pan = Math.Clamp(pan, -1.0, 1.0);
        });
    }

    public void SetAudioBusPanMode(Guid busId, AudioPanMode panMode)
    {
        if (!Enum.IsDefined(panMode)) throw new ArgumentOutOfRangeException(nameof(panMode));
        Execute(p =>
        {
            var bus = p.AudioBuses.FirstOrDefault(b => b.Id == busId);
            if (bus == null) return;
            bus.PanMode = panMode;
        });
    }

    public void SetAudioBusMute(Guid busId, bool isMuted)
    {
        Execute(p =>
        {
            var bus = p.AudioBuses.FirstOrDefault(b => b.Id == busId);
            if (bus == null) return;
            bus.IsMuted = isMuted;
        });
    }

    public void SetAudioBusSolo(Guid busId, bool isSolo)
    {
        Execute(p =>
        {
            var bus = p.AudioBuses.FirstOrDefault(b => b.Id == busId);
            if (bus == null) return;
            bus.IsSolo = isSolo;
        });
    }

    public ClipEnvelope? GetAudioBusEnvelope(Guid busId, EnvelopeType type)
    {
        var bus = Project.AudioBuses?.FirstOrDefault(b => b.Id == busId);
        return bus?.Envelopes?.FirstOrDefault(e => e.Type == type);
    }

    public bool HasAudioBusEnvelope(Guid busId, EnvelopeType type) => GetAudioBusEnvelope(busId, type) != null;

    public void ToggleAudioBusEnvelope(Guid busId, EnvelopeType type)
    {
        if (HasAudioBusEnvelope(busId, type))
            RemoveAudioBusEnvelope(busId, type);
        else
            AddAudioBusEnvelope(busId, type);
    }

    public void AddAudioBusEnvelope(Guid busId, EnvelopeType type)
    {
        Execute(p =>
        {
            var bus = p.AudioBuses.FirstOrDefault(b => b.Id == busId) ?? throw new KeyNotFoundException("Audio bus not found.");
            bus.Envelopes ??= [];
            if (bus.Envelopes.Any(e => e.Type == type)) return;

            var env = new ClipEnvelope
            {
                Type = type,
                IsEnabled = true
            };
            var defaultValue = env.DefaultValue;
            var duration = Math.Max(1, p.DurationFrames);
            env.Points.Add(new EnvelopePoint(0, defaultValue, EnvelopeCurveType.Linear));
            env.Points.Add(new EnvelopePoint(duration, defaultValue, EnvelopeCurveType.Linear));
            bus.Envelopes.Add(env);
        });
    }

    public void RemoveAudioBusEnvelope(Guid busId, EnvelopeType type)
    {
        Execute(p =>
        {
            var bus = p.AudioBuses.FirstOrDefault(b => b.Id == busId);
            if (bus?.Envelopes == null) return;
            bus.Envelopes.RemoveAll(e => e.Type == type);
            if (bus.Envelopes.Count == 0) bus.Envelopes = null;
        });
    }

    public void SetAudioBusEnvelopePoint(Guid busId, EnvelopeType type, long frame, double value, EnvelopeCurveType curve = EnvelopeCurveType.Linear)
    {
        Execute(p =>
        {
            var bus = p.AudioBuses.FirstOrDefault(b => b.Id == busId) ?? throw new KeyNotFoundException("Audio bus not found.");
            bus.Envelopes ??= [];
            var env = bus.Envelopes.FirstOrDefault(e => e.Type == type);
            if (env == null)
            {
                env = new ClipEnvelope { Type = type, IsEnabled = true };
                bus.Envelopes.Add(env);
            }

            var clampedValue = Math.Clamp(value, env.MinValue, env.MaxValue);
            var clampedFrame = Math.Max(0, frame);
            var existing = env.Points.FirstOrDefault(pt => pt.Frame == clampedFrame);
            if (existing != null)
            {
                existing.Value = clampedValue;
                existing.Curve = curve;
            }
            else
            {
                env.Points.Add(new EnvelopePoint(clampedFrame, clampedValue, curve));
                env.Points.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            }
        });
    }

    public void RemoveAudioBusEnvelopePoint(Guid busId, EnvelopeType type, long frame)
    {
        Execute(p =>
        {
            var bus = p.AudioBuses.FirstOrDefault(b => b.Id == busId);
            var env = bus?.Envelopes?.FirstOrDefault(e => e.Type == type);
            if (env == null || env.Points.Count <= 2) return;
            env.Points.RemoveAll(pt => pt.Frame == frame);
        });
    }

    public void SetAudioBusEnvelopePointCurve(Guid busId, EnvelopeType type, long frame, EnvelopeCurveType curve)
    {
        Execute(p =>
        {
            var bus = p.AudioBuses.FirstOrDefault(b => b.Id == busId);
            var env = bus?.Envelopes?.FirstOrDefault(e => e.Type == type);
            var pt = env?.Points.FirstOrDefault(p => p.Frame == frame);
            if (pt != null) pt.Curve = curve;
        });
    }

    public ClipEnvelope? GetMasterAudioEnvelope(EnvelopeType type)
    {
        return Project.MasterAudioEnvelopes?.FirstOrDefault(e => e.Type == type);
    }

    public bool HasMasterAudioEnvelope(EnvelopeType type) => GetMasterAudioEnvelope(type) != null;

    public void ToggleMasterAudioEnvelope(EnvelopeType type)
    {
        if (HasMasterAudioEnvelope(type))
            RemoveMasterAudioEnvelope(type);
        else
            AddMasterAudioEnvelope(type);
    }

    public void AddMasterAudioEnvelope(EnvelopeType type)
    {
        Execute(p =>
        {
            p.MasterAudioEnvelopes ??= [];
            if (p.MasterAudioEnvelopes.Any(e => e.Type == type)) return;

            var env = new ClipEnvelope
            {
                Type = type,
                IsEnabled = true
            };
            var defaultValue = env.DefaultValue;
            var duration = Math.Max(1, p.DurationFrames);
            env.Points.Add(new EnvelopePoint(0, defaultValue, EnvelopeCurveType.Linear));
            env.Points.Add(new EnvelopePoint(duration, defaultValue, EnvelopeCurveType.Linear));
            p.MasterAudioEnvelopes.Add(env);
        });
    }

    public void RemoveMasterAudioEnvelope(EnvelopeType type)
    {
        Execute(p =>
        {
            if (p.MasterAudioEnvelopes == null) return;
            p.MasterAudioEnvelopes.RemoveAll(e => e.Type == type);
            if (p.MasterAudioEnvelopes.Count == 0) p.MasterAudioEnvelopes = null;
        });
    }

    public void SetMasterAudioEnvelopePoint(EnvelopeType type, long frame, double value, EnvelopeCurveType curve = EnvelopeCurveType.Linear)
    {
        Execute(p =>
        {
            p.MasterAudioEnvelopes ??= [];
            var env = p.MasterAudioEnvelopes.FirstOrDefault(e => e.Type == type);
            if (env == null)
            {
                env = new ClipEnvelope { Type = type, IsEnabled = true };
                p.MasterAudioEnvelopes.Add(env);
            }

            var clampedValue = Math.Clamp(value, env.MinValue, env.MaxValue);
            var clampedFrame = Math.Max(0, frame);
            var existing = env.Points.FirstOrDefault(pt => pt.Frame == clampedFrame);
            if (existing != null)
            {
                existing.Value = clampedValue;
                existing.Curve = curve;
            }
            else
            {
                env.Points.Add(new EnvelopePoint(clampedFrame, clampedValue, curve));
                env.Points.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            }
        });
    }

    public void RemoveMasterAudioEnvelopePoint(EnvelopeType type, long frame)
    {
        Execute(p =>
        {
            var env = p.MasterAudioEnvelopes?.FirstOrDefault(e => e.Type == type);
            if (env == null || env.Points.Count <= 2) return;
            env.Points.RemoveAll(pt => pt.Frame == frame);
        });
    }

    public void SetMasterAudioEnvelopePointCurve(EnvelopeType type, long frame, EnvelopeCurveType curve)
    {
        Execute(p =>
        {
            var env = p.MasterAudioEnvelopes?.FirstOrDefault(e => e.Type == type);
            var pt = env?.Points.FirstOrDefault(p => p.Frame == frame);
            if (pt != null) pt.Curve = curve;
        });
    }
}
