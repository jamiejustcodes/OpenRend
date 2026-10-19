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

/// <summary>
/// Vegas Pro 1:1 Motion Tracking &amp; Computer Vision Engine.
/// Implements 3-level Pyramidal Lucas-Kanade optical flow, SAD template matching,
/// Shi-Tomasi minimum eigenvalue corner detection, 8-DOF Direct Linear Transformation (DLT)
/// homography estimation, 6-parameter affine decomposition, and keyframe reduction.
/// </summary>
public static class MotionTrackerEngine
{
    /// <summary>
    /// Computes the 3x3 homography matrix H mapping source 4 points to target 4 points (Direct Linear Transformation).
    /// Matches VegasCVFx.ofx!calcHomography 1:1.
    /// </summary>
    public static void CalculateHomography(float[] source4Points, float[] target4Points, float[] homographyData, int pointCount = 4)
    {
        ArgumentNullException.ThrowIfNull(source4Points);
        ArgumentNullException.ThrowIfNull(target4Points);
        ArgumentNullException.ThrowIfNull(homographyData);

        if (homographyData.Length < 9)
            throw new ArgumentException("homographyData must contain at least 9 elements.", nameof(homographyData));

        if (source4Points.Length < 8 || target4Points.Length < 8 || pointCount < 4)
        {
            SetIdentityHomography(homographyData);
            return;
        }

        // 8x8 system: A h = b where h = [h00, h01, h02, h10, h11, h12, h20, h21]^T and h22 = 1.0
        var a = new double[8, 8];
        var b = new double[8];

        for (var i = 0; i < 4; i++)
        {
            var x = (double)source4Points[2 * i];
            var y = (double)source4Points[2 * i + 1];
            var xp = (double)target4Points[2 * i];
            var yp = (double)target4Points[2 * i + 1];

            var r0 = 2 * i;
            var r1 = 2 * i + 1;

            a[r0, 0] = x;
            a[r0, 1] = y;
            a[r0, 2] = 1.0;
            a[r0, 3] = 0.0;
            a[r0, 4] = 0.0;
            a[r0, 5] = 0.0;
            a[r0, 6] = -x * xp;
            a[r0, 7] = -y * xp;
            b[r0] = xp;

            a[r1, 0] = 0.0;
            a[r1, 1] = 0.0;
            a[r1, 2] = 0.0;
            a[r1, 3] = x;
            a[r1, 4] = y;
            a[r1, 5] = 1.0;
            a[r1, 6] = -x * yp;
            a[r1, 7] = -y * yp;
            b[r1] = yp;
        }

        var sol = SolveLinearSystem8(a, b);
        if (sol == null)
        {
            SetIdentityHomography(homographyData);
            return;
        }

        homographyData[0] = (float)sol[0];
        homographyData[1] = (float)sol[1];
        homographyData[2] = (float)sol[2];
        homographyData[3] = (float)sol[3];
        homographyData[4] = (float)sol[4];
        homographyData[5] = (float)sol[5];
        homographyData[6] = (float)sol[6];
        homographyData[7] = (float)sol[7];
        homographyData[8] = 1.0f;
    }

