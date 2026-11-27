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
/// Interactive master and RGB curves with editable control points.
/// </summary>
public sealed class ColorCurvesControl : CurveGraphControl
{
    private const double PointRadius = 4.5;
    private const double HitRadius = 9.0;

    private CurveChannel activeChannel = CurveChannel.Master;
    private ColorCurvesData curves = new();
    private int selectedPointIndex = -1;
    private ColorCurvesData? beforeDrag;
    internal override bool IsDragging => beforeDrag is not null;

    public event Action? CurvesChanged;
    internal event Action? CurvePreviewChanged;
    public event Action<string>? PointSelected;

    public CurveChannel ActiveChannel
    {
        get => activeChannel;
        set
        {
            if (activeChannel != value)
            {
                CancelDrag();
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
            CancelDrag();
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

    public void ResetActive()
    {
        CancelDrag();
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
        CancelDrag();
        curves = new ColorCurvesData();
        selectedPointIndex = -1;
        InvalidateVisual();
        UpdateReadout();
        CurvesChanged?.Invoke();
    }

    public void SmoothActive()
    {
        CancelDrag();
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

    protected override void OnRender(DrawingContext dc)
    {
        var rect = GraphRect;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        // Background
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 24, 26)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 18, 20)), new Pen(new SolidColorBrush(Color.FromRgb(45, 45, 48)), 1.0), rect);

        // Keep the grid in curve coordinates as the view pans and zooms.
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1.0);
        var divisions = (int)Zoom * 4;
        for (var i = 1; i < divisions; i++)
        {
            var coordinate = i / (double)divisions;
            var position = NormalizedToScreen(coordinate, coordinate);
            if (position.X > rect.Left && position.X < rect.Right)
                dc.DrawLine(gridPen, new Point(position.X, rect.Top), new Point(position.X, rect.Bottom));
            if (position.Y > rect.Top && position.Y < rect.Bottom)
                dc.DrawLine(gridPen, new Point(rect.Left, position.Y), new Point(rect.Right, position.Y));
        }

        BeginGraphClip(dc);
        var diagPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1.0) { DashStyle = DashStyles.Dash };
        dc.DrawLine(diagPen, NormalizedToScreen(0, 0), NormalizedToScreen(1, 1));

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
        dc.Pop();
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
            var pStart = NormalizedToScreen(VisibleLeft, curve.Evaluate(VisibleLeft));
            ctx.BeginFigure(pStart, isFilled: false, isClosed: false);
            for (var i = 1; i <= steps; i++)
            {
                var x = VisibleLeft + (VisibleRight - VisibleLeft) * i / steps;
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
            if (IsVisiblePoint(screenPt) && (screenPt - pos).Length <= HitRadius)
            {
                hitIdx = i;
                break;
            }
        }

        if (e.ChangedButton == MouseButton.Left && (hitIdx >= 0 || GraphRect.Contains(pos)))
        {
            e.Handled = true;
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

            if (!CaptureMouse()) return;
            beforeDrag = curves.DeepClone();
            selectedPointIndex = hitIdx;
            if (hitIdx < 0)
            {
                // Click on curve: add new control point
                var newPt = new CurvePoint(norm.X, norm.Y);
                pts.Add(newPt);
                pts.Sort((a, b) => a.X.CompareTo(b.X));
                selectedPointIndex = pts.IndexOf(newPt);
                CurvePreviewChanged?.Invoke();
            }
            InvalidateVisual();
            UpdateReadout();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsDragging && selectedPointIndex >= 0 && selectedPointIndex < ActiveCurve.Points.Count)
        {
            var pos = e.GetPosition(this);
            var norm = ScreenToNormalized(pos);
            var pts = ActiveCurve.Points;
            var pt = pts[selectedPointIndex];
            var previous = (pt.X, pt.Y);

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
                var left = pts[selectedPointIndex - 1].X;
                var right = pts[selectedPointIndex + 1].X;
                var gap = right - left >= 0.01 ? 0.005 : 0;
                pt.X = Math.Clamp(norm.X, left + gap, right - gap);
                pt.Y = norm.Y;
            }

            if (previous != (pt.X, pt.Y))
            {
                InvalidateVisual();
                UpdateReadout();
                CurvePreviewChanged?.Invoke();
            }
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !IsDragging) return;
        e.Handled = true;
        beforeDrag = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        CurvesChanged?.Invoke();
        CurvePreviewChanged?.Invoke();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        CancelDrag();
    }

    internal void CancelDrag()
    {
        if (beforeDrag is not { } previous) return;
        beforeDrag = null;
        curves = previous;
        selectedPointIndex = -1;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
        UpdateReadout();
        CurvePreviewChanged?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (IsDragging)
        {
            if (e.Key == Key.Escape || e.Key == Key.Delete)
            {
                if (e.Key == Key.Escape) CancelDrag();
                e.Handled = true;
            }
            return;
        }
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

    protected override void SelectAdjacentPoint(int direction)
    {
        var points = ActiveCurve.Points;
        if (points.Count == 0) return;
        selectedPointIndex = AdjacentPointIndex(selectedPointIndex, points.Count, direction);
        var point = points[selectedPointIndex];
        RevealPoint(point.X, point.Y);
        InvalidateVisual();
        UpdateReadout();
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
