using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using OpenRend.Core;

namespace OpenRend.Media;

public sealed class ExportService(TimelineRenderService renderer)
{
    public Task ExportAsync(ProjectDocument project, string output, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        => ExportAsync(project, output, new ExportOptions(), progress, cancellationToken);

    public Task ExportAsync(ProjectDocument project, string output, ExportOptions options, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = project.DeepClone();
        snapshot.Validate();
        if (snapshot.DurationFrames <= 0) throw new InvalidOperationException("The timeline is empty.");
        var range = options.Range ?? new FrameRange(0, snapshot.DurationFrames);
        range.Validate(snapshot.DurationFrames);
        var template = options.CustomTemplate ?? RenderTemplateCatalog.Find(options.TemplateId);
        template.Validate(requireCustom: options.CustomTemplate is not null);
        if (template.Id != options.TemplateId)
            throw new InvalidDataException("The custom render template does not match the selected template ID.");
        if ((snapshot.Width & 1) != 0 || (snapshot.Height & 1) != 0) throw new InvalidOperationException("H.264 export requires even project dimensions.");
        var destination = Path.GetFullPath(output);
        if (snapshot.Media.Any(asset => string.Equals(Path.GetFullPath(asset.Path), destination, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose an export destination different from the original media.");
        return Task.Run(() => ExportCoreAsync(snapshot, destination, range, template, progress, cancellationToken), cancellationToken);
    }

    private async Task ExportCoreAsync(ProjectDocument project, string destination, FrameRange range, RenderTemplate template, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporaryVideo = Path.Combine(directory, $".openrend-{Guid.NewGuid():N}.mp4");
        var temporaryAudio = Path.Combine(Path.GetTempPath(), $"openrend-{Guid.NewGuid():N}.f32le");
        try
        {
            var totalFrames = range.FrameCount;
            // Count sample instants in the half-open frame range without rounding its rational duration to a double.
            var totalSamples = checked((long)decimal.Ceiling((decimal)totalFrames * project.FrameRate.Denominator * MediaService.SampleRate / project.FrameRate.Numerator));
            var startSeconds = project.FrameRate.Seconds(range.StartFrame);
            progress?.Report(0);
            await using (var audioFile = new FileStream(temporaryAudio, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                const int blockSize = 8192;
                for (long sample = 0; sample < totalSamples; sample += blockSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = (int)Math.Min(blockSize, totalSamples - sample);
                    var samples = renderer.RenderAudio(project, startSeconds + (double)sample / MediaService.SampleRate, count);
                    var bytes = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
                    await audioFile.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    progress?.Report(0.2 * (sample + count) / totalSamples);
                }
            }

            var start = new ProcessStartInfo(MediaExecutables.Find("ffmpeg"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            // Pipe EOF bounds video; a frame limit can stop muxing before AAC drains.
            string[] arguments =
            [
                "-hide_banner", "-nostdin", "-y", "-loglevel", "error",
                "-f", "rawvideo", "-pixel_format", "bgra", "-video_size", $"{project.Width}x{project.Height}",
                "-framerate", project.FrameRate.ToString(), "-i", "pipe:0",
                "-f", "f32le", "-ar", "48000", "-ac", "2", "-i", temporaryAudio,
                "-map", "0:v:0", "-map", "1:a:0",
                "-c:v", "libx264", "-preset", template.EncoderSpeed.ToString().ToLowerInvariant(), "-crf", template.VideoCrf.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", template.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture) + "k", "-movflags", "+faststart", "-f", "mp4", temporaryVideo
            ];
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            if (!process.Start()) throw new MediaException("The video encoder could not be started.");
            using var cancellation = cancellationToken.Register(() => Kill(process));
            var errors = process.StandardError.ReadToEndAsync();
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            Exception? streamError = null;
            try
            {
                for (long frame = 0; frame < totalFrames; frame++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pixels = renderer.RenderFrame(project, range.StartFrame + frame, project.Width, project.Height);
                    await process.StandardInput.BaseStream.WriteAsync(pixels, cancellationToken).ConfigureAwait(false);
                    progress?.Report(0.2 + 0.75 * (frame + 1) / totalFrames);
                }
                await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                streamError = error;
                Kill(process);
            }
            finally
            {
                process.StandardInput.Close();
            }
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                Kill(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            var diagnostic = await errors.ConfigureAwait(false);
            await standardOutput.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (streamError != null || process.ExitCode != 0)
                throw new MediaException($"Export failed: {(string.IsNullOrWhiteSpace(diagnostic) ? streamError?.Message ?? $"encoder exited with code {process.ExitCode}" : diagnostic.Trim())}");
            if (!File.Exists(temporaryVideo) || new FileInfo(temporaryVideo).Length == 0) throw new MediaException("The encoder did not produce an output file.");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryVideo, destination, overwrite: true);
            progress?.Report(1);
        }
        finally
        {
            DeleteTemporary(temporaryAudio);
            DeleteTemporary(temporaryVideo);
        }
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static void DeleteTemporary(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public static class MediaExecutables
{
    public static string Find(string name)
    {
        var fileName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
        foreach (var origin in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(origin); directory != null; directory = directory.Parent)
            {
                foreach (var relative in new[] { fileName, Path.Combine("bin", fileName), Path.Combine(".deps", "ffmpeg", "bin", fileName) })
                {
                    var candidate = Path.Combine(directory.FullName, relative);
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return fileName;
    }
}
