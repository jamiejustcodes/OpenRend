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
        "Transition" => "Transition Properties…",
        _ => "Event options"
    };

    internal IEnumerable<string> EventButtonActions(TimelineTrack track, TimelineClip clip)
    {
        if (!EventButtonBounds(track.Id, clip, "Transition").IsEmpty) yield return "Transition";
        if (track.Kind == TrackKind.Video && !EventButtonBounds(track.Id, clip, "PanCrop").IsEmpty) yield return "PanCrop";
        if (track.Kind == TrackKind.Video && !EventButtonBounds(track.Id, clip, "VideoEffects").IsEmpty) yield return "VideoEffects";
        if (!EventButtonBounds(track.Id, clip, "More").IsEmpty) yield return "More";
    }

    internal Rect EventButtonBounds(Guid trackId, TimelineClip clip, string action)
    {
        var track = Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null || !track.Clips.Contains(clip) || action is not ("PanCrop" or "VideoEffects" or "More" or "Transition")) return Rect.Empty;
        if (action != "More" && track.Kind != TrackKind.Video) return Rect.Empty;
        var rect = ClipBounds(trackId, clip);
        if (action == "Transition")
        {
            var strip = TransitionStripBounds(trackId, clip);
            if (strip.IsEmpty || strip.Width < EventButtonSize + 4) return Rect.Empty;
            var button = new Rect(strip.Right - EventButtonSize, strip.Top, EventButtonSize, EventButtonSize);
            return button.Left >= HeaderWidth && button.Right <= ActualWidth && button.Top >= RulerHeight && button.Bottom <= ActualHeight
                ? button : Rect.Empty;
        }
        if (rect.Width < (action == "More" ? 22 : 60)) return Rect.Empty;
        var slot = action switch { "PanCrop" => 3, "VideoEffects" => 2, _ => 1 };
        var bounds = new Rect(rect.Right - 2 - slot * EventButtonSize, rect.Bottom - EventButtonSize - 2, EventButtonSize, EventButtonSize);
        // Hidden edges stay hidden: scrolling never creates a new button at the viewport boundary.
        if (bounds.Left < Math.Max(HeaderWidth, rect.Left) || bounds.Right > ActualWidth || bounds.Top < RulerHeight || bounds.Bottom > ActualHeight) return Rect.Empty;
        return bounds;
    }

    internal (TimelineTrack Track, TimelineClip Clip, string Action)? HitEventButton(Point point)
    {
        var track = TrackAt(point.Y);
        if (track is not null)
            foreach (var clip in track.Clips.AsEnumerable().Reverse())
                if (EventButtonBounds(track.Id, clip, "Transition") is { IsEmpty: false } button && button.Contains(point))
                    return (track, clip, "Transition");
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
        var targetClip = track.Clips.Single(c => c.Id == clipId);
        if (action != "More" && targetClip.IsLocked) throw new ElementNotEnabledException();
        if (action != "More" && track.Kind != TrackKind.Video) throw new InvalidOperationException("This event has no video.");
        SelectClip(clipId, false, ensureVisible: false);
        if (action == "Transition")
        {
            if (track.Clips.Single(clip => clip.Id == clipId).FadeInTransition is null) throw new ElementNotAvailableException();
            TransitionPropertiesRequested?.Invoke(clipId);
            return;
        }
        if (action is "PanCrop" or "VideoEffects" or "EditTextMedia" or "ColorGrading" or "MotionTracking" or "TextBasedEditing")
        {
            if (VideoEventActionRequested is not null) VideoEventActionRequested.Invoke(clipId, action);
            else if (action == "PanCrop") PanCropRequested?.Invoke();
            else if (action == "VideoEffects") VideoEffectsRequested?.Invoke();
            else if (action == "ColorGrading") ColorGradingRequested?.Invoke();
            else if (action == "MotionTracking") MotionTrackingRequested?.Invoke(clipId);
            else if (action == "TextBasedEditing") TextBasedEditingRequested?.Invoke(clipId);
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
        var grading = Add("Color Grading…", () => { if (clipId is { } id) InvokeEventAction(id, "ColorGrading"); else ColorGradingRequested?.Invoke(); });
        AutomationProperties.SetAutomationId(grading, "ColorGradingEventMenu");
        var tracking = Add("Motion Tracking…", () => { if (clipId is { } id) InvokeEventAction(id, "MotionTracking"); else MotionTrackingRequested?.Invoke(clipId ?? Guid.Empty); });
        AutomationProperties.SetAutomationId(tracking, "MotionTrackingEventMenu");
        var tbe = Add("Text-Based Editing…", () => { if (clipId is { } id) InvokeEventAction(id, "TextBasedEditing"); else TextBasedEditingRequested?.Invoke(clipId ?? Guid.Empty); });
        AutomationProperties.SetAutomationId(tbe, "TextBasedEditingEventMenu");
        var transition = Add("Transition Properties…", () =>
        {
            var id = clipId ?? Project.Tracks.Where(track => track.Kind == TrackKind.Video)
                .SelectMany(track => track.Clips).Single(clip => Selection.Contains(clip.Id)).Id;
            TransitionPropertiesRequested?.Invoke(id);
        });
        AutomationProperties.SetAutomationId(transition, "TransitionProperties");
        var switches = new MenuItem { Header = "Switches" };
        AutomationProperties.SetAutomationId(switches, "EventSwitchesMenu");
        var loop = new MenuItem { Header = "Loop", IsCheckable = true };
        AutomationProperties.SetAutomationId(loop, "VideoEventLoop");
        switches.Items.Add(loop);
        var muteItem = new MenuItem { Header = "Mute", IsCheckable = true };
        AutomationProperties.SetAutomationId(muteItem, "EventMute");
        switches.Items.Add(muteItem);
        var lockItem = new MenuItem { Header = "Lock", IsCheckable = true };
        AutomationProperties.SetAutomationId(lockItem, "EventLock");
        switches.Items.Add(lockItem);
        menu.Items.Add(switches);

        lockItem.Click += (_, _) =>
        {
            var events = Project.Tracks.Where(track => !track.IsLocked)
                .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
            if (IsEnabled && events.Length == 1)
                ClipLockRequested?.Invoke(events[0].Id, lockItem.IsChecked);
        };
        muteItem.Click += (_, _) =>
        {
            var events = Project.Tracks.Where(track => !track.IsLocked)
                .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
            if (IsEnabled && events.Length == 1)
                ClipMuteRequested?.Invoke(events[0].Id, muteItem.IsChecked);
        };
        loop.Click += (_, _) =>
        {
            var events = Project.Tracks.Where(track => track.Kind == TrackKind.Video && !track.IsLocked)
                .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
            if (IsEnabled && events.Length == 1)
                VideoSourceModeRequested?.Invoke(events[0].Id, loop.IsChecked ? VideoSourceMode.Loop : VideoSourceMode.HoldLastFrame);
        };
        var fadeInCurves = new MenuItem { Header = "Fade In Curve" };
        AutomationProperties.SetAutomationId(fadeInCurves, "FadeInCurveMenu");
        var fadeOutCurves = new MenuItem { Header = "Fade Out Curve" };
        AutomationProperties.SetAutomationId(fadeOutCurves, "FadeOutCurveMenu");
        foreach (var curve in FadeCurveOrder)
        {
            var inItem = new MenuItem { Header = curve.ToString(), IsCheckable = true };
            AutomationProperties.SetAutomationId(inItem, $"FadeInCurve_{curve}");
            var inCurve = curve;
            inItem.Click += (_, _) =>
            {
                var events = Project.Tracks.Where(track => track.Kind == TrackKind.Video && !track.IsLocked)
                    .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
                if (IsEnabled && events.Length == 1)
                    VideoFadeCurveRequested?.Invoke(events[0].Id, inCurve, null, false);
            };
            fadeInCurves.Items.Add(inItem);

            var outItem = new MenuItem { Header = curve.ToString(), IsCheckable = true };
            AutomationProperties.SetAutomationId(outItem, $"FadeOutCurve_{curve}");
            var outCurve = curve;
            outItem.Click += (_, _) =>
            {
                var events = Project.Tracks.Where(track => track.Kind == TrackKind.Video && !track.IsLocked)
                    .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
                if (IsEnabled && events.Length == 1)
                    VideoFadeCurveRequested?.Invoke(events[0].Id, outCurve, null, true);
            };
            fadeOutCurves.Items.Add(outItem);
        }

        var fadeType = new MenuItem { Header = "Fade Type" };
        AutomationProperties.SetAutomationId(fadeType, "FadeTypeMenu");
        var fadeTypeGrid = new UniformGrid { Rows = 5, Columns = 5, Margin = new Thickness(4) };
        var fadeTypeButtons = new (Button Button, VideoFadeCurve InCurve, VideoFadeCurve OutRecipCurve)[25];
        var cellIndex = 0;
        for (var r = 0; r < 5; r++)
        {
            var outRecip = FadeCurveOrder[r];
            for (var c = 0; c < 5; c++)
            {
                var inCurve = FadeCurveOrder[c];
                var btn = new Button
                {
                    Width = 32,
                    Height = 28,
                    Margin = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromRgb(35, 35, 35)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                    BorderThickness = new Thickness(1),
                    ToolTip = $"In: {inCurve}, Out: {outRecip}",
                    Content = CreateFadeThumbnail(inCurve, outRecip)
                };
                AutomationProperties.SetAutomationId(btn, $"FadeType_{outRecip}_{inCurve}");
                AutomationProperties.SetName(btn, $"Fade Type In {inCurve} Out {outRecip}");
                btn.Click += (_, _) =>
                {
                    menu.IsOpen = false;
                    var events = Project.Tracks.Where(track => track.Kind == TrackKind.Video && !track.IsLocked)
                        .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
                    if (IsEnabled && events.Length == 1)
                        VideoFadeCurveRequested?.Invoke(events[0].Id, inCurve, outRecip, false);
                };
                fadeTypeButtons[cellIndex++] = (btn, inCurve, outRecip);
                fadeTypeGrid.Children.Add(btn);
            }
        }
        fadeType.Items.Add(fadeTypeGrid);

        menu.Items.Add(fadeInCurves);
        menu.Items.Add(fadeOutCurves);
        menu.Items.Add(fadeType);
        menu.Items.Add(new Separator());
        var groupMenu = new MenuItem { Header = "Group" };
        var unlink = new MenuItem { Header = "Unlink clip group", InputGestureText = "U" };
        unlink.Click += (_, _) => UnlinkRequested?.Invoke();
        var link = new MenuItem { Header = "Link selected clips", InputGestureText = "G" };
        link.Click += (_, _) => LinkRequested?.Invoke();
        groupMenu.Items.Add(unlink);
        groupMenu.Items.Add(link);
        menu.Items.Add(groupMenu);
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
            var targetClips = selected.Where(item => clipId is null || item.Clip.Id == clipId).ToArray();
            var singleTarget = targetClips.Length == 1 ? targetClips[0] : (Track: (TimelineTrack?)null, Clip: (TimelineClip?)null);
            var isLocked = singleTarget.Clip?.IsLocked ?? false;
            var video = selected.Where(item => item.Track.Kind == TrackKind.Video && (clipId is null || item.Clip.Id == clipId)).ToArray();
            crop.IsEnabled = fx.IsEnabled = grading.IsEnabled = tracking.IsEnabled = IsEnabled && video.Length == 1 && !video[0].Track.IsLocked && !isLocked;
            transition.Visibility = video.Length == 1 && video[0].Clip.FadeInTransition is not null
                ? Visibility.Visible : Visibility.Collapsed;
            transition.IsEnabled = IsEnabled && video.Length == 1 && !video[0].Track.IsLocked && !isLocked;
            var source = video.Length == 1 ? Project.Media.SingleOrDefault(media => media.Id == video[0].Clip.MediaId) : null;
            switches.Visibility = source is { HasVideo: true, DurationSeconds: > 0 } && !source.Path.StartsWith("openrend://", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
            switches.IsEnabled = IsEnabled && video.Length == 1 && !video[0].Track.IsLocked;
            loop.IsEnabled = switches.IsEnabled && !isLocked;
            loop.IsChecked = video.Length == 1 && video[0].Clip.VideoSourceMode == VideoSourceMode.Loop;
            muteItem.IsEnabled = IsEnabled && video.Length == 1 && !video[0].Track.IsLocked && !isLocked;
            muteItem.IsChecked = singleTarget.Clip?.IsMuted ?? false;
            lockItem.IsEnabled = IsEnabled && video.Length == 1 && !video[0].Track.IsLocked;
            lockItem.IsChecked = isLocked;
            var hasFadeIn = video.Length == 1 && video[0].Clip.FadeInFrames > 0;
            var hasFadeOut = video.Length == 1 && video[0].Clip.FadeOutFrames > 0;
            fadeInCurves.Visibility = hasFadeIn ? Visibility.Visible : Visibility.Collapsed;
            fadeInCurves.IsEnabled = IsEnabled && video.Length == 1 && !video[0].Track.IsLocked && !isLocked;
            fadeOutCurves.Visibility = hasFadeOut ? Visibility.Visible : Visibility.Collapsed;
            fadeOutCurves.IsEnabled = IsEnabled && video.Length == 1 && !video[0].Track.IsLocked && !isLocked;
            fadeType.Visibility = hasFadeIn ? Visibility.Visible : Visibility.Collapsed;
            fadeType.IsEnabled = IsEnabled && video.Length == 1 && !video[0].Track.IsLocked && !isLocked;
            if (video.Length == 1)
            {
                foreach (MenuItem item in fadeInCurves.Items)
                    item.IsChecked = item.Header.ToString() == video[0].Clip.FadeInCurve.ToString();
                foreach (MenuItem item in fadeOutCurves.Items)
                    item.IsChecked = item.Header.ToString() == video[0].Clip.FadeOutCurve.ToString();
                var currentIn = video[0].Clip.FadeInCurve;
                var currentRecip = video[0].Clip.FadeInReciprocalCurve;
                var isPlain = video[0].Clip.FadeInTransition is null;
                foreach (var (btn, inC, outR) in fadeTypeButtons)
                {
                    var isCurrent = isPlain && inC == currentIn && outR == currentRecip;
                    btn.BorderBrush = isCurrent ? new SolidColorBrush(Color.FromRgb(200, 144, 0)) : new SolidColorBrush(Color.FromRgb(70, 70, 70));
                    btn.BorderThickness = isCurrent ? new Thickness(2) : new Thickness(1);
                }
            }
            split.IsEnabled = delete.IsEnabled = IsEnabled && selected.Length > 0 && selected.All(item => !item.Track.IsLocked && !item.Clip.IsLocked);
            headers.IsChecked = ShowEventHeaders; names.IsChecked = ShowEventNames; lengths.IsChecked = ShowEventLengths;
        };
        return menu;
    }

    private void DrawEventButtons(DrawingContext dc, TimelineTrack track, TimelineClip clip, Rect rect)
    {
        DrawTransitionStrip(dc, track, clip);
        var actions = EventButtonActions(track, clip).Where(action => action != "Transition").ToArray();
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

    internal Rect TransitionStripBounds(Guid trackId, TimelineClip clip)
    {
        if (clip.FadeInTransition is null || clip.FadeInFrames <= 0) return Rect.Empty;
        var track = Project.Tracks.FirstOrDefault(track => track.Id == trackId);
        if (track?.Kind != TrackKind.Video || !track.Clips.Contains(clip)) return Rect.Empty;
        var remaining = clip.FadeInFrames - clip.FadeOffsetFrames;
        if (remaining <= 0) return Rect.Empty;
        var rect = ClipBounds(trackId, clip);
        var right = Math.Min(rect.Right, X(checked(clip.StartFrame + Math.Min(remaining, clip.DurationFrames))));
        return right > rect.Left ? new Rect(rect.Left, rect.Top + (rect.Height - EventButtonSize) / 2, right - rect.Left, EventButtonSize) : Rect.Empty;
    }

    private void DrawTransitionStrip(DrawingContext dc, TimelineTrack track, TimelineClip clip)
    {
        var strip = TransitionStripBounds(track.Id, clip);
        if (strip.IsEmpty) return;
        dc.DrawRectangle(Brush(track.IsLocked ? "#485B69" : "#438AC8"), new Pen(Brush("#84BBE4"), .5), strip);
        var button = EventButtonBounds(track.Id, clip, "Transition");
        var labelWidth = strip.Width - (button.IsEmpty ? 0 : EventButtonSize);
        if (labelWidth > 24) Text(dc, "Dissolve", strip.Left + 2, strip.Top + 1, Brushes.WhiteSmoke, 10, labelWidth - 4, TextAlignment.Center);
        if (button.IsEmpty) return;
        var hovered = hoverEventButton == (clip.Id, "Transition");
        dc.DrawRectangle(Brush(hovered && !track.IsLocked ? "#C2DDF1" : "#95BBD8"), new Pen(Brush("#1D3F59"), .5), button);
        var pen = new Pen(Brush("#183448"), 1);
        var x = button.Left + 3; var y = button.Top + 4;
        dc.DrawLine(pen, new Point(x, y), new Point(x + 12, y + 10));
        dc.DrawLine(pen, new Point(x, y + 10), new Point(x + 12, y));
        dc.DrawLine(pen, new Point(x, y), new Point(x, y + 10));
        dc.DrawLine(pen, new Point(x + 12, y), new Point(x + 12, y + 10));
    }

    private static readonly VideoFadeCurve[] FadeCurveOrder =
    [
        VideoFadeCurve.Linear,
        VideoFadeCurve.Fast,
        VideoFadeCurve.Slow,
        VideoFadeCurve.Smooth,
        VideoFadeCurve.Sharp
    ];

    private static Image CreateFadeThumbnail(VideoFadeCurve inCurve, VideoFadeCurve outRecip)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(25, 25, 25)),
            null,
            new RectangleGeometry(new Rect(0, 0, 28, 22))));

        var outGeo = new StreamGeometry();
        using (var ctx = outGeo.Open())
        {
            ctx.BeginFigure(new Point(3, 3), false, false);
            for (var i = 1; i <= 10; i++)
            {
                var t = i / 10.0;
                var x = 3 + 22 * t;
                var y = 3 + 16 * VideoFadeInterpolation.Evaluate(outRecip, t);
                ctx.LineTo(new Point(x, y), true, false);
            }
        }
        outGeo.Freeze();
        group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromRgb(74, 144, 226)), 1.5), outGeo));

        var inGeo = new StreamGeometry();
        using (var ctx = inGeo.Open())
        {
            ctx.BeginFigure(new Point(3, 19), false, false);
            for (var i = 1; i <= 10; i++)
            {
                var t = i / 10.0;
                var x = 3 + 22 * t;
                var y = 19 - 16 * VideoFadeInterpolation.Evaluate(inCurve, t);
                ctx.LineTo(new Point(x, y), true, false);
            }
        }
        inGeo.Freeze();
        group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromRgb(0, 210, 255)), 1.5), inGeo));
        group.Freeze();

        return new Image { Source = new DrawingImage(group), Width = 28, Height = 22, IsHitTestVisible = false };
    }
}
