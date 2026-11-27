using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

/// <summary>
/// Interactive HSL curves with periodic interpolation for hue.
/// </summary>
public sealed class HSLCurvesControl : CurveGraphControl
{
    private const double PointRadius = 4.5;
    private const double HitRadius = 9.0;

    private HSLCurveMode activeMode = HSLCurveMode.HueVsSat;
    private HSLCurvesData curves = new();
    private int selectedPointIndex = -1;
    private HSLCurvesData? beforeDrag;
    internal override bool IsDragging => beforeDrag is not null;

    public event Action? CurvesChanged;
    internal event Action? CurvePreviewChanged;
    internal event Action? PointChanged;

    public HSLCurveMode ActiveMode
    {
        get => activeMode;
        set
        {
            if (activeMode != value)
            {
                CancelDrag();
                activeMode = value;
                selectedPointIndex = -1;
                InvalidateVisual();
                UpdateReadout();
            }
        }
    }

    public HSLCurvesData Curves
    {
        get => curves;
        set
        {
            CancelDrag();
            curves = value ?? new HSLCurvesData();
            selectedPointIndex = -1;
            InvalidateVisual();
            UpdateReadout();
        }
    }

    public HSLCurve ActiveCurve => activeMode switch
    {
        HSLCurveMode.HueVsHue => curves.HueVsHue,
        HSLCurveMode.HueVsSat => curves.HueVsSat,
        HSLCurveMode.HueVsLum => curves.HueVsLum,
        HSLCurveMode.SatVsSat => curves.SatVsSat,
        HSLCurveMode.SatVsLum => curves.SatVsLum,
        HSLCurveMode.LumVsSat => curves.LumVsSat,
        _ => curves.HueVsSat
    };

    internal bool IsHueMode => activeMode is HSLCurveMode.HueVsHue or HSLCurveMode.HueVsSat or HSLCurveMode.HueVsLum;
    private bool IsSaturationMode => activeMode is HSLCurveMode.SatVsSat or HSLCurveMode.SatVsLum;
    private bool UsesFreePoints => activeMode == HSLCurveMode.SatVsLum;

    protected override int PointCount => ActiveCurve.Points.Count;
    protected override int SelectedPointIndex
    {
        get => selectedPointIndex;
        set { selectedPointIndex = value; InvalidateVisual(); UpdateReadout(); }
    }
    protected override bool WrapTangents => IsHueMode;
    protected override bool ClampStoredTangents => true;

    internal Point? SelectedPoint => selectedPointIndex >= 0 && selectedPointIndex < PointCount
        ? CurvePointAt(selectedPointIndex) : null;

    internal bool CanMoveSelectedInput => SelectedPoint is not null &&
        (IsHueMode || UsesFreePoints || (selectedPointIndex > 0 && selectedPointIndex < PointCount - 1));

    internal bool SetSelectedCoordinate(bool input, double value)
    {
        if (SelectedPoint is not { } selected || IsDragging || !double.IsFinite(value) || value is < 0 or > 1)
            return false;
        var points = ActiveCurve.Points;
        var current = points[selectedPointIndex];
        if (input)
        {
            if (!CanMoveSelectedInput) return false;
            if (selected.X == value) return true;
            if (IsHueMode)
            {
                if (value == 1) value = 0;
                if (points.Any(point => point != current &&
                    (Math.Abs(point.X - value) < 1e-7 || Math.Abs(point.X - value) > 1 - 1e-7))) return false;
            }
            else if ((selectedPointIndex > 0 && value <= points[selectedPointIndex - 1].X) ||
                (selectedPointIndex + 1 < points.Count && value >= points[selectedPointIndex + 1].X)) return false;

            current.X = value;
            ActiveCurve.SortPoints();
            selectedPointIndex = points.IndexOf(current);
        }
        else
        {
            if (selected.Y == value) return true;
            current.Y = value;
        }
        RevealPoint(current.X, current.Y);
        InvalidateVisual();
        UpdateReadout();
        CurvesChanged?.Invoke();
        return true;
    }

    protected override Point CurvePointAt(int index)
    {
        var point = ActiveCurve.Points[index];
        return new(point.X, point.Y);
    }

    protected override CurveTangent? StoredTangent(int index, bool incoming)
    {
        var point = ActiveCurve.Points[index];
        return incoming ? point.InTangent : point.OutTangent;
    }

    protected override void SetTangent(int index, bool incoming, CurveTangent value)
    {
        var point = ActiveCurve.Points[index];
        if (incoming) point.InTangent = value;
        else point.OutTangent = value;
    }

    protected override bool TangentsUnlinked(int index) => ActiveCurve.Points[index].TangentsUnlinked;
    protected override void SetTangentsUnlinked(int index, bool unlinked) => ActiveCurve.Points[index].TangentsUnlinked = unlinked;