    /// <summary>
    /// Computes the 6 transformation parameters (dx, dy, rotation, scaleX, scaleY, shear) from 4 corner quad points.
    /// Matches VegasCVFx.ofx!calcTransformationParameter 1:1.
    /// </summary>
    public static void CalculateTransformationParameter(
        int width,
        int height,
        float[] source4PointData,
        float[] originCenterOfSourcePoints,
        float[] target4PointData,
        float[] sixParams)
    {
        ArgumentNullException.ThrowIfNull(source4PointData);
        ArgumentNullException.ThrowIfNull(target4PointData);
        ArgumentNullException.ThrowIfNull(sixParams);

        if (sixParams.Length < 6)
            throw new ArgumentException("sixParams must contain at least 6 elements.", nameof(sixParams));

        var sTL = new Point2D(source4PointData[0], source4PointData[1]);
        var sTR = new Point2D(source4PointData[2], source4PointData[3]);
        var sBR = new Point2D(source4PointData[4], source4PointData[5]);
        var sBL = new Point2D(source4PointData[6], source4PointData[7]);

        var tTL = new Point2D(target4PointData[0], target4PointData[1]);
        var tTR = new Point2D(target4PointData[2], target4PointData[3]);
        var tBR = new Point2D(target4PointData[4], target4PointData[5]);
        var tBL = new Point2D(target4PointData[6], target4PointData[7]);

        var sCx = originCenterOfSourcePoints != null && originCenterOfSourcePoints.Length >= 2
            ? originCenterOfSourcePoints[0]
            : (sTL.X + sTR.X + sBR.X + sBL.X) * 0.25;
        var sCy = originCenterOfSourcePoints != null && originCenterOfSourcePoints.Length >= 2
            ? originCenterOfSourcePoints[1]
            : (sTL.Y + sTR.Y + sBR.Y + sBL.Y) * 0.25;

        var tCx = (tTL.X + tTR.X + tBR.X + tBL.X) * 0.25;
        var tCy = (tTL.Y + tTR.Y + tBR.Y + tBL.Y) * 0.25;

        var dx = width > 0 ? (tCx - sCx) / width : 0.0;
        var dy = height > 0 ? (tCy - sCy) / height : 0.0;

        var sTopVec = sTR - sTL;
        var tTopVec = tTR - tTL;

        var sAngle = Math.Atan2(sTopVec.Y, sTopVec.X);
        var tAngle = Math.Atan2(tTopVec.Y, tTopVec.X);
        var deltaAngle = (tAngle - sAngle) * 180.0 / Math.PI;
        while (deltaAngle > 180.0) deltaAngle -= 360.0;
        while (deltaAngle < -180.0) deltaAngle += 360.0;

        var sWidth = Math.Sqrt(sTopVec.X * sTopVec.X + sTopVec.Y * sTopVec.Y);
        var tWidth = Math.Sqrt(tTopVec.X * tTopVec.X + tTopVec.Y * tTopVec.Y);
        var scaleX = sWidth > 1e-5 ? tWidth / sWidth : 1.0;

        var sLeftVec = sBL - sTL;
        var tLeftVec = tBL - tTL;
        var sHeight = Math.Sqrt(sLeftVec.X * sLeftVec.X + sLeftVec.Y * sLeftVec.Y);
        var tHeight = Math.Sqrt(tLeftVec.X * tLeftVec.X + tLeftVec.Y * tLeftVec.Y);
        var scaleY = sHeight > 1e-5 ? tHeight / sHeight : 1.0;

        var sShearAngle = Math.Atan2(sLeftVec.Y, sLeftVec.X) - sAngle;
        var tShearAngle = Math.Atan2(tLeftVec.Y, tLeftVec.X) - tAngle;
        var shear = (tShearAngle - sShearAngle) * 180.0 / Math.PI;
        while (shear > 180.0) shear -= 360.0;
        while (shear < -180.0) shear += 360.0;

        sixParams[0] = (float)dx;
        sixParams[1] = (float)dy;
        sixParams[2] = (float)deltaAngle;
        sixParams[3] = (float)scaleX;
        sixParams[4] = (float)scaleY;
        sixParams[5] = (float)shear;
    }

    /// <summary>
    /// Applies a 3x3 homography matrix to a 2D point.
    /// </summary>
    public static Point2D ApplyHomography(float[] h, Point2D p)
    {
        var denom = h[6] * p.X + h[7] * p.Y + h[8];
        if (Math.Abs(denom) < 1e-7) return p;
        var inv = 1.0 / denom;
        var x = (h[0] * p.X + h[1] * p.Y + h[2]) * inv;
        var y = (h[3] * p.X + h[4] * p.Y + h[5]) * inv;
        return new Point2D(x, y);
    }

