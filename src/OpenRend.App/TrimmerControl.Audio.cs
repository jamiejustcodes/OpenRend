using System.IO;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

public sealed partial class TrimmerControl
{
    private const string SourceAudioPlaying = "Playing Trimmer audio.";
    private Func<Task>? prepareSourceAudio;
    private Func<ProjectDocument, long, int, FrameRange?, ISourceAudioSession>? createSourceAudio;
    private CancellationTokenSource? sourceAudioRequest;
    private Task sourceAudioTask = Task.CompletedTask;
    private ISourceAudioSession? sourceAudio;
    private double sourceMonitorGain = 1;

    internal Task SourceAudioCompletion => sourceAudioTask;

    internal void ConfigureSourceAudio(Func<Task> prepare, Func<ProjectDocument, long, int, FrameRange?, ISourceAudioSession> create)
    {
        prepareSourceAudio = prepare;
        createSourceAudio = create;
    }

    internal void SetSourceMonitorGain(double gain)
    {
        sourceMonitorGain = gain;
        if (sourceAudio is not null) sourceAudio.MasterGain = gain;
    }

    private void StopSourceAudio()
    {
        var request = sourceAudioRequest;
        sourceAudioRequest = null;
        sourceAudio = null;
        request?.Cancel();
        if (request is not null && vm is { Status: SourceAudioPlaying } viewModel)
            viewModel.Status = "Ready";
    }

    private void RestartSourceAudio()
    {
        StopSourceAudio();
        if (createSourceAudio is not { } create || vm is not { } viewModel ||
            activeMedia is not { HasAudio: true } media || !File.Exists(media.Path) ||
            Math.Abs((long)shuttleSpeed) is not (1 or 2 or 4 or 8 or 16))
            return;

        var request = new CancellationTokenSource();
        var predecessor = Task.WhenAll(sourceAudioTask, prepareSourceAudio?.Invoke() ?? Task.CompletedTask);
        sourceAudioRequest = request;
        sourceAudioTask = PlaySourceAudioAsync(viewModel, media, PlacementRange, currentFrame, shuttleSpeed,
            isLooping, predecessor, request, create);
    }

    private async Task PlaySourceAudioAsync(EditorViewModel viewModel, MediaAsset media, FrameRange range, long anchor,
        int speed, bool looping, Task predecessor, CancellationTokenSource request,
        Func<ProjectDocument, long, int, FrameRange?, ISourceAudioSession> create)
    {
        var token = request.Token;
        try
        {
            await predecessor;
            token.ThrowIfCancellationRequested();
            var preview = MediaService.CreateSourcePreview(media, viewModel.Project.FrameRate, range);
            FrameRange? audioLoop = looping && speed == 1 ? new FrameRange(0, range.FrameCount) : null;
            var start = Math.Clamp(anchor - range.StartFrame, 0, range.FrameCount - 1);
            // Reverse playback starts at the end of the displayed frame.
            if (speed < 0) start++;

            do
            {
                await using (var session = await Task.Run(() => create(preview, start, speed, audioLoop), token))
                {
                    token.ThrowIfCancellationRequested();
                    sourceAudio = session;
                    session.MasterGain = sourceMonitorGain;
                    session.Start();
                    viewModel.Status = SourceAudioPlaying;
                    while (!session.IsFinished)
                    {
                        await session.CheckAsync();
                        token.ThrowIfCancellationRequested();
                        var frame = Math.Clamp(session.CurrentFrame - (speed < 0 ? 1 : 0), 0, range.FrameCount - 1);
                        ShowPlaybackFrame(range.StartFrame + frame, finished: false);
                        await Task.Delay(15, token);
                    }
                }

                token.ThrowIfCancellationRequested();
                sourceAudio = null;
                start = speed < 0 ? range.FrameCount : 0;
            }
            while (looping);

            ShowPlaybackFrame(speed > 0 ? range.EndFrame : range.StartFrame, finished: true);
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            viewModel.Status = $"Trimmer audio: {error.Message}";
            ResetElapsedPlayback();
        }
        finally
        {
            if (ReferenceEquals(request, sourceAudioRequest))
            {
                sourceAudioRequest = null;
                sourceAudio = null;
            }
            request.Dispose();
        }
    }
}
