using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace OpenRend.App;

public sealed class MasterStereoMeter : FrameworkElement
{
    private static readonly Typeface ScaleTypeface = new("Segoe UI");
    private static readonly Brush MeterBackground = FrozenBrush(24, 24, 24);
    private static readonly Brush NormalLevel = FrozenBrush(79, 162, 111);
    private static readonly Brush OverLevel = FrozenBrush(218, 123, 93);
    private static readonly double[] Scale = [6, 0, -12, -24, -36, -48, -60];
    private MasterMeterReading reading = MasterMeterReading.Empty;

    internal void Update(MasterMeterReading value)
    {
        if (reading == value) return;
        reading = value;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth < 26 || ActualHeight < 48) return;
        const double pad = 6;
        var meterHeight = ActualHeight - 2 * pad;
        var barWidth = Math.Min(14, (ActualWidth - 20) / 2);
        var center = ActualWidth / 2;
        DrawChannel(0, reading.Left, reading.HeldLeft);
        DrawChannel(ActualWidth - barWidth, reading.Right, reading.HeldRight);
        var labelSpacing = 0.0;
        foreach (var db in Scale)
        {
            var y = Position(db);
            if (y < labelSpacing && db != -60) continue;
            var label = db switch { 6 => "+6", -60 => "−∞", _ => db.ToString("0", CultureInfo.CurrentCulture) };
            var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, ScaleTypeface, 8,
                Brushes.Silver, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawing.DrawText(text, new Point(center - text.Width / 2, y - text.Height / 2));
            labelSpacing = y + 9;
        }

        double Position(double db) => pad + meterHeight * (6 - Math.Clamp(db, -60, 6)) / 66;
        void DrawChannel(double x, double peak, double held)
        {
            var bottom = pad + meterHeight;
            drawing.DrawRectangle(MeterBackground, null, new Rect(x, pad, barWidth, meterHeight));
            var level = Position(20 * Math.Log10(peak));
            var zero = Position(0);
            var normalStart = Math.Max(level, zero);
            drawing.DrawRectangle(NormalLevel, null, new Rect(x, normalStart, barWidth, bottom - normalStart));
            if (level < zero) drawing.DrawRectangle(OverLevel, null, new Rect(x, level, barWidth, zero - level));
            if (held > .001)
                drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(x, Position(20 * Math.Log10(held)) - .5, barWidth, 1));
        }
    }

    private static Brush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
