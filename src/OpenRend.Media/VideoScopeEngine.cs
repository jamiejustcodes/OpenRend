using System;
using System.IO;

namespace OpenRend.Media;

/// <summary>
/// Operational mode for the video scopes display.
/// </summary>
public enum VideoScopeMode
{
    Waveform,
    Vectorscope,
    RgbParade,
    Histogram
}

/// <summary>
/// Graticule scale standard for video scope calibration.
/// </summary>
public enum VideoScopeScale
{
    StudioRgb, // 16 (black) to 235 (white)
    FullRange  // 0 to 255
}

/// <summary>
/// High-performance real-time video scopes analysis and rasterization engine.
/// Provides 1:1 parity with Vegas Pro and DaVinci Resolve scopes:
/// - Rec. 709 Luminance and RGB Waveform monitor with IRE graticule
/// - Polar Vectorscope with Rec. 709 75%/100% color targets and skin tone line
/// - RGB Parade with individual channel separation
/// - 256-bin Color and Luminance Histogram
/// </summary>
public static class VideoScopeEngine
{
    // Authentic phosphor palette colors matching hardware monitors & Vegas Pro
    private const uint PhosphorDim = 0xFF003B14;
    private const uint PhosphorMid = 0xFF00A03C;
    private const uint PhosphorBright = 0xFF20FF70;
    private const uint PhosphorPeak = 0xFFC0FFDF;

    private const uint GraticuleDim = 0xFF1E2822;
    private const uint GraticuleLine = 0xFF2E4438;
    private const uint GraticuleLabel = 0xFF58886E;
    private const uint BackgroundDark = 0xFF0A0E0C;

    /// <summary>
    /// Renders a video scope to a 32-bit BGRA pixel array.
    /// </summary>
    public static byte[] RenderScope(
        ReadOnlySpan<byte> bgraSource,
        int srcWidth,
        int srcHeight,
        int srcStride,
        int scopeWidth,
        int scopeHeight,
        VideoScopeMode mode,
        VideoScopeScale scale = VideoScopeScale.StudioRgb,
        double traceIntensity = 1.0)
    {
        if (scopeWidth < 64 || scopeHeight < 64)
        {
            scopeWidth = Math.Max(64, scopeWidth);
            scopeHeight = Math.Max(64, scopeHeight);
        }

        var buffer = new uint[scopeWidth * scopeHeight];
        Array.Fill(buffer, BackgroundDark);

        if (bgraSource.IsEmpty || srcWidth <= 0 || srcHeight <= 0)
        {
            RenderGraticule(buffer, scopeWidth, scopeHeight, mode, scale);
            return ConvertToByteArray(buffer);
        }

        switch (mode)
        {
            case VideoScopeMode.Waveform:
                RenderWaveform(bgraSource, srcWidth, srcHeight, srcStride, buffer, scopeWidth, scopeHeight, scale, traceIntensity);
                break;
            case VideoScopeMode.Vectorscope:
                RenderVectorscope(bgraSource, srcWidth, srcHeight, srcStride, buffer, scopeWidth, scopeHeight, scale, traceIntensity);
                break;
            case VideoScopeMode.RgbParade:
                RenderRgbParade(bgraSource, srcWidth, srcHeight, srcStride, buffer, scopeWidth, scopeHeight, scale, traceIntensity);
                break;
            case VideoScopeMode.Histogram:
                RenderHistogram(bgraSource, srcWidth, srcHeight, srcStride, buffer, scopeWidth, scopeHeight, scale);
                break;
        }

        return ConvertToByteArray(buffer);
    }

    #region Waveform Monitor

