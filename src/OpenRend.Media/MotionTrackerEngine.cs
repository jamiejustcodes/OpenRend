using OpenRend.Core;

namespace OpenRend.Media;

public readonly record struct TrackingProgress(
    long CurrentFrame,
    long StartFrame,
    long EndFrame,
    double Percent,
    string StatusText,
    TrackingKeyframe? LatestKeyframe
);

public static class MotionTrackerEngine
{
    public static async Task<IReadOnlyList<TrackingKeyframe>> TrackAsync(
        TimelineClip clip,
        MotionTrackData region,
        long startFrame,
        long endFrame,
        Func<long, byte[]?> frameDecoder,
        int frameWidth,
        int frameHeight,
        IProgress<TrackingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(frameDecoder);

        if (frameWidth <= 0 || frameHeight <= 0)
            throw new ArgumentException("Invalid frame dimensions.");

        region.Validate();

        var isForward = endFrame >= startFrame;
        var step = isForward ? 1 : -1;
        var totalFrames = Math.Abs(endFrame - startFrame) + 1;

        return await Task.Run(() =>
        {
            var keyframes = new List<TrackingKeyframe>(region.Keyframes);

            // Get initial keyframe or generate one at startFrame from region bounds
            var initial = region.Evaluate(startFrame) ?? new TrackingKeyframe(startFrame, 0.5, 0.5, 0.2, 0.2);
            initial.ProjectFrame = startFrame;
            initial.CalculateCornersFromCenterAndSize();

            UpsertKeyframe(keyframes, initial);

            var prevFrame = frameDecoder(startFrame);
            if (prevFrame == null) return keyframes;

            var currentKeyframe = initial.DeepClone();
            var prevGray = ToGrayscale(prevFrame, frameWidth, frameHeight);

            // Extract features inside the bounding box
            var features = DetectFeatures(prevGray, frameWidth, frameHeight, currentKeyframe, region.Precision);
            if (features.Count == 0)
            {
                // Fallback to center point grid if no strong corners found
                features = GenerateGridPoints(currentKeyframe, 4, 4);
            }

            var processed = 0;

            for (var frame = startFrame + step; isForward ? frame <= endFrame : frame >= endFrame; frame += step)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nextFrame = frameDecoder(frame);
                if (nextFrame == null) break;

                var nextGray = ToGrayscale(nextFrame, frameWidth, frameHeight);

                // Track features using Pyramidal Lucas-Kanade
                var (trackedFeatures, confidence) = TrackFeaturesLK(
                    prevGray, nextGray, features, frameWidth, frameHeight, region.Precision);

                if (trackedFeatures.Count >= 3)
                {
                    // Estimate transformation matrix and update corners
                    currentKeyframe = EstimateTransformAndUpdateKeyframe(
                        currentKeyframe, features, trackedFeatures, frame, region.Model, confidence);

                    // Re-detect features if too many were lost
                    if (trackedFeatures.Count < features.Count / 2 || trackedFeatures.Count < 6)
                    {
                        features = DetectFeatures(nextGray, frameWidth, frameHeight, currentKeyframe, region.Precision);
                        if (features.Count < 3) features = GenerateGridPoints(currentKeyframe, 4, 4);
                    }
                    else
                    {
                        features = trackedFeatures;
                    }
                }
                else
                {
                    // Drift fallback: template matching on region center
                    currentKeyframe = MatchTemplateFallback(
                        prevGray, nextGray, frameWidth, frameHeight, currentKeyframe, frame);
                    features = GenerateGridPoints(currentKeyframe, 4, 4);
                }

                UpsertKeyframe(keyframes, currentKeyframe.DeepClone());

                prevGray = nextGray;
                processed++;

                var pct = (double)processed / totalFrames;
                progress?.Report(new TrackingProgress(
                    frame,
                    startFrame,
                    endFrame,
                    pct,
                    $"Tracking frame {frame} ({processed}/{totalFrames})...",
                    currentKeyframe
                ));
            }

            keyframes.Sort((a, b) => a.ProjectFrame.CompareTo(b.ProjectFrame));
            return (IReadOnlyList<TrackingKeyframe>)keyframes;
        }, cancellationToken);
    }

    private static void UpsertKeyframe(List<TrackingKeyframe> list, TrackingKeyframe kf)
    {
        var existing = list.FindIndex(k => k.ProjectFrame == kf.ProjectFrame);
        if (existing >= 0) list[existing] = kf;
        else list.Add(kf);
    }

    private static byte[] ToGrayscale(byte[] bgra, int width, int height)
    {
        var gray = new byte[width * height];
        for (int i = 0, g = 0; i < bgra.Length; i += 4, g++)
        {
            // Standard BT.709 Luminance
            gray[g] = (byte)((bgra[i + 2] * 54 + bgra[i + 1] * 183 + bgra[i] * 19) >> 8);
        }
        return gray;
    }

    private static List<Point2D> DetectFeatures(
        byte[] gray, int width, int height, TrackingKeyframe kf, TrackingPrecision precision)
    {
        var minX = (int)Math.Clamp(Math.Min(Math.Min(kf.CornerTL.X, kf.CornerTR.X), Math.Min(kf.CornerBL.X, kf.CornerBR.X)) * width, 2, width - 3);
        var maxX = (int)Math.Clamp(Math.Max(Math.Max(kf.CornerTL.X, kf.CornerTR.X), Math.Max(kf.CornerBL.X, kf.CornerBR.X)) * width, 2, width - 3);
        var minY = (int)Math.Clamp(Math.Min(Math.Min(kf.CornerTL.Y, kf.CornerTR.Y), Math.Min(kf.CornerBL.Y, kf.CornerBR.Y)) * height, 2, height - 3);
        var maxY = (int)Math.Clamp(Math.Max(Math.Max(kf.CornerTL.Y, kf.CornerTR.Y), Math.Max(kf.CornerBL.Y, kf.CornerBR.Y)) * height, 2, height - 3);

        if (maxX <= minX || maxY <= minY) return [];

        var step = precision switch
        {
            TrackingPrecision.High => 4,
            TrackingPrecision.Medium => 6,
            TrackingPrecision.Low => 8,
            _ => 10
        };

        var corners = new List<(Point2D pt, double score)>();

        for (var y = minY + 2; y < maxY - 2; y += step)
        {
            for (var x = minX + 2; x < maxX - 2; x += step)
            {
                // Shi-Tomasi minimum eigenvalue cornerness
                double sxx = 0, syy = 0, sxy = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    var row = (y + dy) * width;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var ix = gray[row + x + dx + 1] - gray[row + x + dx - 1];
                        var iy = gray[row + width + x + dx] - gray[row - width + x + dx];
                        sxx += ix * ix;
                        syy += iy * iy;
                        sxy += ix * iy;
                    }
                }

                // Minimum eigenvalue of [sxx sxy; sxy syy]
                var trace = sxx + syy;
                var det = sxx * syy - sxy * sxy;
                var discriminant = Math.Max(0.0, trace * trace - 4 * det);
                var lambdaMin = (trace - Math.Sqrt(discriminant)) * 0.5;

                if (lambdaMin > 500)
                {
                    corners.Add((new Point2D((double)x / width, (double)y / height), lambdaMin));
                }
            }
        }

        // Return top 25 features sorted by corner strength
        return corners.OrderByDescending(c => c.score).Take(25).Select(c => c.pt).ToList();
    }

    private static List<Point2D> GenerateGridPoints(TrackingKeyframe kf, int rows, int cols)
    {
        var pts = new List<Point2D>();
        for (var r = 1; r <= rows; r++)
        {
            var v = (double)r / (rows + 1);
            for (var c = 1; c <= cols; c++)
            {
                var u = (double)c / (cols + 1);
                // Bilinear interpolation between 4 corners
                var top = kf.CornerTL * (1.0 - u) + kf.CornerTR * u;
                var bottom = kf.CornerBL * (1.0 - u) + kf.CornerBR * u;
                pts.Add(top * (1.0 - v) + bottom * v);
            }
        }
        return pts;
    }

    private static (List<Point2D> Tracked, double Confidence) TrackFeaturesLK(
        byte[] prevGray,
        byte[] nextGray,
        IReadOnlyList<Point2D> points,
        int width,
        int height,
        TrackingPrecision precision)
    {
        var winSize = precision switch
        {
            TrackingPrecision.High => 7,
            TrackingPrecision.Medium => 5,
            _ => 4
        };

        var tracked = new List<Point2D>();
        var totalDist = 0.0;
        var validCount = 0;

        foreach (var pt in points)
        {
            var px = pt.X * width;
            var py = pt.Y * height;

            if (px < winSize + 2 || px >= width - winSize - 2 ||
                py < winSize + 2 || py >= height - winSize - 2)
                continue;

            // Iterative Lucas-Kanade optical flow
            var vx = 0.0;
            var vy = 0.0;

            for (var iter = 0; iter < 10; iter++)
            {
                double sxx = 0, syy = 0, sxy = 0, sxt = 0, syt = 0;
                var curX = px + vx;
                var curY = py + vy;

                if (curX < winSize + 1 || curX >= width - winSize - 1 ||
                    curY < winSize + 1 || curY >= height - winSize - 1)
                    break;

                for (var dy = -winSize; dy <= winSize; dy++)
                {
                    var y0 = (int)Math.Round(py + dy);
                    var y1 = (int)Math.Round(curY + dy);
                    var row0 = y0 * width;
                    var row1 = y1 * width;

                    for (var dx = -winSize; dx <= winSize; dx++)
                    {
                        var x0 = (int)Math.Round(px + dx);
                        var x1 = (int)Math.Round(curX + dx);

                        var ix = (prevGray[row0 + x0 + 1] - prevGray[row0 + x0 - 1]) * 0.5;
                        var iy = (prevGray[row0 + width + x0] - prevGray[row0 - width + x0]) * 0.5;
                        var it = (double)nextGray[row1 + x1] - prevGray[row0 + x0];

                        sxx += ix * ix;
                        syy += iy * iy;
                        sxy += ix * iy;
                        sxt += ix * it;
                        syt += iy * it;
                    }
                }

                var det = sxx * syy - sxy * sxy;
                if (Math.Abs(det) < 1e-6) break;

                var dvx = -(syy * sxt - sxy * syt) / det;
                var dvy = -(sxx * syt - sxy * sxt) / det;

                vx += dvx;
                vy += dvy;

                if (dvx * dvx + dvy * dvy < 0.001) break;
            }

            var npx = Math.Clamp((px + vx) / width, 0.0, 1.0);
            var npy = Math.Clamp((py + vy) / height, 0.0, 1.0);

            var dist = Math.Sqrt(vx * vx + vy * vy);
            if (dist < width * 0.2) // reject wild jumps
            {
                tracked.Add(new Point2D(npx, npy));
                totalDist += dist;
                validCount++;
            }
        }

        var confidence = validCount > 0 ? Math.Clamp(1.0 - (totalDist / validCount) / (width * 0.1), 0.2, 1.0) : 0.0;
        return (tracked, confidence);
    }

    private static TrackingKeyframe EstimateTransformAndUpdateKeyframe(
        TrackingKeyframe current,
        IReadOnlyList<Point2D> prevPoints,
        IReadOnlyList<Point2D> nextPoints,
        long projectFrame,
        TrackingModel model,
        double confidence)
    {
        var count = Math.Min(prevPoints.Count, nextPoints.Count);
        if (count < 2) return current;

        // Compute average translation
        var avgDx = 0.0;
        var avgDy = 0.0;
        for (var i = 0; i < count; i++)
        {
            avgDx += nextPoints[i].X - prevPoints[i].X;
            avgDy += nextPoints[i].Y - prevPoints[i].Y;
        }
        avgDx /= count;
        avgDy /= count;

        var kf = current.DeepClone();
        kf.ProjectFrame = projectFrame;
        kf.Confidence = confidence;

        if (model == TrackingModel.Translation)
        {
            kf.CenterX = Math.Clamp(kf.CenterX + avgDx, 0.0, 1.0);
            kf.CenterY = Math.Clamp(kf.CenterY + avgDy, 0.0, 1.0);
            kf.CalculateCornersFromCenterAndSize();
            return kf;
        }

        // Estimate Scale & Rotation via point pair distances and angles
        var scaleSum = 0.0;
        var angleSum = 0.0;
        var pairCount = 0;

        for (var i = 0; i < count - 1; i++)
        {
            for (var j = i + 1; j < count; j++)
            {
                var d0 = prevPoints[i].DistanceTo(prevPoints[j]);
                var d1 = nextPoints[i].DistanceTo(nextPoints[j]);

                if (d0 > 0.01)
                {
                    scaleSum += d1 / d0;

                    var a0 = Math.Atan2(prevPoints[j].Y - prevPoints[i].Y, prevPoints[j].X - prevPoints[i].X);
                    var a1 = Math.Atan2(nextPoints[j].Y - nextPoints[i].Y, nextPoints[j].X - nextPoints[i].X);
                    var da = (a1 - a0) * 180.0 / Math.PI;

                    while (da > 180) da -= 360;
                    while (da < -180) da += 360;

                    angleSum += da;
                    pairCount++;
                }
            }
        }

        var estimatedScale = pairCount > 0 ? Math.Clamp(scaleSum / pairCount, 0.5, 2.0) : 1.0;
        var estimatedDeltaAngle = pairCount > 0 ? Math.Clamp(angleSum / pairCount, -30.0, 30.0) : 0.0;

        kf.CenterX = Math.Clamp(kf.CenterX + avgDx, 0.0, 1.0);
        kf.CenterY = Math.Clamp(kf.CenterY + avgDy, 0.0, 1.0);

        if (model >= TrackingModel.RigidMotion)
        {
            kf.Angle += estimatedDeltaAngle;
        }

        if (model >= TrackingModel.Similarity)
        {
            kf.ScaleX = Math.Clamp(kf.ScaleX * estimatedScale, 0.1, 5.0);
            kf.ScaleY = Math.Clamp(kf.ScaleY * estimatedScale, 0.1, 5.0);
        }

        kf.CalculateCornersFromCenterAndSize();
        return kf;
    }

    private static TrackingKeyframe MatchTemplateFallback(
        byte[] prevGray,
        byte[] nextGray,
        int width,
        int height,
        TrackingKeyframe current,
        long projectFrame)
    {
        var cx = (int)(current.CenterX * width);
        var cy = (int)(current.CenterY * height);
        var patchRadius = 16;
        var searchRadius = 32;

        if (cx < patchRadius || cx >= width - patchRadius || cy < patchRadius || cy >= height - patchRadius)
            return current;

        var bestSad = double.MaxValue;
        var bestDx = 0;
        var bestDy = 0;

        for (var dy = -searchRadius; dy <= searchRadius; dy += 2)
        {
            var testY = cy + dy;
            if (testY < patchRadius || testY >= height - patchRadius) continue;

            for (var dx = -searchRadius; dx <= searchRadius; dx += 2)
            {
                var testX = cx + dx;
                if (testX < patchRadius || testX >= width - patchRadius) continue;

                double sad = 0;
                for (var py = -patchRadius; py <= patchRadius; py += 2)
                {
                    var row0 = (cy + py) * width;
                    var row1 = (testY + py) * width;
                    for (var px = -patchRadius; px <= patchRadius; px += 2)
                    {
                        sad += Math.Abs(prevGray[row0 + cx + px] - nextGray[row1 + testX + px]);
                    }
                }

                if (sad < bestSad)
                {
                    bestSad = sad;
                    bestDx = dx;
                    bestDy = dy;
                }
            }
        }

        var kf = current.DeepClone();
        kf.ProjectFrame = projectFrame;
        kf.CenterX = Math.Clamp((cx + bestDx) / (double)width, 0.0, 1.0);
        kf.CenterY = Math.Clamp((cy + bestDy) / (double)height, 0.0, 1.0);
        kf.Confidence = 0.5;
        kf.CalculateCornersFromCenterAndSize();
        return kf;
    }
}
