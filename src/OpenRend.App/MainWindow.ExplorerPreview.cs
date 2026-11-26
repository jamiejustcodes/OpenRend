using System.IO;
using System.Windows;
using System.Windows.Controls;
using OpenRend.Media;

namespace OpenRend.App;

public partial class MainWindow
{
    private CancellationTokenSource? explorerPreviewRequest;
    private Task explorerPreviewTask = Task.CompletedTask;
    private PlaybackSession? explorerPlayback;
    private string? explorerPreviewName;

    private void ExplorerAutoPlayClick(object sender, RoutedEventArgs e) => UpdateExplorerSelectionPreview();

    private void ExplorerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExplorerAutoPlay?.IsChecked == true) UpdateExplorerSelectionPreview();
    }

    private void UpdateExplorerSelectionPreview()
    {
        if (ExplorerAutoPlay?.IsChecked == true && ExplorerList?.SelectedItem is FileInfo source) StartExplorerPreview(source);
        else if (explorerPreviewName is not null)
        {
            StopExplorerPreview();
            RequestPreview();
        }
    }

    private void StartExplorerPreview(FileInfo file)
    {
        Pause();
        var request = new CancellationTokenSource();
        explorerPreviewRequest = request;
        explorerPreviewName = file.Name;
        UpdatePreviewStatus();
        explorerPreviewTask = PlayExplorerPreviewAsync(file.FullName, explorerPreviewTask, request);
    }

    private void StopExplorerPreview()
    {
        var request = explorerPreviewRequest;
        explorerPreviewRequest = null;
        explorerPreviewName = null;
        request?.Cancel();
    }

    private async Task PlayExplorerPreviewAsync(string path, Task predecessor, CancellationTokenSource request)
    {
        var token = request.Token;
        try
        {
            await predecessor;
            await scrubShutdown;
            token.ThrowIfCancellationRequested();
            var source = await vm.Media.ProbeAsync(path, token);
            var project = MediaService.CreateSourcePreview(source, vm.Project.FrameRate);
            var frameCount = project.DurationFrames;
            var (width, height) = FitSize(project, 640);
            async Task Present(long frame)
            {
                var pixels = await Task.Run(() => vm.Renderer.RenderFrame(project, frame, width, height), token);
                token.ThrowIfCancellationRequested();
                if (IsLoaded) Preview.Present(pixels, width, height);
            }

            if (frameCount == 1 && !source.HasAudio)
            {
                await Present(0);
                return;
            }
            if (!source.HasVideo) await Present(0);

            await using var session = await Task.Run(() => new PlaybackSession(vm.Renderer, project, 0, startImmediately: false), token);
            token.ThrowIfCancellationRequested();
            session.MasterGain = masterMuted ? 0 : MasterGain.Value;
            explorerPlayback = session;
            session.Start();
            var lastFrame = -1L;
            while (!session.IsFinished)
            {
                await session.CheckAsync();
                var frame = Math.Min(frameCount - 1, session.CurrentFrame);
                if (source.HasVideo && frame != lastFrame)
                {
                    await Present(frame);
                    lastFrame = frame;
                }
                await Task.Delay(16, token);
            }

        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (ReferenceEquals(request, explorerPreviewRequest))
        {
            explorerPreviewName = null;
            vm.Status = $"Source preview: {error.Message}";
            RequestPreview();
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        finally
        {
            explorerPlayback = null;
            if (ReferenceEquals(request, explorerPreviewRequest)) explorerPreviewRequest = null;
            request.Dispose();
        }
    }
}
