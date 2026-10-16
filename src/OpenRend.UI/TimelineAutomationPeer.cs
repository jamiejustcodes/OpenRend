using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using OpenRend.Core;

namespace OpenRend.UI;

public sealed class TimelineAutomationPeer(TimelineView owner) : FrameworkElementAutomationPeer(owner), ISelectionProvider
{
    private readonly Dictionary<Guid, TimelineTrackAutomationPeer> tracks = [];
    internal TimelineView View => (TimelineView)Owner;
    public bool CanSelectMultiple => true;
    public bool IsSelectionRequired => false;
    protected override string GetClassNameCore() => "OpenRendTimeline";
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;
    protected override string GetItemStatusCore() => View.Project.Tracks.Count == 0 ? "No tracks" : $"Active track: {View.Project.Tracks[View.ActiveTrackIndex].Name}";
    public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Selection ? this : base.GetPattern(patternInterface);

    protected override List<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>();
        foreach (var track in View.Project.Tracks)
        {
            if (!tracks.TryGetValue(track.Id, out var peer)) tracks.Add(track.Id, peer = new TimelineTrackAutomationPeer(this, track.Id));
            children.Add(peer);
        }
        return children;
    }

    public IRawElementProviderSimple[] GetSelection()
    {
        _ = GetChildrenCore();
        return View.Project.Tracks.SelectMany(t => tracks[t.Id].ClipPeers())
            .Where(c => c.IsSelected).Select(ProviderFromPeer).ToArray();
    }

    internal IRawElementProviderSimple Provider => ProviderFromPeer(this);
    internal void NotifyTrackLevelFocus(Guid trackId, string level)
    {
        _ = GetChildrenCore();
        tracks[trackId].NotifyLevelFocus(level);
    }

    internal void RefreshSelection(HashSet<Guid> previous)
    {
        _ = GetChildrenCore();
        foreach (var clip in tracks.Values.SelectMany(t => t.ClipPeers()))
            clip.RefreshSelection(previous.Contains(clip.Id));
        if (ListenerExists(AutomationEvents.SelectionPatternOnInvalidated)) RaiseAutomationEvent(AutomationEvents.SelectionPatternOnInvalidated);
    }

    internal void RefreshProject()
    {
        foreach (var id in tracks.Keys.Where(id => View.Project.Tracks.All(t => t.Id != id)).ToArray()) tracks.Remove(id);
        foreach (var track in tracks.Values) track.RefreshState();
        InvalidatePeer();
    }

    internal void RefreshGeometry()
    {
        foreach (var track in tracks.Values) track.RefreshGeometry();
        InvalidatePeer();
    }
}

internal abstract class TimelineChildAutomationPeer(TimelineAutomationPeer timeline) : AutomationPeer
{
    protected TimelineAutomationPeer Timeline { get; } = timeline;
    protected TimelineView View => Timeline.View;
    protected override string GetAcceleratorKeyCore() => "";
    protected override string GetAccessKeyCore() => "";
    protected override string GetItemTypeCore() => "";
    protected override AutomationPeer? GetLabeledByCore() => null;
    protected override string GetLocalizedControlTypeCore() => GetAutomationControlTypeCore().ToString();
    protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;
    protected override bool HasKeyboardFocusCore() => false;
    protected override bool IsContentElementCore() => true;
    protected override bool IsControlElementCore() => true;
    protected override bool IsEnabledCore() => View.IsEnabled;
    protected override bool IsKeyboardFocusableCore() => true;
    protected override bool IsOffscreenCore() => GetBoundingRectangleCore().IsEmpty;
    protected override bool IsPasswordCore() => false;
    protected override bool IsRequiredForFormCore() => false;
    protected override Point GetClickablePointCore()
    {
        var bounds = GetBoundingRectangleCore();
        return bounds.IsEmpty ? new Point(double.NaN, double.NaN) : new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }
    protected override void SetFocusCore() => View.Focus();
    protected override List<AutomationPeer> GetChildrenCore() => [];
    protected override string GetHelpTextCore() => "";
    protected override string GetItemStatusCore() => "";
    protected void EnsureEnabled()
    {
        if (!View.IsEnabled) throw new ElementNotEnabledException();
    }
}

