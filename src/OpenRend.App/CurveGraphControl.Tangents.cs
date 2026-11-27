using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

public abstract partial class CurveGraphControl
{
    private const double HandleRadius = 4;
    private static readonly Pen TangentPen = MakeTangentPen();
    private TangentHit? mouseTangent;
    private Key? tangentKey;
    private bool showAllTangents;

    private readonly record struct TangentHit(int Index, bool Incoming, double Wrap);

    protected abstract int PointCount { get; }
    protected abstract int SelectedPointIndex { get; set; }
    protected virtual bool WrapTangents => false;
    protected virtual bool ClampStoredTangents => false;
    protected abstract Point CurvePointAt(int index);
    protected abstract CurveTangent? StoredTangent(int index, bool incoming);
    protected abstract void SetTangent(int index, bool incoming, CurveTangent value);
    protected abstract bool TangentsUnlinked(int index);
    protected abstract void SetTangentsUnlinked(int index, bool unlinked);
    protected abstract void SnapshotCurveGesture();
    protected abstract void PreviewCurveGesture();
    protected abstract void CommitCurveGesture();

    private static Pen MakeTangentPen()
    {
        var pen = new Pen(Brushes.Goldenrod, 1);
        pen.Freeze();
        return pen;
    }

    private double TangentSpan(int index, bool incoming)
    {
        var x = CurvePointAt(index).X;
        if (incoming)
            return index > 0 ? x - CurvePointAt(index - 1).X
                : WrapTangents ? x + 1 - CurvePointAt(PointCount - 1).X : 0;
        return index < PointCount - 1 ? CurvePointAt(index + 1).X - x
            : WrapTangents ? 1 + CurvePointAt(0).X - x : 0;
    }

    private CurveTangent TangentAt(int index, bool incoming)
    {
        var span = TangentSpan(index, incoming);
        var value = StoredTangent(index, incoming) ?? new CurveTangent(incoming ? -span / 3 : span / 3, 0);
        if (ClampStoredTangents)
            value = value with { X = Math.Clamp(value.X, incoming ? -span : 0, incoming ? 0 : span) };
        return value;
    }

    private double WrapOffset(double x) => WrapTangents ? (x < 0 ? 1 : x > 1 ? -1 : 0) : 0;

    protected void DrawTangents(DrawingContext drawing)
    {
        for (var index = 0; index < PointCount; index++)
        {
            if (!showAllTangents && index != SelectedPointIndex) continue;
            DrawTangent(index, incoming: true);
            DrawTangent(index, incoming: false);
        }

        void DrawTangent(int index, bool incoming)
        {
            if (TangentSpan(index, incoming) <= 1e-7) return;
            var point = CurvePointAt(index);
            var offset = TangentAt(index, incoming);
            var x = point.X + offset.X;
            var y = point.Y + offset.Y;
            var wrap = WrapOffset(x);
            if (wrap != 0) drawing.PushClip(new RectangleGeometry(GraphRect));
            drawing.DrawLine(TangentPen, NormalizedToScreen(point.X, point.Y), NormalizedToScreen(x, y));
            if (wrap != 0)
            {
                drawing.DrawLine(TangentPen, NormalizedToScreen(point.X + wrap, point.Y), NormalizedToScreen(x + wrap, y));
                drawing.Pop();
            }
            var handle = NormalizedToScreen(x + wrap, y);
            drawing.DrawRectangle(Brushes.Black, TangentPen,
                new Rect(handle.X - HandleRadius, handle.Y - HandleRadius, HandleRadius * 2, HandleRadius * 2));
        }
    }

    protected bool TryBeginTangentDrag(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsDragging) return false;
        var pointer = e.GetPosition(this);
        TangentHit? hit = null;
        var best = HandleRadius + 3;
        for (var i = 0; i < PointCount; i++)
        {
            if (!showAllTangents && i != SelectedPointIndex) continue;
            FindHandle(i, incoming: true);
            FindHandle(i, incoming: false);
        }
        if (hit is not { } target || !CaptureMouse()) return false;

        SnapshotCurveGesture();
        SelectedPointIndex = target.Index;
        mouseTangent = target;
        e.Handled = true;
        return true;