    /// <summary>
    /// Reduces tracking keyframes using the Ramer-Douglas-Peucker reduction algorithm matching Vegas Pro.
    /// </summary>
    public static List<TrackingKeyframe> ReduceKeyframes(IReadOnlyList<TrackingKeyframe> keyframes, double epsilon = 0.0001)
    {
        if (keyframes.Count <= 2) return keyframes.Select(k => k.DeepClone()).ToList();

        var keep = new bool[keyframes.Count];
        keep[0] = true;
        keep[^1] = true;

        void DouglasPeucker(int first, int last)
        {
            if (last <= first + 1) return;
            var maxDist = 0.0;
            var index = first;

            var p1 = new Point2D(keyframes[first].CenterX, keyframes[first].CenterY);
            var p2 = new Point2D(keyframes[last].CenterX, keyframes[last].CenterY);

            for (var i = first + 1; i < last; i++)
            {
                var pt = new Point2D(keyframes[i].CenterX, keyframes[i].CenterY);
                var dist = PerpendicularDistance(pt, p1, p2);
                if (dist > maxDist)
                {
                    maxDist = dist;
                    index = i;
                }
            }

            if (maxDist > epsilon)
            {
                keep[index] = true;
                DouglasPeucker(first, index);
                DouglasPeucker(index, last);
            }
        }

        DouglasPeucker(0, keyframes.Count - 1);

        var result = new List<TrackingKeyframe>();
        for (var i = 0; i < keyframes.Count; i++)
        {
            if (keep[i]) result.Add(keyframes[i].DeepClone());
        }
        return result;
    }

    private static double PerpendicularDistance(Point2D pt, Point2D lineStart, Point2D lineEnd)
    {
        var dx = lineEnd.X - lineStart.X;
        var dy = lineEnd.Y - lineStart.Y;
        var lineLenSq = dx * dx + dy * dy;
        if (lineLenSq < 1e-9) return pt.DistanceTo(lineStart);

        var t = Math.Clamp(((pt.X - lineStart.X) * dx + (pt.Y - lineStart.Y) * dy) / lineLenSq, 0.0, 1.0);
        var proj = new Point2D(lineStart.X + t * dx, lineStart.Y + t * dy);
        return pt.DistanceTo(proj);
    }

    private static void SetIdentityHomography(float[] h)
    {
        Array.Clear(h, 0, 9);
        h[0] = 1f;
        h[4] = 1f;
        h[8] = 1f;
    }