internal sealed class TimelineTrackAutomationPeer(TimelineAutomationPeer timeline, Guid id) : TimelineChildAutomationPeer(timeline)
{
    private readonly Dictionary<Guid, TimelineClipAutomationPeer> clips = [];
    private readonly Dictionary<string, TimelineTrackButtonAutomationPeer> buttons = [];
    private readonly Dictionary<string, TimelineTrackLevelAutomationPeer> levels = [];
    internal TimelineTrack Track => View.Project.Tracks.FirstOrDefault(t => t.Id == id) ?? throw new ElementNotAvailableException();
    protected override string GetAutomationIdCore() => $"Track_{id:N}";
    protected override string GetClassNameCore() => "TimelineTrack";
    protected override string GetNameCore() => Track.Name;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    protected override string GetItemStatusCore() => $"{Track.Kind}; {(Track.IsMuted ? "muted" : "unmuted")}; {(Track.IsSolo ? "solo" : "not solo")}; {(Track.IsLocked ? "locked" : "unlocked")}; {(Track.IsVisible ? "visible" : "hidden")}";
    protected override Rect GetBoundingRectangleCore()
    {
        var index = View.Project.Tracks.FindIndex(t => t.Id == id);
        return index < 0 ? Rect.Empty : View.ToScreenBounds(new Rect(0, TimelineView.RulerHeight + index * TimelineView.TrackHeight - View.VerticalOffset, View.ActualWidth, TimelineView.TrackHeight));
    }
    protected override void SetFocusCore() { EnsureEnabled(); View.ActivateTrack(View.Project.Tracks.FindIndex(t => t.Id == id)); View.Focus(); }
    public override object? GetPattern(PatternInterface patternInterface) => null;

    protected override List<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>();
        foreach (var action in Track.Kind == TrackKind.Video ? new[] { "Mute", "Solo", "Lock", "Visible" } : new[] { "Mute", "Solo", "Lock" })
        {
            if (!buttons.TryGetValue(action, out var button)) buttons.Add(action, button = new TimelineTrackButtonAutomationPeer(Timeline, id, action));
            children.Add(button);
        }
        foreach (var level in Track.Kind == TrackKind.Video ? new[] { "Opacity" } : new[] { "Gain", "Pan" })
        {
            if (!levels.TryGetValue(level, out var slider)) levels.Add(level, slider = new TimelineTrackLevelAutomationPeer(Timeline, id, level));
            children.Add(slider);
        }
        children.AddRange(ClipPeers());
        return children;
    }

    internal IEnumerable<TimelineClipAutomationPeer> ClipPeers()
    {
        var track = View.Project.Tracks.FirstOrDefault(t => t.Id == id);
        if (track is null) yield break;
        foreach (var clip in track.Clips.OrderBy(c => c.StartFrame).ThenBy(c => c.Id))
        {
            if (!clips.TryGetValue(clip.Id, out var peer)) clips.Add(clip.Id, peer = new TimelineClipAutomationPeer(Timeline, id, clip.Id));
            yield return peer;
        }
    }

    internal void RefreshState()
    {
        foreach (var clipId in clips.Keys.Where(clipId => !Track.Clips.Any(c => c.Id == clipId)).ToArray()) clips.Remove(clipId);
        foreach (var clip in clips.Values) clip.RefreshGeometry();
        foreach (var button in buttons.Values) button.RefreshState();
        foreach (var slider in levels.Values) slider.RefreshState();
        InvalidatePeer();
    }
    internal void RefreshGeometry()
    {
        foreach (var clip in clips.Values) clip.RefreshGeometry();
        foreach (var button in buttons.Values) button.InvalidatePeer();
        foreach (var slider in levels.Values) slider.InvalidatePeer();
        InvalidatePeer();
    }
    internal void NotifyLevelFocus(string level)
    {
        _ = GetChildrenCore();
        levels[level].NotifyFocus();
    }
}

