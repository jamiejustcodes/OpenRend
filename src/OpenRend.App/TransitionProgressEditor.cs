using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

internal sealed class TransitionProgressEditor : StackPanel
{
    private readonly Action<TransitionProgressCurve?> commit;
    private readonly TransitionProgressGraph graph = new();
    private readonly ComboBox points = new() { MinWidth = 124 };
    private readonly ComboBox shape = new() { MinWidth = 82, ItemsSource = Enum.GetValues<EnvelopeCurveType>() };
    private readonly TextBox offset = new() { MinWidth = 92 };
    private readonly TextBox percent = new() { MinWidth = 82 };
    private readonly Button add, apply, remove, reset;
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSalmon };
    private TransitionProgressCurve model = new();
    private long duration = 1;
    private long selected;
    private bool refreshing;

    public TransitionProgressEditor(Action<TransitionProgressCurve?> commit, Action<long> seek)
    {
        this.commit = commit;
        var fields = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        points.SelectedValuePath = nameof(FrameworkElement.Tag);
        AddField(fields, "Point", "TransitionProgressPoint", points);
        AddField(fields, "Offset (frames)", "TransitionProgressOffset", offset);
        AddField(fields, "Progress (%)", "TransitionProgressPercent", percent);
        AddField(fields, "Curve to next point", "TransitionProgressShape", shape);
        points.SelectionChanged += (_, _) =>
        {
            if (!refreshing && points.SelectedValue is long frame) Select(frame);
        };
        offset.KeyDown += ApplyFromKeyboard;
        percent.KeyDown += ApplyFromKeyboard;

        add = CreateButton("Add Point", "AddTransitionProgressPoint", AddPoint);
        apply = CreateButton("Apply Point", "ApplyTransitionProgressPoint", ApplyPoint);
        remove = CreateButton("Remove Point", "RemoveTransitionProgressPoint", RemovePoint);
        reset = CreateButton("Reset Curve", "ResetTransitionProgress", () => Commit(null, 0));
        var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        foreach (var button in new[] { add, apply, remove, reset }) actions.Children.Add(button);
        Children.Add(graph);
        Children.Add(fields);
        Children.Add(actions);
        Children.Add(feedback);

        graph.PointSelected += Select;
        graph.SeekRequested += seek;
        graph.PointAdded += InsertPoint;
        graph.PointMoved += (frame, curve) => Commit(curve, frame);
    }

    public void Bind(long frames, TransitionProgressCurve? source, bool editable)
    {
        duration = Math.Max(1, frames);
        model = source ?? new();
        IsEnabled = editable;
        reset.IsEnabled = source is not null;
        var choices = model.Points.TakeWhile(point => point.Frame < duration)
            .Select(point => new ComboBoxItem { Tag = point.Frame, Content = point.Frame == 0 ? "Start" : $"Frame {point.Frame}" })
            .Append(new ComboBoxItem { Tag = duration, Content = "End" }).ToArray();
        refreshing = true;
        points.ItemsSource = choices;
        refreshing = false;
        add.IsEnabled = choices.LongLength - 1 < duration;
        Select(choices.Any(point => (long)point.Tag == selected) ? selected : 0);
    }

    private void Select(long frame)
    {
        selected = frame;
        refreshing = true;
        points.SelectedValue = frame;
        var key = model.Points.Find(point => point.Frame == frame);
        offset.IsReadOnly = frame == 0 || frame == duration;
        shape.IsEnabled = frame != duration;
        remove.IsEnabled = !offset.IsReadOnly;
        offset.Text = frame.ToString(CultureInfo.CurrentCulture);
        percent.Text = ((frame == duration ? model.EndValue : key?.Value ?? 0) * 100).ToString("0.###", CultureInfo.CurrentCulture);
        shape.SelectedItem = key?.Curve ?? EnvelopeCurveType.Linear;
        refreshing = false;
        graph.Bind(model, duration, frame);
        feedback.Text = "";
    }

    private void AddPoint()
    {
        var visible = model.Points.TakeWhile(point => point.Frame < duration).Select(point => point.Frame).Append(duration).ToArray();
        var gap = visible.Zip(visible.Skip(1)).MaxBy(range => range.Second - range.First);
        if (gap.Second - gap.First < 2) return;
        var frame = gap.First + (gap.Second - gap.First) / 2;
        InsertPoint(frame, model.ValueAt(frame, duration));
    }

    private void InsertPoint(long frame, double value)
    {
        if (frame <= 0 || frame >= duration || model.Points.Exists(point => point.Frame == frame)) return;
        var updated = model.DeepClone();
        var index = updated.Points.FindLastIndex(point => point.Frame < frame);
        updated.Points.Insert(index + 1, new EnvelopePoint(frame, value, updated.Points[index].Curve));
        Commit(updated, frame);
    }

    private void ApplyPoint()
    {
        var culture = CultureInfo.CurrentCulture;
        if (!double.TryParse(percent.Text, NumberStyles.Float, culture, out var value) || !double.IsFinite(value) || value is < 0 or > 100)
        {
            feedback.Text = "Enter a progress value from 0 to 100.";
            percent.Focus();
            return;
        }

        var frame = selected;
        if (!offset.IsReadOnly && (!long.TryParse(offset.Text, NumberStyles.Integer, culture, out frame) || frame <= 0 || frame >= duration ||
            model.Points.Exists(point => point.Frame == frame && frame != selected)))
        {
            feedback.Text = "Choose an unused frame inside the overlap.";
            offset.Focus();
            return;
        }

        var updated = model.DeepClone();
        if (selected == duration) updated.EndValue = value / 100;
        else
        {
            var point = updated.Points.Find(key => key.Frame == selected)!;
            point.Frame = frame;
            point.Value = value / 100;
            point.Curve = (EnvelopeCurveType)shape.SelectedItem;
            updated.Points.Sort((left, right) => left.Frame.CompareTo(right.Frame));
        }
        Commit(updated, frame);
    }

    private void RemovePoint()
    {
        if (selected == 0 || selected == duration) return;
        var updated = model.DeepClone();
        updated.Points.RemoveAll(point => point.Frame == selected);
        Commit(updated, updated.Points.Last(point => point.Frame < selected).Frame);
    }

    private void Commit(TransitionProgressCurve? curve, long frame)
    {
        if (curve is not null && curve.EndValue == model.EndValue && curve.Points.SequenceEqual(model.Points)) { Select(frame); return; }
        var previous = selected;
        selected = frame;
        try { commit(curve); }
        catch (Exception error) when (error is InvalidOperationException or System.IO.InvalidDataException)
        {
            Select(previous);
            feedback.Text = error.Message;
        }
    }

    private void ApplyFromKeyboard(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyPoint();
        e.Handled = true;
    }

    private static void AddField(Panel parent, string label, string id, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 5) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) });
        panel.Children.Add(control);
        AutomationProperties.SetName(control, label);
        AutomationProperties.SetAutomationId(control, id);
        parent.Children.Add(panel);
    }

    private static Button CreateButton(string label, string id, Action pressed)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 3, 8, 3) };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => pressed();
        return button;
    }
}
