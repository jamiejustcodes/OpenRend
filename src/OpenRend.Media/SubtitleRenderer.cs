using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenRend.Media;

/// <summary>
/// High-performance subtitle and text renderer matching Vegas Pro Titles &amp; Text OFX output.
/// Supports 9-point anchor alignment, active word karaoke highlighting, custom fonts,
/// drop shadows, outlines, and background pill formatting.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SubtitleRenderer
{
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();
    private const int MaxCacheSize = 256;

    public static byte[] RenderSubtitle(string text, int width, int height, string alignment = "Bottom Center", string style = "Classic White")
    {
        var subStyle = SubtitleStyle.FromPreset(style);
        subStyle.AnchorAlignment = alignment;
        return RenderSubtitle(text, width, height, subStyle, false, out _);
    }

    public static byte[] RenderSubtitle(string text, int width, int height, SubtitleStyle style)
    {
        return RenderSubtitle(text, width, height, style, false, out _);
    }

    public static byte[] RenderSubtitle(string text, int width, int height, SubtitleStyle style, bool renderHandles, out RectangleF textBounds)
    {
        textBounds = RectangleF.Empty;
        if (width <= 0 || height <= 0 || string.IsNullOrWhiteSpace(text))
            return new byte[Math.Max(0, width * height * 4)];

        var renderedText = style.Uppercase ? text.Trim().ToUpperInvariant() : text.Trim();
        var cacheKey = $"{renderedText}|{width}x{height}|{style.AnchorAlignment}|{style.HasCustomLocation}|{style.LocationX:F4}|{style.LocationY:F4}|{style.FontFamily}|{style.FontSize}|{style.Bold}|{style.Italic}|{style.TextColor}|{style.OutlineColor}|{style.OutlineWidth}|{style.ShadowEnabled}|{style.BackgroundColor}|{style.FitBackgroundColor}|{style.ActiveWordIndex}|{style.HighlightColor}|{renderHandles}";

        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var pixels = new byte[width * height * 4];

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        // Compute effective font size: explicit if > 0, otherwise proportional to canvas height
        var fontSize = style.FontSize > 0f
            ? style.FontSize
            : (style.PresetName.Contains("Shorts", StringComparison.OrdinalIgnoreCase) || style.PresetName.Contains("YouTube", StringComparison.OrdinalIgnoreCase))
                ? Math.Clamp(height * 0.065f, 18f, 130f)
                : Math.Clamp(height * 0.048f, 16f, 96f);

        FontFamily family;
        try
        {
            family = new FontFamily(style.FontFamily);
        }
        catch
        {
            family = new FontFamily("Segoe UI");
        }

        var fontStyle = FontStyle.Regular;
        if (style.Bold) fontStyle |= FontStyle.Bold;
        if (style.Italic) fontStyle |= FontStyle.Italic;

        using (family)
        using (var font = new Font(family, fontSize, fontStyle))
        using (var sf = new StringFormat(StringFormat.GenericTypographic))
        {
            var hAlign = style.HorizontalAlignment.ToLowerInvariant();
            var vAlign = style.VerticalAlignment.ToLowerInvariant();

            sf.Alignment = hAlign switch
            {
                "left" => StringAlignment.Near,
                "right" => StringAlignment.Far,
                _ => StringAlignment.Center
            };

            sf.LineAlignment = vAlign switch
            {
                "top" => StringAlignment.Near,
                "middle" or "center" => StringAlignment.Center,
                _ => StringAlignment.Far
            };

            var marginX = width * 0.06f;
            var isShorts = style.PresetName.Contains("Shorts", StringComparison.OrdinalIgnoreCase) ||
                           style.PresetName.Contains("YouTube", StringComparison.OrdinalIgnoreCase) ||
                           style.PresetName.Contains("Neon", StringComparison.OrdinalIgnoreCase);

            var marginY = (isShorts && vAlign == "bottom")
                ? height * 0.14f // Keep clear of mobile video interaction chrome
                : height * 0.07f;

            var textColor = SubtitleStyle.ParseColor(style.TextColor, Color.White);
            var outlineColor = SubtitleStyle.ParseColor(style.OutlineColor, Color.Black);
            var shadowColor = SubtitleStyle.ParseColor(style.ShadowColor, Color.FromArgb(200, 0, 0, 0));
            var bgColor = SubtitleStyle.ParseColor(style.BackgroundColor, Color.Transparent);
            var highlightColor = SubtitleStyle.ParseColor(style.HighlightColor, Color.FromArgb(255, 255, 230, 0));

            var words = renderedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var isKaraoke = style.ActiveWordIndex >= 0 && words.Length > 0;

            if (isKaraoke)
            {
                // Word-by-word karaoke pass with exact per-word bounds measurement
                var wordSizes = new SizeF[words.Length];
                var totalW = 0f;
                var spaceW = g.MeasureString(" ", font, 10000, sf).Width;

                for (var i = 0; i < words.Length; i++)
                {
                    wordSizes[i] = g.MeasureString(words[i], font, 10000, sf);
                    totalW += wordSizes[i].Width;
                    if (i < words.Length - 1) totalW += spaceW;
                }

                var textH = font.Height;
                var startX = style.HasCustomLocation
                    ? style.LocationX * width - totalW * 0.5f
                    : hAlign switch
                    {
                        "left" => marginX,
                        "right" => width - marginX - totalW,
                        _ => (width - totalW) * 0.5f
                    };

                var startY = style.HasCustomLocation
                    ? style.LocationY * height - textH * 0.5f
                    : vAlign switch
                    {
                        "top" => marginY,
                        "middle" or "center" => (height - textH) * 0.5f,
                        _ => height - marginY - textH
                    };

                textBounds = new RectangleF(startX, startY, totalW, textH);

                // Draw background pill/box behind karaoke phrase
                if (bgColor.A > 0)
                {
                    var padX = Math.Clamp(fontSize * 0.4f, 8f, 24f);
                    var padY = Math.Clamp(fontSize * 0.2f, 4f, 14f);
                    using var bgBrush = new SolidBrush(bgColor);
                    if (style.FitBackgroundColor)
                    {
                        var pillRect = new RectangleF(startX - padX, startY - padY, totalW + 2 * padX, textH + 2 * padY);
                        g.FillRectangle(bgBrush, pillRect);
                    }
                    else
                    {
                        var barRect = new RectangleF(0, startY - padY, width, textH + 2 * padY);
                        g.FillRectangle(bgBrush, barRect);
                    }
                }

                var curX = startX;
                for (var i = 0; i < words.Length; i++)
                {
                    var word = words[i];
                    var wW = wordSizes[i].Width;
                    var wordRect = new RectangleF(curX, startY, wW, textH);

                    using var wordPath = new GraphicsPath();
                    wordPath.AddString(word, family, (int)fontStyle, fontSize, wordRect, sf);

                    // Drop shadow
                    if (style.ShadowEnabled)
                    {
                        using var shadowMatrix = new Matrix();
                        shadowMatrix.Translate(style.ShadowOffsetX, style.ShadowOffsetY);
                        using var shadowPath = (GraphicsPath)wordPath.Clone();
                        shadowPath.Transform(shadowMatrix);

                        if (style.OutlineWidth > 0f)
                        {
                            using var shadowPen = new Pen(shadowColor, style.OutlineWidth + style.ShadowBlur) { LineJoin = LineJoin.Round };
                            g.DrawPath(shadowPen, shadowPath);
                        }
                        using var shadowBrush = new SolidBrush(shadowColor);
                        g.FillPath(shadowBrush, shadowPath);
                    }

                    // Stroke outline
                    if (style.OutlineWidth > 0f)
                    {
                        using var strokePen = new Pen(outlineColor, style.OutlineWidth) { LineJoin = LineJoin.Round };
                        g.DrawPath(strokePen, wordPath);
                    }

                    // Text fill: highlight color on spoken word, base color elsewhere
                    var fillColor = (i == style.ActiveWordIndex) ? highlightColor : textColor;
                    using var fillBrush = new SolidBrush(fillColor);
                    g.FillPath(fillBrush, wordPath);

                    curX += wW + spaceW;
                }
            }
            else
            {
                // Full phrase layout pass
                RectangleF layoutRect;
                if (style.HasCustomLocation)
                {
                    var targetCenterX = style.LocationX * width;
                    var targetCenterY = style.LocationY * height;
                    var maxW = width - 2 * marginX;
                    var maxH = height - 2 * marginY;
                    layoutRect = new RectangleF(targetCenterX - maxW * 0.5f, targetCenterY - maxH * 0.5f, maxW, maxH);
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                }
                else
                {
                    layoutRect = new RectangleF(marginX, marginY, width - 2 * marginX, height - 2 * marginY);
                }

                using var path = new GraphicsPath();
                path.AddString(renderedText, family, (int)fontStyle, fontSize, layoutRect, sf);

                var bounds = path.GetBounds();
                textBounds = bounds;
                if (bounds.Width > 0 && bounds.Height > 0 && bgColor.A > 0)
                {
                    var padX = Math.Clamp(fontSize * 0.45f, 8f, 24f);
                    var padY = Math.Clamp(fontSize * 0.22f, 4f, 14f);
                    using var bgBrush = new SolidBrush(bgColor);

                    if (style.FitBackgroundColor)
                    {
                        var pillRect = new RectangleF(bounds.X - padX, bounds.Y - padY, bounds.Width + 2 * padX, bounds.Height + 2 * padY);
                        g.FillRectangle(bgBrush, pillRect);
                    }
                    else
                    {
                        var barRect = new RectangleF(0, bounds.Y - padY, width, bounds.Height + 2 * padY);
                        g.FillRectangle(bgBrush, barRect);
                    }
                }

                // Drop shadow
                if (style.ShadowEnabled)
                {
                    using var shadowMatrix = new Matrix();
                    shadowMatrix.Translate(style.ShadowOffsetX, style.ShadowOffsetY);
                    using var shadowPath = (GraphicsPath)path.Clone();
                    shadowPath.Transform(shadowMatrix);

                    if (style.OutlineWidth > 0f)
                    {
                        using var shadowPen = new Pen(shadowColor, style.OutlineWidth + style.ShadowBlur) { LineJoin = LineJoin.Round };
                        g.DrawPath(shadowPen, shadowPath);
                    }
                    using var shadowBrush = new SolidBrush(shadowColor);
                    g.FillPath(shadowBrush, shadowPath);
                }

                // Outline
                if (style.OutlineWidth > 0f)
                {
                    using var strokePen = new Pen(outlineColor, style.OutlineWidth) { LineJoin = LineJoin.Round };
                    g.DrawPath(strokePen, path);
                }

                // Text fill
                using var fillBrush = new SolidBrush(textColor);
                g.FillPath(fillBrush, path);
            }

            if (renderHandles && textBounds.Width > 0 && textBounds.Height > 0)
            {
                var cx = style.HasCustomLocation ? style.LocationX * width : textBounds.X + textBounds.Width * 0.5f;
                var cy = style.HasCustomLocation ? style.LocationY * height : textBounds.Y + textBounds.Height * 0.5f;
                DrawInteractHandles(g, textBounds, cx, cy, width, height);
            }
        }

        // Copy raw 32-bit ARGB/BGRA scanlines
        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        if (Cache.Count >= MaxCacheSize)
            Cache.Clear();

        Cache[cacheKey] = pixels;
        return pixels;
    }

    public static RectangleF MeasureSubtitleBounds(string text, int width, int height, SubtitleStyle style)
    {
        if (width <= 0 || height <= 0 || string.IsNullOrWhiteSpace(text))
            return RectangleF.Empty;

        var renderedText = style.Uppercase ? text.Trim().ToUpperInvariant() : text.Trim();
        var fontSize = style.FontSize > 0f
            ? style.FontSize
            : (style.PresetName.Contains("Shorts", StringComparison.OrdinalIgnoreCase) || style.PresetName.Contains("YouTube", StringComparison.OrdinalIgnoreCase))
                ? Math.Clamp(height * 0.065f, 18f, 130f)
                : Math.Clamp(height * 0.048f, 16f, 96f);

        FontFamily family;
        try { family = new FontFamily(style.FontFamily); }
        catch { family = new FontFamily("Segoe UI"); }

        var fontStyle = FontStyle.Regular;
        if (style.Bold) fontStyle |= FontStyle.Bold;
        if (style.Italic) fontStyle |= FontStyle.Italic;

        using (family)
        using (var font = new Font(family, fontSize, fontStyle))
        using (var sf = new StringFormat(StringFormat.GenericTypographic))
        {
            var hAlign = style.HorizontalAlignment.ToLowerInvariant();
            var vAlign = style.VerticalAlignment.ToLowerInvariant();

            sf.Alignment = hAlign switch
            {
                "left" => StringAlignment.Near,
                "right" => StringAlignment.Far,
                _ => StringAlignment.Center
            };

            sf.LineAlignment = vAlign switch
            {
                "top" => StringAlignment.Near,
                "middle" or "center" => StringAlignment.Center,
                _ => StringAlignment.Far
            };

            var marginX = width * 0.06f;
            var isShorts = style.PresetName.Contains("Shorts", StringComparison.OrdinalIgnoreCase) ||
                           style.PresetName.Contains("YouTube", StringComparison.OrdinalIgnoreCase) ||
                           style.PresetName.Contains("Neon", StringComparison.OrdinalIgnoreCase);

            var marginY = (isShorts && vAlign == "bottom")
                ? height * 0.14f
                : height * 0.07f;

            var words = renderedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var isKaraoke = style.ActiveWordIndex >= 0 && words.Length > 0;

            if (isKaraoke)
            {
                using var dummyBmp = new Bitmap(1, 1);
                using var g = Graphics.FromImage(dummyBmp);
                var totalW = 0f;
                var spaceW = g.MeasureString(" ", font, 10000, sf).Width;

                for (var i = 0; i < words.Length; i++)
                {
                    totalW += g.MeasureString(words[i], font, 10000, sf).Width;
                    if (i < words.Length - 1) totalW += spaceW;
                }

                var textH = font.Height;
                var startX = style.HasCustomLocation
                    ? style.LocationX * width - totalW * 0.5f
                    : hAlign switch
                    {
                        "left" => marginX,
                        "right" => width - marginX - totalW,
                        _ => (width - totalW) * 0.5f
                    };

                var startY = style.HasCustomLocation
                    ? style.LocationY * height - textH * 0.5f
                    : vAlign switch
                    {
                        "top" => marginY,
                        "middle" or "center" => (height - textH) * 0.5f,
                        _ => height - marginY - textH
                    };

                return new RectangleF(startX, startY, totalW, textH);
            }
            else
            {
                RectangleF layoutRect;
                if (style.HasCustomLocation)
                {
                    var targetCenterX = style.LocationX * width;
                    var targetCenterY = style.LocationY * height;
                    var maxW = width - 2 * marginX;
                    var maxH = height - 2 * marginY;
                    layoutRect = new RectangleF(targetCenterX - maxW * 0.5f, targetCenterY - maxH * 0.5f, maxW, maxH);
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                }
                else
                {
                    layoutRect = new RectangleF(marginX, marginY, width - 2 * marginX, height - 2 * marginY);
                }

                using var path = new GraphicsPath();
                path.AddString(renderedText, family, (int)fontStyle, fontSize, layoutRect, sf);
                return path.GetBounds();
            }
        }
    }

    private static void DrawInteractHandles(Graphics g, RectangleF bounds, float cx, float cy, int width, int height)
    {
        // VEGAS Pro Titles & Text OFX interact styling:
        // Alternating cyan/dark dashed bounding rectangle, 8 edge/corner resize handles,
        // and center 4-way crosshair location anchor point.
        var pad = 6f;
        var box = new RectangleF(bounds.X - pad, bounds.Y - pad, bounds.Width + pad * 2, bounds.Height + pad * 2);

        using var darkPen = new Pen(Color.FromArgb(180, 10, 10, 10), 1f);
        using var cyanPen = new Pen(Color.FromArgb(255, 0, 210, 255), 1.2f)
        {
            DashStyle = DashStyle.Dash,
            DashPattern = new[] { 4f, 4f }
        };

        g.DrawRectangle(darkPen, box.X, box.Y, box.Width, box.Height);
        g.DrawRectangle(cyanPen, box.X, box.Y, box.Width, box.Height);

        // 8 handle points: corners and edge midpoints
        var handlePts = new PointF[]
        {
            new(box.Left, box.Top),
            new(box.Left + box.Width * 0.5f, box.Top),
            new(box.Right, box.Top),
            new(box.Right, box.Top + box.Height * 0.5f),
            new(box.Right, box.Bottom),
            new(box.Left + box.Width * 0.5f, box.Bottom),
            new(box.Left, box.Bottom),
            new(box.Left, box.Top + box.Height * 0.5f)
        };

        var handleSize = 6.5f;
        using var handleBrush = new SolidBrush(Color.White);
        using var handleBorderPen = new Pen(Color.FromArgb(240, 20, 20, 20), 1f);

        foreach (var pt in handlePts)
        {
            var hRect = new RectangleF(pt.X - handleSize * 0.5f, pt.Y - handleSize * 0.5f, handleSize, handleSize);
            g.FillRectangle(handleBrush, hRect);
            g.DrawRectangle(handleBorderPen, hRect.X, hRect.Y, hRect.Width, hRect.Height);
        }

        // Center Location crosshair / OFX Point2D parameter handle
        using var crosshairPen = new Pen(Color.FromArgb(255, 0, 210, 255), 1.2f);
        using var crosshairShadowPen = new Pen(Color.FromArgb(180, 0, 0, 0), 2.2f);

        var tick = 7f;
        var radius = 5.5f;

        // Shadow outline
        g.DrawEllipse(crosshairShadowPen, cx - radius, cy - radius, radius * 2, radius * 2);
        g.DrawLine(crosshairShadowPen, cx - radius - tick, cy, cx - radius, cy);
        g.DrawLine(crosshairShadowPen, cx + radius, cy, cx + radius + tick, cy);
        g.DrawLine(crosshairShadowPen, cx, cy - radius - tick, cx, cy - radius);
        g.DrawLine(crosshairShadowPen, cx, cy + radius, cx, cy + radius + tick);

        // Foreground cyan ticks and center circle
        g.DrawEllipse(crosshairPen, cx - radius, cy - radius, radius * 2, radius * 2);
        g.DrawLine(crosshairPen, cx - radius - tick, cy, cx - radius, cy);
        g.DrawLine(crosshairPen, cx + radius, cy, cx + radius + tick, cy);
        g.DrawLine(crosshairPen, cx, cy - radius - tick, cx, cy - radius);
        g.DrawLine(crosshairPen, cx, cy + radius, cx, cy + radius + tick);
        g.FillEllipse(handleBrush, cx - 1.5f, cy - 1.5f, 3f, 3f);
    }
}