internal sealed class TimelineTrackLevelAutomationPeer(TimelineAutomationPeer timeline, Guid trackId, string level) : TimelineChildAutomationPeer(timeline), IRangeValueProvider
{
    private double previous = timeline.View.ReadTrackLevel(trackId, level);
    private TimelineTrack Track => View.Project.Tracks.FirstOrDefault(t => t.Id == trackId) ?? throw new ElementNotAvailableException();
    public bool IsReadOnly => Track.IsLocked || !View.IsEnabled;
    public double Minimum => level == "Pan" ? -1 : 0;
    public double Maximum => level == "Gain" ? Math.Max(4, Value) : 1;
    public double SmallChange => .01;
    public double LargeChange => .1;
    public double Value => View.ReadTrackLevel(trackId, level);
    protected override string GetAutomationIdCore() => $"Track_{trackId:N}_{level}";
    protected override string GetClassNameCore() => "TimelineTrackLevel";
    protected override string GetNameCore() => $"{level} {Track.Name}";
    protected override string GetHelpTextCore() => "Drag to adjust. Double-click to reset. Arrow keys adjust a focused track level.";
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
    protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.Horizontal;
    protected override Rect GetBoundingRectangleCore() => View.ToScreenBounds(View.TrackLevelBounds(trackId, level));
    protected override bool IsEnabledCore() => View.IsEnabled && !Track.IsLocked;
    protected override bool HasKeyboardFocusCore() => View.IsTrackLevelFocused(trackId, level);
    protected override void SetFocusCore() { EnsureEnabled(); View.FocusTrackLevel(trackId, level); }
    public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.RangeValue ? this : null;
    public void SetValue(double value)
    {
        EnsureEnabled();
        if (IsReadOnly) throw new ElementNotEnabledException();
        if (!double.IsFinite(value) || value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value));
        View.SetTrackLevel(trackId, level, value);
    }
    internal void RefreshState()
    {
        var current = Value;
        if (current != previous) RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, previous, current);
        previous = current;
        InvalidatePeer();
    }
    internal void NotifyFocus()
    {
        if (ListenerExists(AutomationEvents.AutomationFocusChanged)) RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
    }
}

internal sealed class TimelineClipAutomationPeer(TimelineAutomationPeer timeline, Guid trackId, Guid id) : TimelineChildAutomationPeer(timeline), ISelectionItemProvider
{
    private readonly Dictionary<string, TimelineEventButtonAutomationPeer> buttons = [];
    internal Guid Id => id;
    private TimelineTrack Track => View.Project.Tracks.FirstOrDefault(t => t.Id == trackId) ?? throw new ElementNotAvailableException();
    private TimelineClip Clip => Track.Clips.FirstOrDefault(c => c.Id == id) ?? throw new ElementNotAvailableException();
    public bool IsSelected => View.Selection.Contains(id);
    public IRawElementProviderSimple SelectionContainer => Timeline.Provider;
    protected override string GetAutomationIdCore() => $"Clip_{id:N}";
    protected override string GetClassNameCore() => "TimelineClip";
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
    protected override string GetNameCore() => $"{Clip.Name}, {Track.Name}, start {Clip.StartFrame}, duration {Clip.DurationFrames} frames";
    protected override string GetHelpTextCore() => $"Source in {Clip.SourceInFrame} frames; opacity {Clip.Opacity:0.###}; gain {Clip.Gain:0.###}; pan {Clip.Pan:0.###}.";
    protected override string GetItemStatusCore() => $"{(IsSelected ? "Selected" : "Not selected")}; {(Track.IsLocked ? "locked" : "unlocked")}; {(Clip.GroupId.HasValue ? "linked" : "independent")}";
    protected override bool HasKeyboardFocusCore() => View.IsKeyboardFocusWithin && View.ActiveClipId == id && IsSelected;
    protected override Rect GetBoundingRectangleCore() => View.ToScreenBounds(View.ClipBounds(trackId, Clip), clipContent: true);
    protected override void SetFocusCore() { EnsureEnabled(); View.SelectClip(id, false); View.Focus(); }
    public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.SelectionItem ? this : null;
    public void Select() { EnsureEnabled(); _ = Clip; View.SelectClip(id, false); }
    public void AddToSelection() { EnsureEnabled(); _ = Clip; View.SelectClip(id, true); }
    public void RemoveFromSelection() { EnsureEnabled(); _ = Clip; View.RemoveClipSelection(id); }
    protected override List<AutomationPeer> GetChildrenCore()
    {
        var children = new List<AutomationPeer>();
        foreach (var action in View.EventButtonActions(Track, Clip))
        {
            if (!buttons.TryGetValue(action, out var button)) buttons.Add(action, button = new TimelineEventButtonAutomationPeer(Timeline, trackId, id, action));
            children.Add(button);
        }
        return children;
    }

    internal void RefreshGeometry()
    {
        // Button availability changes with zoom and clipping, even when the project is unchanged.
        ResetChildrenCache();
        foreach (var button in buttons.Values) button.InvalidatePeer();
        InvalidatePeer();
    }