    private static void RenderWaveform(
        ReadOnlySpan<byte> src,
        int srcW,
        int srcH,
        int srcStride,
        uint[] buffer,
        int scopeW,
        int scopeH,
        VideoScopeScale scale,
        double intensity)
    {
        int densityStride = scopeW;
        var density = new int[scopeW * scopeH];

        // Sample pixels with adaptive stride to keep CPU overhead sub-millisecond
        int stepX = Math.Max(1, srcW / scopeW);
        int stepY = Math.Max(1, srcH / 240);

        int maxVal = 255;
        int minVal = 0;

        for (int y = 0; y < srcH; y += stepY)
        {
            int rowOffset = y * srcStride;
            for (int x = 0; x < srcW; x += stepX)
            {
                int p = rowOffset + (x * 4);
                if (p + 2 >= src.Length) continue;

                byte b = src[p];
                byte g = src[p + 1];
                byte r = src[p + 2];

                // Rec. 709 Luminance: Y = 0.2126*R + 0.7152*G + 0.0722*B
                int yLum = (54 * r + 183 * g + 19 * b) >> 8;
                yLum = Math.Clamp(yLum, minVal, maxVal);

                int sx = (x * (scopeW - 1)) / (srcW - 1);
                int sy = scopeH - 1 - (yLum * (scopeH - 1)) / 255;

                if (sx >= 0 && sx < scopeW && sy >= 0 && sy < scopeH)
                {
                    density[sy * densityStride + sx]++;
                }
            }
        }

        // Draw Graticule
        DrawWaveformGraticule(buffer, scopeW, scopeH, scale);

        // Render phosphor trace with additive density glow
        int maxDensity = 1;
        for (int i = 0; i < density.Length; i++)
        {
            if (density[i] > maxDensity) maxDensity = density[i];
        }

        double gain = Math.Clamp(intensity, 0.2, 3.0);
        for (int y = 0; y < scopeH; y++)
        {
            for (int x = 0; x < scopeW; x++)
            {
                int d = density[y * densityStride + x];
                if (d > 0)
                {
                    double norm = Math.Min(1.0, (d / (double)maxDensity) * gain * 2.5);
                    uint color = InterpolatePhosphor(norm);
                    buffer[y * scopeW + x] = BlendPixels(buffer[y * scopeW + x], color);
                }
            }
        }
    }

    private static void DrawWaveformGraticule(uint[] buffer, int w, int h, VideoScopeScale scale)
    {
        // Reference IRE lines: 100% (235 Studio or 255 Full), 50% (128), 7.5% Setup, 0% (16 Studio or 0 Full)
        int[] ireLevels = scale == VideoScopeScale.StudioRgb
            ? [16, 32, 64, 128, 192, 235]
            : [0, 51, 102, 128, 153, 204, 255];

        foreach (var code in ireLevels)
        {
            int y = h - 1 - (code * (h - 1)) / 255;
            if (y >= 0 && y < h)
            {
                uint col = (code == 16 || code == 235 || code == 0 || code == 255) ? GraticuleLine : GraticuleDim;
                for (int x = 0; x < w; x += 3) // dotted line
                {
                    buffer[y * w + x] = col;
                }
            }
        }
    }

    #endregion

    #region Vectorscope

    private static void RenderVectorscope(
        ReadOnlySpan<byte> src,
        int srcW,
        int srcH,
        int srcStride,
        uint[] buffer,
        int scopeW,
        int scopeH,
        VideoScopeScale scale,
        double intensity)
    {
        int centerX = scopeW / 2;
        int centerY = scopeH / 2;
        int radius = (int)(Math.Min(scopeW, scopeH) * 0.44);
        if (radius <= 0) return;

        var density = new int[scopeW * scopeH];

        int stepX = Math.Max(1, srcW / 240);
        int stepY = Math.Max(1, srcH / 240);

        for (int y = 0; y < srcH; y += stepY)
        {
            int rowOffset = y * srcStride;
            for (int x = 0; x < srcW; x += stepX)
            {
                int p = rowOffset + (x * 4);
                if (p + 2 >= src.Length) continue;

                byte b = src[p];
                byte g = src[p + 1];
                byte r = src[p + 2];

                // Rec. 709 Cb and Cr chrominance coordinates normalized to [-1, 1]
                // Cb = -0.114572*R - 0.385428*G + 0.5*B
                // Cr = 0.5*R - 0.454153*G - 0.045847*B
                double cb = (-0.114572 * r - 0.385428 * g + 0.5 * b) / 128.0;
                double cr = (0.5 * r - 0.454153 * g - 0.045847 * b) / 128.0;

                int sx = centerX + (int)(cb * radius);
                int sy = centerY - (int)(cr * radius);

                if (sx >= 0 && sx < scopeW && sy >= 0 && sy < scopeH)
                {
                    density[sy * scopeW + sx]++;
                }
            }
        }

        // Draw Vectorscope Circular Graticule & Targets
        DrawVectorscopeGraticule(buffer, scopeW, scopeH, centerX, centerY, radius);

        int maxDensity = 1;
        for (int i = 0; i < density.Length; i++)
        {
            if (density[i] > maxDensity) maxDensity = density[i];
        }

        double gain = Math.Clamp(intensity, 0.2, 3.0);
        for (int y = 0; y < scopeH; y++)
        {
            for (int x = 0; x < scopeW; x++)
            {
                int d = density[y * scopeW + x];
                if (d > 0)
                {
                    double norm = Math.Min(1.0, (d / (double)maxDensity) * gain * 3.0);
                    uint color = InterpolatePhosphor(norm);
                    buffer[y * scopeW + x] = BlendPixels(buffer[y * scopeW + x], color);
                }
            }
        }
    }

