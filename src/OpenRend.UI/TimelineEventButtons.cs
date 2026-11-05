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
        var aspectItem = new MenuItem { Header = "Maintain Aspect Ratio", IsCheckable = true };
        AutomationProperties.SetAutomationId(aspectItem, "EventMaintainAspectRatio");
        switches.Items.Add(aspectItem);
        var interlaceItem = new MenuItem { Header = "Reduce Interlace Flicker", IsCheckable = true };
        AutomationProperties.SetAutomationId(interlaceItem, "EventReduceInterlace");
        switches.Items.Add(interlaceItem);
        var normalizeItem = new MenuItem { Header = "Normalize", IsCheckable = true };
        AutomationProperties.SetAutomationId(normalizeItem, "EventNormalize");
        switches.Items.Add(normalizeItem);
        var invertPhaseItem = new MenuItem { Header = "Invert Phase", IsCheckable = true };
        AutomationProperties.SetAutomationId(invertPhaseItem, "EventInvertPhase");
        switches.Items.Add(invertPhaseItem);
        menu.Items.Add(switches);

        var reverseItem = new MenuItem { Header = "Reverse", IsCheckable = true };
        AutomationProperties.SetAutomationId(reverseItem, "EventReverse");
        menu.Items.Add(reverseItem);

        var playbackRateMenu = new MenuItem { Header = "Playback Rate" };
        AutomationProperties.SetAutomationId(playbackRateMenu, "EventPlaybackRateMenu");
        var rates = new (string Label, double Rate)[]
        {
            ("0.25x (Quarter Speed)", 0.25),
            ("0.5x (Half Speed)", 0.5),
            ("0.75x", 0.75),
            ("1.0x (Normal Speed)", 1.0),
            ("1.25x", 1.25),
            ("1.5x", 1.5),
            ("2.0x (Double Speed)", 2.0),
            ("3.0x (Triple Speed)", 3.0),
            ("4.0x (Quad Speed)", 4.0)
        };
        foreach (var (label, rate) in rates)
        {
            var item = new MenuItem { Header = label, IsCheckable = true };
            var r = rate;
            item.Click += (_, _) =>
            {
                var events = Project.Tracks.Where(track => !track.IsLocked)
                    .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
                if (IsEnabled && events.Length == 1)
                    ClipPlaybackRateRequested?.Invoke(events[0].Id, r);
            };
            playbackRateMenu.Items.Add(item);
        }
        menu.Items.Add(playbackRateMenu);

        var resampleMenu = new MenuItem { Header = "Resample Mode" };
        AutomationProperties.SetAutomationId(resampleMenu, "EventResampleModeMenu");
        var modes = new (string Label, VideoResampleMode Mode)[]
        {
            ("Smart Resample", VideoResampleMode.Smart),
            ("Force Resample", VideoResampleMode.Force),
            ("Disable Resample", VideoResampleMode.Disable)
        };
        foreach (var (label, mode) in modes)
        {
            var item = new MenuItem { Header = label, IsCheckable = true };
            var m = mode;
            item.Click += (_, _) =>
            {
                var events = Project.Tracks.Where(track => track.Kind == TrackKind.Video && !track.IsLocked)
                    .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
                if (IsEnabled && events.Length == 1)
                    ClipResampleModeRequested?.Invoke(events[0].Id, m);
            };
            resampleMenu.Items.Add(item);
        }
        menu.Items.Add(resampleMenu);

        var undersampleMenu = new MenuItem { Header = "Undersample Rate" };
        AutomationProperties.SetAutomationId(undersampleMenu, "EventUndersampleMenu");
        var underRates = new (string Label, double Rate)[]
        {
            ("1.000 (Full Rate)", 1.0),
            ("0.500 (Half Rate)", 0.5),
            ("0.250 (Quarter Rate)", 0.25),
            ("0.100 (Stop-Motion / Strobe)", 0.1)
        };
        foreach (var (label, rate) in underRates)
        {
            var item = new MenuItem { Header = label, IsCheckable = true };
            var ur = rate;
            item.Click += (_, _) =>
            {
                var events = Project.Tracks.Where(track => track.Kind == TrackKind.Video && !track.IsLocked)
                    .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
                if (IsEnabled && events.Length == 1)
                    ClipUnderSampleRateRequested?.Invoke(events[0].Id, ur);
            };
            undersampleMenu.Items.Add(item);
        }
        menu.Items.Add(undersampleMenu);

        var channelsMenu = new MenuItem { Header = "Channels" };
        AutomationProperties.SetAutomationId(channelsMenu, "EventChannelsMenu");
        var channelOptions = new (string Label, AudioChannelMode Mode)[]
        {
            ("Both", AudioChannelMode.Both),
            ("Left Only", AudioChannelMode.LeftOnly),
            ("Right Only", AudioChannelMode.RightOnly),
            ("Swap", AudioChannelMode.Swap)
        };
        foreach (var (label, chMode) in channelOptions)
        {
            var item = new MenuItem { Header = label, IsCheckable = true };
            var cm = chMode;
            item.Click += (_, _) =>
            {
                var events = Project.Tracks.Where(track => track.Kind == TrackKind.Audio && !track.IsLocked)
                    .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
                if (IsEnabled && events.Length == 1)
                    ClipChannelModeRequested?.Invoke(events[0].Id, cm);
            };
            channelsMenu.Items.Add(item);
        }
        menu.Items.Add(channelsMenu);

        var pitchMenu = new MenuItem { Header = "Pitch Shift" };
        AutomationProperties.SetAutomationId(pitchMenu, "EventPitchShiftMenu");
        var pitchOptions = new (string Label, double Semis)[]
        {
            ("+12 Semitones (Octave Up)", 12.0),
            ("+3 Semitones", 3.0),
            ("+2 Semitones", 2.0),
            ("+1 Semitone (+)", 1.0),
            ("0 (Original Pitch)", 0.0),
            ("-1 Semitone (-)", -1.0),
            ("-2 Semitones", -2.0),
            ("-3 Semitones", -3.0),
            ("-12 Semitones (Octave Down)", -12.0)
        };
        foreach (var (label, semis) in pitchOptions)
        {
            var item = new MenuItem { Header = label, IsCheckable = true };
            var ps = semis;
            item.Click += (_, _) =>
            {
                var events = Project.Tracks.Where(track => track.Kind == TrackKind.Audio && !track.IsLocked)
                    .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
                if (IsEnabled && events.Length == 1)
                    ClipPitchSemisRequested?.Invoke(events[0].Id, ps);
            };
            pitchMenu.Items.Add(item);
        }
        menu.Items.Add(pitchMenu);

        var takesMenu = new MenuItem { Header = "Take" };
        AutomationProperties.SetAutomationId(takesMenu, "EventTakesMenu");
        var nextTake = new MenuItem { Header = "Next Take", InputGestureText = "T" };
        AutomationProperties.SetAutomationId(nextTake, "NextTakeMenuItem");
        var prevTake = new MenuItem { Header = "Previous Take", InputGestureText = "Shift+T" };
        AutomationProperties.SetAutomationId(prevTake, "PreviousTakeMenuItem");
        var deleteTake = new MenuItem { Header = "Delete Active Take" };
        AutomationProperties.SetAutomationId(deleteTake, "DeleteTakeMenuItem");

        nextTake.Click += (_, _) =>
        {
            var events = Project.Tracks.Where(track => !track.IsLocked)
                .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
            if (IsEnabled && events.Length == 1)
                NextTakeRequested?.Invoke(events[0].Id);
        };
        prevTake.Click += (_, _) =>
        {
            var events = Project.Tracks.Where(track => !track.IsLocked)
                .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
            if (IsEnabled && events.Length == 1)
                PreviousTakeRequested?.Invoke(events[0].Id);
        };
        deleteTake.Click += (_, _) =>
        {
            var events = Project.Tracks.Where(track => !track.IsLocked)
                .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
            if (IsEnabled && events.Length == 1)
                DeleteTakeRequested?.Invoke(events[0].Id);
        };

        takesMenu.Items.Add(nextTake);
        takesMenu.Items.Add(prevTake);
        takesMenu.Items.Add(deleteTake);
        menu.Items.Add(takesMenu);

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
        reverseItem.Click += (_, _) =>
        {
            var events = Project.Tracks.Where(track => !track.IsLocked)
                .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
            if (IsEnabled && events.Length == 1)
                ClipReverseRequested?.Invoke(events[0].Id, reverseItem.IsChecked);
        };
        aspectItem.Click += (_, _) =>
        {
            var events = Project.Tracks.Where(track => track.Kind == TrackKind.Video && !track.IsLocked)
                .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
            if (IsEnabled && events.Length == 1)
                ClipMaintainAspectRatioRequested?.Invoke(events[0].Id, aspectItem.IsChecked);
        };
        interlaceItem.Click += (_, _) =>
        {
            var events = Project.Tracks.Where(track => track.Kind == TrackKind.Video && !track.IsLocked)
                .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
            if (IsEnabled && events.Length == 1)
                ClipReduceInterlaceRequested?.Invoke(events[0].Id, interlaceItem.IsChecked);
        };
        normalizeItem.Click += (_, _) =>
        {
            var events = Project.Tracks.Where(track => track.Kind == TrackKind.Audio && !track.IsLocked)
                .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
            if (IsEnabled && events.Length == 1)
                ClipNormalizeRequested?.Invoke(events[0].Id, normalizeItem.IsChecked);
        };
        invertPhaseItem.Click += (_, _) =>
        {
            var events = Project.Tracks.Where(track => track.Kind == TrackKind.Audio && !track.IsLocked)
                .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
            if (IsEnabled && events.Length == 1)
                ClipInvertPhaseRequested?.Invoke(events[0].Id, invertPhaseItem.IsChecked);
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

        var envelopeMenu = new MenuItem { Header = "Insert/Remove Envelope" };
        AutomationProperties.SetAutomationId(envelopeMenu, "InsertRemoveEnvelopeMenu");
        var velocityItem = new MenuItem { Header = "Velocity", IsCheckable = true };
        AutomationProperties.SetAutomationId(velocityItem, "EnvelopeVelocity");
        var compositeItem = new MenuItem { Header = "Composite Level", IsCheckable = true };
        AutomationProperties.SetAutomationId(compositeItem, "EnvelopeComposite");
        envelopeMenu.Items.Add(velocityItem);
        envelopeMenu.Items.Add(compositeItem);
        menu.Items.Add(envelopeMenu);

        velocityItem.Click += (_, _) =>
        {
            var events = Project.Tracks.Where(track => track.Kind == TrackKind.Video && !track.IsLocked)
                .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
            if (IsEnabled && events.Length == 1)
                ToggleEnvelopeRequested?.Invoke(events[0].Id, EnvelopeType.Velocity);
        };
        compositeItem.Click += (_, _) =>
        {
            var events = Project.Tracks.Where(track => track.Kind == TrackKind.Video && !track.IsLocked)
                .SelectMany(track => track.Clips.Where(clip => Selection.Contains(clip.Id) && (clipId is null || clip.Id == clipId))).ToArray();
            if (IsEnabled && events.Length == 1)
                ToggleEnvelopeRequested?.Invoke(events[0].Id, EnvelopeType.Composite);
        };
        menu.Items.Add(new Separator());
        var groupMenu = new MenuItem { Header = "Group" };
        var link = new MenuItem { Header = "Create New", InputGestureText = "G" };
        link.Click += (_, _) => LinkRequested?.Invoke();
        var unlink = new MenuItem { Header = "Remove From", InputGestureText = "U" };
        unlink.Click += (_, _) => UnlinkRequested?.Invoke();
        var clear = new MenuItem { Header = "Clear" };
        clear.Click += (_, _) => ClearGroupRequested?.Invoke();
        var selectAllInGroup = new MenuItem { Header = "Select All in Group" };
        selectAllInGroup.Click += (_, _) => SelectAllInGroupRequested?.Invoke(clipId ?? Guid.Empty);
        groupMenu.Items.Add(link);
        groupMenu.Items.Add(unlink);
        groupMenu.Items.Add(clear);
        groupMenu.Items.Add(selectAllInGroup);
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
            muteItem.IsEnabled = IsEnabled && singleTarget.Clip != null && !singleTarget.Track!.IsLocked && !isLocked;
            muteItem.IsChecked = singleTarget.Clip?.IsMuted ?? false;
            lockItem.IsEnabled = IsEnabled && singleTarget.Clip != null && !singleTarget.Track!.IsLocked;
            lockItem.IsChecked = isLocked;
            reverseItem.IsEnabled = IsEnabled && singleTarget.Clip != null && !singleTarget.Track!.IsLocked && !isLocked;
            reverseItem.IsChecked = singleTarget.Clip?.IsReversed ?? false;
            playbackRateMenu.IsEnabled = IsEnabled && singleTarget.Clip != null && !singleTarget.Track!.IsLocked && !isLocked;
            foreach (MenuItem item in playbackRateMenu.Items)
            {
                if (item.Header is string h && double.TryParse(h.Split('x')[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var rate))
                    item.IsChecked = singleTarget.Clip != null && Math.Abs(singleTarget.Clip.PlaybackRate - rate) < 0.01;
            }
            resampleMenu.IsEnabled = IsEnabled && video.Length == 1 && !isLocked;
            resampleMenu.Visibility = singleTarget.Track?.Kind == TrackKind.Video ? Visibility.Visible : Visibility.Collapsed;
            foreach (MenuItem item in resampleMenu.Items)
            {
                if (item.Header is string h && Enum.TryParse<VideoResampleMode>(h.Replace(" Resample", ""), out var mode))
                    item.IsChecked = singleTarget.Clip != null && singleTarget.Clip.ResampleMode == mode;
            }
            undersampleMenu.IsEnabled = IsEnabled && video.Length == 1 && !isLocked;
            undersampleMenu.Visibility = singleTarget.Track?.Kind == TrackKind.Video ? Visibility.Visible : Visibility.Collapsed;
            foreach (MenuItem item in undersampleMenu.Items)
            {
                if (item.Header is string h && double.TryParse(h.Split(' ')[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var rate))
                    item.IsChecked = singleTarget.Clip != null && Math.Abs(singleTarget.Clip.UnderSampleRate - rate) < 0.01;
            }
            aspectItem.IsEnabled = IsEnabled && video.Length == 1 && !isLocked;
            aspectItem.IsChecked = singleTarget.Clip?.MaintainAspectRatio ?? true;
            aspectItem.Visibility = singleTarget.Track?.Kind == TrackKind.Video ? Visibility.Visible : Visibility.Collapsed;
            interlaceItem.IsEnabled = IsEnabled && video.Length == 1 && !isLocked;
            interlaceItem.IsChecked = singleTarget.Clip?.ReduceInterlace ?? false;
            interlaceItem.Visibility = singleTarget.Track?.Kind == TrackKind.Video ? Visibility.Visible : Visibility.Collapsed;
            normalizeItem.IsEnabled = IsEnabled && singleTarget.Track?.Kind == TrackKind.Audio && !isLocked;
            normalizeItem.IsChecked = singleTarget.Clip?.Normalize ?? false;
            normalizeItem.Visibility = singleTarget.Track?.Kind == TrackKind.Audio ? Visibility.Visible : Visibility.Collapsed;
            invertPhaseItem.IsEnabled = IsEnabled && singleTarget.Track?.Kind == TrackKind.Audio && !isLocked;
            invertPhaseItem.IsChecked = singleTarget.Clip?.InvertPhase ?? false;
            invertPhaseItem.Visibility = singleTarget.Track?.Kind == TrackKind.Audio ? Visibility.Visible : Visibility.Collapsed;
            channelsMenu.IsEnabled = IsEnabled && singleTarget.Track?.Kind == TrackKind.Audio && !isLocked;
            channelsMenu.Visibility = singleTarget.Track?.Kind == TrackKind.Audio ? Visibility.Visible : Visibility.Collapsed;
            foreach (MenuItem item in channelsMenu.Items)
            {
                if (item.Header is string h)
                {
                    var clean = h.Replace(" ", "");
                    if (Enum.TryParse<AudioChannelMode>(clean, out var cm))
                        item.IsChecked = singleTarget.Clip != null && singleTarget.Clip.ChannelMode == cm;
                }
            }
            pitchMenu.IsEnabled = IsEnabled && singleTarget.Track?.Kind == TrackKind.Audio && !isLocked;
            pitchMenu.Visibility = singleTarget.Track?.Kind == TrackKind.Audio ? Visibility.Visible : Visibility.Collapsed;
            foreach (MenuItem item in pitchMenu.Items)
            {
                if (item.Header is string h)
                {
                    if (h.StartsWith("0"))
                        item.IsChecked = singleTarget.Clip != null && Math.Abs(singleTarget.Clip.PitchSemis) < 0.001;
                    else if (double.TryParse(h.Split(' ')[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ps))
                        item.IsChecked = singleTarget.Clip != null && Math.Abs(singleTarget.Clip.PitchSemis - ps) < 0.001;
                }
            }
            var singleClip = singleTarget.Clip;
            var hasTakes = singleClip?.Takes is { Count: > 1 };
            takesMenu.Visibility = hasTakes ? Visibility.Visible : Visibility.Collapsed;
            takesMenu.IsEnabled = IsEnabled && hasTakes && !singleTarget.Track!.IsLocked && !isLocked;
            nextTake.IsEnabled = hasTakes;
            prevTake.IsEnabled = hasTakes;
            deleteTake.IsEnabled = hasTakes;
            while (takesMenu.Items.Count > 3) takesMenu.Items.RemoveAt(takesMenu.Items.Count - 1);
            if (hasTakes && singleClip?.Takes is { } tList)
            {
                takesMenu.Items.Add(new Separator());
                for (int tIdx = 0; tIdx < tList.Count; tIdx++)
                {
                    var takeItemIdx = tIdx;
                    var take = tList[tIdx];
                    var takeMenuItem = new MenuItem
                    {
                        Header = $"Take {takeItemIdx + 1}: {take.Name}",
                        IsCheckable = true,
                        IsChecked = takeItemIdx == singleClip.ActiveTakeIndex
                    };
                    takeMenuItem.Click += (_, _) =>
                    {
                        if (IsEnabled && !singleTarget.Track!.IsLocked && !isLocked)
                            SelectTakeRequested?.Invoke(singleClip.Id, takeItemIdx);
                    };
                    takesMenu.Items.Add(takeMenuItem);
                }
            }
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
            envelopeMenu.Visibility = video.Length == 1 ? Visibility.Visible : Visibility.Collapsed;
            envelopeMenu.IsEnabled = IsEnabled && video.Length == 1 && !video[0].Track.IsLocked && !isLocked;
            if (video.Length == 1)
            {
                velocityItem.IsChecked = video[0].Clip.Envelopes?.Any(e => e.Type == EnvelopeType.Velocity) == true;
                compositeItem.IsChecked = video[0].Clip.Envelopes?.Any(e => e.Type == EnvelopeType.Composite) == true;
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