    public void ResetActive()
    {
        CancelDrag();
        var fresh = UsesFreePoints ? new HSLCurve() : IsHueMode ? HSLCurve.CreateDefaultHue() : HSLCurve.CreateDefaultLinear();
        switch (activeMode)
        {
            case HSLCurveMode.HueVsHue: curves.HueVsHue = fresh; break;
            case HSLCurveMode.HueVsSat: curves.HueVsSat = fresh; break;
            case HSLCurveMode.HueVsLum: curves.HueVsLum = fresh; break;
            case HSLCurveMode.SatVsSat: curves.SatVsSat = fresh; break;
            case HSLCurveMode.SatVsLum: curves.SatVsLum = fresh; break;
            case HSLCurveMode.LumVsSat: curves.LumVsSat = fresh; break;
        }
        selectedPointIndex = -1;
        InvalidateVisual();
        UpdateReadout();
        CurvesChanged?.Invoke();
    }

    public void ResetAll()
    {
        CancelDrag();
        curves = new HSLCurvesData();
        selectedPointIndex = -1;
        InvalidateVisual();
        UpdateReadout();
        CurvesChanged?.Invoke();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = GraphRect;
        if (rect.Width <= 0 || rect.Height <= 0) return;

        // Background
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 24, 26)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 18, 20)), new Pen(new SolidColorBrush(Color.FromRgb(45, 45, 48)), 1.0), rect);

        // Spectrum Gradient bar along bottom (4px height)
        var spectrumRect = new Rect(rect.Left, rect.Bottom - 6, rect.Width, 6);
        LinearGradientBrush spectrumBrush;
        if (IsHueMode)
        {
            spectrumBrush = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(255, 0, 0), 0.0),
                    new(Color.FromRgb(255, 255, 0), 0.1667),
                    new(Color.FromRgb(0, 255, 0), 0.3333),
                    new(Color.FromRgb(0, 255, 255), 0.5),
                    new(Color.FromRgb(0, 0, 255), 0.6667),
                    new(Color.FromRgb(255, 0, 255), 0.8333),
                    new(Color.FromRgb(255, 0, 0), 1.0)
                },
                new Point(0, 0), new Point(1, 0));
        }
        else if (IsSaturationMode)
        {
            spectrumBrush = new LinearGradientBrush(
                Color.FromRgb(128, 128, 128), Color.FromRgb(255, 0, 128), 0.0);
        }
        else
        {
            spectrumBrush = new LinearGradientBrush(
                Colors.Black, Colors.White, 0.0);
        }
        spectrumBrush.MappingMode = BrushMappingMode.Absolute;
        spectrumBrush.StartPoint = new Point(NormalizedToScreen(0, 0).X, spectrumRect.Top);
        spectrumBrush.EndPoint = new Point(NormalizedToScreen(1, 0).X, spectrumRect.Top);
        dc.DrawRectangle(spectrumBrush, null, spectrumRect);

        BeginGraphClip(dc);
        // Neutral center baseline (Y = 0.5)
        var centerPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1.0) { DashStyle = DashStyles.Dash };
        var yCenter = NormalizedToScreen(0, 0.5).Y;
        dc.DrawLine(centerPen, new Point(rect.Left, yCenter), new Point(rect.Right, yCenter));

        // Upper & lower guideline (+50%, -50%)
        var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)), 1.0);
        var upperGuide = NormalizedToScreen(0, 0.75).Y;
        var lowerGuide = NormalizedToScreen(0, 0.25).Y;
        dc.DrawLine(guidePen, new Point(rect.Left, upperGuide), new Point(rect.Right, upperGuide));
        dc.DrawLine(guidePen, new Point(rect.Left, lowerGuide), new Point(rect.Right, lowerGuide));

        // Draw curve polyline
        var curve = ActiveCurve;
        var curvePen = new Pen(new SolidColorBrush(Color.FromRgb(79, 195, 247)), 2.2);
        const int steps = 120;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var pStart = NormalizedToScreen(VisibleLeft, curve.Evaluate(VisibleLeft, IsHueMode));
            ctx.BeginFigure(pStart, isFilled: false, isClosed: false);
            for (var i = 1; i <= steps; i++)
            {
                var x = VisibleLeft + (VisibleRight - VisibleLeft) * i / steps;
                var y = curve.Evaluate(x, IsHueMode);
                ctx.LineTo(NormalizedToScreen(x, y), isStroked: true, isSmoothJoin: true);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, curvePen, geometry);
        DrawTangents(dc);

        // Draw control points
        for (var i = 0; i < curve.Points.Count; i++)
        {
            var p = curve.Points[i];
            var screenPt = NormalizedToScreen(p.X, p.Y);
            var isSelected = i == selectedPointIndex;

            // Compute dot fill color based on input hue / coordinate
            var ptColor = ColorFromCoordinate(p.X);

            if (isSelected)
            {
                // Glow ring
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), 3.0), screenPt, PointRadius + 3.0, PointRadius + 3.0);
            }

            dc.DrawEllipse(new SolidColorBrush(ptColor), new Pen(Brushes.White, isSelected ? 1.5 : 1.0), screenPt, PointRadius, PointRadius);
        }
        dc.Pop();
    }

    private Color ColorFromCoordinate(double x)
    {
        if (IsHueMode)
        {
            // X in [0, 1] is Hue in [0, 360]
            var h = x * 6.0;
            var c = 1.0;
            var xVal = c * (1.0 - Math.Abs((h % 2.0) - 1.0));
            var (r, g, b) = ((int)h) switch
            {
                0 => (c, xVal, 0.0),
                1 => (xVal, c, 0.0),
                2 => (0.0, c, xVal),
                3 => (0.0, xVal, c),
                4 => (xVal, 0.0, c),
                _ => (c, 0.0, xVal)
            };
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        if (IsSaturationMode)
            return Color.FromRgb((byte)(128 + x * 127), (byte)(128 * (1 - x)), 128);

        var l = (byte)(x * 255);
        return Color.FromRgb(l, l, l);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (IsKeyboardTangentEdit) CommitCurveGesture();
        if (TryBeginTangentDrag(e)) return;
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

        if (e.ChangedButton == MouseButton.Right)
        {
            OpenTangentMenu(hitIdx);
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Left && (hitIdx >= 0 || GraphRect.Contains(pos)))
        {
            e.Handled = true;
            if (e.ClickCount == 2 && hitIdx >= 0 && (UsesFreePoints || pts.Count > 2))
            {
                // Double click removes point
                pts.RemoveAt(hitIdx);
                selectedPointIndex = -1;
                InvalidateVisual();
                UpdateReadout();
                CurvesChanged?.Invoke();
                return;
            }
            if (UsesFreePoints && hitIdx < 0 && e.ClickCount < 2) return;

            if (!CaptureMouse()) return;
            SnapshotCurveGesture();
            selectedPointIndex = hitIdx;
            if (hitIdx < 0)
            {
                // A click on the line should use its value despite screen-pixel rounding.
                var curveY = ActiveCurve.Evaluate(norm.X, IsHueMode);
                var y = UsesFreePoints && Math.Abs(NormalizedToScreen(norm.X, curveY).Y - pos.Y) <= HitRadius
                    ? curveY : norm.Y;
                var newPt = new HSLCurvePoint(norm.X, y);
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
        if (TryMoveTangent(e) || IsKeyboardTangentEdit) return;
        if (IsDragging && selectedPointIndex >= 0 && selectedPointIndex < ActiveCurve.Points.Count)
        {
            var pos = e.GetPosition(this);
            var norm = ScreenToNormalized(pos);
            var pts = ActiveCurve.Points;
            var pt = pts[selectedPointIndex];
            var previous = (pt.X, pt.Y);

            pt.Y = norm.Y;

            if (!IsHueMode)
            {
                var first = selectedPointIndex == 0;
                var last = selectedPointIndex == pts.Count - 1;
                if (!UsesFreePoints && first) pt.X = 0.0;
                else if (!UsesFreePoints && last) pt.X = 1.0;
                else
                {
                    var left = first ? 0 : pts[selectedPointIndex - 1].X;
                    var right = last ? 1 : pts[selectedPointIndex + 1].X;
                    var gap = right - left >= 0.01 ? 0.005 : 0;
                    pt.X = Math.Clamp(norm.X, first ? left : left + gap, last ? right : right - gap);
                }
            }

            if (previous != (pt.X, pt.Y))
                PreviewCurveGesture();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !IsDragging || IsKeyboardTangentEdit) return;
        e.Handled = true;
        CommitCurveGesture();
    }

    protected override void SnapshotCurveGesture() => beforeDrag = curves.DeepClone();

    protected override void PreviewCurveGesture()
    {
        InvalidateVisual();
        UpdateReadout();
        CurvePreviewChanged?.Invoke();
    }

    protected override void CommitCurveGesture()
    {
        beforeDrag = null;
        ResetTangentGesture();
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
        if (!IsTangentGesture) selectedPointIndex = -1;
        ResetTangentGesture();
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
        if (e.Key == Key.Delete && selectedPointIndex >= 0 && (UsesFreePoints || ActiveCurve.Points.Count > 2))
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

    private void UpdateReadout() => PointChanged?.Invoke();
}

