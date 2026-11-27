using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

internal sealed class TransitionProgressGraph : FrameworkElement
{
    private const double Inset = 12;
    private TransitionProgressCurve curve = new();
    private TransitionProgressCurve? draft;
    private long duration = 1;
    private long selected;
    private long? dragSource;
    private bool moved;
    private Point press;

    public event Action<long>? PointSelected;
    public event Action<long>? SeekRequested;
    public event Action<long, double>? PointAdded;
    public event Action<long, TransitionProgressCurve>? PointMoved;

    public TransitionProgressGraph()
    {
        Height = 142;
        MinWidth = 140;
        Focusable = true;
        ClipToBounds = true;
        AutomationProperties.SetAutomationId(this, "TransitionProgressGraph");
        AutomationProperties.SetName(this, "Transition progress curve");
        AutomationProperties.SetHelpText(this, "Click to seek. Drag a point to edit it. Double-click to add a point.");
    }

    public void Bind(TransitionProgressCurve value, long frames, long selectedFrame)
    {
        curve = value;
        duration = Math.Max(1, frames);
        selected = selectedFrame;
        InvalidateVisual();
    }

    private Rect Plot => new(Inset, Inset, Math.Max(1, ActualWidth - 2 * Inset), Math.Max(1, ActualHeight - 2 * Inset));

    private Point Position(long frame, double value) => new(Plot.Left + (double)frame / duration * Plot.Width, Plot.Bottom - value * Plot.Height);

    private (long Frame, double Value) Coordinates(Point point)
    {
        var plot = Plot;
        var horizontal = Math.Clamp((point.X - plot.Left) / plot.Width, 0, 1);
        var frame = (long)Math.Round((decimal)horizontal * duration, MidpointRounding.AwayFromZero);
        return (frame, Math.Clamp((plot.Bottom - point.Y) / plot.Height, 0, 1));
    }

    private IEnumerable<EnvelopePoint> VisiblePoints(TransitionProgressCurve source) =>
        source.Points.TakeWhile(point => point.Frame < duration).Append(new EnvelopePoint(duration, source.EndValue));

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    protected override void OnRender(DrawingContext dc)
    {
        var plot = Plot;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(25, 27, 30)), null, new Rect(RenderSize));
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(61, 66, 73)), 1);
        for (var division = 0; division <= 4; division++)
        {
            var x = plot.Left + division * plot.Width / 4;
            var y = plot.Top + division * plot.Height / 4;
            dc.DrawLine(grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        var active = VisiblePoints(draft ?? curve).ToArray();
        var line = new StreamGeometry();
        using (var points = line.Open())
        {
            points.BeginFigure(Position(active[0].Frame, active[0].Value), false, false);
            foreach (var (from, to) in active.Zip(active.Skip(1)))
            {
                if (from.Curve == EnvelopeCurveType.Hold) points.LineTo(Position(to.Frame, from.Value), true, false);
                else
                {
                    var width = Position(to.Frame, to.Value).X - Position(from.Frame, from.Value).X;
                    var samples = (int)Math.Min(to.Frame - from.Frame, Math.Clamp(Math.Ceiling(width / 4), 1, 240));
                    for (var index = 1; index < samples; index++)
                    {
                        var frame = from.Frame + (long)Math.Round((decimal)(to.Frame - from.Frame) * index / samples);
                        points.LineTo(Position(frame, (draft ?? curve).ValueAt(frame, duration)), true, false);
                    }
                }
                points.LineTo(Position(to.Frame, to.Value), true, false);
            }
        }
        var stroke = new SolidColorBrush(Color.FromRgb(91, 189, 202));
        dc.DrawGeometry(null, new Pen(stroke, 1.5), line);
        foreach (var point in active)
            dc.DrawEllipse(point.Frame == selected ? Brushes.WhiteSmoke : stroke,
                point.Frame == selected ? new Pen(stroke, 1.5) : null, Position(point.Frame, point.Value), 4, 4);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var point = e.GetPosition(this);
        var nearest = VisiblePoints(curve).FirstOrDefault(key => (Position(key.Frame, key.Value) - point).LengthSquared <= 49);
        if (nearest is null)
        {
            if (!Plot.Contains(point)) return;
            var (frame, value) = Coordinates(point);
            if (e.ClickCount == 2) PointAdded?.Invoke(frame, value);
            else SeekRequested?.Invoke(frame);
        }
        else
        {
            Focus();
            PointSelected?.Invoke(nearest.Frame);
            SeekRequested?.Invoke(nearest.Frame);
            press = point;
            dragSource = nearest.Frame;
            moved = false;
            CaptureMouse();
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragSource is not long source || !IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(this);
        if (!moved && (point - press).LengthSquared < 9) return;
        moved = true;
        var (frame, value) = Coordinates(point);
        draft = curve.DeepClone();
        if (source == duration)
        {
            frame = duration;
            draft.EndValue = value;
        }
        else
        {
            var index = draft.Points.FindIndex(key => key.Frame == source);
            var key = draft.Points[index];
            var minimum = index == 0 ? 0 : draft.Points[index - 1].Frame + 1;
            var nextFrame = index + 1 < draft.Points.Count ? draft.Points[index + 1].Frame : duration;
            frame = index == 0 ? 0 : Math.Clamp(frame, minimum, Math.Min(duration, nextFrame) - 1);
            key.Frame = frame;
            key.Value = value;
        }
        selected = frame;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (dragSource is null) return;
        var changed = draft;
        var frame = selected;
        CancelDrag();
        if (changed is not null) PointMoved?.Invoke(frame, changed);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (dragSource is not null) CancelDrag();
        base.OnLostMouseCapture(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && dragSource is not null)
        {
            CancelDrag();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void CancelDrag()
    {
        if (dragSource is long source) selected = source;
        dragSource = null;
        draft = null;
        ReleaseMouseCapture();
        InvalidateVisual();
    }
}