        void FindHandle(int index, bool incoming)
        {
            if (TangentSpan(index, incoming) <= 1e-7) return;
            var point = CurvePointAt(index);
            var offset = TangentAt(index, incoming);
            var x = point.X + offset.X;
            var wrap = WrapOffset(x);
            var handle = NormalizedToScreen(x + wrap, point.Y + offset.Y);
            // A collapsed handle remains keyboard editable without intercepting a click on its point.
            if (!IsVisiblePoint(handle) || (handle - NormalizedToScreen(point.X, point.Y)).Length < HandleRadius + 2)
                return;
            var distance = (pointer - handle).Length;
            if (distance >= best) return;
            best = distance;
            hit = new(index, incoming, wrap);
        }
    }

    protected bool TryMoveTangent(MouseEventArgs e)
    {
        if (mouseTangent is not { } handle) return false;
        var pointer = ScreenToNormalized(e.GetPosition(this));
        var point = CurvePointAt(handle.Index);
        ApplyTangent(handle.Index, handle.Incoming, new(pointer.X - handle.Wrap - point.X, pointer.Y - point.Y));
        e.Handled = true;
        return true;
    }

    private void ApplyTangent(int index, bool incoming, CurveTangent proposed)
    {
        var point = CurvePointAt(index);
        var span = TangentSpan(index, incoming);
        var oppositeSpan = TangentSpan(index, !incoming);
        var linked = oppositeSpan > 1e-7 && !TangentsUnlinked(index);
        if (linked) span = Math.Min(span, oppositeSpan);
        var low = -point.Y;
        var high = 1 - point.Y;
        if (linked)
        {
            high = Math.Min(point.Y, high);
            low = -high;
        }
        var value = new CurveTangent(Math.Clamp(proposed.X, incoming ? -span : 0, incoming ? 0 : span),
            Math.Clamp(proposed.Y, low, high));
        if (value == TangentAt(index, incoming)) return;
        SetTangent(index, incoming, value);
        if (linked) SetTangent(index, !incoming, new(-value.X, -value.Y));
        PreviewCurveGesture();
    }

    internal static bool IsTangentKey(Key key, ModifierKeys modifiers) =>
        (key is Key.Left or Key.Right or Key.Up or Key.Down) &&
        (modifiers == ModifierKeys.Shift || modifiers == (ModifierKeys.Control | ModifierKeys.Shift));

    private bool TryEditTangentWithKey(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (!IsTangentKey(e.Key, modifiers)) return false;
        e.Handled = true;
        var index = SelectedPointIndex;
        var incoming = modifiers == ModifierKeys.Shift;
        if (mouseTangent is not null || (IsDragging && tangentKey is null) ||
            index < 0 || index >= PointCount || TangentSpan(index, incoming) <= 1e-7)
            return true;
        if (tangentKey is null) SnapshotCurveGesture();
        tangentKey = e.Key;
        var value = TangentAt(index, incoming);
        // A key press nudges the handle by one graph DIP at the current zoom.
        var dx = 1 / (GraphRect.Width * Zoom);
        var dy = 1 / (GraphRect.Height * Zoom);
        ApplyTangent(index, incoming, e.Key switch
        {
            Key.Left => value with { X = value.X - dx },
            Key.Right => value with { X = value.X + dx },
            Key.Up => value with { Y = value.Y + dy },
            _ => value with { Y = value.Y - dy }
        });
        return true;
    }

    protected bool IsKeyboardTangentEdit => tangentKey.HasValue;
    protected bool IsTangentGesture => mouseTangent.HasValue || tangentKey.HasValue;

    protected void ResetTangentGesture()
    {
        mouseTangent = null;
        tangentKey = null;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (tangentKey != e.Key && !(tangentKey.HasValue && e.Key is Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl)) return;
        CommitCurveGesture();
        e.Handled = true;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (tangentKey.HasValue) CommitCurveGesture();
    }

    protected void OpenTangentMenu(int index)
    {
        if (IsDragging) return;
        var menu = new ContextMenu { PlacementTarget = this };
        if (index >= 0)
        {
            SelectedPointIndex = index;
            var link = new MenuItem
            {
                Header = "Link tangents", IsCheckable = true, IsChecked = !TangentsUnlinked(index),
                IsEnabled = TangentSpan(index, true) > 1e-7 && TangentSpan(index, false) > 1e-7
            };
            AutomationProperties.SetAutomationId(link, "Curve_LinkTangents");
            link.Click += (_, _) =>
            {
                if (SelectedPointIndex != index || index >= PointCount) return;
                SetTangentsUnlinked(index, !link.IsChecked);
                CommitCurveGesture();
            };
            menu.Items.Add(link);
        }
        var all = new MenuItem { Header = "Show all tangents", IsCheckable = true, IsChecked = showAllTangents };
        AutomationProperties.SetAutomationId(all, "Curve_ShowAllTangents");
        all.Click += (_, _) => { showAllTangents = all.IsChecked; InvalidateVisual(); };
        menu.Items.Add(all);
        menu.IsOpen = true;
    }
}