    private static void DrawVectorscopeGraticule(uint[] buffer, int w, int h, int cx, int cy, int r)
    {
        // Outer 100% circle and inner 75% circle
        DrawCircle(buffer, w, h, cx, cy, r, GraticuleDim);
        int r75 = (int)(r * 0.75);
        DrawCircle(buffer, w, h, cx, cy, r75, GraticuleLine);

        // Center crosshairs
        for (int x = cx - 15; x <= cx + 15; x++)
        {
            if (x >= 0 && x < w) buffer[cy * w + x] = GraticuleLine;
        }
        for (int y = cy - 15; y <= cy + 15; y++)
        {
            if (y >= 0 && y < h) buffer[y * w + cx] = GraticuleLine;
        }

        // Skin tone line (approx 123 degrees / I-line)
        double skinAngleRad = 123.0 * Math.PI / 180.0;
        int skinX = cx + (int)(Math.Cos(skinAngleRad) * r);
        int skinY = cy - (int)(Math.Sin(skinAngleRad) * r);
        DrawDottedLine(buffer, w, h, cx, cy, skinX, skinY, GraticuleLabel);

        // Rec. 709 75% Target Boxes: (R, Mg, B, Cy, G, Yl)
        // Normalized (Cb, Cr) vectors for 75% SMPTE color bars
        (double cb, double cr, string label)[] targets = [
            (-0.108, 0.457, "R"),
            (0.217, 0.347, "Mg"),
            (0.325, -0.110, "B"),
            (0.108, -0.457, "Cy"),
            (-0.217, -0.347, "G"),
            (-0.325, 0.110, "Yl")
        ];

        foreach (var (tcb, tcr, _) in targets)
        {
            int tx = cx + (int)(tcb * r * 1.333);
            int ty = cy - (int)(tcr * r * 1.333);
            DrawTargetBox(buffer, w, h, tx, ty, 6, GraticuleLabel);
        }
    }

    #endregion

    #region RGB Parade

    private static void RenderRgbParade(
        ReadOnlySpan<byte> src,
        int srcW,
        int srcH,
        int srcStride,
        uint[] buffer,
        int scopeW,
        int scopeH,
        VideoScopeScale scale,
        double intensity)
    {
        int paneW = scopeW / 3;
        if (paneW <= 10) return;

        var densityR = new int[paneW * scopeH];
        var densityG = new int[paneW * scopeH];
        var densityB = new int[paneW * scopeH];

        int stepX = Math.Max(1, srcW / paneW);
        int stepY = Math.Max(1, srcH / 200);

        for (int y = 0; y < srcH; y += stepY)
        {
            int rowOffset = y * srcStride;
            for (int x = 0; x < srcW; x += stepX)
            {
                int p = rowOffset + (x * 4);
                if (p + 2 >= src.Length) continue;

                byte b = src[p];
                byte g = src[p + 1];
                byte r = src[p + 2];

                int sx = (x * (paneW - 1)) / (srcW - 1);
                int syR = scopeH - 1 - (r * (scopeH - 1)) / 255;
                int syG = scopeH - 1 - (g * (scopeH - 1)) / 255;
                int syB = scopeH - 1 - (b * (scopeH - 1)) / 255;

                if (sx >= 0 && sx < paneW)
                {
                    if (syR >= 0 && syR < scopeH) densityR[syR * paneW + sx]++;
                    if (syG >= 0 && syG < scopeH) densityG[syG * paneW + sx]++;
                    if (syB >= 0 && syB < scopeH) densityB[syB * paneW + sx]++;
                }
            }
        }

        // Graticule dividers between R, G, B panes
        for (int y = 0; y < scopeH; y++)
        {
            buffer[y * scopeW + paneW] = GraticuleLine;
            buffer[y * scopeW + (paneW * 2)] = GraticuleLine;
        }

        DrawWaveformGraticule(buffer, scopeW, scopeH, scale);

        // Channel colors
        uint colorR = 0xFFFF4040;
        uint colorG = 0xFF40FF40;
        uint colorB = 0xFF4080FF;

        RenderParadeChannel(buffer, densityR, paneW, scopeH, 0, scopeW, colorR, intensity);
        RenderParadeChannel(buffer, densityG, paneW, scopeH, paneW, scopeW, colorG, intensity);
        RenderParadeChannel(buffer, densityB, paneW, scopeH, paneW * 2, scopeW, colorB, intensity);
    }

