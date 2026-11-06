using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.UI;

public sealed partial class TimelineView
{
    public event Action<TimelineMarker>? MarkerSelected;
    public event Action<TimelineMarker>? MarkerEditRequested;
    public event Action<Guid>? MarkerDeleteRequested;

    private TimelineMarker? HitMarker(Point point)
    {
        if (point.Y < 8 || point.Y > 32 || point.X < HeaderWidth || Project.Markers.Count == 0) return null;
        foreach (var marker in Project.Markers)
        {
            var mx = X(marker.Frame);
            if (Math.Abs(point.X - mx) <= 9) return marker;
        }
        return null;
    }

    private void DrawMarkers(DrawingContext dc)
    {
        if (Project.Markers.Count == 0) return;
        var whiteBrush = Brush("#FFFFFF");
        var darkBorderPen = FrozenPen("#181818", 1);

        foreach (var marker in Project.Markers)
        {
            var mx = X(marker.Frame);
            if (mx < HeaderWidth - 20 || mx > ActualWidth + 40) continue;

            var markerBrush = Brush(marker.ColorHex);

            // Vertical guideline down through tracks
            dc.DrawLine(FrozenDashedPen(marker.ColorHex, 1, [4, 4], 0),
                new Point(mx, RulerHeight), new Point(mx, ActualHeight));

            // Ruler marker tag (inverted house / pin shape)
            var streamGeom = new StreamGeometry();
            using (var ctx = streamGeom.Open())
            {
                ctx.BeginFigure(new Point(mx - 7, 12), true, true);
                ctx.LineTo(new Point(mx + 7, 12), true, false);
                ctx.LineTo(new Point(mx + 7, 23), true, false);
                ctx.LineTo(new Point(mx, 28), true, false);
                ctx.LineTo(new Point(mx - 7, 23), true, false);
            }
            streamGeom.Freeze();

            dc.DrawGeometry(markerBrush, darkBorderPen, streamGeom);

            // Marker index number inside tag
            var indexText = marker.Index.ToString(CultureInfo.InvariantCulture);
            var indexFormatted = new FormattedText(
                indexText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                9,
                whiteBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            var textX = mx - indexFormatted.Width / 2.0;
            dc.DrawText(indexFormatted, new Point(textX, 13));

            // Marker text label (if any)
            if (!string.IsNullOrWhiteSpace(marker.Label))
            {
                Text(dc, marker.Label, mx + 9, 14, Brush("#E0E0E0"), 9, maxWidth: 120);
            }
        }
    }
}

