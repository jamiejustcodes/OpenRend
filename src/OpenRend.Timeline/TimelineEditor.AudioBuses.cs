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
}