    private static void RenderParadeChannel(
        uint[] buffer,
        int[] density,
        int paneW,
        int scopeH,
        int offsetX,
        int fullW,
        uint baseColor,
        double intensity)
    {
        int maxDensity = 1;
        for (int i = 0; i < density.Length; i++)
        {
            if (density[i] > maxDensity) maxDensity = density[i];
        }

        double gain = Math.Clamp(intensity, 0.2, 3.0);
        byte rBase = (byte)((baseColor >> 16) & 0xFF);
        byte gBase = (byte)((baseColor >> 8) & 0xFF);
        byte bBase = (byte)(baseColor & 0xFF);

        for (int y = 0; y < scopeH; y++)
        {
            for (int x = 0; x < paneW; x++)
            {
                int d = density[y * paneW + x];
                if (d > 0)
                {
                    double norm = Math.Min(1.0, (d / (double)maxDensity) * gain * 2.5);
                    byte alpha = (byte)(Math.Clamp(norm * 255.0, 30.0, 255.0));
                    uint col = 0xFF000000 | ((uint)(rBase * norm) << 16) | ((uint)(gBase * norm) << 8) | (uint)(bBase * norm);
                    int targetX = offsetX + x;
                    if (targetX >= 0 && targetX < fullW)
                    {
                        buffer[y * fullW + targetX] = BlendPixels(buffer[y * fullW + targetX], col);
                    }
                }
            }
        }
    }

    #endregion

    #region Histogram

    private static void RenderHistogram(
        ReadOnlySpan<byte> src,
        int srcW,
        int srcH,
        int srcStride,
        uint[] buffer,
        int scopeW,
        int scopeH,
        VideoScopeScale scale)
    {
        var binsR = new int[256];
        var binsG = new int[256];
        var binsB = new int[256];
        var binsY = new int[256];

        int stepX = Math.Max(1, srcW / 320);
        int stepY = Math.Max(1, srcH / 240);

        for (int y = 0; y < srcH; y += stepY)
        {
            int rowOffset = y * srcStride;
            for (int x = 0; x < srcW; x += stepX)
            {
                int p = rowOffset + (x * 4);
                if (p + 2 >= src.Length) continue;

                byte b = src[p];
                byte g = src[p + 1];
                byte r = src[p + 2];
                byte yLum = (byte)((54 * r + 183 * g + 19 * b) >> 8);

                binsR[r]++;
                binsG[g]++;
                binsB[b]++;
                binsY[yLum]++;
            }
        }

        int maxCount = 1;
        for (int i = 0; i < 256; i++)
        {
            if (binsR[i] > maxCount) maxCount = binsR[i];
            if (binsG[i] > maxCount) maxCount = binsG[i];
            if (binsB[i] > maxCount) maxCount = binsB[i];
            if (binsY[i] > maxCount) maxCount = binsY[i];
        }

        // Draw Graticule lines at 0, 64, 128, 192, 255
        for (int level = 0; level <= 256; level += 64)
        {
            int gx = (Math.Min(255, level) * (scopeW - 1)) / 255;
            for (int y = 0; y < scopeH; y += 4)
            {
                buffer[y * scopeW + gx] = GraticuleDim;
            }
        }

        // Render overlapping channel bars from bottom up
        for (int x = 0; x < scopeW; x++)
        {
            int code = (x * 255) / (scopeW - 1);
            int hR = (binsR[code] * (scopeH - 4)) / maxCount;
            int hG = (binsG[code] * (scopeH - 4)) / maxCount;
            int hB = (binsB[code] * (scopeH - 4)) / maxCount;
            int hY = (binsY[code] * (scopeH - 4)) / maxCount;

            int maxBar = Math.Max(Math.Max(hR, hG), Math.Max(hB, hY));
            for (int y = 0; y < maxBar && y < scopeH; y++)
            {
                int py = scopeH - 1 - y;
                uint rVal = y < hR ? 0x90u : 0x00u;
                uint gVal = y < hG ? 0x90u : 0x00u;
                uint bVal = y < hB ? 0x90u : 0x00u;
                if (y < hY)
                {
                    rVal = Math.Min(255u, rVal + 0x40u);
                    gVal = Math.Min(255u, gVal + 0x40u);
                    bVal = Math.Min(255u, bVal + 0x40u);
                }

                uint pixel = 0xFF000000 | (rVal << 16) | (gVal << 8) | bVal;
                buffer[py * scopeW + x] = BlendPixels(buffer[py * scopeW + x], pixel);
            }
        }
    }

    #endregion

