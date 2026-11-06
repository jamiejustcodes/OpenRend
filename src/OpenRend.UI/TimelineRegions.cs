using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.UI;

public sealed partial class TimelineView
{
    public event Action<TimelineRegion>? RegionSelected;
    public event Action<TimelineRegion>? RegionEditRequested;
    public event Action<Guid>? RegionDeleteRequested;

    private TimelineRegion? HitRegion(Point point)
    {
        if (point.Y < 0 || point.Y > 16 || point.X < HeaderWidth || Project.Regions.Count == 0) return null;
        foreach (var region in Project.Regions)
        {
            var sx = X(region.StartFrame);
            var ex = X(region.EndFrame);
            if (point.X >= sx - 8 && point.X <= ex + 8) return region;
        }
        return null;
    }

    private void DrawRegions(DrawingContext dc)
    {
        if (Project.Regions.Count == 0) return;
        var whiteBrush = Brush("#FFFFFF");
        var darkBorderPen = FrozenPen("#181818", 1);

        foreach (var region in Project.Regions)
        {
            var sx = X(region.StartFrame);
            var ex = X(region.EndFrame);
            if (ex < HeaderWidth - 20 || sx > ActualWidth + 40) continue;

            var regionBrush = Brush(region.ColorHex);
            var spanWidth = Math.Max(4, ex - sx);

            // Vertical guidelines down through tracks for start and end
            if (sx >= HeaderWidth)
            {
                dc.DrawLine(FrozenDashedPen(region.ColorHex, 1, [3, 3], 0),
                    new Point(sx, RulerHeight), new Point(sx, ActualHeight));
            }
            if (ex >= HeaderWidth && ex <= ActualWidth)
            {
                dc.DrawLine(FrozenDashedPen(region.ColorHex, 1, [3, 3], 0),
                    new Point(ex, RulerHeight), new Point(ex, ActualHeight));
            }

            // Top connecting bar across the region span
            var fillBrush = Brush(region.ColorHex.Length == 7 ? "#40" + region.ColorHex.Substring(1) : region.ColorHex);
            dc.DrawRectangle(fillBrush, null, new Rect(Math.Max(HeaderWidth, sx), 1, Math.Min(ex, ActualWidth) - Math.Max(HeaderWidth, sx), 10));
            dc.DrawLine(FrozenPen(region.ColorHex, 2), new Point(Math.Max(HeaderWidth, sx), 1), new Point(Math.Min(ex, ActualWidth), 1));

            // Start flag (pointing right)
            if (sx >= HeaderWidth - 10)
            {
                var startGeom = new StreamGeometry();
                using (var ctx = startGeom.Open())
                {
                    ctx.BeginFigure(new Point(sx, 1), true, true);
                    ctx.LineTo(new Point(sx + 10, 1), true, false);
                    ctx.LineTo(new Point(sx + 10, 9), true, false);
                    ctx.LineTo(new Point(sx, 12), true, false);
                }
                startGeom.Freeze();
                dc.DrawGeometry(regionBrush, darkBorderPen, startGeom);

                // Index number in start flag
                var idxText = new FormattedText(
                    region.Index.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    8,
                    whiteBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(idxText, new Point(sx + 2, 1));
            }

            // End flag (pointing left)
            if (ex <= ActualWidth + 10 && ex >= HeaderWidth)
            {
                var endGeom = new StreamGeometry();
                using (var ctx = endGeom.Open())
                {
                    ctx.BeginFigure(new Point(ex, 1), true, true);
                    ctx.LineTo(new Point(ex - 10, 1), true, false);
                    ctx.LineTo(new Point(ex - 10, 9), true, false);
                    ctx.LineTo(new Point(ex, 12), true, false);
                }
                endGeom.Freeze();
                dc.DrawGeometry(regionBrush, darkBorderPen, endGeom);

                var idxText = new FormattedText(
                    region.Index.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    8,
                    whiteBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(idxText, new Point(ex - 8, 1));
            }

            // Region label text along the connecting bar
            var label = string.IsNullOrWhiteSpace(region.Label) ? $"Region {region.Index}" : $"{region.Index}: {region.Label}";
            var labelX = Math.Max(HeaderWidth + 12, sx + 12);
            if (labelX < ex - 14)
            {
                Text(dc, label, labelX, 1, Brush("#F0F0F0"), 8, maxWidth: Math.Max(20, ex - labelX - 12));
            }
        }
    }
}
