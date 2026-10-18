using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro 2D Color Wheel interactive control.
/// Renders an HSL color gradient disc with a draggable puck/crosshair for 2-axis color bias adjustment.
/// </summary>
public sealed class ColorGradingWheelControl : FrameworkElement
{
    private const int WheelSize = 130;
    private const double WheelRadius = 58.0;

    private static readonly WriteableBitmap CachedWheelBitmap = CreateWheelBitmap(WheelSize);

    public static readonly DependencyProperty ValueXProperty =
        DependencyProperty.Register(nameof(ValueX), typeof(double), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

    public static readonly DependencyProperty ValueYProperty =
        DependencyProperty.Register(nameof(ValueY), typeof(double), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnValueChanged));

    public static readonly DependencyProperty WheelNameProperty =
        DependencyProperty.Register(nameof(WheelName), typeof(string), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata("Wheel", FrameworkPropertyMetadataOptions.AffectsRender));

    public double ValueX
    {
        get => (double)GetValue(ValueXProperty);
        set => SetValue(ValueXProperty, Math.Clamp(value, -1.0, 1.0));
    }

    public double ValueY
    {
        get => (double)GetValue(ValueYProperty);
        set => SetValue(ValueYProperty, Math.Clamp(value, -1.0, 1.0));
    }

    public string WheelName
    {
        get => (string)GetValue(WheelNameProperty);
        set => SetValue(WheelNameProperty, value);
    }

    public event Action<double, double>? ValuesChanged;

    private bool isDragging;

    public ColorGradingWheelControl()
    {
        Width = WheelSize;
        Height = WheelSize;
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorGradingWheelControl control && !control.isDragging)
        {
            control.InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(WheelSize, WheelSize);

    protected override void OnRender(DrawingContext dc)
    {
        var center = new Point(WheelSize / 2.0, WheelSize / 2.0);

        // Draw cached wheel bitmap
        dc.DrawImage(CachedWheelBitmap, new Rect(0, 0, WheelSize, WheelSize));

        // Draw dark border ring
        var ringPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1.0);
        dc.DrawEllipse(null, ringPen, center, WheelRadius, WheelRadius);

        // Draw crosshair axes at center
        var crossPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1.0);
        dc.DrawLine(crossPen, new Point(center.X - 10, center.Y), new Point(center.X + 10, center.Y));
        dc.DrawLine(crossPen, new Point(center.X, center.Y - 10), new Point(center.X, center.Y + 10));

        // Puck position
        var puckPos = new Point(center.X + ValueX * (WheelRadius - 4), center.Y + ValueY * (WheelRadius - 4));

        // Draw line from center to puck
        if (Math.Abs(ValueX) > 0.01 || Math.Abs(ValueY) > 0.01)
        {
            var linePen = new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 1.5);
            dc.DrawLine(linePen, center, puckPos);
        }

        // Draw puck (outer white ring with dark drop shadow, inner bright dot)
        var shadowPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 2.5);
        dc.DrawEllipse(null, shadowPen, puckPos, 5.0, 5.0);

        var puckPen = new Pen(Brushes.White, 1.5);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), puckPen, puckPos, 4.0, 4.0);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                // Double click resets to center
                ValueX = 0;
                ValueY = 0;
                ValuesChanged?.Invoke(0, 0);
                InvalidateVisual();
                return;
            }

            CaptureMouse();
            isDragging = true;
            UpdateFromMouse(e.GetPosition(this));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (isDragging && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateFromMouse(e.GetPosition(this));
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (isDragging && e.ChangedButton == MouseButton.Left)
        {
            isDragging = false;
            ReleaseMouseCapture();
        }
    }

    private void UpdateFromMouse(Point pos)
    {
        var center = new Point(WheelSize / 2.0, WheelSize / 2.0);
        var dx = (pos.X - center.X) / (WheelRadius - 4);
        var dy = (pos.Y - center.Y) / (WheelRadius - 4);

        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > 1.0 && dist > 0)
        {
            dx /= dist;
            dy /= dist;
        }

        ValueX = Math.Round(dx, 3);
        ValueY = Math.Round(dy, 3);
        ValuesChanged?.Invoke(ValueX, ValueY);
        InvalidateVisual();
    }

    public void Reset()
    {
        ValueX = 0;
        ValueY = 0;
        ValuesChanged?.Invoke(0, 0);
        InvalidateVisual();
    }

    private static WriteableBitmap CreateWheelBitmap(int size)
    {
        var wb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[size * size * 4];
        var center = size / 2.0;
        var radius = WheelRadius;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var idx = (y * size + x) * 4;
                var dx = (x - center) / radius;
                var dy = (y - center) / radius;
                var dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist <= 1.0)
                {
                    var angle = Math.Atan2(dy, dx); // -pi to +pi
                    var hue = (angle / (2 * Math.PI) + 1.0) % 1.0;
                    var sat = dist;
                    var val = 0.5 + 0.4 * (1.0 - dist * 0.5);

                    // HSV to RGB
                    var (r, g, b) = HsvToRgb(hue, sat, val);
                    pixels[idx] = (byte)(b * 255);
                    pixels[idx + 1] = (byte)(g * 255);
                    pixels[idx + 2] = (byte)(r * 255);
                    pixels[idx + 3] = (byte)(dist > 0.95 ? (1.0 - (dist - 0.95) / 0.05) * 255 : 255);
                }
                else
                {
                    pixels[idx] = 0;
                    pixels[idx + 1] = 0;
                    pixels[idx + 2] = 0;
                    pixels[idx + 3] = 0;
                }
            }
        }

        wb.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        wb.Freeze();
        return wb;
    }

    private static (double R, double G, double B) HsvToRgb(double h, double s, double v)
    {
        if (s <= 0.0001) return (v, v, v);
        var sector = h * 6.0;
        var i = (int)Math.Floor(sector);
        var f = sector - i;
        var p = v * (1.0 - s);
        var q = v * (1.0 - s * f);
        var t = v * (1.0 - s * (1.0 - f));
        return (i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q)
        };
    }
}
