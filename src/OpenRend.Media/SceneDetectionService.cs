using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenRend.Core;

namespace OpenRend.Media;

/// <summary>
/// Asynchronously analyzes video media assets to detect camera cuts and scene transitions.
/// Uses fast downsampled proxy frames and composite luminance/chromatic histogram metrics.
/// </summary>
public sealed class SceneDetectionService
{
    private const int ProxyWidth = 160;
    private const int ProxyHeight = 90;

    private readonly MediaService mediaService;

    public SceneDetectionService(MediaService mediaService)
    {
        this.mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
    }

    /// <summary>
    /// Analyzes a video file across a designated frame range [startFrame, startFrame + totalFrames]
    /// and reports detected scene cut points with progress feedback.
    /// </summary>
    public async Task<IReadOnlyList<SceneCut>> DetectScenesAsync(
        string mediaPath,
        long startFrame,
        long totalFrames,
        Rational frameRate,
        SceneDetectionOptions options,
        long clipStartAbsoluteFrame = 0,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();

        var minFrames = options.CalculateMinFrames(frameRate);
        if (totalFrames < minFrames * 2)
            return Array.Empty<SceneCut>();

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scores = new List<(long Offset, double Score)>();
            byte[]? prevFrame = null;

            // Frame-by-frame evaluation for frame-accurate cut placement
            // For longer clips, stride by 2 for preliminary scanning while maintaining high accuracy
            var stride = totalFrames > 3000 ? 2 : 1;

            for (long offset = 0; offset < totalFrames; offset += stride)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentFrameIndex = startFrame + offset;
                var timeSec = frameRate.Seconds(currentFrameIndex);

                byte[] currFrame;
                try
                {
                    currFrame = mediaService.GetFrame(mediaPath, timeSec, ProxyWidth, ProxyHeight);
                }
                catch
                {
                    // If frame decode fails at stream boundary, continue
                    break;
                }

                if (prevFrame is not null)
                {
                    var diff = SceneDetectionEngine.ComputeFrameDifference(
                        currFrame,
                        prevFrame,
                        ProxyWidth,
                        ProxyHeight
                    );

                    scores.Add((offset, diff));
                }

                prevFrame = currFrame;

                if (progress is not null && (offset % 20 == 0 || offset + stride >= totalFrames))
                {
                    var pct = (double)offset / totalFrames * 100.0;
                    progress.Report(Math.Min(100.0, pct));
                }
            }

            progress?.Report(100.0);

            return SceneDetectionEngine.DetectCutsFromScores(
                scores,
                totalFrames,
                options,
                frameRate,
                clipStartAbsoluteFrame
            );
        }, cancellationToken).ConfigureAwait(false);
    }
}

