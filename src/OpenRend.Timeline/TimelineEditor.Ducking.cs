using System;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    /// <summary>
    /// Configures the sidechain ducking role and optional settings on an audio track.
    /// Matches Vegas Pro track header Auto Ducking Controls.
    /// </summary>
    public bool SetTrackDucking(Guid trackId, AudioDuckingRole role, AudioDuckingSettings? settings = null)
    {
        var track = Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null || track.Kind != TrackKind.Audio) return false;

        Execute(p =>
        {
            var t = p.Tracks.First(x => x.Id == trackId);
            t.DuckingRole = role;
            if (role == AudioDuckingRole.Listener)
            {
                var s = settings?.DeepClone() ?? new AudioDuckingSettings();
                s.Validate();
                t.DuckingSettings = s;
            }
            else
            {
                t.DuckingSettings = null;
            }
        });
        return true;
    }

    /// <summary>
    /// Bakes dynamic speech ducking into a frame-accurate volume ClipEnvelope on the listener track.
    /// Replicates Vegas Pro "Bake Ducking to Track Envelope" / "Create Ducking Envelope".
    /// </summary>
    public bool BakeTrackDuckingEnvelope(Guid listenerTrackId)
    {
        var track = Project.Tracks.FirstOrDefault(t => t.Id == listenerTrackId);
        if (track is null || track.Kind != TrackKind.Audio || track.DuckingRole != AudioDuckingRole.Listener)
            return false;

        Execute(p =>
        {
            var t = p.Tracks.First(x => x.Id == listenerTrackId);
            AudioDuckingEngine.BakeDuckingToEnvelope(p, t);
        });
        return true;
    }

    /// <summary>
    /// Removes the volume ducking envelope from the specified track.
    /// </summary>
    public bool ClearTrackDuckingEnvelope(Guid trackId)
    {
        var track = Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null || track.Envelopes is null) return false;

        Execute(p =>
        {
            var t = p.Tracks.First(x => x.Id == trackId);
            t.Envelopes?.RemoveAll(e => e.Type == EnvelopeType.Volume);
            if (t.Envelopes?.Count == 0) t.Envelopes = null;
        });
        return true;
    }
}

