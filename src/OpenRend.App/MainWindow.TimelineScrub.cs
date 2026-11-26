using System.Diagnostics;
using OpenRend.Core;

namespace OpenRend.App;

public partial class MainWindow
{
    private TimelineScrubSession? timelineScrub;
    private Task scrubShutdown = Task.CompletedTask;
    private long lastScrubAt;
    private bool scrubAudioUnavailable;

    private void SeekTimeline(long frame)
    {
        Pause(frame);
        lastScrubAt = Stopwatch.GetTimestamp();
    }

    private void ScrubTimeline(long frame)
    {
        if (playbackStarting || playback is not null)
            Pause();
        var previous = vm.Position;
        vm.Position = frame;
        if (vm.Position == previous) return;
        UpdatePosition();
        RequestPreview();

        var now = Stopwatch.GetTimestamp();
        var elapsed = lastScrubAt == 0 ? .04 : Math.Max(.012, Stopwatch.GetElapsedTime(lastScrubAt, now).TotalSeconds);
        lastScrubAt = now;
        if (scrubAudioUnavailable || masterMuted || MasterGain.Value <= 0 || vm.Project.MasterAudioMute ||
            !vm.Project.Tracks.Any(track => track.Kind == TrackKind.Audio && !track.IsMuted && track.Clips.Count > 0))
            return;

        var movement = vm.Project.FrameRate.Seconds(vm.Position - previous);
        var speed = Math.Clamp(Math.Abs(movement) / elapsed, .25, TimelineScrubSession.MaxRate);
        timelineScrub ??= new TimelineScrubSession(vm.Renderer, vm.Project.DeepClone(), scrubShutdown);
        if (!timelineScrub.Queue(vm.Position, Math.CopySign(speed, movement), MasterGain.Value))
        {
            scrubAudioUnavailable = true;
            vm.Status = "Audio scrubbing is unavailable on the current output device.";
        }
    }

    private void StopTimelineScrub()
    {
        lastScrubAt = 0;
        scrubAudioUnavailable = false;
        if (timelineScrub is not { } session) return;
        timelineScrub = null;
        scrubShutdown = session.DisposeAsync().AsTask();
    }
}
