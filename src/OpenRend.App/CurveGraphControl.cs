using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenRend.App;

public abstract partial class CurveGraphControl : FrameworkElement
{
    private const double Padding = 14;
    private const double MaximumZoom = 16;
    private double zoom = 1;
    private double left, bottom;

    internal abstract bool IsDragging { get; }
    internal event Action? ViewChanged;
    internal double Zoom => zoom;
    protected double VisibleLeft => left;
    protected double VisibleRight => left + Span;
    private double Span => 1 / zoom;
    protected Rect GraphRect => new(Padding, Padding, Math.Max(10, ActualWidth - 2 * Padding), Math.Max(10, ActualHeight - 2 * Padding));

    protected CurveGraphControl()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        MinHeight = MinWidth = 180;
    }

    protected Point NormalizedToScreen(double x, double y)
    {
        var rect = GraphRect;
        return new(rect.Left + (x - left) * zoom * rect.Width, rect.Bottom - (y - bottom) * zoom * rect.Height);
    }

    protected Point ScreenToNormalized(Point point)
    {
        var rect = GraphRect;
        return new(left + Span * Math.Clamp((point.X - rect.Left) / rect.Width, 0, 1),
            bottom + Span * Math.Clamp((rect.Bottom - point.Y) / rect.Height, 0, 1));
    }

    protected bool IsVisiblePoint(Point point) => GraphRect.Contains(point);

    protected void BeginGraphClip(DrawingContext drawing)
    {
        var rect = GraphRect;
        // Retain the full endpoint markers at the edges of the signal domain.
        var leftEdge = left == 0 ? 0 : rect.Left;
        var topEdge = bottom + Span == 1 ? 0 : rect.Top;
        var rightEdge = left + Span == 1 ? ActualWidth : rect.Right;
        var bottomEdge = bottom == 0 ? ActualHeight : rect.Bottom;
        drawing.PushClip(new RectangleGeometry(new Rect(
            new Point(leftEdge, topEdge), new Point(Math.Max(leftEdge, rightEdge), Math.Max(topEdge, bottomEdge)))));
    }

    protected void RevealPoint(double x, double y)
    {
        var dx = x < left || x > VisibleRight ? x - (left + Span / 2) : 0;
        var dy = y < bottom || y > bottom + Span ? y - (bottom + Span / 2) : 0;
        UpdateView(zoom, left + dx, bottom + dy);
    }

    internal void StepZoom(int steps)
    {
        var rect = GraphRect;
        ZoomAround(steps, new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));
    }

    private void ZoomAround(int steps, Point anchor)
    {
        if (IsDragging || steps == 0) return;
        var point = ScreenToNormalized(anchor);
        var next = Math.Clamp(zoom * Math.Pow(2, steps), 1, MaximumZoom);
        var ratio = zoom / next;
        UpdateView(next, point.X - (point.X - left) * ratio, point.Y - (point.Y - bottom) * ratio);
    }

    internal void ResetView()
    {
        if (!IsDragging) UpdateView(1, 0, 0);
    }

    private void UpdateView(double zoom, double left, double bottom)
    {
        var limit = 1 - 1 / zoom;
        left = Math.Clamp(left, 0, limit);
        bottom = Math.Clamp(bottom, 0, limit);
        if ((this.zoom, this.left, this.bottom) == (zoom, left, bottom)) return;
        (this.zoom, this.left, this.bottom) = (zoom, left, bottom);
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    internal static bool IsNavigationKey(Key key) => key is Key.Add or Key.Subtract or
        Key.NumPad2 or Key.NumPad4 or Key.NumPad5 or Key.NumPad6 or Key.NumPad8 or Key.PageUp or Key.PageDown;

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var point = e.GetPosition(this);
        if (!GraphRect.Contains(point) && !IsDragging) return;
        if (e.Delta != 0) ZoomAround(Math.Sign(e.Delta), point);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (TryEditTangentWithKey(e)) return;
        if (Keyboard.Modifiers != ModifierKeys.None || !IsNavigationKey(e.Key)) return;
        e.Handled = true;
        if (IsDragging) return;
        var step = Span / 4;
        switch (e.Key)
        {
            case Key.Add: StepZoom(1); break;
            case Key.Subtract: StepZoom(-1); break;
            case Key.NumPad5: ResetView(); break;
            case Key.NumPad2: UpdateView(zoom, left, bottom - step); break;
            case Key.NumPad4: UpdateView(zoom, left - step, bottom); break;
            case Key.NumPad6: UpdateView(zoom, left + step, bottom); break;
            case Key.NumPad8: UpdateView(zoom, left, bottom + step); break;
            case Key.PageUp: SelectAdjacentPoint(1); break;
            case Key.PageDown: SelectAdjacentPoint(-1); break;
        }
    }

    protected abstract void SelectAdjacentPoint(int direction);

    protected static int AdjacentPointIndex(int current, int count, int direction) =>
        current < 0 ? (direction > 0 ? 0 : count - 1) : Math.Clamp(current + direction, 0, count - 1);
}