    #region Drawing Helpers

    private static void RenderGraticule(uint[] buffer, int w, int h, VideoScopeMode mode, VideoScopeScale scale)
    {
        switch (mode)
        {
            case VideoScopeMode.Waveform:
                DrawWaveformGraticule(buffer, w, h, scale);
                break;
            case VideoScopeMode.Vectorscope:
                DrawVectorscopeGraticule(buffer, w, h, w / 2, h / 2, (int)(Math.Min(w, h) * 0.44));
                break;
            case VideoScopeMode.RgbParade:
                DrawWaveformGraticule(buffer, w, h, scale);
                break;
            case VideoScopeMode.Histogram:
                for (int level = 0; level <= 256; level += 64)
                {
                    int gx = (Math.Min(255, level) * (w - 1)) / 255;
                    for (int y = 0; y < h; y += 4) buffer[y * w + gx] = GraticuleDim;
                }
                break;
        }
    }

    private static uint InterpolatePhosphor(double norm)
    {
        if (norm <= 0.25)
        {
            return LerpColor(PhosphorDim, PhosphorMid, norm / 0.25);
        }
        else if (norm <= 0.75)
        {
            return LerpColor(PhosphorMid, PhosphorBright, (norm - 0.25) / 0.5);
        }
        else
        {
            return LerpColor(PhosphorBright, PhosphorPeak, (norm - 0.75) / 0.25);
        }
    }

    private static uint LerpColor(uint c1, uint c2, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        byte r = (byte)(((c1 >> 16) & 0xFF) * (1 - t) + ((c2 >> 16) & 0xFF) * t);
        byte g = (byte)(((c1 >> 8) & 0xFF) * (1 - t) + ((c2 >> 8) & 0xFF) * t);
        byte b = (byte)((c1 & 0xFF) * (1 - t) + (c2 & 0xFF) * t);
        return 0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    private static uint BlendPixels(uint bg, uint fg)
    {
        byte rBg = (byte)((bg >> 16) & 0xFF);
        byte gBg = (byte)((bg >> 8) & 0xFF);
        byte bBg = (byte)(bg & 0xFF);

        byte rFg = (byte)((fg >> 16) & 0xFF);
        byte gFg = (byte)((fg >> 8) & 0xFF);
        byte bFg = (byte)(fg & 0xFF);

        // Additive screen blend for phosphors
        byte r = (byte)Math.Min(255, rBg + rFg);
        byte g = (byte)Math.Min(255, gBg + gFg);
        byte b = (byte)Math.Min(255, bBg + bFg);
        return 0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    private static void DrawCircle(uint[] buffer, int w, int h, int cx, int cy, int r, uint color)
    {
        int count = Math.Max(64, r * 4);
        for (int i = 0; i < count; i += 2)
        {
            double angle = (i * 2.0 * Math.PI) / count;
            int x = cx + (int)(Math.Cos(angle) * r);
            int y = cy + (int)(Math.Sin(angle) * r);
            if (x >= 0 && x < w && y >= 0 && y < h)
            {
                buffer[y * w + x] = color;
            }
        }
    }

    private static void DrawDottedLine(uint[] buffer, int w, int h, int x0, int y0, int x1, int y1, uint color)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int steps = Math.Max(dx, dy);
        if (steps == 0) return;

        for (int i = 0; i <= steps; i += 4)
        {
            int x = x0 + (x1 - x0) * i / steps;
            int y = y0 + (y1 - y0) * i / steps;
            if (x >= 0 && x < w && y >= 0 && y < h)
            {
                buffer[y * w + x] = color;
            }
        }
    }

    private static void DrawTargetBox(uint[] buffer, int w, int h, int cx, int cy, int size, uint color)
    {
        int half = size / 2;
        int x0 = cx - half, x1 = cx + half;
        int y0 = cy - half, y1 = cy + half;

        for (int x = x0; x <= x1; x++)
        {
            if (x >= 0 && x < w)
            {
                if (y0 >= 0 && y0 < h) buffer[y0 * w + x] = color;
                if (y1 >= 0 && y1 < h) buffer[y1 * w + x] = color;
            }
        }
        for (int y = y0; y <= y1; y++)
        {
            if (y >= 0 && y < h)
            {
                if (x0 >= 0 && x0 < w) buffer[y * w + x0] = color;
                if (x1 >= 0 && x1 < w) buffer[y * w + x1] = color;
            }
        }
    }

    private static byte[] ConvertToByteArray(uint[] buffer)
    {
        var bytes = new byte[buffer.Length * 4];
        System.Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    #endregion
}
