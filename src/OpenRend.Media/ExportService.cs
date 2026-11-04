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
        var outWidth = template.Width ?? snapshot.Width;
        var outHeight = template.Height ?? snapshot.Height;
        if (!template.IsAudioOnly && ((outWidth & 1) != 0 || (outHeight & 1) != 0))
            throw new InvalidOperationException("Video export requires even dimensions.");
        var destination = Path.GetFullPath(output);
        if (snapshot.Media.Any(asset => string.Equals(Path.GetFullPath(asset.Path), destination, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose an export destination different from the original media.");
        return Task.Run(() => ExportCoreAsync(snapshot, destination, range, template, options, progress, cancellationToken), cancellationToken);
    }

    public static void ApplyChannelRemapping(float[] samples, ChannelRemapping mapping)
    {
        if (mapping == ChannelRemapping.None) return;
        for (int i = 0; i < samples.Length - 1; i += 2)
        {
            var left = samples[i];
            var right = samples[i + 1];
            switch (mapping)
            {
                case ChannelRemapping.Mono:
                    var mono = 0.5f * (left + right);
                    samples[i] = mono;
                    samples[i + 1] = mono;
                    break;
                case ChannelRemapping.Swap:
                    samples[i] = right;
                    samples[i + 1] = left;
                    break;
                case ChannelRemapping.MuteLeft:
                    samples[i] = 0f;
                    samples[i + 1] = right;
                    break;
                case ChannelRemapping.MuteRight:
                    samples[i] = left;
                    samples[i + 1] = 0f;
                    break;
            }
        }
    }

    private async Task ExportCoreAsync(ProjectDocument project, string destination, FrameRange range, RenderTemplate template, ExportOptions options, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var tempExt = template.Extension;
        var temporaryMedia = Path.Combine(directory, $".openrend-{Guid.NewGuid():N}{tempExt}");
        var temporaryAudio = Path.Combine(Path.GetTempPath(), $"openrend-{Guid.NewGuid():N}.f32le");
        try
        {
            var totalFrames = range.FrameCount;
            // Count sample instants in the half-open frame range without rounding its rational duration to a double.
            var totalSamples = checked((long)decimal.Ceiling((decimal)totalFrames * project.FrameRate.Denominator * MediaService.SampleRate / project.FrameRate.Numerator));
            var startSeconds = project.FrameRate.Seconds(range.StartFrame);
            progress?.Report(0);

            double sumSquares = 0;
            float maxPeak = 0;
            long analyzedCount = 0;

            await using (var audioFile = new FileStream(temporaryAudio, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                const int blockSize = 8192;
                for (long sample = 0; sample < totalSamples; sample += blockSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = (int)Math.Min(blockSize, totalSamples - sample);
                    var samples = renderer.RenderAudio(project, startSeconds + (double)sample / MediaService.SampleRate, count);
                    if (options.ChannelMapping != ChannelRemapping.None)
                        ApplyChannelRemapping(samples, options.ChannelMapping);

                    if (options.GenerateLoudnessLog)
                    {
                        for (int i = 0; i < samples.Length; i++)
                        {
                            var val = samples[i];
                            sumSquares += (double)val * val;
                            var abs = Math.Abs(val);
                            if (abs > maxPeak) maxPeak = abs;
                        }
                        analyzedCount += samples.Length;
                    }

                    var bytes = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
                    await audioFile.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    progress?.Report((template.IsAudioOnly ? 0.8 : 0.2) * (sample + count) / totalSamples);
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

            if (template.IsAudioOnly)
            {
                var audioArgs = new List<string>
                {
                    "-hide_banner", "-nostdin", "-y", "-loglevel", "error",
                    "-f", "f32le", "-ar", "48000", "-ac", "2", "-i", temporaryAudio
                };
                if (template.AudioCodec == "libmp3lame")
                {
                    audioArgs.AddRange(["-c:a", "libmp3lame", "-b:a", $"{template.AudioBitrateKbps}k", "-ar", template.AudioSampleRate.ToString(CultureInfo.InvariantCulture), "-f", "mp3", temporaryMedia]);
                }
                else
                {
                    audioArgs.AddRange(["-c:a", template.AudioCodec, "-ar", template.AudioSampleRate.ToString(CultureInfo.InvariantCulture), "-f", "wav", temporaryMedia]);
                }
                foreach (var arg in audioArgs) start.ArgumentList.Add(arg);
                using var audioProcess = new Process { StartInfo = start };
                if (!audioProcess.Start()) throw new MediaException("The audio encoder could not be started.");
                using var cancellation = cancellationToken.Register(() => Kill(audioProcess));
                var err = await audioProcess.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                await audioProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (audioProcess.ExitCode != 0)
                    throw new MediaException($"Export failed: {err.Trim()}");
            }
            else
            {
                var arguments = new List<string>
                {
                    "-hide_banner", "-nostdin", "-y", "-loglevel", "error",
                    "-f", "rawvideo", "-pixel_format", "bgra", "-video_size", $"{project.Width}x{project.Height}",
                    "-framerate", project.FrameRate.ToString(), "-i", "pipe:0",
                    "-f", "f32le", "-ar", "48000", "-ac", "2", "-i", temporaryAudio,
                    "-map", "0:v:0", "-map", "1:a:0"
                };

                var outWidth = template.Width ?? project.Width;
                var outHeight = template.Height ?? project.Height;
                if (outWidth != project.Width || outHeight != project.Height)
                {
                    if (options.StretchToFill)
                        arguments.AddRange(["-vf", $"scale={outWidth}:{outHeight}"]);
                    else
                        arguments.AddRange(["-vf", $"scale={outWidth}:{outHeight}:force_original_aspect_ratio=decrease,pad={outWidth}:{outHeight}:(ow-iw)/2:(oh-ih)/2"]);
                }

                if (template.VideoCodec == "prores_ks")
                {
                    arguments.AddRange(["-c:v", "prores_ks", "-profile:v", (template.ProResProfile ?? 2).ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv422p10le", "-c:a", "pcm_s24le", "-f", "mov", temporaryMedia]);
                }
                else
                {
                    var isNvenc = false;
                    var isAmf = false;
                    var isQsv = false;
                    var codec = template.VideoCodec;

                    var hw = template.HardwareAcceleration;
                    if (hw == HardwareAcceleration.Auto)
                    {
                        // Hardware encoders (NVENC, AMF, QSV) enforce minimum dimension constraints (typically 128x128).
                        if (outWidth >= 128 && outHeight >= 128)
                        {
                            var detected = HardwareAccelerationHelper.Current;
                            if (detected.HasNvenc) hw = HardwareAcceleration.NVENC;
                            else if (detected.HasQsv) hw = HardwareAcceleration.QSV;
                            else if (detected.HasAmf) hw = HardwareAcceleration.AMF;
                            else hw = HardwareAcceleration.CPU;
                        }
                        else
                        {
                            hw = HardwareAcceleration.CPU;
                        }
                    }
                    else if (hw == HardwareAcceleration.NVENC && (outWidth < 128 || outHeight < 128))
                    {
                        hw = HardwareAcceleration.CPU;
                    }

                    if (hw == HardwareAcceleration.NVENC)
                    {
                        codec = template.VideoCodec == "libx265" ? "hevc_nvenc" : "h264_nvenc";
                        isNvenc = true;
                    }
                    else if (hw == HardwareAcceleration.AMF)
                    {
                        codec = template.VideoCodec == "libx265" ? "hevc_amf" : "h264_amf";
                        isAmf = true;
                    }
                    else if (hw == HardwareAcceleration.QSV)
                    {
                        codec = template.VideoCodec == "libx265" ? "hevc_qsv" : "h264_qsv";
                        isQsv = true;
                    }

                    arguments.AddRange(["-c:v", codec]);

                    if (isNvenc)
                    {
                        var nvencPreset = template.EncoderSpeed switch
                        {
                            EncoderSpeed.Ultrafast or EncoderSpeed.Superfast or EncoderSpeed.Veryfast => "p1",
                            EncoderSpeed.Faster or EncoderSpeed.Fast => "p3",
                            EncoderSpeed.Medium => "p4",
                            EncoderSpeed.Slow => "p5",
                            EncoderSpeed.Slower => "p6",
                            EncoderSpeed.Veryslow => "p7",
                            _ => "p4"
                        };
                        arguments.AddRange(["-preset", nvencPreset]);
                        if (template.BitrateMode == BitrateMode.CBR)
                        {
                            arguments.AddRange(["-b:v", template.AverageBitrateBps.ToString(CultureInfo.InvariantCulture),
                                                "-maxrate", template.AverageBitrateBps.ToString(CultureInfo.InvariantCulture),
                                                "-bufsize", (template.AverageBitrateBps * 2).ToString(CultureInfo.InvariantCulture)]);
                        }
                        else if (template.BitrateMode is BitrateMode.VBR_1Pass or BitrateMode.VBR_2Pass)
                        {
                            arguments.AddRange(["-b:v", template.AverageBitrateBps.ToString(CultureInfo.InvariantCulture),
                                                "-maxrate", template.MaxBitrateBps.ToString(CultureInfo.InvariantCulture),
                                                "-bufsize", (template.MaxBitrateBps * 2).ToString(CultureInfo.InvariantCulture)]);
                        }
                        else
                        {
                            arguments.AddRange(["-cq", template.VideoCrf.ToString(CultureInfo.InvariantCulture)]);
                        }
                    }
                    else if (isAmf || isQsv)
                    {
                        if (template.BitrateMode == BitrateMode.CBR)
                        {
                            arguments.AddRange(["-b:v", template.AverageBitrateBps.ToString(CultureInfo.InvariantCulture)]);
                        }
                        else if (template.BitrateMode is BitrateMode.VBR_1Pass or BitrateMode.VBR_2Pass)
                        {
                            arguments.AddRange(["-b:v", template.AverageBitrateBps.ToString(CultureInfo.InvariantCulture),
                                                "-maxrate", template.MaxBitrateBps.ToString(CultureInfo.InvariantCulture)]);
                        }
                        else
                        {
                            arguments.AddRange(["-global_quality", template.VideoCrf.ToString(CultureInfo.InvariantCulture)]);
                        }
                    }
                    else
                    {
                        arguments.AddRange(["-preset", template.EncoderSpeed.ToString().ToLowerInvariant()]);
                        if (template.BitrateMode == BitrateMode.CBR)
                        {
                            arguments.AddRange(["-b:v", template.AverageBitrateBps.ToString(CultureInfo.InvariantCulture),
                                                "-minrate", template.AverageBitrateBps.ToString(CultureInfo.InvariantCulture),
                                                "-maxrate", template.AverageBitrateBps.ToString(CultureInfo.InvariantCulture),
                                                "-bufsize", (template.AverageBitrateBps * 2).ToString(CultureInfo.InvariantCulture)]);
                        }
                        else if (template.BitrateMode is BitrateMode.VBR_1Pass or BitrateMode.VBR_2Pass)
                        {
                            arguments.AddRange(["-b:v", template.AverageBitrateBps.ToString(CultureInfo.InvariantCulture),
                                                "-maxrate", template.MaxBitrateBps.ToString(CultureInfo.InvariantCulture),
                                                "-bufsize", (template.MaxBitrateBps * 2).ToString(CultureInfo.InvariantCulture)]);
                        }
                        else
                        {
                            arguments.AddRange(["-crf", template.VideoCrf.ToString(CultureInfo.InvariantCulture)]);
                        }
                    }

                    if (template.VideoCodec == "libx264" || isNvenc || isAmf || isQsv)
                    {
                        arguments.AddRange(["-profile:v", template.Profile.ToString().ToLowerInvariant()]);
                    }

                    arguments.AddRange(["-pix_fmt", "yuv420p"]);
                    arguments.AddRange(["-c:a", "aac", "-b:a", template.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture) + "k"]);
                    if (template.AudioChannels != 2)
                    {
                        arguments.AddRange(["-ac", template.AudioChannels.ToString(CultureInfo.InvariantCulture)]);
                    }
                    if (template.FastStart)
                    {
                        arguments.AddRange(["-movflags", "+faststart"]);
                    }
                    arguments.AddRange(["-f", "mp4", temporaryMedia]);
                }

                foreach (var argument in arguments) start.ArgumentList.Add(argument);
                using var process = new Process { StartInfo = start };
                if (!process.Start()) throw new MediaException("The video encoder could not be started.");
                using var cancellation = cancellationToken.Register(() => Kill(process));
                var errors = process.StandardError.ReadToEndAsync(cancellationToken);
                var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
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
            }

            if (!File.Exists(temporaryMedia) || new FileInfo(temporaryMedia).Length == 0)
                throw new MediaException("The encoder did not produce an output file.");

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryMedia, destination, overwrite: true);

            if (options.GenerateLoudnessLog)
            {
                var logPath = destination + ".loudness.log";
                var durationSec = (double)totalFrames * project.FrameRate.Denominator / project.FrameRate.Numerator;
                var rms = analyzedCount > 0 ? Math.Sqrt(sumSquares / analyzedCount) : 0;
                var lufs = rms > 1e-9 ? 10.0 * Math.Log10(rms * rms) - 0.691 : -70.0;
                var peakDbfs = maxPeak > 1e-9 ? 20.0 * Math.Log10(maxPeak) : -90.0;
                var logText = $"""
                    ==================================================
                    OpenRend Loudness and Peak Analysis Log
                    Target: {Path.GetFileName(destination)}
                    Render Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                    Template: {template.Name}
                    Format: {template.FormatName}
                    --------------------------------------------------
                    Integrated Loudness: {lufs:F1} LUFS
                    Maximum True Peak: {peakDbfs:F1} dBFS
                    Duration: {TimeSpan.FromSeconds(durationSec):hh\:mm\:ss\.fff} ({totalFrames} frames)
                    Sample Rate: {MediaService.SampleRate} Hz (Stereo)
                    Channel Mapping: {options.ChannelMapping}
                    Total Audio Samples: {analyzedCount}
                    ==================================================
                    """;
                await File.WriteAllTextAsync(logPath, logText, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(1);
        }
        finally
        {
            DeleteTemporary(temporaryAudio);
            DeleteTemporary(temporaryMedia);
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
