using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

public enum CurveChannel
{
    Master,
    Red,
    Green,
    Blue
}

/// <summary>
/// Vegas Pro 1:1 Color Curves 2D interactive canvas.
/// Supports Master (W), Red (R), Green (G), and Blue (B) curves with draggable control points,
/// cubic bezier interpolation, point addition/removal, and real-time live preview.
/// </summary>
public sealed class ColorCurvesControl : FrameworkElement
{
    private const double Padding = 14.0;
    private const double PointRadius = 4.5;
    private const double HitRadius = 9.0;

    private CurveChannel activeChannel = CurveChannel.Master;
    private ColorCurvesData curves = new();
    private int selectedPointIndex = -1;
    private bool isDragging;

    public event Action? CurvesChanged;
    public event Action<string>? PointSelected;

    public CurveChannel ActiveChannel
    {
        get => activeChannel;
        set
        {
            if (activeChannel != value)
            {
                activeChannel = value;
                selectedPointIndex = -1;
                InvalidateVisual();
                UpdateReadout();
            }
        }
    }

    public ColorCurvesData Curves
    {
        get => curves;
        set
        {
            curves = value ?? new ColorCurvesData();
            selectedPointIndex = -1;
            InvalidateVisual();
            UpdateReadout();
        }
    }

    public ColorCurve ActiveCurve => activeChannel switch
    {
        CurveChannel.Red => curves.Red,
        CurveChannel.Green => curves.Green,
        CurveChannel.Blue => curves.Blue,
        _ => curves.Master
    };