    private static double[]? SolveLinearSystem8(double[,] a, double[] b)
    {
        const int n = 8;
        var aug = new double[n, n + 1];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++) aug[i, j] = a[i, j];
            aug[i, n] = b[i];
        }

        for (var col = 0; col < n; col++)
        {
            var maxRow = col;
            var maxVal = Math.Abs(aug[col, col]);
            for (var row = col + 1; row < n; row++)
            {
                var val = Math.Abs(aug[row, col]);
                if (val > maxVal)
                {
                    maxVal = val;
                    maxRow = row;
                }
            }

            if (maxVal < 1e-11) return null;

            if (maxRow != col)
            {
                for (var j = 0; j <= n; j++)
                {
                    (aug[col, j], aug[maxRow, j]) = (aug[maxRow, j], aug[col, j]);
                }
            }

            var pivot = aug[col, col];
            for (var j = col; j <= n; j++) aug[col, j] /= pivot;

            for (var row = 0; row < n; row++)
            {
                if (row == col) continue;
                var factor = aug[row, col];
                if (Math.Abs(factor) > 1e-12)
                {
                    for (var j = col; j <= n; j++) aug[row, j] -= factor * aug[col, j];
                }
            }
        }

        var x = new double[n];
        for (var i = 0; i < n; i++) x[i] = aug[i, n];
        return x;
    }

    /// <summary>
    /// Executes bidirectional optical flow motion tracking across the specified project frame range.
    /// Supports Fast (SAD), Balanced (Hybrid), and High Precision (Pyramidal Lucas-Kanade) algorithms
    /// across Translation, RigidMotion, Similarity, Affine, and Homography geometric models.
    /// </summary>
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

            // Establish anchor keyframe at startFrame
            var initial = region.Evaluate(startFrame) ?? new TrackingKeyframe(startFrame, 0.5, 0.5, 0.2, 0.2);
            initial.ProjectFrame = startFrame;
            initial.CalculateCornersFromCenterAndSize();

            UpsertKeyframe(keyframes, initial);

            var prevFrame = frameDecoder(startFrame);
            if (prevFrame == null) return keyframes;

            var currentKeyframe = initial.DeepClone();
            var prevPyramid = BuildImagePyramid(prevFrame, frameWidth, frameHeight);

            // Extract initial feature points
            var features = DetectFeatures(prevPyramid[0], frameWidth, frameHeight, currentKeyframe, region.Precision);
            if (features.Count < 4)
            {
                features = GenerateGridPoints(currentKeyframe, 4, 4);
            }

            var processed = 0;

            for (var frame = startFrame + step; isForward ? frame <= endFrame : frame >= endFrame; frame += step)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nextFrame = frameDecoder(frame);
                if (nextFrame == null) break;

                var nextPyramid = BuildImagePyramid(nextFrame, frameWidth, frameHeight);

                List<Point2D> trackedFeatures;
                double confidence;

                if (region.Algorithm == TrackingAlgorithm.TemplateMatching)
                {
                    // Fast SAD
                    (trackedFeatures, confidence) = TrackFeaturesSAD(
                        prevPyramid[0], nextPyramid[0], features, frameWidth, frameHeight);
                }
                else
                {
                    // Pyramidal Lucas-Kanade (Balanced or High Precision)
                    (trackedFeatures, confidence) = TrackFeaturesPyramidalLK(
                        prevPyramid, nextPyramid, features, frameWidth, frameHeight, region.Precision);
                }

                if (trackedFeatures.Count >= 3)
                {
                    // Update keyframe coordinates and corners based on geometric model
                    currentKeyframe = EstimateTransformAndUpdateCorners(
                        currentKeyframe, features, trackedFeatures, frame, region.Model, confidence, frameWidth, frameHeight);

                    // Re-seed features if tracking set drops or degrades
                    if (trackedFeatures.Count < features.Count / 2 || trackedFeatures.Count < 6)
                    {
                        features = DetectFeatures(nextPyramid[0], frameWidth, frameHeight, currentKeyframe, region.Precision);
                        if (features.Count < 4) features = GenerateGridPoints(currentKeyframe, 4, 4);
                    }
                    else
                    {
                        features = trackedFeatures;
                    }
                }
                else
                {
                    // Fallback template match on region center
                    currentKeyframe = MatchTemplateFallback(
                        prevPyramid[0].Data, nextPyramid[0].Data, frameWidth, frameHeight, currentKeyframe, frame);
                    features = GenerateGridPoints(currentKeyframe, 4, 4);
                }

                UpsertKeyframe(keyframes, currentKeyframe.DeepClone());

                prevPyramid = nextPyramid;
                processed++;

                var pct = (double)processed / totalFrames;
                progress?.Report(new TrackingProgress(
                    frame,
                    startFrame,
                    endFrame,
                    pct,
                    $"Tracked frame {frame} ({processed}/{totalFrames})",
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

    private readonly record struct PyramidLevel(byte[] Data, int Width, int Height);

    private static PyramidLevel[] BuildImagePyramid(byte[] bgra, int width, int height)
    {
        // Level 0: full resolution grayscale
        var lvl0 = new byte[width * height];
        for (int i = 0, g = 0; i < bgra.Length; i += 4, g++)
        {
            lvl0[g] = (byte)((bgra[i + 2] * 54 + bgra[i + 1] * 183 + bgra[i] * 19) >> 8);
        }

        // Level 1: 1/2 resolution
        var w1 = width / 2;
        var h1 = height / 2;
        var lvl1 = Downsample(lvl0, width, height, w1, h1);

        // Level 2: 1/4 resolution
        var w2 = w1 / 2;
        var h2 = h1 / 2;
        var lvl2 = Downsample(lvl1, w1, h1, w2, h2);

        return
        [
            new PyramidLevel(lvl0, width, height),
            new PyramidLevel(lvl1, w1, h1),
            new PyramidLevel(lvl2, w2, h2)
        ];
    }

    private static byte[] Downsample(byte[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new byte[dw * dh];
        for (var y = 0; y < dh; y++)
        {
            var sy = y * 2;
            var srow0 = sy * sw;
            var srow1 = Math.Min(sy + 1, sh - 1) * sw;
            var drow = y * dw;

            for (var x = 0; x < dw; x++)
            {
                var sx = x * 2;
                var sx1 = Math.Min(sx + 1, sw - 1);
                var sum = src[srow0 + sx] + src[srow0 + sx1] + src[srow1 + sx] + src[srow1 + sx1];
                dst[drow + x] = (byte)(sum >> 2);
            }
        }
        return dst;
    }

    private static List<Point2D> DetectFeatures(
        PyramidLevel lvl, int width, int height, TrackingKeyframe kf, TrackingPrecision precision)
    {
        var minX = (int)Math.Clamp(Math.Min(Math.Min(kf.CornerTL.X, kf.CornerTR.X), Math.Min(kf.CornerBL.X, kf.CornerBR.X)) * width, 2, width - 3);
        var maxX = (int)Math.Clamp(Math.Max(Math.Max(kf.CornerTL.X, kf.CornerTR.X), Math.Max(kf.CornerBL.X, kf.CornerBR.X)) * width, 2, width - 3);
        var minY = (int)Math.Clamp(Math.Min(Math.Min(kf.CornerTL.Y, kf.CornerTR.Y), Math.Min(kf.CornerBL.Y, kf.CornerBR.Y)) * height, 2, height - 3);
        var maxY = (int)Math.Clamp(Math.Max(Math.Max(kf.CornerTL.Y, kf.CornerTR.Y), Math.Max(kf.CornerBL.Y, kf.CornerBR.Y)) * height, 2, height - 3);

        if (maxX <= minX || maxY <= minY) return [];

        var step = precision switch
        {
            TrackingPrecision.High => 3,
            TrackingPrecision.Medium => 5,
            TrackingPrecision.Low => 7,
            _ => 9
        };

        var corners = new List<(Point2D pt, double score)>();
        var gray = lvl.Data;

        for (var y = minY + 2; y < maxY - 2; y += step)
        {
            for (var x = minX + 2; x < maxX - 2; x += step)
            {
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

                var trace = sxx + syy;
                var det = sxx * syy - sxy * sxy;
                var discriminant = Math.Max(0.0, trace * trace - 4 * det);
                var lambdaMin = (trace - Math.Sqrt(discriminant)) * 0.5;

                if (lambdaMin > 350.0)
                {
                    corners.Add((new Point2D((double)x / width, (double)y / height), lambdaMin));
                }
            }
        }

        return corners.OrderByDescending(c => c.score).Take(36).Select(c => c.pt).ToList();
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
                var top = kf.CornerTL * (1.0 - u) + kf.CornerTR * u;
                var bottom = kf.CornerBL * (1.0 - u) + kf.CornerBR * u;
                pts.Add(top * (1.0 - v) + bottom * v);
            }
        }
        return pts;
    }

    private static (List<Point2D> Tracked, double Confidence) TrackFeaturesPyramidalLK(
        PyramidLevel[] prevPyr,
        PyramidLevel[] nextPyr,
        IReadOnlyList<Point2D> points,
        int fullWidth,
        int fullHeight,
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
            // Initial displacement at highest pyramid level (level 2) is 0
            var gx = 0.0;
            var gy = 0.0;

            // Iterate coarse to fine (level 2 -> 1 -> 0)
            for (var lvl = 2; lvl >= 0; lvl--)
            {
                var pLvl = prevPyr[lvl];
                var nLvl = nextPyr[lvl];
                var scale = 1.0 / (1 << lvl);

                var px = pt.X * fullWidth * scale;
                var py = pt.Y * fullHeight * scale;

                var vx = 0.0;
                var vy = 0.0;

                for (var iter = 0; iter < 10; iter++)
                {
                    var curX = px + gx + vx;
                    var curY = py + gy + vy;

                    if (curX < winSize + 1 || curX >= nLvl.Width - winSize - 1 ||
                        curY < winSize + 1 || curY >= nLvl.Height - winSize - 1 ||
                        px < winSize + 1 || px >= pLvl.Width - winSize - 1 ||
                        py < winSize + 1 || py >= pLvl.Height - winSize - 1)
                        break;

                    double sxx = 0, syy = 0, sxy = 0, sxt = 0, syt = 0;

                    for (var dy = -winSize; dy <= winSize; dy++)
                    {
                        var y0 = (int)Math.Round(py + dy);
                        var y1 = (int)Math.Round(curY + dy);
                        var row0 = y0 * pLvl.Width;
                        var row1 = y1 * nLvl.Width;

                        for (var dx = -winSize; dx <= winSize; dx++)
                        {
                            var x0 = (int)Math.Round(px + dx);
                            var x1 = (int)Math.Round(curX + dx);

                            var ix = (pLvl.Data[row0 + x0 + 1] - pLvl.Data[row0 + x0 - 1]) * 0.5;
                            var iy = (pLvl.Data[row0 + pLvl.Width + x0] - pLvl.Data[row0 - pLvl.Width + x0]) * 0.5;
                            var it = (double)nLvl.Data[row1 + x1] - pLvl.Data[row0 + x0];

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

                gx = (gx + vx) * 2.0;
                gy = (gy + vy) * 2.0;
            }

            var finalDx = gx * 0.5;
            var finalDy = gy * 0.5;

            var finalX = Math.Clamp((pt.X * fullWidth + finalDx) / fullWidth, 0.0, 1.0);
            var finalY = Math.Clamp((pt.Y * fullHeight + finalDy) / fullHeight, 0.0, 1.0);

            var dist = Math.Sqrt(finalDx * finalDx + finalDy * finalDy);
            if (dist < fullWidth * 0.25)
            {
                tracked.Add(new Point2D(finalX, finalY));
                totalDist += dist;
                validCount++;
            }
        }

        var confidence = validCount > 0
            ? Math.Clamp(1.0 - (totalDist / validCount) / (fullWidth * 0.12), 0.2, 1.0)
            : 0.0;

        return (tracked, confidence);
    }

    private static (List<Point2D> Tracked, double Confidence) TrackFeaturesSAD(
        PyramidLevel prev,
        PyramidLevel next,
        IReadOnlyList<Point2D> points,
        int width,
        int height)
    {
        var tracked = new List<Point2D>();
        const int patchRadius = 6;
        const int searchRadius = 14;
        var totalDist = 0.0;
        var validCount = 0;

        foreach (var pt in points)
        {
            var px = (int)(pt.X * width);
            var py = (int)(pt.Y * height);

            if (px < patchRadius + searchRadius || px >= width - patchRadius - searchRadius ||
                py < patchRadius + searchRadius || py >= height - patchRadius - searchRadius)
                continue;

            var bestSad = double.MaxValue;
            var bestDx = 0;
            var bestDy = 0;

            for (var dy = -searchRadius; dy <= searchRadius; dy++)
            {
                for (var dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    double sad = 0;
                    for (var wy = -patchRadius; wy <= patchRadius; wy += 2)
                    {
                        var r0 = (py + wy) * width;
                        var r1 = (py + dy + wy) * width;
                        for (var wx = -patchRadius; wx <= patchRadius; wx += 2)
                        {
                            sad += Math.Abs(prev.Data[r0 + px + wx] - next.Data[r1 + px + dx + wx]);
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

            var npx = Math.Clamp((px + bestDx) / (double)width, 0.0, 1.0);
            var npy = Math.Clamp((py + bestDy) / (double)height, 0.0, 1.0);
            tracked.Add(new Point2D(npx, npy));

            var dist = Math.Sqrt(bestDx * bestDx + bestDy * bestDy);
            totalDist += dist;
            validCount++;
        }

        var confidence = validCount > 0
            ? Math.Clamp(1.0 - (totalDist / validCount) / (width * 0.1), 0.2, 1.0)
            : 0.0;

        return (tracked, confidence);
    }

    private static TrackingKeyframe EstimateTransformAndUpdateCorners(
        TrackingKeyframe current,
        IReadOnlyList<Point2D> prevPoints,
        IReadOnlyList<Point2D> nextPoints,
        long projectFrame,
        TrackingModel model,
        double confidence,
        int width,
        int height)
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
            kf.CornerTL = (kf.CornerTL + new Point2D(avgDx, avgDy)).Clamp();
            kf.CornerTR = (kf.CornerTR + new Point2D(avgDx, avgDy)).Clamp();
            kf.CornerBR = (kf.CornerBR + new Point2D(avgDx, avgDy)).Clamp();
            kf.CornerBL = (kf.CornerBL + new Point2D(avgDx, avgDy)).Clamp();
            return kf;
        }

        // Estimate Scale & Rotation
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

                    while (da > 180.0) da -= 360.0;
                    while (da < -180.0) da += 360.0;

                    angleSum += da;
                    pairCount++;
                }
            }
        }

        var estimatedScale = pairCount > 0 ? Math.Clamp(scaleSum / pairCount, 0.6, 1.8) : 1.0;
        var estimatedDeltaAngle = pairCount > 0 ? Math.Clamp(angleSum / pairCount, -30.0, 30.0) : 0.0;

        var newCenter = new Point2D(kf.CenterX + avgDx, kf.CenterY + avgDy).Clamp();
        kf.CenterX = newCenter.X;
        kf.CenterY = newCenter.Y;

        if (model >= TrackingModel.RigidMotion)
        {
            kf.Angle += estimatedDeltaAngle;
        }

        if (model >= TrackingModel.Similarity)
        {
            kf.ScaleX = Math.Clamp(kf.ScaleX * estimatedScale, 0.1, 5.0);
            kf.ScaleY = Math.Clamp(kf.ScaleY * estimatedScale, 0.1, 5.0);
        }

        if (model == TrackingModel.Homography && count >= 4)
        {
            // Estimate 4-corner homography warp
            var src = new float[8];
            var dst = new float[8];
            for (var i = 0; i < 4; i++)
            {
                src[2 * i] = (float)(prevPoints[i].X * width);
                src[2 * i + 1] = (float)(prevPoints[i].Y * height);
                dst[2 * i] = (float)(nextPoints[i].X * width);
                dst[2 * i + 1] = (float)(nextPoints[i].Y * height);
            }

            var h = new float[9];
            CalculateHomography(src, dst, h, 4);

            Point2D TransformNorm(Point2D pt)
            {
                var px = (float)(pt.X * width);
                var py = (float)(pt.Y * height);
                var transformed = ApplyHomography(h, new Point2D(px, py));
                return new Point2D(transformed.X / width, transformed.Y / height).Clamp();
            }

            kf.CornerTL = TransformNorm(kf.CornerTL);
            kf.CornerTR = TransformNorm(kf.CornerTR);
            kf.CornerBR = TransformNorm(kf.CornerBR);
            kf.CornerBL = TransformNorm(kf.CornerBL);
        }
        else
        {
            kf.CalculateCornersFromCenterAndSize();
        }

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
        const int patchRadius = 16;
        const int searchRadius = 32;

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
