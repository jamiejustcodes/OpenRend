using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro HSL Curves interactive canvas.
/// Supports Hue vs Hue, Hue vs Sat, Hue vs Lum, Sat vs Sat, and Lum vs Sat curves
/// with multi-point displacement, spectrum background gradient, and periodic hue interpolation.
/// </summary>
public sealed class HSLCurvesControl : FrameworkElement
{
    private const double Padding = 14.0;
    private const double PointRadius = 4.5;
    private const double HitRadius = 9.0;

    private HSLCurveMode activeMode = HSLCurveMode.HueVsSat;
    private HSLCurvesData curves = new();
    private int selectedPointIndex = -1;
    private bool isDragging;

    public event Action? CurvesChanged;
    public event Action<string>? PointSelected;

    public HSLCurveMode ActiveMode
    {
        get => activeMode;
        set
        {
            if (activeMode != value)
            {
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
        HSLCurveMode.LumVsSat => curves.LumVsSat,
        _ => curves.HueVsSat
    };

    private bool IsHueMode => activeMode is HSLCurveMode.HueVsHue or HSLCurveMode.HueVsSat or HSLCurveMode.HueVsLum;

    public HSLCurvesControl()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        MinHeight = 180;
        MinWidth = 180;
    }

    public void ResetActive()
    {
        var fresh = IsHueMode ? HSLCurve.CreateDefaultHue() : HSLCurve.CreateDefaultLinear();
        switch (activeMode)
        {
            case HSLCurveMode.HueVsHue: curves.HueVsHue = fresh; break;
            case HSLCurveMode.HueVsSat: curves.HueVsSat = fresh; break;
            case HSLCurveMode.HueVsLum: curves.HueVsLum = fresh; break;
            case HSLCurveMode.SatVsSat: curves.SatVsSat = fresh; break;
            case HSLCurveMode.LumVsSat: curves.LumVsSat = fresh; break;
        }
        selectedPointIndex = -1;
        InvalidateVisual();
        UpdateReadout();
        CurvesChanged?.Invoke();
    }

    public void ResetAll()
    {
        curves = new HSLCurvesData();
        selectedPointIndex = -1;
        InvalidateVisual();
        UpdateReadout();
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

        // Spectrum Gradient bar along bottom (4px height)
        var spectrumRect = new Rect(rect.Left, rect.Bottom - 6, rect.Width, 6);
        Brush spectrumBrush;
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
        else if (activeMode == HSLCurveMode.SatVsSat)
        {
            spectrumBrush = new LinearGradientBrush(
                Color.FromRgb(128, 128, 128), Color.FromRgb(255, 0, 128), 0.0);
        }
        else
        {
            spectrumBrush = new LinearGradientBrush(
                Colors.Black, Colors.White, 0.0);
        }
        dc.DrawRectangle(spectrumBrush, null, spectrumRect);

        // Neutral center baseline (Y = 0.5)
        var centerPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1.0) { DashStyle = DashStyles.Dash };
        var yCenter = rect.Bottom - 0.5 * rect.Height;
        dc.DrawLine(centerPen, new Point(rect.Left, yCenter), new Point(rect.Right, yCenter));

        // Upper & lower guideline (+50%, -50%)
        var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)), 1.0);
        dc.DrawLine(guidePen, new Point(rect.Left, rect.Bottom - 0.75 * rect.Height), new Point(rect.Right, rect.Bottom - 0.75 * rect.Height));
        dc.DrawLine(guidePen, new Point(rect.Left, rect.Bottom - 0.25 * rect.Height), new Point(rect.Right, rect.Bottom - 0.25 * rect.Height));

        // Draw curve polyline
        var curve = ActiveCurve;
        var curvePen = new Pen(new SolidColorBrush(Color.FromRgb(79, 195, 247)), 2.2);
        const int steps = 120;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var pStart = NormalizedToScreen(0, curve.Evaluate(0, IsHueMode));
            ctx.BeginFigure(pStart, isFilled: false, isClosed: false);
            for (var i = 1; i <= steps; i++)
            {
                var x = i / (double)steps;
                var y = curve.Evaluate(x, IsHueMode);
                ctx.LineTo(NormalizedToScreen(x, y), isStroked: true, isSmoothJoin: true);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, curvePen, geometry);

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

        if (activeMode == HSLCurveMode.SatVsSat)
        {
            var s = (byte)(x * 255);
            return Color.FromRgb(255, (byte)(255 - s), (byte)(255 - s / 2));
        }

        var l = (byte)(x * 255);
        return Color.FromRgb(l, l, l);
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
            if (e.ClickCount == 2 && hitIdx >= 0 && pts.Count > 2)
            {
                // Double click removes point
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
                // Click on curve adds new point
                var newPt = new HSLCurvePoint(norm.X, norm.Y);
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

            pt.Y = norm.Y;

            // For linear non-hue curves, clamp endpoints
            if (!IsHueMode)
            {
                if (selectedPointIndex == 0) pt.X = 0.0;
                else if (selectedPointIndex == pts.Count - 1) pt.X = 1.0;
                else
                {
                    var minX = pts[selectedPointIndex - 1].X + 0.005;
                    var maxX = pts[selectedPointIndex + 1].X - 0.005;
                    pt.X = Math.Clamp(norm.X, minX, maxX);
                }
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
        if (e.Key == Key.Delete && selectedPointIndex >= 0 && ActiveCurve.Points.Count > 2)
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
            var deltaPercent = (p.Y - 0.5) * 200.0;
            var sign = deltaPercent >= 0 ? "+" : "";
            if (IsHueMode)
            {
                var deg = (int)Math.Round(p.X * 360.0);
                PointSelected?.Invoke(string.Format(CultureInfo.InvariantCulture, "Hue: {0}°   Shift: {1}{2:F1}%", deg, sign, deltaPercent));
            }
            else
            {
                PointSelected?.Invoke(string.Format(CultureInfo.InvariantCulture, "In: {0:F1}%   Mod: {1}{2:F1}%", p.X * 100.0, sign, deltaPercent));
            }
        }
        else
        {
            var modeName = activeMode switch
            {
                HSLCurveMode.HueVsHue => "Hue vs Hue",
                HSLCurveMode.HueVsSat => "Hue vs Saturation",
                HSLCurveMode.HueVsLum => "Hue vs Luminance",
                HSLCurveMode.SatVsSat => "Saturation vs Saturation",
                HSLCurveMode.LumVsSat => "Luminance vs Saturation",
                _ => "HSL Curve"
            };
            PointSelected?.Invoke(string.Format(CultureInfo.InvariantCulture, "Mode: {0} ({1} pts)", modeName, pts.Count));
        }
    }
}