    public ColorCurvesControl()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        MinHeight = 180;
        MinWidth = 180;
    }

    public void ResetActive()
    {
        var fresh = ColorCurve.CreateDefault();
        switch (activeChannel)
        {
            case CurveChannel.Red: curves.Red = fresh; break;
            case CurveChannel.Green: curves.Green = fresh; break;
            case CurveChannel.Blue: curves.Blue = fresh; break;
            default: curves.Master = fresh; break;
        }
        selectedPointIndex = -1;
        InvalidateVisual();
        UpdateReadout();
        CurvesChanged?.Invoke();
    }

    public void ResetAll()
    {
        curves = new ColorCurvesData();
        selectedPointIndex = -1;
        InvalidateVisual();
        UpdateReadout();
        CurvesChanged?.Invoke();
    }

    public void SmoothActive()
    {
        var pts = ActiveCurve.Points;
        if (pts.Count <= 2) return;

        for (var i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var prev = i > 0 ? pts[i - 1] : p;
            var next = i < pts.Count - 1 ? pts[i + 1] : p;

            var dx = (next.X - prev.X) * 0.25;
            var dy = (next.Y - prev.Y) * 0.25;

            p.InHandleX = -dx;
            p.InHandleY = -dy;
            p.OutHandleX = dx;
            p.OutHandleY = dy;
        }

        InvalidateVisual();
        CurvesChanged?.Invoke();
    }

    private Rect GraphRect => new(Padding, Padding, Math.Max(10, ActualWidth - Padding * 2), Math.Max(10, ActualHeight - Padding * 2));

    private Point NormalizedToScreen(double x, double y)
    {
        var rect = GraphRect;
        return new Point(rect.Left + x * rect.Width, rect.Bottom - y * rect.Height);
    }

    private Point ScreenToNormalized(Point p)
    {
        var rect = GraphRect;
        var x = Math.Clamp((p.X - rect.Left) / rect.Width, 0.0, 1.0);
        var y = Math.Clamp((rect.Bottom - p.Y) / rect.Height, 0.0, 1.0);
        return new Point(x, y);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = GraphRect;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        // Background
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 24, 26)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 18, 20)), new Pen(new SolidColorBrush(Color.FromRgb(45, 45, 48)), 1.0), rect);

        // Grid lines at 25%, 50%, 75%
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1.0);
        for (var i = 1; i < 4; i++)
        {
            var frac = i * 0.25;
            var x = rect.Left + frac * rect.Width;
            var y = rect.Bottom - frac * rect.Height;
            dc.DrawLine(gridPen, new Point(x, rect.Top), new Point(x, rect.Bottom));
            dc.DrawLine(gridPen, new Point(rect.Left, y), new Point(rect.Right, y));
        }

        // Diagonal reference
        var diagPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1.0) { DashStyle = DashStyles.Dash };
        dc.DrawLine(diagPen, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Top));

        // Draw inactive channel curves in faint colors
        DrawChannelCurve(dc, curves.Master, Color.FromArgb(70, 220, 220, 220), 1.0, activeChannel == CurveChannel.Master);
        DrawChannelCurve(dc, curves.Red, Color.FromArgb(70, 239, 83, 80), 1.0, activeChannel == CurveChannel.Red);
        DrawChannelCurve(dc, curves.Green, Color.FromArgb(70, 102, 187, 106), 1.0, activeChannel == CurveChannel.Green);
        DrawChannelCurve(dc, curves.Blue, Color.FromArgb(70, 66, 165, 245), 1.0, activeChannel == CurveChannel.Blue);

        // Draw active channel curve on top
        var (activeColor, activePen) = activeChannel switch
        {
            CurveChannel.Red => (Color.FromRgb(239, 83, 80), new Pen(new SolidColorBrush(Color.FromRgb(239, 83, 80)), 2.2)),
            CurveChannel.Green => (Color.FromRgb(102, 187, 106), new Pen(new SolidColorBrush(Color.FromRgb(102, 187, 106)), 2.2)),
            CurveChannel.Blue => (Color.FromRgb(66, 165, 245), new Pen(new SolidColorBrush(Color.FromRgb(66, 165, 245)), 2.2)),
            _ => (Color.FromRgb(240, 240, 240), new Pen(new SolidColorBrush(Color.FromRgb(240, 240, 240)), 2.2))
        };
        DrawCurvePolyline(dc, ActiveCurve, activePen);

        // Draw control points on active curve
        var pts = ActiveCurve.Points;
        for (var i = 0; i < pts.Count; i++)
        {
            var screenPt = NormalizedToScreen(pts[i].X, pts[i].Y);
            var isSelected = i == selectedPointIndex;

            if (isSelected)
            {
                // Glow ring
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(120, activeColor.R, activeColor.G, activeColor.B)), 3.0), screenPt, PointRadius + 3.0, PointRadius + 3.0);
            }

            dc.DrawEllipse(new SolidColorBrush(activeColor), new Pen(Brushes.White, isSelected ? 1.5 : 1.0), screenPt, PointRadius, PointRadius);
        }
    }

    private void DrawChannelCurve(DrawingContext dc, ColorCurve curve, Color color, double thickness, bool isActive)
    {
        if (isActive) return; // Drawn separately
        var pen = new Pen(new SolidColorBrush(color), thickness);
        DrawCurvePolyline(dc, curve, pen);
    }

    private void DrawCurvePolyline(DrawingContext dc, ColorCurve curve, Pen pen)
    {
        const int steps = 100;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var pStart = NormalizedToScreen(0, curve.Evaluate(0));
            ctx.BeginFigure(pStart, isFilled: false, isClosed: false);
            for (var i = 1; i <= steps; i++)
            {
                var x = i / (double)steps;
                var y = curve.Evaluate(x);
                ctx.LineTo(NormalizedToScreen(x, y), isStroked: true, isSmoothJoin: true);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        var pos = e.GetPosition(this);
        var norm = ScreenToNormalized(pos);
        var pts = ActiveCurve.Points;

        // Check if clicked existing point
        var hitIdx = -1;
        for (var i = 0; i < pts.Count; i++)
        {
            var screenPt = NormalizedToScreen(pts[i].X, pts[i].Y);
            if ((screenPt - pos).Length <= HitRadius)
            {
                hitIdx = i;
                break;
            }
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2 && hitIdx > 0 && hitIdx < pts.Count - 1)
            {
                // Double click removes intermediate point
                pts.RemoveAt(hitIdx);
                selectedPointIndex = -1;
                InvalidateVisual();
                UpdateReadout();
                CurvesChanged?.Invoke();
                return;
            }

            if (hitIdx >= 0)
            {
                selectedPointIndex = hitIdx;
                isDragging = true;
                CaptureMouse();
                InvalidateVisual();
                UpdateReadout();
            }
            else
            {
                // Click on curve: add new control point
                var newPt = new CurvePoint(norm.X, norm.Y);
                pts.Add(newPt);
                pts.Sort((a, b) => a.X.CompareTo(b.X));
                selectedPointIndex = pts.IndexOf(newPt);
                isDragging = true;
                CaptureMouse();
                InvalidateVisual();
                UpdateReadout();
                CurvesChanged?.Invoke();
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (isDragging && selectedPointIndex >= 0 && selectedPointIndex < ActiveCurve.Points.Count)
        {
            var pos = e.GetPosition(this);
            var norm = ScreenToNormalized(pos);
            var pts = ActiveCurve.Points;
            var pt = pts[selectedPointIndex];

            // Endpoints are clamped to X=0 and X=1
            if (selectedPointIndex == 0)
            {
                pt.X = 0.0;
                pt.Y = norm.Y;
            }
            else if (selectedPointIndex == pts.Count - 1)
            {
                pt.X = 1.0;
                pt.Y = norm.Y;
            }
            else
            {
                var minX = pts[selectedPointIndex - 1].X + 0.005;
                var maxX = pts[selectedPointIndex + 1].X - 0.005;
                pt.X = Math.Clamp(norm.X, minX, maxX);
                pt.Y = norm.Y;
            }

            InvalidateVisual();
            UpdateReadout();
            CurvesChanged?.Invoke();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (isDragging)
        {
            isDragging = false;
            ReleaseMouseCapture();
            CurvesChanged?.Invoke();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Delete && selectedPointIndex > 0 && selectedPointIndex < ActiveCurve.Points.Count - 1)
        {
            ActiveCurve.Points.RemoveAt(selectedPointIndex);
            selectedPointIndex = -1;
            InvalidateVisual();
            UpdateReadout();
            CurvesChanged?.Invoke();
            e.Handled = true;
        }
    }

    private void UpdateReadout()
    {
        var pts = ActiveCurve.Points;
        if (selectedPointIndex >= 0 && selectedPointIndex < pts.Count)
        {
            var p = pts[selectedPointIndex];
            PointSelected?.Invoke(string.Format(CultureInfo.InvariantCulture, "In: {0:F1}%   Out: {1:F1}%", p.X * 100.0, p.Y * 100.0));
        }
        else
        {
            PointSelected?.Invoke(string.Format(CultureInfo.InvariantCulture, "Curve: {0} ({1} pts)", activeChannel, pts.Count));
        }
    }
}
