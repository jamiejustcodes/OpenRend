using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.UI;

public sealed partial class TimelineView
{
    private const double EventButtonSize = 18;
    private (Guid ClipId, string Action)? pressedEventButton;
    private (Guid ClipId, string Action)? hoverEventButton;
    public bool ShowEventHeaders { get; private set; } = true;
    public bool ShowEventNames { get; private set; } = true;
    public bool ShowEventLengths { get; private set; }

    internal static string EventButtonLabel(string action) => action switch
    {
        "PanCrop" => "Video event Pan/Crop…",
        "VideoEffects" => "Video Event FX…",
        _ => "Event options"
    };

    internal IEnumerable<string> EventButtonActions(TimelineTrack track, TimelineClip clip)
    {
        if (track.Kind == TrackKind.Video && !EventButtonBounds(track.Id, clip, "PanCrop").IsEmpty) yield return "PanCrop";
        if (track.Kind == TrackKind.Video && !EventButtonBounds(track.Id, clip, "VideoEffects").IsEmpty) yield return "VideoEffects";
        if (!EventButtonBounds(track.Id, clip, "More").IsEmpty) yield return "More";
    }

    internal Rect EventButtonBounds(Guid trackId, TimelineClip clip, string action)
    {
        var track = Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null || !track.Clips.Contains(clip) || action is not ("PanCrop" or "VideoEffects" or "More")) return Rect.Empty;
        if (action != "More" && track.Kind != TrackKind.Video) return Rect.Empty;
        var rect = ClipBounds(trackId, clip);
        if (rect.Width < (action == "More" ? 22 : 60)) return Rect.Empty;
        var slot = action switch { "PanCrop" => 3, "VideoEffects" => 2, _ => 1 };
        var bounds = new Rect(rect.Right - 2 - slot * EventButtonSize, rect.Bottom - EventButtonSize - 2, EventButtonSize, EventButtonSize);
        // Hidden edges stay hidden: scrolling never creates a new button at the viewport boundary.
        if (bounds.Left < Math.Max(HeaderWidth, rect.Left) || bounds.Right > ActualWidth || bounds.Top < RulerHeight || bounds.Bottom > ActualHeight) return Rect.Empty;
        return bounds;
    }

    internal (TimelineTrack Track, TimelineClip Clip, string Action)? HitEventButton(Point point)
    {
        var hit = HitClip(point);
        if (hit is null || !ClipBounds(hit.Value.Track.Id, hit.Value.Clip).Contains(point)) return null;
        foreach (var action in EventButtonActions(hit.Value.Track, hit.Value.Clip))
            if (EventButtonBounds(hit.Value.Track.Id, hit.Value.Clip, action).Contains(point)) return (hit.Value.Track, hit.Value.Clip, action);
        return null;
    }

    internal bool BeginEventButton(Point point)
    {
        var hit = HitEventButton(point);
        if (hit is null) return false;
        SelectClip(hit.Value.Clip.Id, false, ensureVisible: false);
        if (hit.Value.Track.IsLocked || !IsEnabled) return true;
        pressedEventButton = (hit.Value.Clip.Id, hit.Value.Action);
        CaptureMouse(); InvalidateVisual(); return true;
    }

    internal void CompleteEventButton(Point point)
    {
        var pressed = pressedEventButton;
        pressedEventButton = null;
        var hit = HitEventButton(point);
        // Dialogs and menus take their own capture, so release ours before invoking them.
        ReleaseMouseCapture(); UpdateHover(point);
        if (pressed is { } button && hit is { } target && target.Clip.Id == button.ClipId && target.Action == button.Action && !target.Track.IsLocked)
            InvokeEventAction(button.ClipId, button.Action);
    }

    internal void InvokeEventAction(Guid clipId, string action)
    {
        var track = Project.Tracks.FirstOrDefault(t => t.Clips.Any(c => c.Id == clipId)) ?? throw new ElementNotAvailableException();
        if (!IsEnabled || track.IsLocked) throw new ElementNotEnabledException();
        if (action != "More" && track.Kind != TrackKind.Video) throw new InvalidOperationException("This event has no video.");
        SelectClip(clipId, false, ensureVisible: false);
        if (action is "PanCrop" or "VideoEffects" or "EditTextMedia")
        {
            if (VideoEventActionRequested is not null) VideoEventActionRequested.Invoke(clipId, action);
            else if (action == "PanCrop") PanCropRequested?.Invoke();
            else if (action == "VideoEffects") VideoEffectsRequested?.Invoke();
        }
        else if (action == "More")
        {
            var menu = CreateEventMenu(clipId);
            menu.PlacementTarget = this; menu.Placement = PlacementMode.Relative;
            var bounds = EventButtonBounds(track.Id, track.Clips.Single(c => c.Id == clipId), "More");
            menu.HorizontalOffset = bounds.IsEmpty ? HeaderWidth : bounds.Left;
            menu.VerticalOffset = bounds.IsEmpty ? RulerHeight : bounds.Bottom;
            menu.IsOpen = true;
        }
        else throw new ArgumentOutOfRangeException(nameof(action));
    }

    internal ContextMenu CreateEventMenu(Guid? clipId = null)
    {
        var menu = new ContextMenu();
        MenuItem Add(string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => action(); menu.Items.Add(item); return item;
        }
        MenuItem? editText = null;
        if (clipId is { } id && Project.Tracks.SelectMany(t => t.Clips).Any(c => c.Id == id && Project.Media.Any(m => m.Id == c.MediaId && m.Path.StartsWith("openrend://text", StringComparison.OrdinalIgnoreCase))))
        {
            editText = Add("Edit Text Media…", () => InvokeEventAction(id, "EditTextMedia"));
        }
        var crop = Add(EventButtonLabel("PanCrop"), () => { if (clipId is { } id) InvokeEventAction(id, "PanCrop"); else PanCropRequested?.Invoke(); });
        var fx = Add(EventButtonLabel("VideoEffects"), () => { if (clipId is { } id) InvokeEventAction(id, "VideoEffects"); else VideoEffectsRequested?.Invoke(); });
        var transition = Add("Transition Properties…", () =>
        {
            var id = clipId ?? Project.Tracks.Where(track => track.Kind == TrackKind.Video)
                .SelectMany(track => track.Clips).Single(clip => Selection.Contains(clip.Id)).Id;
            TransitionPropertiesRequested?.Invoke(id);
        });
        AutomationProperties.SetAutomationId(transition, "TransitionProperties");
        menu.Items.Add(new Separator());
        var split = Add("Split at cursor", () => SplitRequested?.Invoke());
        var delete = Add("Delete selection", () => DeleteRequested?.Invoke());
        menu.Items.Add(new Separator());
        var headers = Add("Show event headers", () => { ShowEventHeaders = !ShowEventHeaders; InvalidateVisual(); });
        var names = Add("Show event names", () => { ShowEventNames = !ShowEventNames; InvalidateVisual(); });
        var lengths = Add("Show event lengths", () => { ShowEventLengths = !ShowEventLengths; InvalidateVisual(); });
        foreach (var toggle in new[] { headers, names, lengths }) toggle.IsCheckable = true;
        menu.Opened += (_, _) =>
        {
            if (clipId is { } id && Project.Tracks.Any(t => t.Clips.Any(c => c.Id == id))) SelectClip(id, false, ensureVisible: false);
            var selected = Project.Tracks.SelectMany(t => t.Clips.Where(c => Selection.Contains(c.Id)).Select(c => (Track: t, Clip: c))).ToArray();
            var video = selected.Where(item => item.Track.Kind == TrackKind.Video && (clipId is null || item.Clip.Id == clipId)).ToArray();
            crop.IsEnabled = fx.IsEnabled = IsEnabled && video.Length == 1 && !video[0].Track.IsLocked;
            transition.Visibility = video.Length == 1 && video[0].Clip.FadeInTransition is not null
                ? Visibility.Visible : Visibility.Collapsed;
            transition.IsEnabled = IsEnabled && video.Length == 1 && !video[0].Track.IsLocked;
            split.IsEnabled = delete.IsEnabled = IsEnabled && selected.Length > 0 && selected.All(item => !item.Track.IsLocked);
            headers.IsChecked = ShowEventHeaders; names.IsChecked = ShowEventNames; lengths.IsChecked = ShowEventLengths;
        };
        return menu;
    }

    private void DrawEventButtons(DrawingContext dc, TimelineTrack track, TimelineClip clip, Rect rect)
    {
        var actions = EventButtonActions(track, clip).ToArray();
        var left = actions.Length == 0 ? rect.Right : actions.Min(action => EventButtonBounds(track.Id, clip, action).Left);
        if (ShowEventLengths && left - rect.Left > 90)
            Text(dc, FormatTimecode(clip.DurationFrames, Project.FrameRate), rect.Left + 3, rect.Bottom - 18, Brushes.WhiteSmoke, 10, left - rect.Left - 6);
        foreach (var action in actions)
        {
            var bounds = EventButtonBounds(track.Id, clip, action);
            var hovered = hoverEventButton == (clip.Id, action);
            var effectsActive = action == "VideoEffects" && clip.VideoEffects.Any(effect => effect.IsEnabled);
            var fill = hovered && !track.IsLocked ? "#59636A" : effectsActive && !track.IsLocked ? "#3D663D" : "#293137";
            dc.DrawRectangle(Brush(fill), new Pen(Brush("#819099"), .5), bounds);
            var ink = track.IsLocked ? Brush("#80858A") : Brushes.WhiteSmoke;
            var pen = new Pen(ink, 1);
            var x = bounds.X; var y = bounds.Y;
            if (action == "PanCrop")
            {
                dc.DrawRectangle(null, pen, new Rect(x + 5, y + 5, 9, 9));
                dc.DrawLine(pen, new Point(x + 2, y + 5), new Point(x + 8, y + 5));
                dc.DrawLine(pen, new Point(x + 5, y + 2), new Point(x + 5, y + 8));
                dc.DrawLine(pen, new Point(x + 11, y + 13), new Point(x + 16, y + 13));
                dc.DrawLine(pen, new Point(x + 13, y + 11), new Point(x + 13, y + 16));
            }
            else if (action == "VideoEffects") Text(dc, "fx", x + 2, y, ink, 13, 16);
            else foreach (var dx in new[] { 5, 9, 13 }) dc.DrawEllipse(ink, null, new Point(x + dx, y + 9), 1, 1);
            if (pressedEventButton == (clip.Id, action)) dc.DrawRectangle(null, new Pen(Brushes.WhiteSmoke, 1), bounds);
        }
    }
}
