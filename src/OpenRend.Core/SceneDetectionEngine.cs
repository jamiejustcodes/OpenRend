using System;
using System.Collections.Generic;

namespace OpenRend.Core;

/// <summary>
/// Core analytical video scene cut detection engine.
/// Computes per-frame luminance and chromatic histogram deltas and partitions
/// continuous shots adhering to the Vegas Pro 500 ms debounce heuristic.
/// </summary>
public static class SceneDetectionEngine
{
    private const int HistogramBins = 16;
    private const int ShiftFor16Bins = 4; // 256 / 16 = 16 values per bin (>> 4)

    /// <summary>
    /// Computes the normalized visual difference between two consecutive BGRA video frames (0.0 to 255.0).
    /// Combines per-pixel Rec. 709 luminance absolute error with 16-bin 3-channel color histogram divergence.
    /// </summary>
    public static double ComputeFrameDifference(
        ReadOnlySpan<byte> currentBgra,
        ReadOnlySpan<byte> previousBgra,
        int width,
        int height)
    {
        if (width <= 0 || height <= 0) return 0.0;
        var pixelCount = width * height;
        var expectedBytes = pixelCount * 4;
        if (currentBgra.Length < expectedBytes || previousBgra.Length < expectedBytes) return 0.0;

        Span<int> currHistR = stackalloc int[HistogramBins];
        Span<int> currHistG = stackalloc int[HistogramBins];
        Span<int> currHistB = stackalloc int[HistogramBins];
        Span<int> prevHistR = stackalloc int[HistogramBins];
        Span<int> prevHistG = stackalloc int[HistogramBins];
        Span<int> prevHistB = stackalloc int[HistogramBins];

        long lumaDiffSum = 0;

        for (var i = 0; i < pixelCount; i++)
        {
            var offset = i * 4;
            var cB = currentBgra[offset];
            var cG = currentBgra[offset + 1];
            var cR = currentBgra[offset + 2];

            var pB = previousBgra[offset];
            var pG = previousBgra[offset + 1];
            var pR = previousBgra[offset + 2];

            // Rec. 709 integer luminance proxy: 54R + 183G + 19B (~ 0.2126R + 0.7152G + 0.0722B)
            var cLuma = (cR * 54 + cG * 183 + cB * 19) >> 8;
            var pLuma = (pR * 54 + pG * 183 + pB * 19) >> 8;

            lumaDiffSum += Math.Abs(cLuma - pLuma);

            currHistB[cB >> ShiftFor16Bins]++;
            currHistG[cG >> ShiftFor16Bins]++;
            currHistR[cR >> ShiftFor16Bins]++;

            prevHistB[pB >> ShiftFor16Bins]++;
            prevHistG[pG >> ShiftFor16Bins]++;
            prevHistR[pR >> ShiftFor16Bins]++;
        }

        var avgLumaDiff = (double)lumaDiffSum / pixelCount;

        // Histogram divergence (Manhattan distance across normalized R, G, B bins)
        long histDiffSum = 0;
        for (var b = 0; b < HistogramBins; b++)
        {
            histDiffSum += Math.Abs(currHistB[b] - prevHistB[b]);
            histDiffSum += Math.Abs(currHistG[b] - prevHistG[b]);
            histDiffSum += Math.Abs(currHistR[b] - prevHistR[b]);
        }

        // Each channel has N pixels total, sum across 3 channels = 3N. Max possible diff is 2 * 3N = 6N.
        var normalizedHistDiff = (double)histDiffSum / (3.0 * 2.0 * pixelCount); // 0.0 to 1.0
        var histScore255 = normalizedHistDiff * 255.0;

        // 60% luminance pixel difference + 40% chromatic color distribution difference
        return 0.60 * avgLumaDiff + 0.40 * histScore255;
    }

    /// <summary>
    /// Evaluates a sequence of (frameOffset, differenceScore) pairs against detection options
    /// and debounces scene boundaries to ensure no cuts occur closer than MinSceneLengthMs.
    /// </summary>
    public static IReadOnlyList<SceneCut> DetectCutsFromScores(
        IReadOnlyList<(long Offset, double Score)> scores,
        long totalFrames,
        SceneDetectionOptions options,
        Rational frameRate,
        long clipStartAbsoluteFrame = 0)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(options);

        var cuts = new List<SceneCut>();
        var minFrames = options.CalculateMinFrames(frameRate);
        var threshold = options.Threshold;

        long lastCutOffset = 0;

        foreach (var (offset, score) in scores)
        {
            if (offset <= 0 || offset >= totalFrames) continue;

            if (score >= threshold && (offset - lastCutOffset) >= minFrames)
            {
                var timeSec = frameRate.Seconds(offset);
                var absFrame = clipStartAbsoluteFrame + offset;
                cuts.Add(new SceneCut(offset, absFrame, timeSec, score, SceneCutType.HardCut));
                lastCutOffset = offset;
            }
        }

        return cuts;
    }

    /// <summary>
    /// Partitions a video event of totalFrames duration into contiguous SceneSpans based on detected cut points.
    /// </summary>
    public static IReadOnlyList<SceneSpan> BuildSceneSpans(
        IReadOnlyList<SceneCut> cuts,
        long totalFrames,
        long clipStartAbsoluteFrame = 0,
        string baseName = "Scene")
    {
        var spans = new List<SceneSpan>();
        if (totalFrames <= 0) return spans;

        var cutOffsets = new List<long> { 0 };
        if (cuts != null)
        {
            foreach (var cut in cuts)
            {
                if (cut.FrameOffset > 0 && cut.FrameOffset < totalFrames && !cutOffsets.Contains(cut.FrameOffset))
                {
                    cutOffsets.Add(cut.FrameOffset);
                }
            }
        }
        cutOffsets.Sort();
        cutOffsets.Add(totalFrames);

        var sceneIndex = 1;
        for (var i = 0; i < cutOffsets.Count - 1; i++)
        {
            var start = cutOffsets[i];
            var end = cutOffsets[i + 1];
            if (end <= start) continue;

            var absStart = clipStartAbsoluteFrame + start;
            var absEnd = clipStartAbsoluteFrame + end;
            var name = string.IsNullOrWhiteSpace(baseName) ? $"Scene {sceneIndex}" : $"{baseName} [Scene {sceneIndex}]";

            spans.Add(new SceneSpan(sceneIndex, start, end, absStart, absEnd, name));
            sceneIndex++;
        }

        return spans;
    }
}

