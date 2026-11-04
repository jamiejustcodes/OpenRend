using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.UI;

public sealed record ClipGesture(Guid ClipId, Guid TrackId, long Start, long SourceIn, long Duration, string Mode, int? NewTrackIndex = null, TrackKind? NewTrackKind = null, bool IgnoreGroups = false, bool Ripple = false, AutoRippleMode RippleMode = AutoRippleMode.AffectedTracks, double? NewPlaybackRate = null, Guid? RollAdjacentClipId = null);
public sealed record TrackLevelGesture(Guid TrackId, string Level, double Value);
public sealed record TransitionGesture(Guid OutgoingClipId, Guid IncomingClipId, VideoTransition Transition, bool IsCut = false);

public sealed partial class TimelineView : FrameworkElement
{
    private ProjectDocument project = new();
    public ProjectDocument Project
    {
        get => project;
        set
        {
            if (!ReferenceEquals(project, value)) loopRegion = null;
            project = value ?? throw new ArgumentNullException(nameof(value));
            if (loopRegion is { } range && range.EndFrame > project.DurationFrames) loopRegion = null;
            if (levelTrack is { } levelId && project.Tracks.All(t => t.Id != levelId)) CancelTrackLevelInput();
            ActiveTrackIndex = Math.Clamp(ActiveTrackIndex, 0, Math.Max(0, project.Tracks.Count - 1));
            (UIElementAutomationPeer.FromElement(this) as TimelineAutomationPeer)?.RefreshProject();
            InvalidateVisual();
        }
    }
    public int ActiveTrackIndex { get; private set; }
    public long Position { get; set; }
    public HashSet<Guid> Selection { get; } = [];
    public Dictionary<Guid, float[]> Waveforms { get; } = [];
    public Dictionary<Guid, ImageSource> Thumbnails { get; } = [];
    private double pixelsPerSecond = 13.4;
    public double PixelsPerSecond
    {
        get => pixelsPerSecond;
        set { if (pixelsPerSecond == value) return; pixelsPerSecond = value; RefreshGeometry(); }
    }
    public bool Snapping { get; set; } = true;
    public bool Ripple { get; set; }
    public AutoRippleMode RippleMode { get; set; } = AutoRippleMode.AffectedTracks;
    public bool IgnoreGroups { get; set; }
    internal bool ShouldIgnoreGroups => IgnoreGroups || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
    private double offset;
    public double Offset
    {
        get => offset;
        set { if (offset == value) return; offset = value; RefreshGeometry(); }
    }
    private double verticalOffset;
    public double VerticalOffset
    {
        get => verticalOffset;
        set { if (verticalOffset == value) return; verticalOffset = value; RefreshGeometry(); }
    }
    public const double HeaderWidth = 256;
    public const double RulerHeight = 46;
    public const double TrackHeight = 69;
    public event Action<long>? SeekRequested;
    public event Action? SelectionChanged;
    public event Action<ClipGesture>? EditRequested;
    public event Action? UnlinkRequested;
    public event Action? LinkRequested;
    public event Action? ClearGroupRequested;
    public event Action<Guid>? SelectAllInGroupRequested;
    public event Action<Guid, string>? TrackAction;
    public event Action<TrackLevelGesture>? TrackLevelRequested;
    public event Action<Guid, Guid, long>? MediaDropped;
    public event Action<TransitionGesture>? TransitionDropped;
    public event Action<Guid, Guid>? OverlapToCutRequested;
    public event Action? DeleteRequested;
    public event Action? SplitRequested;
    public event Action? PanCropRequested;
    public event Action? VideoEffectsRequested;
    public event Action? ColorGradingRequested;
    public event Action<Guid>? TransitionPropertiesRequested;
    public event Action<Guid, VideoSourceMode>? VideoSourceModeRequested;
    public event Action<Guid, VideoFadeCurve, VideoFadeCurve?, bool>? VideoFadeCurveRequested;
    public event Action<Guid, string>? VideoEventActionRequested;
    public event Action<Guid, bool>? ClipLockRequested;
    public event Action<Guid, bool>? ClipMuteRequested;
    public event Action<int>? InsertVideoTrackRequested;
    public event Action<int>? InsertAudioTrackRequested;
    public event Action<Guid>? DuplicateTrackRequested;
    public event Action<Guid>? DeleteTrackRequested;
    public event Action<Guid>? TrackFxRequested;
    public event Action<Guid>? TrackAudioFxRequested;
    public event Action<Guid>? TrackPhaseInvertRequested;
    public event Action<Guid>? TrackMotionRequested;
    public event Action<Guid>? TrackColorGradingRequested;
    public event Action<Guid, TrackCompositeMode>? TrackCompositeModeRequested;
    public event Action<Guid>? TrackMakeCompositingChildRequested;
    public event Action<Guid>? TrackMakeCompositingParentRequested;
    public event Action<Guid>? TrackAdjustmentRequested;
    public event Action<Guid, Guid?>? TrackAudioBusRequested;
    public event Action<Guid>? MotionTrackingRequested;
    public event Action<Guid>? TextBasedEditingRequested;
    public event Action<Guid>? NextTakeRequested;
    public event Action<Guid>? PreviousTakeRequested;
    public event Action<Guid, int>? SelectTakeRequested;
    public event Action<Guid>? DeleteTakeRequested;
    public event Action<Guid, bool>? ClipReverseRequested;
    public event Action<Guid, double>? ClipPlaybackRateRequested;
    public event Action<Guid, double>? ClipUnderSampleRateRequested;
    public event Action<Guid, VideoResampleMode>? ClipResampleModeRequested;
    public event Action<Guid, bool>? ClipMaintainAspectRatioRequested;
    public event Action<Guid, bool>? ClipReduceInterlaceRequested;
    public event Action<Guid, bool>? ClipNormalizeRequested;
    public event Action<Guid, bool>? ClipInvertPhaseRequested;
    public event Action<Guid, EnvelopeType>? ToggleEnvelopeRequested;
    public event Action<Guid, EnvelopeType, long, double, EnvelopeCurveType>? SetEnvelopePointRequested;
    public event Action<Guid, EnvelopeType, long>? RemoveEnvelopePointRequested;
    public event Action<Guid, EnvelopeType, long, EnvelopeCurveType>? SetEnvelopePointCurveRequested;
    public event Action<Guid, EnvelopeType>? ToggleTrackEnvelopeRequested;
    public event Action<Guid, EnvelopeType, long, double, EnvelopeCurveType>? SetTrackEnvelopePointRequested;
    public event Action<Guid, EnvelopeType, long>? RemoveTrackEnvelopePointRequested;
    public event Action<Guid, EnvelopeType, long, EnvelopeCurveType>? SetTrackEnvelopePointCurveRequested;
    public event Action<Guid, EnvelopeType>? ResetEnvelopeRequested;
    public event Action<Guid, EnvelopeType>? ResetTrackEnvelopeRequested;
    public event Action<Guid, bool>? TrackArmRequested;
    public event Action<Guid, string?>? TrackColorRequested;
    public event Action<Guid, TrackAutomationMode>? TrackAutomationModeRequested;
    public RulerFormat RulerFormat
    {
        get => Project.RulerFormat;
        set
        {
            if (Project.RulerFormat == value) return;
            Project.RulerFormat = value;
            InvalidateVisual();
            RulerFormatChanged?.Invoke(value);
        }
    }
    public event Action<RulerFormat>? RulerFormatChanged;
    public event Action? SetTimeAtCursorRequested;
    public event Action? InsertTimeRequested;
    public event Action? DeleteTimeRequested;
    public EditToolMode EditTool
    {
        get => editTool;
        set
        {
            if (editTool == value) return;
            editTool = value;
            Cursor = CursorAt(Mouse.GetPosition(this));
            EditToolChanged?.Invoke(editTool);
            InvalidateVisual();
        }
    }
    private EditToolMode editTool = EditToolMode.Normal;
    public event Action<EditToolMode>? EditToolChanged;
    public event Action<double>? ZoomChanged;
    public void CycleNextEditTool() => EditTool = EditTool.NextTool();
    public void CyclePreviousEditTool() => EditTool = EditTool.PreviousTool();

    private bool isMarqueeSelecting;
    private Point marqueeStart;
    private Rect marqueeRect;
    private ModifierKeys marqueeModifiers;

    private bool isZooming;
    private Point zoomStart;
    private Rect zoomRect;
    public bool HasTrackEnvelope(Guid trackId, EnvelopeType type) =>
        Project.Tracks.FirstOrDefault(t => t.Id == trackId)?.Envelopes?.Any(e => e.Type == type && e.IsEnabled) ?? false;
    private TimelineClip? dragging;
    private TimelineTrack? dragTrack;
    private string mode = "";
    private Point origin;
    private long delta;
    private int destination;
    private bool destinationIsNewTrack;
    private int destinationNewTrackIndex;
    private TrackKind destinationNewTrackKind;
    private long? snappedFrame;
    private HashSet<Guid>? movingClipIds;
    private bool isDragging;
    private bool alreadySelectedOnDown;
    public const double DragDistanceThreshold = 4.0;
    private bool seeking;
    private Guid? levelTrack;
    private string? levelKind;
    private double pendingLevel;
    private bool adjustingLevel;
    private Guid? activeClipId;
    private Guid? hoverClipId;
    private string hoverMode = "";
    internal Guid? ActiveClipId => activeClipId;
    internal bool IsTrackLevelFocused(Guid trackId, string level) => IsKeyboardFocusWithin && levelTrack == trackId && levelKind == level;
    public void CancelTrackLevelInput()
    {
        var capturedLevel = adjustingLevel;
        adjustingLevel = false; levelTrack = null; levelKind = null;
        if (capturedLevel) ReleaseMouseCapture();
        InvalidateVisual();
    }
    public void CancelPointerInput()
    {
        pressedEventButton = null;
        if (selectingLoop) loopRegion = loopBeforeDrag;
        selectingLoop = false;
        isDragging = false; alreadySelectedOnDown = false;
        dragging = null; dragTrack = null; seeking = false; mode = ""; delta = 0;
        destinationIsNewTrack = false; movingClipIds = null; snappedFrame = null;
        isMarqueeSelecting = false; marqueeRect = Rect.Empty;
        isZooming = false; zoomRect = Rect.Empty;
        CancelTrackLevelInput();
        ReleaseMouseCapture(); ClearHover(); InvalidateVisual();
    }
    private static readonly ConcurrentDictionary<string, SolidColorBrush> BrushMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<(string Hex, double Thickness), Pen> PenMap = new();
    private static readonly ConcurrentDictionary<(string Hex, double Thickness, string Dash), Pen> DashedPenMap = new();
    private static readonly Brush BackgroundBrush = Brush("#1E1E1E");
    private static readonly Typeface Face = new("Segoe UI");