    internal void RefreshSelection(bool previous)
    {
        if (previous == IsSelected) return;
        RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, previous, IsSelected);
        var change = IsSelected ? (View.Selection.Count == 1 ? AutomationEvents.SelectionItemPatternOnElementSelected : AutomationEvents.SelectionItemPatternOnElementAddedToSelection) : AutomationEvents.SelectionItemPatternOnElementRemovedFromSelection;
        if (ListenerExists(change)) RaiseAutomationEvent(change);
    }
}

internal sealed class TimelineEventButtonAutomationPeer(TimelineAutomationPeer timeline, Guid trackId, Guid clipId, string action) : TimelineChildAutomationPeer(timeline), IInvokeProvider
{
    private TimelineTrack Track => View.Project.Tracks.FirstOrDefault(t => t.Id == trackId) ?? throw new ElementNotAvailableException();
    private TimelineClip Clip => Track.Clips.FirstOrDefault(c => c.Id == clipId) ?? throw new ElementNotAvailableException();
    protected override string GetAutomationIdCore() => $"Clip_{clipId:N}_{action}";
    protected override string GetClassNameCore() => "TimelineEventButton";
    protected override string GetNameCore() => $"{TimelineView.EventButtonLabel(action)} {Clip.Name}";
    protected override string GetHelpTextCore() => TimelineView.EventButtonLabel(action);
    protected override string GetItemStatusCore() => action == "VideoEffects" ? $"{Clip.VideoEffects.Count(effect => effect.IsEnabled)} enabled effects" : "";
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    protected override bool IsEnabledCore() => View.IsEnabled && !Track.IsLocked;
    protected override Rect GetBoundingRectangleCore() => View.ToScreenBounds(View.EventButtonBounds(trackId, Clip, action), clipContent: true);
    protected override void SetFocusCore() { EnsureEnabled(); View.SelectClip(clipId, false); View.Focus(); }
    public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Invoke ? this : null;
    public void Invoke()
    {
        EnsureEnabled(); _ = Clip;
        if (Track.IsLocked) throw new ElementNotEnabledException();
        // UIA must return before a dialog starts its modal message loop.
        View.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            var track = View.Project.Tracks.FirstOrDefault(t => t.Id == trackId);
            if (!View.IsEnabled || track is null || track.IsLocked || !track.Clips.Any(c => c.Id == clipId)) return;
            if (action != "More" && track.Kind != TrackKind.Video) return;
            View.InvokeEventAction(clipId, action);
        }));
    }
}

internal sealed class TimelineTrackButtonAutomationPeer(TimelineAutomationPeer timeline, Guid trackId, string action) : TimelineChildAutomationPeer(timeline), IInvokeProvider, IToggleProvider
{
    private ToggleState previous = ReadState(timeline.View.Project.Tracks.Single(t => t.Id == trackId), action);
    private TimelineTrack Track => View.Project.Tracks.FirstOrDefault(t => t.Id == trackId) ?? throw new ElementNotAvailableException();
    public ToggleState ToggleState => ReadState(Track, action);
    private static ToggleState ReadState(TimelineTrack track, string action) => (action switch { "Mute" => track.IsMuted, "Solo" => track.IsSolo, "Lock" => track.IsLocked, _ => track.IsVisible }) ? ToggleState.On : ToggleState.Off;
    protected override string GetAutomationIdCore() => $"Track_{trackId:N}_{action}";
    protected override string GetClassNameCore() => "TimelineTrackButton";
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
    protected override string GetNameCore() => $"{(action == "Visible" ? "Video visibility" : action)} {Track.Name}";
    protected override string GetItemStatusCore() => ToggleState.ToString();
    protected override string GetHelpTextCore() => $"Toggle {(action == "Visible" ? "video visibility" : action.ToLowerInvariant())} for {Track.Name}. Shortcut: {GetAcceleratorKeyCore()}.";
    protected override string GetAcceleratorKeyCore() => action switch { "Mute" => "M", "Solo" => "Shift+M", "Lock" => "L", _ => "V" };
    protected override Rect GetBoundingRectangleCore() => View.ToScreenBounds(View.TrackButtonBounds(trackId, action));
    public override object? GetPattern(PatternInterface patternInterface) => patternInterface is PatternInterface.Invoke or PatternInterface.Toggle ? this : null;
    public void Invoke() { EnsureEnabled(); _ = Track; View.InvokeTrackAction(trackId, action); }
    public void Toggle() => Invoke();
    internal void RefreshState()
    {
        var current = ToggleState;
        if (current != previous) RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty, previous, current);
        previous = current;
        InvalidatePeer();
    }
}