    public TimelineView()
    {
        Focusable = true;
        ClipToBounds = true;
        AllowDrop = true;
        AutomationProperties.SetName(this, "Timeline");
        AutomationProperties.SetHelpText(this, "Up and Down choose a track. Left and Right select clips. Shift extends the selection. I and O set loop in and out points. M mutes, Shift+M solos, L locks, and V changes video visibility. S splits at the cursor; Delete removes selected clips.");
        Drop += (_, e) =>
        {
            if (e.Data.GetData(TransitionDataFormat) is VideoTransition transition)
            {
                if (TransitionAt(e.GetPosition(this), transition) is { } target) TransitionDropped?.Invoke(target);
                e.Handled = true;
                return;
            }
            if (e.Data.GetData("OpenRend.Media") is not Guid media) return;
            var p = e.GetPosition(this);
            var track = TrackAt(p.Y);
            if (track is not null) { MediaDropped?.Invoke(media, track.Id, FrameAt(p.X)); e.Handled = true; }
        };
        DragOver += (_, e) =>
        {
            if (e.Data.GetData(TransitionDataFormat) is not VideoTransition transition) return;
            e.Effects = TransitionAt(e.GetPosition(this), transition) is null ? DragDropEffects.None : DragDropEffects.Copy;
            e.Handled = true;
        };
        ContextMenu = CreateEventMenu();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TimelineAutomationPeer(this);

    private void RefreshGeometry()
    {
        (UIElementAutomationPeer.FromElement(this) as TimelineAutomationPeer)?.RefreshGeometry();
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RefreshGeometry();
    }

    public bool HandleKeyboardInput(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Divide && modifiers == ModifierKeys.Control)
        {
            RequestOverlapToCut();
            return true;
        }
        if (key == Key.D)
        {
            if (modifiers == ModifierKeys.Control)
            {
                EditTool = EditToolMode.Normal;
                return true;
            }
            if (modifiers == ModifierKeys.None)
            {
                CycleNextEditTool();
                return true;
            }
            if (modifiers == ModifierKeys.Shift)
            {
                CyclePreviousEditTool();
                return true;
            }
        }
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return false;
        if (Project.Tracks.Count == 0) return false;
        if (modifiers == ModifierKeys.None && key is Key.I or Key.O)
        {
            var at = Math.Clamp(Position, 0, Project.DurationFrames);
            var start = key == Key.I ? at : LoopRegion?.StartFrame ?? 0;
            var end = key == Key.O ? at : LoopRegion?.EndFrame ?? Project.DurationFrames;
            if (end < start) { if (key == Key.I) end = Project.DurationFrames; else start = 0; }
            LoopRegion = end > start ? new FrameRange(start, end - start) : null;
            return true;
        }
        if (levelTrack is { } levelId && levelKind is { } level && key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            if (Project.Tracks.Single(t => t.Id == levelId).IsLocked) return true;
            var current = ReadTrackLevel(levelId, level);
            var min = level == "Pan" ? -1 : 0; var max = level == "Gain" ? Math.Max(4, current) : 1;
            var value = key switch { Key.Home => min, Key.End => max, _ => current + (key == Key.Left ? -.01 : .01) };
            SetTrackLevel(levelId, level, Math.Clamp(value, min, max));
            return true;
        }
        if (key == Key.Escape) { CancelPointerInput(); return true; }
        if (key is Key.Up or Key.Down)
        {
            ActivateTrack(Math.Clamp(ActiveTrackIndex + (key == Key.Down ? 1 : -1), 0, Project.Tracks.Count - 1));
            return true;
        }
        var track = Project.Tracks[ActiveTrackIndex];
        if (key is Key.Left or Key.Right)
        {
            var clips = track.Clips.OrderBy(c => c.StartFrame).ThenBy(c => c.Id).ToArray();
            if (clips.Length == 0) return true;
            var current = Array.FindIndex(clips, c => c.Id == activeClipId);
            if (current < 0) current = Array.FindIndex(clips, c => Selection.Contains(c.Id));
            var next = current >= 0 ? Math.Clamp(current + (key == Key.Right ? 1 : -1), 0, clips.Length - 1)
                : key == Key.Right ? Array.FindIndex(clips, c => c.EndFrame > Position) : Array.FindLastIndex(clips, c => c.StartFrame < Position);
            if (next < 0) next = key == Key.Right ? clips.Length - 1 : 0;
            SelectClip(clips[next].Id, modifiers.HasFlag(ModifierKeys.Shift));
            return true;
        }
        if (modifiers.HasFlag(ModifierKeys.Control)) return false;
        var action = key switch
        {
            Key.M => modifiers.HasFlag(ModifierKeys.Shift) ? "Solo" : "Mute",
            Key.L => "Lock",
            Key.V when track.Kind == TrackKind.Video => "Visible",
            _ => null
        };
        if (action is not null) { InvokeTrackAction(track.Id, action); return true; }
        if (key == Key.S) { SplitRequested?.Invoke(); return true; }
        if (key == Key.Delete) { DeleteRequested?.Invoke(); return true; }
        return false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && HandleKeyboardInput(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

    public void ActivateTrack(int index)
    {
        if (index < 0 || index >= Project.Tracks.Count) return;
        ActiveTrackIndex = index;
        if (levelTrack != Project.Tracks[index].Id) { levelTrack = null; levelKind = null; }
        var top = RulerHeight + index * TrackHeight;
        if (top - VerticalOffset < RulerHeight) VerticalOffset = index * TrackHeight;
        else if (ActualHeight > RulerHeight && top + TrackHeight - VerticalOffset > ActualHeight)
            VerticalOffset = Math.Max(0, top + TrackHeight - ActualHeight);
        InvalidateVisual();
        (UIElementAutomationPeer.FromElement(this) as TimelineAutomationPeer)?.InvalidatePeer();
    }

    internal void SelectClip(Guid clipId, bool add, bool ensureVisible = true)
    {
        var index = Project.Tracks.FindIndex(t => t.Clips.Any(c => c.Id == clipId));
        if (index < 0) throw new ElementNotAvailableException();
        var before = Selection.ToHashSet();
        if (!add) Selection.Clear();
        levelTrack = null; levelKind = null;
        var clip = Project.Tracks[index].Clips.Single(c => c.Id == clipId);
        if (!ShouldIgnoreGroups && clip.GroupId.HasValue)
        {
            var grouped = Project.Tracks.SelectMany(t => t.Clips)
                .Where(c => c.GroupId == clip.GroupId.Value)
                .Select(c => c.Id);
            Selection.UnionWith(grouped);
        }
        else
        {
            Selection.Add(clipId);
        }
        activeClipId = clipId;
        ActivateTrack(index);
        if (ensureVisible && (X(clip.StartFrame) < HeaderWidth || ActualWidth > HeaderWidth && X(clip.StartFrame) >= ActualWidth))
            Offset = Math.Max(0, Project.FrameRate.Seconds(clip.StartFrame) * PixelsPerSecond - 10);
        NotifySelection(before);
    }

    internal void RemoveClipSelection(Guid clipId)
    {
        var before = Selection.ToHashSet();
        var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is not null && !ShouldIgnoreGroups && clip.GroupId.HasValue)
        {
            var grouped = Project.Tracks.SelectMany(t => t.Clips)
                .Where(c => c.GroupId == clip.GroupId.Value)
                .Select(c => c.Id);
            foreach (var id in grouped) Selection.Remove(id);
        }
        else
        {
            Selection.Remove(clipId);
        }
        NotifySelection(before);
    }

    private void NotifySelection(HashSet<Guid> before)
    {
        SelectionChanged?.Invoke();
        InvalidateVisual();
        (UIElementAutomationPeer.FromElement(this) as TimelineAutomationPeer)?.RefreshSelection(before);
    }

    internal void InvokeTrackAction(Guid trackId, string action)
    {
        var index = Project.Tracks.FindIndex(t => t.Id == trackId);
        if (index < 0) throw new ElementNotAvailableException();
        ActivateTrack(index);
        TrackAction?.Invoke(trackId, action);
        if (action == "Arm") TrackArmRequested?.Invoke(trackId, !Project.Tracks[index].ArmRecord);
        InvalidateVisual();
        (UIElementAutomationPeer.FromElement(this) as TimelineAutomationPeer)?.RefreshProject();
    }

    internal Rect ClipBounds(Guid trackId, TimelineClip clip)
    {
        var index = Project.Tracks.FindIndex(t => t.Id == trackId);
        return new Rect(X(clip.StartFrame), RulerHeight + index * TrackHeight - VerticalOffset + 1,
            Math.Max(2, Project.FrameRate.Seconds(clip.DurationFrames) * PixelsPerSecond), TrackHeight - 2);
    }

    internal Rect TrackButtonBounds(Guid trackId, string action)
    {
        var index = Project.Tracks.FindIndex(t => t.Id == trackId);
        var y = RulerHeight + index * TrackHeight - VerticalOffset;
        return action switch
        {
            "Automation" => new Rect(138, y + 4, 21, 20),
            "FX" => new Rect(161, y + 4, 21, 20),
            "Lock" => new Rect(184, y + 4, 21, 20),
            "Mute" => new Rect(207, y + 4, 21, 20),
            "Solo" => new Rect(230, y + 4, 21, 20),
            "Motion" => new Rect(207, y + 46, 21, 20),
            "CompChild" => new Rect(184, y + 46, 21, 20),
            "Visible" => new Rect(230, y + 46, 21, 18),
            "Arm" => new Rect(207, y + 46, 21, 20),
            "Phase" => new Rect(230, y + 46, 21, 20),
            _ => new Rect(230, y + 4, 21, 20)
        };
    }

    internal Rect TrackLevelBounds(Guid trackId, string level)
    {
        var index = Project.Tracks.FindIndex(t => t.Id == trackId);
        var y = RulerHeight + index * TrackHeight - VerticalOffset;
        return level switch { "Opacity" => new Rect(116, y + 25, 124, 20), "Gain" => new Rect(108, y + 25, 90, 20), _ => new Rect(108, y + 46, 90, 18) };
    }
    internal double ReadTrackLevel(Guid trackId, string level)
    {
        var track = Project.Tracks.FirstOrDefault(t => t.Id == trackId) ?? throw new ElementNotAvailableException();
        if (adjustingLevel && levelTrack == trackId && levelKind == level) return pendingLevel;
        return level switch { "Gain" => track.Gain, "Opacity" => track.Opacity, _ => track.Pan };
    }
    internal void FocusTrackLevel(Guid trackId, string level)
    {
        var index = Project.Tracks.FindIndex(t => t.Id == trackId);
        if (index < 0) throw new ElementNotAvailableException();
        var changed = levelTrack != trackId || levelKind != level;
        ActivateTrack(index); levelTrack = trackId; levelKind = level; Focus(); InvalidateVisual();
        if (changed) (UIElementAutomationPeer.FromElement(this) as TimelineAutomationPeer)?.NotifyTrackLevelFocus(trackId, level);
    }
    internal void SetTrackLevel(Guid trackId, string level, double value)
    {
        FocusTrackLevel(trackId, level);
        TrackLevelRequested?.Invoke(new(trackId, level, value));
        InvalidateVisual();
        (UIElementAutomationPeer.FromElement(this) as TimelineAutomationPeer)?.RefreshProject();
    }
    private static double LevelFraction(string level, double value) => level switch
    {
        "Gain" => value <= 0 ? 0 : Math.Clamp((20 * Math.Log10(value) + 60) / (60 + 20 * Math.Log10(4)), 0, 1),
        "Pan" => (value + 1) / 2,
        _ => value
    };
    private double LevelAt(double x)
    {
        var bounds = TrackLevelBounds(levelTrack!.Value, levelKind!);
        var fraction = Math.Clamp((x - bounds.X - 4) / (bounds.Width - 8), 0, 1);
        return levelKind switch { "Gain" => fraction == 0 ? 0 : Math.Pow(10, (-60 + (60 + 20 * Math.Log10(4)) * fraction) / 20), "Pan" => fraction * 2 - 1, _ => fraction };
    }

    internal Rect ToScreenBounds(Rect bounds, bool clipContent = false)
    {
        if (PresentationSource.FromVisual(this) is null || !IsVisible) return Rect.Empty;
        bounds.Intersect(new Rect(clipContent ? HeaderWidth : 0, RulerHeight,
            Math.Max(0, ActualWidth - (clipContent ? HeaderWidth : 0)), Math.Max(0, ActualHeight - RulerHeight)));
        return bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 ? Rect.Empty : new Rect(PointToScreen(bounds.TopLeft), PointToScreen(bounds.BottomRight));
    }

    private static SolidColorBrush Brush(string hex) =>
        BrushMap.GetOrAdd(hex, static h =>
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h));
            brush.Freeze();
            return brush;
        });

    private static Pen FrozenPen(string hex, double thickness = 1.0) =>
        PenMap.GetOrAdd((hex, thickness), static key =>
        {
            var pen = new Pen(Brush(key.Hex), key.Thickness);
            pen.Freeze();
            return pen;
        });

    private static Pen FrozenPen(Brush brush, double thickness = 1.0)
    {
        if (brush is SolidColorBrush scb)
        {
            return FrozenPen(scb.Color.ToString(), thickness);
        }
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static Pen FrozenDashedPen(string hex, double thickness, double[] dashes, double offset = 0)
    {
        var dashKey = string.Join(",", dashes) + ":" + offset;
        return DashedPenMap.GetOrAdd((hex, thickness, dashKey), _ =>
        {
            var style = new DashStyle(dashes, offset);
            style.Freeze();
            var pen = new Pen(Brush(hex), thickness) { DashStyle = style };
            pen.Freeze();
            return pen;
        });
    }
    private double X(long frame) => HeaderWidth + Project.FrameRate.Seconds(frame) * PixelsPerSecond - Offset;
    private long FrameAt(double x) => Math.Max(0, Project.FrameRate.Frames((x - HeaderWidth + Offset) / PixelsPerSecond));
    private TimelineTrack? TrackAt(double y)
    {
        var index = (int)Math.Floor((y - RulerHeight + VerticalOffset) / TrackHeight);
        return index >= 0 && index < Project.Tracks.Count ? Project.Tracks[index] : null;
    }
    internal (bool IsNewTrack, int TrackIndex, TrackKind NewTrackKind) EvaluateDragTarget(double y, TrackKind sourceTrackKind, int? sourceTrackIndex = null)
    {
        const double boundaryThreshold = 18.0;
        var timelineTop = RulerHeight - VerticalOffset;
        var timelineBottom = RulerHeight + Project.Tracks.Count * TrackHeight - VerticalOffset;

        if (Project.Tracks.Count == 0)
        {
            return (true, 0, sourceTrackKind);
        }

        if (y < timelineTop || y < RulerHeight)
        {
            return (true, 0, sourceTrackKind);
        }

        if (y >= timelineBottom)
        {
            return (true, Project.Tracks.Count, sourceTrackKind);
        }

        var rawIndex = (int)Math.Floor((y - timelineTop) / TrackHeight);
        var trackIndex = Math.Clamp(rawIndex, 0, Project.Tracks.Count - 1);
        var track = Project.Tracks[trackIndex];
        var trackTop = timelineTop + trackIndex * TrackHeight;
        var offsetInTrack = y - trackTop;

        if (track.Kind == sourceTrackKind)
        {
            if (sourceTrackIndex.HasValue)
            {
                if (trackIndex == sourceTrackIndex.Value)
                {
                    return (false, trackIndex, sourceTrackKind);
                }
                const double interTrackMargin = 4.0;
                if (offsetInTrack < interTrackMargin)
                {
                    return (true, trackIndex, sourceTrackKind);
                }
                if (offsetInTrack > TrackHeight - interTrackMargin)
                {
                    return (true, trackIndex + 1, sourceTrackKind);
                }
                return (false, trackIndex, sourceTrackKind);
            }

            if (offsetInTrack < boundaryThreshold)
            {
                return (true, trackIndex, sourceTrackKind);
            }
            if (offsetInTrack > TrackHeight - boundaryThreshold)
            {
                return (true, trackIndex + 1, sourceTrackKind);
            }
            return (false, trackIndex, sourceTrackKind);
        }

        // Incompatible track: upper half inserts above, lower half inserts below
        var insertIndex = offsetInTrack < TrackHeight / 2.0 ? trackIndex : trackIndex + 1;
        return (true, insertIndex, sourceTrackKind);
    }
    private HashSet<Guid> GetMovingClipIds(TimelineClip primary)
    {
        var ids = ShouldIgnoreGroups
            ? [primary.Id]
            : (Selection.Contains(primary.Id) ? Selection.ToHashSet() : [primary.Id]);
        if (primary.GroupId.HasValue && !ShouldIgnoreGroups)
        {
            var grouped = Project.Tracks.SelectMany(t => t.Clips)
                .Where(c => c.GroupId == primary.GroupId.Value)
                .Select(c => c.Id);
            ids.UnionWith(grouped);
        }
        return ids;
    }
    internal (TimelineTrack Track, TimelineClip Clip, string Mode)? HitClip(Point point)
    {
        if (point.X < HeaderWidth || point.X > ActualWidth || point.Y < RulerHeight || point.Y >= ActualHeight) return null;
        var track = TrackAt(point.Y);
        if (track is null) return null;
        var inside = track.Clips.LastOrDefault(clip => X(clip.StartFrame) <= point.X && X(clip.EndFrame) >= point.X);
        if (inside is not null)
        {
            var left = X(inside.StartFrame); var right = X(inside.EndFrame);
            // Keep a usable middle on short events while enlarging normal trim targets.
            var reach = Math.Min(10, Math.Max(2, (right - left) / 3));
            var atLeft = left >= HeaderWidth && point.X - left <= reach;
            var atRight = right <= ActualWidth && right - point.X <= reach;
            var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            if (alt) return (track, inside, ctrl ? "Slide" : "Slip");
            if (atLeft && (!atRight || point.X - left <= right - point.X))
            {
                return (track, inside, ctrl ? "TimeStretchLeft" : "TrimLeft");
            }
            if (atRight)
            {
                return (track, inside, ctrl ? "TimeStretchRight" : "TrimRight");
            }
            return (track, inside, "Move");
        }
        var edge = track.Clips.SelectMany(clip => new[] { (Clip: clip, X: X(clip.StartFrame), Mode: "TrimLeft"), (Clip: clip, X: X(clip.EndFrame), Mode: "TrimRight") })
            .Where(item => item.X >= HeaderWidth && item.X <= ActualWidth && Math.Abs(item.X - point.X) <= 10)
            .OrderBy(item => Math.Abs(item.X - point.X)).ThenByDescending(item => Selection.Contains(item.Clip.Id)).FirstOrDefault();
        return edge.Clip is null ? null : (track, edge.Clip, edge.Mode);
    }
    internal Cursor CursorAt(Point point)
    {
        if (EditTool == EditToolMode.Zoom) return Cursors.SizeAll;
        if (EditTool == EditToolMode.Selection) return Cursors.Cross;
        if (EditTool == EditToolMode.Envelope)
        {
            if (HitEnvelopePoint(point, out _, out _, out _) || HitTrackEnvelopePoint(point, out _, out _, out _))
                return Cursors.Hand;
            return Cursors.Arrow;
        }

        if (HitEventButton(point) is { } button) return button.Track.IsLocked || (button.Clip.IsLocked && button.Action != "More") ? Cursors.No : Cursors.Hand;
        var hit = HitClip(point);
        if (hit is null) return Cursors.Arrow;

        if (hit.Value.Track.IsLocked || hit.Value.Clip.IsLocked) return Cursors.No;
        return hit.Value.Mode switch
        {
            "Slip" => Cursors.ScrollWE,
            "Slide" => Cursors.SizeWE,
            "Move" => Cursors.Arrow,
            _ => Cursors.SizeWE
        };
    }
    private void UpdateHover(Point point)
    {
        var button = HitEventButton(point);
        ToolTip = button is { } target ? EventButtonLabel(target.Action) : null;
        var nextButton = button is { } hovered ? (hovered.Clip.Id, hovered.Action) : ((Guid, string)?)null;
        if (hoverEventButton != nextButton) { hoverEventButton = nextButton; InvalidateVisual(); }
        var hit = HitClip(point);

        var id = button is null && hit is { Track.IsLocked: false, Clip.IsLocked: false } && hit.Value.Mode != "Move" ? hit.Value.Clip.Id : (Guid?)null;
        var nextMode = id is not null ? hit!.Value.Mode : "";
        Cursor = CursorAt(point);
        if (hoverClipId == id && hoverMode == nextMode) return;
        hoverClipId = id; hoverMode = nextMode; InvalidateVisual();
    }
    private void ClearHover() { hoverClipId = null; hoverMode = ""; hoverEventButton = null; ToolTip = null; Cursor = null; InvalidateVisual(); }
    private void Text(DrawingContext dc, string text, double x, double y, Brush? color = null, double size = 12, double maxWidth = 10000,
        TextAlignment alignment = TextAlignment.Left)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size,
            color ?? Brushes.WhiteSmoke, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(1, maxWidth), MaxTextHeight = 28, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = alignment };
        dc.DrawText(ft, new Point(x, y));
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));
        var pen = FrozenPen("#383838", 1);
        var step = PixelsPerSecond >= 130 ? 1 : PixelsPerSecond >= 50 ? 2 : PixelsPerSecond >= 25 ? 5 : PixelsPerSecond >= 10 ? 10 : 20;
        var first = Math.Max(0, (int)(Offset / PixelsPerSecond / step));
        for (var n = first; n * step * PixelsPerSecond - Offset < ActualWidth - HeaderWidth; n++)
        {
            var x = HeaderWidth + n * step * PixelsPerSecond - Offset;
            dc.DrawLine(FrozenDashedPen("#444444", 1, [2, 3]), new Point(x, RulerHeight), new Point(x, ActualHeight));
            var tickFrame = Project.FrameRate.Frames(n * step); Text(dc, TimecodeFormatter.Format(tickFrame, Project.FrameRate, RulerFormat, Project.AudioSampleRate, Project.BeatsPerMinute, Project.BeatsPerMeasure, Project.RulerStartTimeFrames, rulerTick: true), x + 3, 30, Brush("#B8B8B8"), 9);
            for (var minor = 1; minor < 5; minor++)
                dc.DrawLine(pen, new Point(x + minor * step * PixelsPerSecond / 5, 40), new Point(x + minor * step * PixelsPerSecond / 5, RulerHeight));
        }
        DrawLoopRegion(dc);
        DrawMarkers(dc);
        dc.PushClip(new RectangleGeometry(new Rect(HeaderWidth, RulerHeight, Math.Max(0, ActualWidth - HeaderWidth), Math.Max(0, ActualHeight - RulerHeight))));
        var insertionY = 0.0;
        if (destinationIsNewTrack && isDragging && dragging is not null && mode == "Move")
        {
            var pSlot = Math.Clamp(destinationNewTrackIndex, 0, Project.Tracks.Count);
            insertionY = RulerHeight + pSlot * TrackHeight - VerticalOffset;

            if (pSlot == Project.Tracks.Count)
            {
                var phantomBodyRect = new Rect(HeaderWidth, insertionY + 1, Math.Max(0, ActualWidth - HeaderWidth), TrackHeight - 2);
                dc.DrawRectangle(Brush(destinationNewTrackKind == TrackKind.Video ? "#152433" : "#132E2B"),
                    FrozenDashedPen("#00D2FF", 1.5, [4, 4]), phantomBodyRect);
                Text(dc, $"[Drop to place event on new {destinationNewTrackKind.ToString().ToLowerInvariant()} layer]",
                    HeaderWidth + 20, insertionY + TrackHeight / 2 - 6, Brush("#4D7790"), 11);
            }

            var guideY = pSlot == 0 ? Math.Max(RulerHeight + 1.5, insertionY) : insertionY;
            dc.DrawLine(FrozenPen("#00D2FF", 3), new Point(HeaderWidth, guideY), new Point(ActualWidth, guideY));

            var arrowGeo = new StreamGeometry();
            using (var ctx = arrowGeo.Open())
            {
                ctx.BeginFigure(new Point(HeaderWidth - 1, guideY - 6), true, true);
                ctx.LineTo(new Point(HeaderWidth + 8, guideY), true, false);
                ctx.LineTo(new Point(HeaderWidth - 1, guideY + 6), true, false);
            }
            arrowGeo.Freeze();
            dc.DrawGeometry(Brush("#00D2FF"), null, arrowGeo);
        }

        for (var i = 0; i < Project.Tracks.Count; i++)
        {
            var track = Project.Tracks[i];
            var y = RulerHeight + i * TrackHeight - VerticalOffset;
            if (y + TrackHeight < RulerHeight || y > ActualHeight) continue;
            dc.DrawLine(FrozenPen("#111111", 3), new Point(HeaderWidth, y + TrackHeight), new Point(ActualWidth, y + TrackHeight));

            if (!destinationIsNewTrack && destination == i && isDragging && dragging is not null && mode == "Move" && track.Kind == dragTrack?.Kind)
            {
                dc.DrawRectangle(null, FrozenPen("#00D2FF", 1.5), new Rect(HeaderWidth, y + 1, Math.Max(0, ActualWidth - HeaderWidth), TrackHeight - 2));
            }

            foreach (var clip in track.Clips)
            {
                var start = clip.StartFrame;
                var duration = clip.DurationFrames;
                var cy = y;
                var isPrimaryDragged = isDragging && dragging?.Id == clip.Id;
                var isMovingMember = isDragging && dragging is not null && mode == "Move" && movingClipIds is not null && movingClipIds.Contains(clip.Id);

                if (isMovingMember)
                {
                    start = Math.Max(0, clip.StartFrame + delta);
                    if (isPrimaryDragged)
                    {
                        cy = destinationIsNewTrack
                            ? (destinationNewTrackIndex == Project.Tracks.Count ? insertionY : insertionY - TrackHeight / 2)
                            : RulerHeight + destination * TrackHeight - VerticalOffset;
                    }
                }
                else if (isPrimaryDragged)
                {
                    if (mode == "TrimLeft") { start += delta; duration -= delta; }
                    if (mode == "TrimRight") duration += delta;
                }
                var rect = new Rect(X(start), cy + 1, Math.Max(2, Project.FrameRate.Seconds(Math.Max(1, duration)) * PixelsPerSecond), TrackHeight - 2);
                if (rect.Right < HeaderWidth || rect.Left > ActualWidth) continue;
                var isMuted = clip.IsMuted || track.IsMuted;
                var selected = Selection.Contains(clip.Id);
                dc.DrawRectangle(Brush(isMuted ? "#2D2D2D" : track.Kind == TrackKind.Video ? "#233442" : "#365D61"),
                    FrozenPen(isPrimaryDragged ? "#00E5FF" : selected ? "#E4DF8F" : "#5E737C", isPrimaryDragged ? 1.5 : 1), rect);
                dc.PushClip(new RectangleGeometry(rect));
                if (ShowEventHeaders) dc.DrawRectangle(Brush(selected ? "#4D5356" : isMuted ? "#383838" : "#30383C"), null, new Rect(rect.X, rect.Y, rect.Width, 18));
                var headerOffset = 3.0;
                if (clip.IsLocked)
                {
                    var lockX = rect.X + 3;
                    var lockY = rect.Y + 3;
                    var lockPen = FrozenPen("#D0D0D0", 1.2);
                    var shackleGeo = new StreamGeometry();
                    using (var sgc = shackleGeo.Open())
                    {
                        sgc.BeginFigure(new Point(lockX + 2.5, lockY + 5), false, false);
                        sgc.ArcTo(new Point(lockX + 7.5, lockY + 5), new Size(2.5, 2.5), 0, false, SweepDirection.Clockwise, true, false);
                    }
                    shackleGeo.Freeze();
                    dc.DrawGeometry(null, lockPen, shackleGeo);
                    dc.DrawRectangle(Brush("#E0A020"), null, new Rect(lockX + 1, lockY + 5, 8, 6));
                    headerOffset = 13.0;
                }
                var groupPrefix = clip.GroupId.HasValue ? "[G] " : "";
                var takePrefix = clip.Takes is { Count: > 1 } ? $"[Take {clip.ActiveTakeIndex + 1}/{clip.Takes.Count}] " : "";
                var reversePrefix = clip.IsReversed ? "[Reverse] " : "";
                var rateSuffix = Math.Abs(clip.PlaybackRate - 1.0) > 0.001 ? $" [{clip.PlaybackRate:0.##}x]" : "";
                var mutedSuffix = clip.IsMuted ? " [Muted]" : "";
                var phaseSuffix = clip.InvertPhase ? " [Phase \u00D8]" : "";
                var displayName = $"{groupPrefix}{takePrefix}{reversePrefix}{clip.Name}{rateSuffix}{phaseSuffix}{mutedSuffix}";
                if (ShowEventNames) Text(dc, displayName, rect.X + headerOffset, rect.Y + 1, size: 11, maxWidth: Math.Max(1, rect.Width - headerOffset - 3));
                if (isPrimaryDragged && mode == "Move")
                {
                    var formattedStart = TimecodeFormatter.Format(start, Project.FrameRate, RulerFormat, Project.AudioSampleRate, Project.BeatsPerMinute, Project.BeatsPerMeasure, Project.RulerStartTimeFrames);
                    var badgeText = destinationIsNewTrack
                        ? $"[+ New {destinationNewTrackKind} Layer {destinationNewTrackIndex + 1}]  Start: {formattedStart}"
                        : $"Start: {formattedStart}  ({(delta >= 0 ? "+" : "")}{delta})";
                    Text(dc, badgeText, rect.X + headerOffset, rect.Y + 20, Brush("#00E5FF"), 10, maxWidth: Math.Max(1, rect.Width - headerOffset - 3));
                }
                if (rect.Width > 4 && track.Kind == TrackKind.Video && Thumbnails.TryGetValue(clip.MediaId, out var image))
                {
                    var height = TrackHeight - 22;
                    var width = Math.Max(1, height * image.Width / image.Height);
                    var firstImage = Math.Max(0, Math.Floor((HeaderWidth - rect.X) / width));
                    for (var x = rect.X + firstImage * width; x < Math.Min(rect.Right, ActualWidth); x += width)
                        dc.DrawImage(image, new Rect(x, rect.Y + 19, width, height));
                }
                if (track.Kind == TrackKind.Audio && Waveforms.TryGetValue(clip.MediaId, out var peaks) && peaks.Length > 0)
                {
                    MediaAsset? media = null;
                    for (var mIdx = 0; mIdx < Project.Media.Count; mIdx++)
                    {
                        if (Project.Media[mIdx].Id == clip.MediaId) { media = Project.Media[mIdx]; break; }
                    }
                    if (media is not null && media.DurationSeconds > 0)
                    {
                        var mid = rect.Y + 42;
                        var wp = FrozenPen("#A5D3B5", 1);
                        var startPx = Math.Max(0.0, HeaderWidth - rect.X);
                        var endPx = Math.Min(rect.Width, ActualWidth - rect.X);
                        for (var px = startPx; px < endPx; px += 2)
                        {
                            var t = Project.FrameRate.Seconds(clip.SourceInFrame) + px / PixelsPerSecond;
                            var index = (int)(t / media.DurationSeconds * peaks.Length);
                            var amplitude = index >= 0 && index < peaks.Length ? Math.Min(1, peaks[index] * clip.Gain) * 24 : 0;
                            dc.DrawLine(wp, new Point(rect.X + px, mid - amplitude), new Point(rect.X + px, mid + amplitude));
                        }
                    }
                }
                var fadePen = FrozenPen("#E4E4E4", 1);
                if (clip.FadeInFrames > 0) dc.DrawLine(fadePen, rect.BottomLeft, new Point(rect.Left + Math.Min(rect.Width, Project.FrameRate.Seconds(clip.FadeInFrames) * PixelsPerSecond), rect.Y + 20));
                if (clip.FadeOutFrames > 0) dc.DrawLine(fadePen, rect.BottomRight, new Point(rect.Right - Math.Min(rect.Width, Project.FrameRate.Seconds(clip.FadeOutFrames) * PixelsPerSecond), rect.Y + 20));
                DrawEnvelopes(dc, track, clip, rect);
                var edgeMode = isDragging && dragging?.Id == clip.Id ? mode : hoverClipId == clip.Id ? hoverMode : "";
                if (edgeMode is "TrimLeft" or "TrimRight")
                {
                    var edgeX = edgeMode == "TrimLeft" ? rect.Left + 1 : rect.Right - 1;
                    dc.DrawLine(FrozenPen("#F5F5F5", 2), new Point(edgeX, rect.Top + 2), new Point(edgeX, rect.Bottom - 2));
                    dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(edgeX - 2, rect.Top + rect.Height / 2 - 7, 4, 14));
                }
                if (dragging is null) DrawEventButtons(dc, track, clip, rect);
                dc.Pop();
            }
            DrawTrackEnvelopes(dc, track, new Rect(HeaderWidth, y + 1, Math.Max(0, ActualWidth - HeaderWidth), TrackHeight - 2));
        }
        dc.Pop();
        dc.DrawRectangle(Brush("#262626"), null, new Rect(0, 0, HeaderWidth, ActualHeight));
        Text(dc, TimecodeFormatter.Format(Position, Project.FrameRate, RulerFormat, Project.AudioSampleRate, Project.BeatsPerMinute, Project.BeatsPerMeasure, Project.RulerStartTimeFrames), 126, 12, size: 20, maxWidth: 130);
        dc.PushClip(new RectangleGeometry(new Rect(0, RulerHeight, HeaderWidth, Math.Max(0, ActualHeight - RulerHeight))));
        for (var i = 0; i < Project.Tracks.Count; i++)
        {
            var t = Project.Tracks[i]; var y = RulerHeight + i * TrackHeight - VerticalOffset;
            var trackAccentColor = t.TrackColor ?? (t.Kind == TrackKind.Video ? "#448DBD" : "#65BECE");
            dc.DrawRectangle(Brush(t.IsLocked ? "#242424" : "#2B2B2B"), FrozenPen("#181818", 1), new Rect(0, y + 1, HeaderWidth - 3, TrackHeight - 1));
            dc.DrawRectangle(Brush(trackAccentColor), null, new Rect(1, y + 1, 23, TrackHeight - 2));
            dc.DrawRectangle(Brush(trackAccentColor), null, new Rect(0, y + 1, 3, TrackHeight - 1));
            if (IsKeyboardFocusWithin && i == ActiveTrackIndex)
                dc.DrawRectangle(null, FrozenPen("#83C9FF", 1), new Rect(4, y + 3, HeaderWidth - 9, TrackHeight - 5));
            Text(dc, "···", 4, y + 1, size: 12);
            Text(dc, $"{i + 1}", 5, y + 24, size: 11);
            var indent = t.Kind == TrackKind.Video ? t.CompositeNestingLevel * 14 : 0;
            if (t.CompositeNestingLevel > 0)
            {
                var branchPen = FrozenPen("#666666", 1.2);
                var branchX = 24 + (t.CompositeNestingLevel - 1) * 14 + 7;
                dc.DrawLine(branchPen, new Point(branchX, y), new Point(branchX, y + TrackHeight / 2));
                dc.DrawLine(branchPen, new Point(branchX, y + TrackHeight / 2), new Point(branchX + 7, y + TrackHeight / 2));
            }
            if (t.IsAdjustmentTrack)
            {
                var adjRect = new Rect(28 + indent, y + 4, 28, 16);
                dc.DrawRoundedRectangle(Brush("#6A1B9A"), null, adjRect, 2, 2);
                Text(dc, "ADJ", adjRect.X + 3, adjRect.Y + 2, Brushes.White, 9.5);
            }
            var nameX = 30 + indent + (t.IsAdjustmentTrack ? 30 : 0);
            Text(dc, t.Name, nameX, y + 5, size: 11, maxWidth: Math.Max(20, 104 - indent - (t.IsAdjustmentTrack ? 30 : 0)));

            // Track Automation mode button [A] at x = 138, y + 4
            var autoBounds = TrackButtonBounds(t.Id, "Automation");
            var autoBrush = t.AutomationMode switch
            {
                TrackAutomationMode.AutomationRead => Brush("#2E7D32"),
                TrackAutomationMode.AutomationTouch => Brush("#C62828"),
                TrackAutomationMode.AutomationLatch => Brush("#E65100"),
                TrackAutomationMode.AutomationWrite => Brush("#D32F2F"),
                _ => Brush("#2A2A2A")
            };
            dc.DrawRectangle(autoBrush, new Pen(Brush("#383838"), 1), autoBounds);
            Text(dc, "A", autoBounds.X + 6, autoBounds.Y + 2, t.AutomationMode == TrackAutomationMode.AutomationOff ? Brush("#888888") : Brush("#FFFFFF"), 10.5);
            if (t.Kind == TrackKind.Video && t.CompositeMode != TrackCompositeMode.SourceAlpha) Text(dc, $"[{t.CompositeMode}]", 180, y + 5, Brush("#00D2FF"), 10);
            foreach (var (label, action, on) in new[] { ("M", "Mute", t.IsMuted), ("S", "Solo", t.IsSolo), ("L", "Lock", t.IsLocked) })
            {
                var bounds = TrackButtonBounds(t.Id, action);
                if (on)
                {
                    var activeBrush = action switch
                    {
                        "Solo" => Brush("#C89000"), // VEGAS Pro amber/gold solo highlight
                        "Mute" => Brush("#2B6CB0"), // VEGAS Pro blue mute highlight
                        _ => Brush("#438AC8")
                    };
                    dc.DrawRectangle(activeBrush, null, bounds);
                }
                Text(dc, label, bounds.X + 5, bounds.Y + 1);
            }
            var gain = ReadTrackLevel(t.Id, "Gain"); var opacity = ReadTrackLevel(t.Id, "Opacity"); var pan = ReadTrackLevel(t.Id, "Pan");
            if (t.Kind == TrackKind.Video)
            {
                Text(dc, $"Level: {opacity * 100:0.0} %", 31, y + 28, Brush("#B8B8B8"), 11, 83);
            }
            else
            {
                string volText = gain <= 0 ? "Vol: −∞ dB" : (20 * Math.Log10(gain)) is var db && db > 0.05 ? $"Vol: +{db:0.0} dB" : $"Vol: {db:0.0} dB";
                Text(dc, volText, 31, y + 28, Brush("#B8B8B8"), 11, 75);
                string panText = Math.Abs(pan) < .005 ? "Pan: Center" : $"Pan: {Math.Abs(pan) * 100:0}% {(pan < 0 ? "L" : "R")}";
                Text(dc, panText, 31, y + 48, Brush("#B8B8B8"), 11, 75);
            }
            if (t.Kind == TrackKind.Audio) Text(dc, Math.Abs(pan) < .005 ? "Center" : $"{Math.Abs(pan) * 100:0} % {(pan < 0 ? "L" : "R")}", 137, y + 28, Brush("#B8B8B8"), 11, 50);
            foreach (var level in t.Kind == TrackKind.Video ? new[] { "Opacity" } : new[] { "Gain", "Pan" })
            {
                var bounds = TrackLevelBounds(t.Id, level); var center = bounds.Y + bounds.Height / 2;
                dc.DrawLine(new Pen(Brush("#141414"), 2), new Point(bounds.Left + 4, center), new Point(bounds.Right - 4, center));
                var thumb = bounds.Left + 4 + LevelFraction(level, ReadTrackLevel(t.Id, level)) * (bounds.Width - 8);
                dc.DrawRectangle(Brush(t.IsLocked ? "#666666" : "#C4C4C4"), null, new Rect(thumb - 5, center - 5, 10, 10));
                dc.DrawLine(new Pen(Brush("#555555"), 1), new Point(thumb, center - 3), new Point(thumb, center + 3));
                if (IsKeyboardFocusWithin && levelTrack == t.Id && levelKind == level)
                    dc.DrawRectangle(null, new Pen(Brush("#91C9EF"), 1), bounds);
            }
            if (t.Kind == TrackKind.Video)
            {
                var motionBounds = TrackButtonBounds(t.Id, "Motion");
                var hasMotion = t.Motion is not null && t.Motion.MotionKeyframes.Count > 0;
                if (hasMotion)
                {
                    dc.DrawRectangle(Brush("#2B6CB0"), null, motionBounds);
                }
                var mPen = new Pen(hasMotion ? Brush("#FFFFFF") : Brush("#C4C4C4"), 1.2);
                var mx = motionBounds.X + 4.5;
                var my = motionBounds.Y + 4.5;
                dc.DrawRectangle(null, mPen, new Rect(mx, my, 11, 10));
                dc.DrawLine(mPen, new Point(mx + 3, my + 3), new Point(mx + 8, my + 3));
                dc.DrawLine(mPen, new Point(mx + 3, my + 7), new Point(mx + 8, my + 7));
                var fxBounds = TrackButtonBounds(t.Id, "FX");
                var hasFx = t.VideoEffects.Count > 0;
                if (hasFx)
                {
                    dc.DrawRectangle(Brush("#2E7D32"), null, fxBounds);
                }
                Text(dc, "fx", fxBounds.X + 4, fxBounds.Y + 1, hasFx ? Brush("#FFFFFF") : Brush("#C4C4C4"), 11);
                dc.DrawRectangle(Brush(t.IsVisible ? "#438AC8" : "#404040"), null, TrackButtonBounds(t.Id, "Visible"));
                Text(dc, "V", 235, y + 47);

                var compBounds = TrackButtonBounds(t.Id, "CompChild");
                var isChild = t.CompositeNestingLevel > 0;
                var canIndent = i > 0 && Project.Tracks.Take(i).Any(tr => tr.Kind == TrackKind.Video);
                if (isChild || canIndent)
                {
                    dc.DrawRectangle(Brush("#2A2A2A"), new Pen(Brush("#383838"), 1), compBounds);
                    var btnPen = new Pen(Brush(isChild ? "#E0E0E0" : "#888888"), 1.2);
                    var cx = compBounds.X + 10.5; var cy = compBounds.Y + 10;
                    if (isChild)
                    {
                        dc.DrawLine(btnPen, new Point(cx + 4, cy + 4), new Point(cx - 1, cy + 4));
                        dc.DrawLine(btnPen, new Point(cx - 1, cy + 4), new Point(cx - 1, cy - 3));
                        dc.DrawLine(btnPen, new Point(cx - 4, cy), new Point(cx - 1, cy - 3));
                        dc.DrawLine(btnPen, new Point(cx + 2, cy), new Point(cx - 1, cy - 3));
                    }
                    else
                    {
                        dc.DrawLine(btnPen, new Point(cx - 4, cy - 4), new Point(cx - 1, cy - 4));
                        dc.DrawLine(btnPen, new Point(cx - 1, cy - 4), new Point(cx - 1, cy + 3));
                        dc.DrawLine(btnPen, new Point(cx - 4, cy), new Point(cx - 1, cy + 3));
                        dc.DrawLine(btnPen, new Point(cx + 2, cy), new Point(cx - 1, cy + 3));
                    }
                }
            }
            else if (t.Kind == TrackKind.Audio)
            {
                if (t.BusTrackId.HasValue && Project.AudioBuses != null)
                {
                    var bus = Project.AudioBuses.FirstOrDefault(b => b.Id == t.BusTrackId.Value);
                    if (bus != null)
                    {
                        Text(dc, $"[{bus.Name}]", 82, y + 5, Brush("#00D2FF"), 9.5);
                    }
                }
                // Track Audio FX button [fx] at x = 161
                var fxBounds = TrackButtonBounds(t.Id, "FX");
                var hasFx = t.AudioEffects.Count > 0;
                if (hasFx)
                {
                    dc.DrawRectangle(Brush("#2E7D32"), null, fxBounds);
                }
                Text(dc, "fx", fxBounds.X + 4, fxBounds.Y + 1, hasFx ? Brush("#FFFFFF") : Brush("#C4C4C4"), 11);

                // Arm for Record button [â—Ź] at x = 207, y + 46
                var armBounds = TrackButtonBounds(t.Id, "Arm");
                if (t.ArmRecord)
                {
                    dc.DrawRectangle(Brush("#D32F2F"), null, armBounds);
                    dc.DrawEllipse(Brushes.White, null, new Point(armBounds.X + 10.5, armBounds.Y + 10), 4, 4);
                }
                else
                {
                    dc.DrawRectangle(Brush("#2A2A2A"), new Pen(Brush("#383838"), 1), armBounds);
                    dc.DrawEllipse(null, new Pen(Brush("#888888"), 1.2), new Point(armBounds.X + 10.5, armBounds.Y + 10), 4, 4);
                    dc.DrawEllipse(Brush("#AA3333"), null, new Point(armBounds.X + 10.5, armBounds.Y + 10), 2, 2);
                }

                // Track Phase Inversion button [Ã˜] at x = 230, y + 46
                var phaseBounds = TrackButtonBounds(t.Id, "Phase");
                if (t.InvertPhase)
                {
                    dc.DrawRectangle(Brush("#2B6CB0"), null, phaseBounds);
                }
                else
                {
                    dc.DrawRectangle(Brush("#2A2A2A"), new Pen(Brush("#383838"), 1), phaseBounds);
                }
                Text(dc, "\u00D8", phaseBounds.X + 5, phaseBounds.Y + 1, t.InvertPhase ? Brush("#FFFFFF") : Brush("#C4C4C4"), 11);
            }
        }
        if (destinationIsNewTrack && isDragging && dragging is not null && mode == "Move")
        {
            var pSlot = Math.Clamp(destinationNewTrackIndex, 0, Project.Tracks.Count);
            var headerY = RulerHeight + pSlot * TrackHeight - VerticalOffset;

            if (pSlot == Project.Tracks.Count)
            {
                var headerRect = new Rect(0, headerY + 1, HeaderWidth - 3, TrackHeight - 1);
                dc.DrawRectangle(Brush("#1B232A"), new Pen(Brush("#00D2FF"), 1.5) { DashStyle = new DashStyle([4, 4], 0) }, headerRect);
                dc.DrawRectangle(Brush(destinationNewTrackKind == TrackKind.Video ? "#448DBD" : "#65BECE"), null, new Rect(1, headerY + 1, 23, TrackHeight - 2));
                Text(dc, "+", 6, headerY + 24, Brush("#00D2FF"), 14);
                Text(dc, $"[New {destinationNewTrackKind} Layer]", 30, headerY + 8, Brush("#00D2FF"), 11, maxWidth: 200);
                Text(dc, $"Insert at layer {pSlot + 1}", 30, headerY + 26, Brush("#88B0C8"), 10, maxWidth: 200);
            }
            else
            {
                var guideY = pSlot == 0 ? Math.Max(RulerHeight + 1.5, headerY) : headerY;
                dc.DrawLine(FrozenPen("#00D2FF", 3), new Point(0, guideY), new Point(HeaderWidth - 3, guideY));

                var badgeRect = new Rect(12, guideY - 11, HeaderWidth - 28, 22);
                dc.DrawRoundedRectangle(Brush("#0C1822"), FrozenPen("#00D2FF", 1.5), badgeRect, 4, 4);
                dc.DrawRectangle(Brush(destinationNewTrackKind == TrackKind.Video ? "#448DBD" : "#65BECE"), null,
                    new Rect(badgeRect.X + 6, badgeRect.Y + 4, 5, 14));
                Text(dc, $"+ [New {destinationNewTrackKind} Layer {pSlot + 1}]", badgeRect.X + 16, badgeRect.Y + 4, Brush("#00E5FF"), 10.5);
            }
        }
        dc.Pop();
        var playX = X(Position);
        if (playX >= HeaderWidth && playX < ActualWidth)
        {
            dc.DrawLine(FrozenPen("#F0F0F0", 1), new Point(playX, 32), new Point(playX, ActualHeight));
            var playheadGeo = new StreamGeometry();
            using (var ctx = playheadGeo.Open())
            {
                ctx.BeginFigure(new Point(playX, 32), true, true);
                ctx.LineTo(new Point(playX - 4, 26), true, false);
                ctx.LineTo(new Point(playX + 4, 26), true, false);
            }
            playheadGeo.Freeze();
            dc.DrawGeometry(Brush("#D8B85D"), null, playheadGeo);
        }
        if (snappedFrame.HasValue && isDragging && dragging is not null)
        {
            var snapX = X(snappedFrame.Value);
            if (snapX >= HeaderWidth && snapX <= ActualWidth)
            {
                dc.DrawLine(FrozenDashedPen("#00E5FF", 1.5, [4, 2]), new Point(snapX, RulerHeight), new Point(snapX, ActualHeight));
            }
        }
        if (isMarqueeSelecting && marqueeRect.Width > 0 && marqueeRect.Height > 0)
        {
            var marqueePen = FrozenDashedPen("#FFE082", 1, [3, 3]);
            dc.DrawRectangle(Brush("#2DE4DF8F"), marqueePen, marqueeRect);
        }
        if (isZooming && zoomRect.Width > 0 && zoomRect.Height > 0)
        {
            var zoomPen = FrozenDashedPen("#00D2FF", 1.2, [4, 4]);
            dc.DrawRectangle(Brush("#2300D2FF"), zoomPen, zoomRect);
        }
        if (Project.Tracks.Count == 0) Text(dc, "Import media, then add it to the timeline", HeaderWidth + 30, RulerHeight + 35, Brush("#B8B8B8"));
    }

    public static string FormatTimecode(long frame, Rational rate) => TimecodeFormatter.FormatStandard(frame, rate);
    public static string FormatTimecode(long frame, Rational rate, RulerFormat format) => TimecodeFormatter.Format(frame, rate, format);
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); Focus(); origin = e.GetPosition(this);
        isDragging = false; alreadySelectedOnDown = false;
        if (BeginEventButton(origin)) { e.Handled = true; return; }
        if (HandleEnvelopeMouseDown(origin, e)) { e.Handled = true; return; }
        if (HitMarker(origin) is { } hitMarker)
        {
            SeekRequested?.Invoke(hitMarker.Frame);
            MarkerSelected?.Invoke(hitMarker);
            if (e.ClickCount == 2) MarkerEditRequested?.Invoke(hitMarker);
            e.Handled = true;
            return;
        }
        if (origin.X < HeaderWidth)
        {
            var track = TrackAt(origin.Y);
            if (track is not null)
            {
                ActivateTrack(Project.Tracks.IndexOf(track));
                foreach (var level in track.Kind == TrackKind.Video ? new[] { "Opacity" } : new[] { "Gain", "Pan" })
                {
                    if (!TrackLevelBounds(track.Id, level).Contains(origin)) continue;
                    FocusTrackLevel(track.Id, level);
                    if (track.IsLocked) return;
                    if (e.ClickCount == 2) { SetTrackLevel(track.Id, level, level == "Pan" ? 0 : 1); return; }
                    adjustingLevel = true; pendingLevel = LevelAt(origin.X); CaptureMouse(); InvalidateVisual(); return;
                }
                foreach (var action in new[] { "Mute", "Solo", "Lock", "Visible", "FX", "Motion", "Phase", "Arm", "Automation", "CompChild" })
                {
                    if (action == "Automation")
                    {
                        if (TrackButtonBounds(track.Id, "Automation").Contains(origin))
                        {
                            var nextMode = track.AutomationMode == TrackAutomationMode.AutomationOff
                                ? TrackAutomationMode.AutomationRead
                                : TrackAutomationMode.AutomationOff;
                            TrackAutomationModeRequested?.Invoke(track.Id, nextMode);
                            break;
                        }
                    }
                    else if (action == "Arm")
                    {
                        if (track.Kind == TrackKind.Audio && TrackButtonBounds(track.Id, "Arm").Contains(origin))
                        {
                            InvokeTrackAction(track.Id, "Arm");
                            break;
                        }
                    }
                    else if (action == "FX")
                    {
                        if (track.Kind == TrackKind.Video && TrackButtonBounds(track.Id, "FX").Contains(origin))
                        {
                            TrackFxRequested?.Invoke(track.Id);
                            break;
                        }
                        else if (track.Kind == TrackKind.Audio && TrackButtonBounds(track.Id, "FX").Contains(origin))
                        {
                            TrackAudioFxRequested?.Invoke(track.Id);
                            break;
                        }
                    }
                    else if (action == "Motion")
                    {
                        if (track.Kind == TrackKind.Video && TrackButtonBounds(track.Id, "Motion").Contains(origin))
                        {
                            TrackMotionRequested?.Invoke(track.Id);
                            break;
                        }
                    }
                    else if (action == "Phase")
                    {
                        if (track.Kind == TrackKind.Audio && TrackButtonBounds(track.Id, "Phase").Contains(origin))
                        {
                            TrackPhaseInvertRequested?.Invoke(track.Id);
                            break;
                        }
                    }
                    else if (action == "CompChild")
                    {
                        if (track.Kind == TrackKind.Video && TrackButtonBounds(track.Id, "CompChild").Contains(origin))
                        {
                            if (track.CompositeNestingLevel > 0)
                            {
                                TrackMakeCompositingParentRequested?.Invoke(track.Id);
                            }
                            else
                            {
                                TrackMakeCompositingChildRequested?.Invoke(track.Id);
                            }
                            break;
                        }
                    }
                    else if ((action != "Visible" || track.Kind == TrackKind.Video) && TrackButtonBounds(track.Id, action).Contains(origin))
                    {
                        InvokeTrackAction(track.Id, action);
                        break;
                    }
                }
            }
            return;
        }
        if (BeginLoopRegion(origin)) { e.Handled = true; return; }
        if (EditTool == EditToolMode.Zoom)
        {
            isZooming = true;
            zoomStart = origin;
            zoomRect = new Rect(origin, origin);
            CaptureMouse();
            e.Handled = true;
            return;
        }
        if (EditTool == EditToolMode.Selection)
        {
            isMarqueeSelecting = true;
            marqueeStart = origin;
            marqueeRect = new Rect(origin, origin);
            marqueeModifiers = Keyboard.Modifiers;
            CaptureMouse();
            e.Handled = true;
            return;
        }
        if (EditTool == EditToolMode.Envelope)
        {
            seeking = true;
            SeekRequested?.Invoke(FrameAt(origin.X));
            CaptureMouse();
            e.Handled = true;
            return;
        }
        var hit = HitClip(origin);
        dragTrack = hit?.Track; dragging = hit?.Clip;
        if (dragging is null)
        {
            seeking = true; SeekRequested?.Invoke(FrameAt(origin.X)); CaptureMouse(); return;
        }
        alreadySelectedOnDown = Selection.Contains(dragging.Id);
        if (!alreadySelectedOnDown || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SelectClip(dragging.Id, Keyboard.Modifiers.HasFlag(ModifierKeys.Control), ensureVisible: false);
        }
        else
        {
            activeClipId = dragging.Id;
            ActivateTrack(Project.Tracks.IndexOf(dragTrack!));
            InvalidateVisual();
        }
        if (dragTrack!.IsLocked || dragging.IsLocked) { dragging = null; return; }
        mode = hit!.Value.Mode;
        destination = Project.Tracks.IndexOf(dragTrack); delta = 0;
        destinationIsNewTrack = false;
        destinationNewTrackIndex = destination;
        destinationNewTrackKind = dragTrack.Kind;
        snappedFrame = null;
        movingClipIds = GetMovingClipIds(dragging);
        CaptureMouse();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); var p = e.GetPosition(this);
        if (HandleEnvelopeMouseMove(p)) return;
        if (isZooming)
        {
            var minX = Math.Max(HeaderWidth, Math.Min(zoomStart.X, p.X));
            var maxX = Math.Min(ActualWidth, Math.Max(zoomStart.X, p.X));
            var minY = Math.Max(RulerHeight, Math.Min(zoomStart.Y, p.Y));
            var maxY = Math.Min(ActualHeight, Math.Max(zoomStart.Y, p.Y));
            zoomRect = new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
            InvalidateVisual();
            return;
        }
        if (isMarqueeSelecting)
        {
            var minX = Math.Max(HeaderWidth, Math.Min(marqueeStart.X, p.X));
            var maxX = Math.Min(ActualWidth, Math.Max(marqueeStart.X, p.X));
            var minY = Math.Max(RulerHeight, Math.Min(marqueeStart.Y, p.Y));
            var maxY = Math.Min(ActualHeight, Math.Max(marqueeStart.Y, p.Y));
            marqueeRect = new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
            InvalidateVisual();
            return;
        }
        if (selectingLoop) { UpdateLoopRegion(p); return; }
        if (pressedEventButton is not null) { UpdateHover(p); return; }
        if (adjustingLevel) { pendingLevel = LevelAt(p.X); InvalidateVisual(); return; }
        if (seeking) { SeekRequested?.Invoke(FrameAt(p.X)); return; }
        if (dragging is null) { UpdateHover(p); return; }

        if (!isDragging)
        {
            if ((p - origin).Length < DragDistanceThreshold) return;
            isDragging = true;
            Cursor = mode == "Move" ? Cursors.SizeAll : Cursors.SizeWE;
        }

        // Edge auto-scrolling during drag (Vegas Pro feel)
        if (p.X > ActualWidth - 30) Offset = Math.Max(0, Offset + 15);
        else if (p.X < HeaderWidth + 30 && Offset > 0) Offset = Math.Max(0, Offset - 15);
        if (p.Y > ActualHeight - 20)
        {
            var maxV = Math.Max(0, Project.Tracks.Count * TrackHeight - ActualHeight + RulerHeight);
            VerticalOffset = Math.Min(maxV, VerticalOffset + 15);
        }
        else if (p.Y < RulerHeight + 20 && VerticalOffset > 0) VerticalOffset = Math.Max(0, VerticalOffset - 15);

        delta = Project.FrameRate.Frames((p.X - origin.X) / PixelsPerSecond);
        if (mode == "Move")
        {
            var minStart = movingClipIds is not null && movingClipIds.Count > 0
                ? Project.Tracks.SelectMany(t => t.Clips).Where(c => movingClipIds.Contains(c.Id)).Min(c => c.StartFrame)
                : dragging.StartFrame;
            delta = Math.Max(-minStart, delta);

            var sourceTrackIdx = dragTrack is not null ? Project.Tracks.IndexOf(dragTrack) : (int?)null;
            var targetResult = EvaluateDragTarget(p.Y, dragTrack!.Kind, sourceTrackIdx);
            destinationIsNewTrack = targetResult.IsNewTrack;
            destinationNewTrackKind = targetResult.NewTrackKind;
            if (targetResult.IsNewTrack)
            {
                destinationNewTrackIndex = targetResult.TrackIndex;
                Cursor = Cursors.SizeAll;
            }
            else
            {
                var targetTrack = Project.Tracks[targetResult.TrackIndex];
                if (targetTrack.IsLocked)
                {
                    Cursor = Cursors.No;
                }
                else
                {
                    destination = targetResult.TrackIndex;
                    Cursor = Cursors.SizeAll;
                }
            }

            snappedFrame = null;
            if (Snapping)
            {
                var ignoreIds = movingClipIds ?? Selection;
                var points = Project.Tracks.SelectMany(t => t.Clips)
                    .Where(c => !ignoreIds.Contains(c.Id))
                    .SelectMany(c => new[] { c.StartFrame, c.EndFrame })
                    .Append(Position)
                    .Append(0)
                    .Concat(Project.Markers.Select(m => m.Frame));
                var start = dragging.StartFrame + delta;
                var end = start + dragging.DurationFrames;
                var threshold = Math.Max(1, Project.FrameRate.Frames(10 / PixelsPerSecond));

                long bestSnapDelta = 0;
                long? bestSnapPoint = null;
                var minDistance = long.MaxValue;

                foreach (var pt in points)
                {
                    var distStart = pt - start;
                    if (Math.Abs(distStart) <= threshold && Math.Abs(distStart) < minDistance)
                    {
                        minDistance = Math.Abs(distStart);
                        bestSnapDelta = distStart;
                        bestSnapPoint = pt;
                    }
                    var distEnd = pt - end;
                    if (Math.Abs(distEnd) <= threshold && Math.Abs(distEnd) < minDistance)
                    {
                        minDistance = Math.Abs(distEnd);
                        bestSnapDelta = distEnd;
                        bestSnapPoint = pt;
                    }
                }

                if (bestSnapPoint.HasValue)
                {
                    delta = Math.Max(-minStart, delta + bestSnapDelta);
                    snappedFrame = bestSnapPoint.Value;
                }
            }
        }
        else if (mode == "Slip")
        {
            delta = Math.Clamp(delta, -dragging.SourceInFrame, long.MaxValue - dragging.SourceInFrame);
        }
        else if (mode == "Slide")
        {
            delta = Math.Clamp(delta, -dragging.StartFrame, long.MaxValue - dragging.StartFrame);
        }
        else if (mode is "TimeStretchLeft" or "TimeStretchRight")
        {
            if (mode == "TimeStretchRight")
            {
                var minDelta = 1 - dragging.DurationFrames;
                var maxDelta = (long)(dragging.DurationFrames * 4.0) - dragging.DurationFrames;
                delta = Math.Clamp(delta, minDelta, Math.Max(minDelta, maxDelta));
            }
            else
            {
                var maxDelta = dragging.DurationFrames - 1;
                delta = Math.Clamp(delta, -dragging.StartFrame, maxDelta);
            }
        }
        else if (mode == "TrimLeft") delta = Math.Clamp(delta,
            dragging.VideoSourceMode == VideoSourceMode.Bounded ? Math.Max(-dragging.SourceInFrame, -dragging.StartFrame) : -dragging.StartFrame,
            dragging.DurationFrames - 1);
        else
        {
            var media = Project.Media.First(m => m.Id == dragging.MediaId);
            var maximum = dragging.VideoSourceMode == VideoSourceMode.Bounded
                ? Math.Max(0, Project.FrameRate.Frames(media.DurationSeconds) - dragging.SourceInFrame - dragging.DurationFrames)
                : long.MaxValue - dragging.StartFrame - dragging.DurationFrames;
            delta = Math.Clamp(delta, 1 - dragging.DurationFrames, maximum);
        }
        InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        if (isZooming)
        {
            isZooming = false;
            ReleaseMouseCapture();
            var box = zoomRect;
            zoomRect = Rect.Empty;
            if ((p - zoomStart).Length >= DragDistanceThreshold && box.Width > 10)
            {
                var startFrame = FrameAt(box.Left);
                var endFrame = FrameAt(box.Right);
                if (endFrame > startFrame)
                {
                    var durSec = Project.FrameRate.Seconds(endFrame - startFrame);
                    var viewWidth = Math.Max(100, ActualWidth - HeaderWidth);
                    var targetPps = Math.Clamp(viewWidth / durSec, 5, 2000);
                    PixelsPerSecond = targetPps;
                    Offset = Math.Max(0, Project.FrameRate.Seconds(startFrame) * PixelsPerSecond);
                    ZoomChanged?.Invoke(PixelsPerSecond);
                }
            }
            else
            {
                var clickFrame = FrameAt(p.X);
                var newPps = Math.Min(2000, PixelsPerSecond * 1.5);
                var clickSec = Project.FrameRate.Seconds(clickFrame);
                var newOffset = Math.Max(0, HeaderWidth + clickSec * newPps - p.X);
                PixelsPerSecond = newPps;
                Offset = newOffset;
                ZoomChanged?.Invoke(PixelsPerSecond);
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (isMarqueeSelecting)
        {
            isMarqueeSelecting = false;
            ReleaseMouseCapture();
            var box = marqueeRect;
            marqueeRect = Rect.Empty;
            if ((p - marqueeStart).Length >= DragDistanceThreshold && box.Width > 2 && box.Height > 2)
            {
                var hitClips = new HashSet<Guid>();
                for (int tIdx = 0; tIdx < Project.Tracks.Count; tIdx++)
                {
                    var track = Project.Tracks[tIdx];
                    var trackY = RulerHeight + tIdx * TrackHeight - VerticalOffset;
                    var trackRect = new Rect(HeaderWidth, trackY, Math.Max(0, ActualWidth - HeaderWidth), TrackHeight);
                    if (!box.IntersectsWith(trackRect)) continue;

                    foreach (var clip in track.Clips)
                    {
                        var clipX = X(clip.StartFrame);
                        var clipW = Math.Max(2, Project.FrameRate.Seconds(clip.DurationFrames) * PixelsPerSecond);
                        var clipRect = new Rect(clipX, trackY + 1, clipW, TrackHeight - 2);
                        if (box.IntersectsWith(clipRect))
                        {
                            hitClips.Add(clip.Id);
                        }
                    }
                }

                var finalHitClips = new HashSet<Guid>();
                foreach (var id in hitClips)
                {
                    var clip = Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == id);
                    if (clip != null && !ShouldIgnoreGroups && clip.GroupId.HasValue)
                    {
                        var groupClips = Project.Tracks.SelectMany(t => t.Clips).Where(c => c.GroupId == clip.GroupId.Value).Select(c => c.Id);
                        foreach (var gId in groupClips) finalHitClips.Add(gId);
                    }
                    else
                    {
                        finalHitClips.Add(id);
                    }
                }

                if (marqueeModifiers.HasFlag(ModifierKeys.Control))
                {
                    foreach (var id in finalHitClips)
                    {
                        if (Selection.Contains(id)) Selection.Remove(id);
                        else Selection.Add(id);
                    }
                    activeClipId = Selection.Count > 0 ? finalHitClips.FirstOrDefault(id => Selection.Contains(id)) : null;
                }
                else if (marqueeModifiers.HasFlag(ModifierKeys.Shift))
                {
                    foreach (var id in finalHitClips) Selection.Add(id);
                    if (finalHitClips.Count > 0) activeClipId = finalHitClips.First();
                }
                else
                {
                    Selection.Clear();
                    foreach (var id in finalHitClips) Selection.Add(id);
                    activeClipId = finalHitClips.FirstOrDefault();
                }

                SelectionChanged?.Invoke();
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            else
            {
                var hit = HitClip(p);
                if (hit is not null)
                {
                    SelectClip(hit.Value.Clip.Id, marqueeModifiers.HasFlag(ModifierKeys.Control), ensureVisible: false);
                }
                else
                {
                    if (!marqueeModifiers.HasFlag(ModifierKeys.Control) && !marqueeModifiers.HasFlag(ModifierKeys.Shift))
                    {
                        Selection.Clear();
                        activeClipId = null;
                        SelectionChanged?.Invoke();
                    }
                    SeekRequested?.Invoke(FrameAt(p.X));
                }
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }
        if (selectingLoop)
        {
            UpdateLoopRegion(e.GetPosition(this)); selectingLoop = false;
            ReleaseMouseCapture(); e.Handled = true; return;
        }
        if (pressedEventButton is not null)
        {
            CompleteEventButton(e.GetPosition(this)); e.Handled = true; return;
        }
        if (HandleEnvelopeMouseUp(e.GetPosition(this))) { e.Handled = true; return;
        }
        var commitLevel = adjustingLevel;
        adjustingLevel = false;
        p = e.GetPosition(this);
        base.OnMouseLeftButtonUp(e);
        seeking = false;

        if (commitLevel)
        {
            SetTrackLevel(levelTrack!.Value, levelKind!, pendingLevel);
            ReleaseMouseCapture();
            UpdateHover(p);
            return;
        }

        CompleteDragGesture(p, Keyboard.Modifiers);
        ReleaseMouseCapture();
    }

    internal void CompleteDragGesture(Point p, ModifierKeys modifiers)
    {
        var wasDragging = isDragging;
        var clip = dragging;
        var commitMode = mode;
        var commitDelta = delta;
        var commitDestination = destination;
        var commitIsNewTrack = destinationIsNewTrack;
        var commitNewTrackIndex = destinationNewTrackIndex;
        var commitNewTrackKind = destinationNewTrackKind;
        var commitDragTrack = dragTrack;
        var commitOrigin = origin;
        var commitAlreadySelected = alreadySelectedOnDown;
        var commitIgnoreGroups = ShouldIgnoreGroups || modifiers.HasFlag(ModifierKeys.Alt);

        // Reset drag fields before releasing capture so OnLostMouseCapture is a no-op
        isDragging = false;
        dragging = null;
        dragTrack = null;
        mode = "";
        delta = 0;
        destinationIsNewTrack = false;
        movingClipIds = null;
        snappedFrame = null;
        alreadySelectedOnDown = false;
        seeking = false;

        if (clip is not null)
        {
            if (!wasDragging)
            {
                if (commitAlreadySelected && !modifiers.HasFlag(ModifierKeys.Control))
                {
                    SelectClip(clip.Id, false, ensureVisible: false);
                }
                SeekRequested?.Invoke(FrameAt(commitOrigin.X));
            }
            else if (commitMode == "Move")
            {
                if (commitIsNewTrack)
                {
                    EditRequested?.Invoke(new ClipGesture(
                        clip.Id,
                        Guid.Empty,
                        clip.StartFrame + commitDelta,
                        clip.SourceInFrame,
                        clip.DurationFrames,
                        "Move",
                        NewTrackIndex: commitNewTrackIndex,
                        NewTrackKind: commitNewTrackKind,
                        Ripple: Ripple,
                        RippleMode: RippleMode,
                        IgnoreGroups: commitIgnoreGroups));
                }
                else if (commitDestination >= 0 && commitDestination < Project.Tracks.Count &&
                         !Project.Tracks[commitDestination].IsLocked &&
                         (commitDelta != 0 || Project.Tracks[commitDestination].Id != commitDragTrack?.Id))
                {
                    EditRequested?.Invoke(new ClipGesture(
                        clip.Id,
                        Project.Tracks[commitDestination].Id,
                        clip.StartFrame + commitDelta,
                        clip.SourceInFrame,
                        clip.DurationFrames,
                        "Move",
                        Ripple: Ripple,
                        RippleMode: RippleMode,
                        IgnoreGroups: commitIgnoreGroups));
                }
            }
            else if (commitMode == "Slip")
            {
                var trackId = commitDestination >= 0 && commitDestination < Project.Tracks.Count
                    ? Project.Tracks[commitDestination].Id
                    : (commitDragTrack?.Id ?? Guid.Empty);
                EditRequested?.Invoke(new ClipGesture(
                    clip.Id,
                    trackId,
                    clip.StartFrame,
                    clip.SourceInFrame + commitDelta,
                    clip.DurationFrames,
                    "Slip",
                    IgnoreGroups: commitIgnoreGroups));
            }
            else if (commitMode == "Slide")
            {
                var trackId = commitDestination >= 0 && commitDestination < Project.Tracks.Count
                    ? Project.Tracks[commitDestination].Id
                    : (commitDragTrack?.Id ?? Guid.Empty);
                EditRequested?.Invoke(new ClipGesture(
                    clip.Id,
                    trackId,
                    clip.StartFrame + commitDelta,
                    clip.SourceInFrame,
                    clip.DurationFrames,
                    "Slide",
                    IgnoreGroups: commitIgnoreGroups));
            }
            else if (commitMode is "TimeStretchLeft" or "TimeStretchRight")
            {
                var trackId = commitDestination >= 0 && commitDestination < Project.Tracks.Count
                    ? Project.Tracks[commitDestination].Id
                    : (commitDragTrack?.Id ?? Guid.Empty);
                var newDuration = commitMode == "TimeStretchRight"
                    ? Math.Max(1L, clip.DurationFrames + commitDelta)
                    : Math.Max(1L, clip.DurationFrames - commitDelta);
                var stretchRatio = (double)clip.DurationFrames / newDuration;
                var newRate = Math.Clamp(clip.PlaybackRate * stretchRatio, 0.25, 4.0);
                EditRequested?.Invoke(new ClipGesture(
                    clip.Id,
                    trackId,
                    commitMode == "TimeStretchLeft" ? clip.StartFrame + commitDelta : clip.StartFrame,
                    clip.SourceInFrame,
                    newDuration,
                    commitMode,
                    IgnoreGroups: commitIgnoreGroups,
                    NewPlaybackRate: newRate));
            }
            else if (commitDelta != 0)
            {
                var left = commitMode == "TrimLeft" ? commitDelta : 0;
                var trackId = commitDestination >= 0 && commitDestination < Project.Tracks.Count
                    ? Project.Tracks[commitDestination].Id
                    : (commitDragTrack?.Id ?? Guid.Empty);
                EditRequested?.Invoke(new ClipGesture(
                    clip.Id,
                    trackId,
                    clip.StartFrame + left,
                    clip.SourceInFrame + left,
                    clip.DurationFrames + (commitMode == "TrimRight" ? commitDelta : -left),
                    commitMode,
                    IgnoreGroups: commitIgnoreGroups,
                    Ripple: Ripple,
                    RippleMode: RippleMode));
            }
        }

        UpdateHover(p);
        InvalidateVisual();
    }

    internal void StartDragForTesting(Point p, ModifierKeys modifiers = ModifierKeys.None)
    {
        origin = p;
        isDragging = false;
        alreadySelectedOnDown = false;
        var hit = HitClip(p);
        if (hit is null) return;
        dragTrack = hit.Value.Track;
        dragging = hit.Value.Clip;
        alreadySelectedOnDown = Selection.Contains(dragging.Id);
        if (!alreadySelectedOnDown || modifiers.HasFlag(ModifierKeys.Control))
        {
            SelectClip(dragging.Id, modifiers.HasFlag(ModifierKeys.Control), ensureVisible: false);
        }
        if (dragTrack!.IsLocked || dragging.IsLocked) { dragging = null; return; }
        mode = hit.Value.Mode;
        destination = Project.Tracks.IndexOf(dragTrack);
        delta = 0;
        destinationIsNewTrack = false;
        destinationNewTrackIndex = destination;
        destinationNewTrackKind = dragTrack.Kind;
        movingClipIds = GetMovingClipIds(dragging);
    }

    internal void DragMoveForTesting(Point p)
    {
        if (dragging is null) return;
        if (!isDragging)
        {
            if ((p - origin).Length < DragDistanceThreshold) return;
            isDragging = true;
        }
        delta = Project.FrameRate.Frames((p.X - origin.X) / PixelsPerSecond);
        if (mode == "Move")
        {
            var sourceTrackIdx = dragTrack is not null ? Project.Tracks.IndexOf(dragTrack) : (int?)null;
            var targetResult = EvaluateDragTarget(p.Y, dragTrack!.Kind, sourceTrackIdx);
            destinationIsNewTrack = targetResult.IsNewTrack;
            destinationNewTrackKind = targetResult.NewTrackKind;
            if (targetResult.IsNewTrack)
            {
                destinationNewTrackIndex = targetResult.TrackIndex;
            }
            else
            {
                destination = targetResult.TrackIndex;
            }
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (isDragging || dragging is not null)
        {
            isDragging = false; alreadySelectedOnDown = false;
            dragging = null; dragTrack = null; mode = ""; delta = 0;
            destinationIsNewTrack = false; movingClipIds = null; snappedFrame = null;
        }
        isMarqueeSelecting = false;
        marqueeRect = Rect.Empty;
        isZooming = false;
        zoomRect = Rect.Empty;
        if (selectingLoop) loopRegion = loopBeforeDrag;
        selectingLoop = false;
        pressedEventButton = null;
        adjustingLevel = false; seeking = false;
        ClearHover();
        InvalidateVisual();
    }
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsMouseCaptured) ClearHover();
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var p = e.GetPosition(this); var time = (p.X - HeaderWidth + Offset) / PixelsPerSecond;
            PixelsPerSecond = Math.Clamp(PixelsPerSecond * (e.Delta > 0 ? 1.25 : .8), 5, 1200);
            Offset = Math.Max(0, time * PixelsPerSecond - p.X + HeaderWidth);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) Offset = Math.Max(0, Offset - e.Delta);
        else VerticalOffset = Math.Clamp(VerticalOffset - e.Delta / 2, 0, Math.Max(0, Project.Tracks.Count * TrackHeight - ActualHeight + RulerHeight));
        InvalidateVisual(); e.Handled = true;
    }
    public void OpenRulerContextMenu()
    {
        var menu = new ContextMenu();
        foreach (var fmt in RulerFormatExtensions.AllStandardFormats)
        {
            var item = new MenuItem
            {
                Header = fmt.ToMenuHeader(),
                IsCheckable = true,
                IsChecked = RulerFormat == fmt
            };
            var chosen = fmt;
            item.Click += (_, _) => RulerFormat = chosen;
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var setTimeItem = new MenuItem { Header = "Set T&ime at Cursor…" };
        setTimeItem.Click += (_, _) => SetTimeAtCursorRequested?.Invoke();
        menu.Items.Add(setTimeItem);

        var insertTimeItem = new MenuItem { Header = "&Insert Time…" };
        insertTimeItem.Click += (_, _) => InsertTimeRequested?.Invoke();
        menu.Items.Add(insertTimeItem);

        var deleteTimeItem = new MenuItem { Header = "&Delete Time", InputGestureText = "Ctrl+Shift+Delete" };
        deleteTimeItem.Click += (_, _) => DeleteTimeRequested?.Invoke();
        menu.Items.Add(deleteTimeItem);

        menu.IsOpen = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var p = e.GetPosition(this);
        if (p.X >= HeaderWidth)
        {
            if (EditTool == EditToolMode.Zoom && p.Y >= RulerHeight)
            {
                var clickFrame = FrameAt(p.X);
                var newPps = Math.Max(5, PixelsPerSecond / 1.5);
                var clickSec = Project.FrameRate.Seconds(clickFrame);
                var newOffset = Math.Max(0, HeaderWidth + clickSec * newPps - p.X);
                PixelsPerSecond = newPps;
                Offset = newOffset;
                ZoomChanged?.Invoke(PixelsPerSecond);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (HandleEnvelopeRightClick(p)) { e.Handled = true; return; }
            if (HitMarker(p) is { } rightMarker)
            {
                var markerMenu = new ContextMenu();
                var editItem = new MenuItem { Header = "Edit Marker…" };
                editItem.Click += (_, _) => MarkerEditRequested?.Invoke(rightMarker);
                markerMenu.Items.Add(editItem);
                var delItem = new MenuItem { Header = "Delete Marker" };
                delItem.Click += (_, _) => MarkerDeleteRequested?.Invoke(rightMarker.Id);
                markerMenu.Items.Add(delItem);
                markerMenu.IsOpen = true;
                e.Handled = true;
                return;
            }
            if (p.Y < RulerHeight)
            {
                OpenRulerContextMenu();
                e.Handled = true;
                return;
            }
            var hit = HitClip(p);
            if (hit is not null && !Selection.Contains(hit.Value.Clip.Id))
            {
                SelectClip(hit.Value.Clip.Id, false, ensureVisible: false);
            }
            return;
        }
        var track = TrackAt(p.Y);
        var trackIndex = track is not null ? Project.Tracks.IndexOf(track) : 0;
        var menu = new ContextMenu();

            var insertVideo = new MenuItem { Header = "Insert Video Track", InputGestureText = "Ctrl+Shift+Q" };
            insertVideo.Click += (_, _) => InsertVideoTrackRequested?.Invoke(trackIndex);
            menu.Items.Add(insertVideo);

            var insertAudio = new MenuItem { Header = "Insert Audio Track", InputGestureText = "Ctrl+Q" };
            insertAudio.Click += (_, _) => InsertAudioTrackRequested?.Invoke(trackIndex >= 0 ? trackIndex + 1 : Project.Tracks.Count);
            menu.Items.Add(insertAudio);

            if (track is not null)
            {
                menu.Items.Add(new Separator());

                var dup = new MenuItem { Header = "Duplicate Track" };
                dup.Click += (_, _) => DuplicateTrackRequested?.Invoke(track.Id);
                menu.Items.Add(dup);

                var del = new MenuItem { Header = "Delete Track" };
                del.Click += (_, _) => DeleteTrackRequested?.Invoke(track.Id);
                menu.Items.Add(del);

                if (track.Kind == TrackKind.Video)
                {
                    var trackFx = new MenuItem { Header = "Track FX…" };
                    trackFx.Click += (_, _) => TrackFxRequested?.Invoke(track.Id);
                    menu.Items.Add(trackFx);
                    var trackMotion = new MenuItem { Header = "Track Motion…" };
                    trackMotion.Click += (_, _) => TrackMotionRequested?.Invoke(track.Id);
                    menu.Items.Add(trackMotion);

                    var trackColorGrading = new MenuItem { Header = "Track Color Grading…" };
                    trackColorGrading.Click += (_, _) => TrackColorGradingRequested?.Invoke(track.Id);
                    menu.Items.Add(trackColorGrading);

                    var compModeMenu = new MenuItem { Header = "Compositing Mode" };
                    var modes = new (TrackCompositeMode Mode, string Label)[]
                    {
                        (TrackCompositeMode.SourceAlpha, "Source Alpha"),
                        (TrackCompositeMode.Add, "Add"),
                        (TrackCompositeMode.Subtract, "Subtract"),
                        (TrackCompositeMode.Multiply, "Multiply"),
                        (TrackCompositeMode.Screen, "Screen"),
                        (TrackCompositeMode.Overlay, "Overlay"),
                        (TrackCompositeMode.HardLight, "Hard Light"),
                        (TrackCompositeMode.Darken, "Darken"),
                        (TrackCompositeMode.Lighten, "Lighten"),
                        (TrackCompositeMode.Difference, "Difference"),
                        (TrackCompositeMode.DifferenceSquared, "Difference Squared"),
                        (TrackCompositeMode.Dodge, "Dodge"),
                        (TrackCompositeMode.Burn, "Burn"),
                        (TrackCompositeMode.Cut, "Cut")
                    };
                    foreach (var (m, label) in modes)
                    {
                        var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = track.CompositeMode == m };
                        var targetMode = m;
                        item.Click += (_, _) => TrackCompositeModeRequested?.Invoke(track.Id, targetMode);
                        compModeMenu.Items.Add(item);
                    }
                    menu.Items.Add(compModeMenu);

                    var makeChild = new MenuItem { Header = "Make Compositing Child" };
                    makeChild.IsEnabled = trackIndex > 0 && Project.Tracks.Take(trackIndex).Any(tr => tr.Kind == TrackKind.Video);
                    makeChild.Click += (_, _) => TrackMakeCompositingChildRequested?.Invoke(track.Id);
                    menu.Items.Add(makeChild);

                    var makeParent = new MenuItem { Header = "Make Compositing Parent" };
                    makeParent.IsEnabled = track.CompositeNestingLevel > 0;
                    makeParent.Click += (_, _) => TrackMakeCompositingParentRequested?.Invoke(track.Id);
                    menu.Items.Add(makeParent);

                    var adjItem = new MenuItem { Header = "Adjustment Track", IsCheckable = true, IsChecked = track.IsAdjustmentTrack };
                    adjItem.Click += (_, _) => TrackAdjustmentRequested?.Invoke(track.Id);
                    menu.Items.Add(adjItem);
                }
                else if (track.Kind == TrackKind.Audio)
                {
                    var busSubmenu = new MenuItem { Header = "Output Bus" };
                    var masterItem = new MenuItem { Header = "Master", IsCheckable = true, IsChecked = !track.BusTrackId.HasValue };
                    masterItem.Click += (_, _) => TrackAudioBusRequested?.Invoke(track.Id, null);
                    busSubmenu.Items.Add(masterItem);
                    if (Project.AudioBuses != null)
                    {
                        foreach (var bus in Project.AudioBuses)
                        {
                            var bItem = new MenuItem { Header = bus.Name, IsCheckable = true, IsChecked = track.BusTrackId == bus.Id };
                            var bId = bus.Id;
                            bItem.Click += (_, _) => TrackAudioBusRequested?.Invoke(track.Id, bId);
                            busSubmenu.Items.Add(bItem);
                        }
                    }
                    menu.Items.Add(busSubmenu);
                }


                var envMenu = new MenuItem { Header = "Insert/Remove Envelope" };
                if (track.Kind == TrackKind.Video)
                {
                    var comp = new MenuItem { Header = "Composite Level", IsCheckable = true, IsChecked = HasTrackEnvelope(track.Id, EnvelopeType.Composite) };
                    comp.Click += (_, _) => ToggleTrackEnvelopeRequested?.Invoke(track.Id, EnvelopeType.Composite);
                    envMenu.Items.Add(comp);

                    var envMute = new MenuItem { Header = "Mute", IsCheckable = true, IsChecked = HasTrackEnvelope(track.Id, EnvelopeType.Mute) };
                    envMute.Click += (_, _) => ToggleTrackEnvelopeRequested?.Invoke(track.Id, EnvelopeType.Mute);
                    envMenu.Items.Add(envMute);
                }
                else if (track.Kind == TrackKind.Audio)
                {
                    var trackFx = new MenuItem { Header = "Track FXâ€¦" };
                    trackFx.Click += (_, _) => TrackAudioFxRequested?.Invoke(track.Id);
                    menu.Items.Add(trackFx);

                    var phase = new MenuItem { Header = "Invert Phase", IsCheckable = true, IsChecked = track.InvertPhase };
                    phase.Click += (_, _) => TrackPhaseInvertRequested?.Invoke(track.Id);
                    menu.Items.Add(phase);

                    var vol = new MenuItem { Header = "Volume", IsCheckable = true, IsChecked = HasTrackEnvelope(track.Id, EnvelopeType.Volume) };
                    vol.Click += (_, _) => ToggleTrackEnvelopeRequested?.Invoke(track.Id, EnvelopeType.Volume);
                    envMenu.Items.Add(vol);

                    var pan = new MenuItem { Header = "Pan", IsCheckable = true, IsChecked = HasTrackEnvelope(track.Id, EnvelopeType.Pan) };
                    pan.Click += (_, _) => ToggleTrackEnvelopeRequested?.Invoke(track.Id, EnvelopeType.Pan);
                    envMenu.Items.Add(pan);

                    var envMute = new MenuItem { Header = "Mute", IsCheckable = true, IsChecked = HasTrackEnvelope(track.Id, EnvelopeType.Mute) };
                    envMute.Click += (_, _) => ToggleTrackEnvelopeRequested?.Invoke(track.Id, EnvelopeType.Mute);
                    envMenu.Items.Add(envMute);
                }
                menu.Items.Add(envMenu);
                menu.Items.Add(new Separator());

                var mute = new MenuItem { Header = "Mute", IsCheckable = true, IsChecked = track.IsMuted };
                mute.Click += (_, _) => InvokeTrackAction(track.Id, "Mute");
                menu.Items.Add(mute);

                var solo = new MenuItem { Header = "Solo", IsCheckable = true, IsChecked = track.IsSolo };
                solo.Click += (_, _) => InvokeTrackAction(track.Id, "Solo");
                menu.Items.Add(solo);

                if (track.Kind == TrackKind.Audio)
                {
                    var armItem = new MenuItem { Header = "Arm for Record", IsCheckable = true, IsChecked = track.ArmRecord, InputGestureText = "Ctrl+Alt+R" };
                    armItem.Click += (_, _) => InvokeTrackAction(track.Id, "Arm");
                    menu.Items.Add(armItem);
                }

                var autoMenu = new MenuItem { Header = "Track Automation Mode" };
                foreach (TrackAutomationMode m in Enum.GetValues<TrackAutomationMode>())
                {
                    var autoItem = new MenuItem
                    {
                        Header = m switch
                        {
                            TrackAutomationMode.AutomationOff => "Automation Off",
                            TrackAutomationMode.AutomationRead => "Read",
                            TrackAutomationMode.AutomationTouch => "Touch",
                            TrackAutomationMode.AutomationLatch => "Latch",
                            TrackAutomationMode.AutomationWrite => "Write",
                            _ => m.ToString()
                        },
                        IsCheckable = true,
                        IsChecked = track.AutomationMode == m
                    };
                    var targetMode = m;
                    autoItem.Click += (_, _) => TrackAutomationModeRequested?.Invoke(track.Id, targetMode);
                    autoMenu.Items.Add(autoItem);
                }
                menu.Items.Add(autoMenu);

                var colorMenu = new MenuItem { Header = "Track Display Color" };
                foreach (var (cName, cHex) in VegasTrackColors.Palette)
                {
                    var colorItem = new MenuItem
                    {
                        Header = cName,
                        IsCheckable = true,
                        IsChecked = (string.IsNullOrEmpty(cHex) && string.IsNullOrEmpty(track.TrackColor)) || track.TrackColor == cHex
                    };
                    if (!string.IsNullOrEmpty(cHex))
                    {
                        var swatch = new System.Windows.Shapes.Rectangle
                        {
                            Width = 14,
                            Height = 14,
                            Fill = (Brush)new BrushConverter().ConvertFromString(cHex)!,
                            Margin = new Thickness(0, 0, 4, 0),
                            Stroke = Brushes.Black,
                            StrokeThickness = 1
                        };
                        colorItem.Icon = swatch;
                    }
                    var targetHex = string.IsNullOrEmpty(cHex) ? null : cHex;
                    colorItem.Click += (_, _) => TrackColorRequested?.Invoke(track.Id, targetHex);
                    colorMenu.Items.Add(colorItem);
                }
                menu.Items.Add(colorMenu);
            }

            menu.PlacementTarget = this;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
    }
}