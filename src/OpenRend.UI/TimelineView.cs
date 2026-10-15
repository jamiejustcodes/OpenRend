using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.UI;

public sealed record ClipGesture(Guid ClipId, Guid TrackId, long Start, long SourceIn, long Duration, string Mode);

public sealed class TimelineView : FrameworkElement
{
    private ProjectDocument project = new();
    public ProjectDocument Project
    {
        get => project;
        set
        {
            project = value ?? throw new ArgumentNullException(nameof(value));
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
    public double PixelsPerSecond { get; set; } = 70;
    public bool Snapping { get; set; } = true;
    public double Offset { get; set; }
    public double VerticalOffset { get; set; }
    public const double HeaderWidth = 250;
    public const double RulerHeight = 32;
    public const double TrackHeight = 88;
    public event Action<long>? SeekRequested;
    public event Action? SelectionChanged;
    public event Action<ClipGesture>? EditRequested;
    public event Action<Guid, string>? TrackAction;
    public event Action<Guid, Guid, long>? MediaDropped;
    public event Action? DeleteRequested;
    public event Action? SplitRequested;
    private TimelineClip? dragging;
    private TimelineTrack? dragTrack;
    private string mode = "";
    private Point origin;
    private long delta;
    private int destination;
    private bool seeking;
    private Guid? activeClipId;
    internal Guid? ActiveClipId => activeClipId;
    private static readonly Brush BackgroundBrush = Brush("#1E1E1E");
    private static readonly Typeface Face = new("Segoe UI");

    public TimelineView()
    {
        Focusable = true;
        ClipToBounds = true;
        AllowDrop = true;
        AutomationProperties.SetName(this, "Timeline");
        AutomationProperties.SetHelpText(this, "Up and Down choose a track. Left and Right select clips. Shift extends the selection. M mutes, Shift+M solos, L locks, and V changes video visibility. S splits at the cursor; Delete removes selected clips.");
        Drop += (_, e) =>
        {
            if (e.Data.GetData("OpenRend.Media") is not Guid media) return;
            var p = e.GetPosition(this);
            var track = TrackAt(p.Y);
            if (track is not null) { MediaDropped?.Invoke(media, track.Id, FrameAt(p.X)); e.Handled = true; }
        };
        var menu = new System.Windows.Controls.ContextMenu();
        foreach (var label in new[] { "Split at cursor", "Delete selection" })
        {
            var item = new System.Windows.Controls.MenuItem { Header = label };
            item.Click += (_, _) => { if (label.StartsWith("Split")) SplitRequested?.Invoke(); else DeleteRequested?.Invoke(); };
            menu.Items.Add(item);
        }
        ContextMenu = menu;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TimelineAutomationPeer(this);

    public bool HandleKeyboardInput(Key key, ModifierKeys modifiers)
    {
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return false;
        if (Project.Tracks.Count == 0) return false;
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

    internal void ActivateTrack(int index)
    {
        if (index < 0 || index >= Project.Tracks.Count) return;
        ActiveTrackIndex = index;
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
        Selection.Add(clipId);
        activeClipId = clipId;
        ActivateTrack(index);
        var clip = Project.Tracks[index].Clips.Single(c => c.Id == clipId);
        if (ensureVisible && (X(clip.StartFrame) < HeaderWidth || ActualWidth > HeaderWidth && X(clip.StartFrame) >= ActualWidth))
            Offset = Math.Max(0, Project.FrameRate.Seconds(clip.StartFrame) * PixelsPerSecond - 10);
        NotifySelection(before);
    }

    internal void RemoveClipSelection(Guid clipId)
    {
        var before = Selection.ToHashSet();
        Selection.Remove(clipId);
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
        InvalidateVisual();
        (UIElementAutomationPeer.FromElement(this) as TimelineAutomationPeer)?.RefreshProject();
    }

    internal Rect ClipBounds(Guid trackId, TimelineClip clip)
    {
        var index = Project.Tracks.FindIndex(t => t.Id == trackId);
        return new Rect(X(clip.StartFrame), RulerHeight + index * TrackHeight - VerticalOffset + 4,
            Math.Max(2, Project.FrameRate.Seconds(clip.DurationFrames) * PixelsPerSecond), TrackHeight - 8);
    }

    internal Rect TrackButtonBounds(Guid trackId, string action)
    {
        var index = Project.Tracks.FindIndex(t => t.Id == trackId);
        var x = action switch { "Mute" => 169, "Solo" => 194, _ => 219 };
        return new Rect(x, RulerHeight + index * TrackHeight - VerticalOffset + (action == "Visible" ? 40 : 8), 21, 23);
    }

    internal Rect ToScreenBounds(Rect bounds, bool clipContent = false)
    {
        if (PresentationSource.FromVisual(this) is null || !IsVisible) return Rect.Empty;
        bounds.Intersect(new Rect(clipContent ? HeaderWidth : 0, RulerHeight,
            Math.Max(0, ActualWidth - (clipContent ? HeaderWidth : 0)), Math.Max(0, ActualHeight - RulerHeight)));
        return bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 ? Rect.Empty : new Rect(PointToScreen(bounds.TopLeft), PointToScreen(bounds.BottomRight));
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
    private double X(long frame) => HeaderWidth + Project.FrameRate.Seconds(frame) * PixelsPerSecond - Offset;
    private long FrameAt(double x) => Math.Max(0, Project.FrameRate.Frames((x - HeaderWidth + Offset) / PixelsPerSecond));
    private TimelineTrack? TrackAt(double y)
    {
        var index = (int)Math.Floor((y - RulerHeight + VerticalOffset) / TrackHeight);
        return index >= 0 && index < Project.Tracks.Count ? Project.Tracks[index] : null;
    }
    private void Text(DrawingContext dc, string text, double x, double y, Brush? color = null, double size = 12, double maxWidth = 10000)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size,
            color ?? Brushes.WhiteSmoke, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(1, maxWidth), MaxTextHeight = 28, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(ft, new Point(x, y));
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));
        var pen = new Pen(Brush("#383838"), 1);
        var step = PixelsPerSecond > 130 ? 1 : PixelsPerSecond > 35 ? 5 : 15;
        var first = Math.Max(0, (int)(Offset / PixelsPerSecond / step));
        for (var n = first; n * step * PixelsPerSecond - Offset < ActualWidth - HeaderWidth; n++)
        {
            var x = HeaderWidth + n * step * PixelsPerSecond - Offset;
            dc.DrawLine(pen, new Point(x, RulerHeight), new Point(x, ActualHeight));
            Text(dc, TimeSpan.FromSeconds(n * step).ToString(@"mm\:ss"), x + 3, 12, Brush("#B8B8B8"), 10);
            for (var minor = 1; minor < 5; minor++)
                dc.DrawLine(pen, new Point(x + minor * step * PixelsPerSecond / 5, 26), new Point(x + minor * step * PixelsPerSecond / 5, 32));
        }
        dc.PushClip(new RectangleGeometry(new Rect(HeaderWidth, RulerHeight, Math.Max(0, ActualWidth - HeaderWidth), Math.Max(0, ActualHeight - RulerHeight))));
        for (var i = 0; i < Project.Tracks.Count; i++)
        {
            var track = Project.Tracks[i];
            var y = RulerHeight + i * TrackHeight - VerticalOffset;
            if (y + TrackHeight < RulerHeight || y > ActualHeight) continue;
            dc.DrawLine(new Pen(Brush("#111111"), 3), new Point(HeaderWidth, y + TrackHeight), new Point(ActualWidth, y + TrackHeight));
            foreach (var clip in track.Clips)
            {
                var start = clip.StartFrame;
                var duration = clip.DurationFrames;
                var cy = y;
                if (dragging?.Id == clip.Id)
                {
                    if (mode == "Move") { start = Math.Max(0, start + delta); cy = RulerHeight + destination * TrackHeight - VerticalOffset; }
                    if (mode == "TrimLeft") { start += delta; duration -= delta; }
                    if (mode == "TrimRight") duration += delta;
                }
                var rect = new Rect(X(start), cy + 4, Math.Max(2, Project.FrameRate.Seconds(Math.Max(1, duration)) * PixelsPerSecond), TrackHeight - 8);
                if (rect.Right < HeaderWidth || rect.Left > ActualWidth) continue;
                var selected = Selection.Contains(clip.Id);
                dc.DrawRectangle(Brush(track.IsMuted ? "#414141" : track.Kind == TrackKind.Video ? "#355D7B" : "#3B6456"),
                    new Pen(selected ? Brush("#83C9FF") : Brush("#577384"), selected ? 2 : 1), rect);
                dc.PushClip(new RectangleGeometry(rect));
                dc.DrawRectangle(Brush(selected ? "#477CA2" : "#304D62"), null, new Rect(rect.X, rect.Y, rect.Width, 20));
                Text(dc, clip.GroupId.HasValue ? "↔ " + clip.Name : clip.Name, rect.X + 5, rect.Y + 2, maxWidth: rect.Width - 10);
                if (rect.Width > 4 && track.Kind == TrackKind.Video && Thumbnails.TryGetValue(clip.MediaId, out var image))
                    dc.DrawImage(image, new Rect(rect.X + 2, rect.Y + 22, Math.Min(88, rect.Width - 4), 49));
                if (track.Kind == TrackKind.Audio && Waveforms.TryGetValue(clip.MediaId, out var peaks) && peaks.Length > 0)
                {
                    var media = Project.Media.FirstOrDefault(m => m.Id == clip.MediaId);
                    if (media is not null)
                    {
                        var mid = rect.Y + 49;
                        var wp = new Pen(Brush("#A5D3B5"), 1);
                        for (var px = 0.0; px < Math.Min(rect.Width, ActualWidth); px += 2)
                        {
                            var t = Project.FrameRate.Seconds(clip.SourceInFrame) + px / PixelsPerSecond;
                            var index = (int)(t / media.DurationSeconds * peaks.Length);
                            var amplitude = index >= 0 && index < peaks.Length ? Math.Min(1, peaks[index] * clip.Gain) * 24 : 0;
                            dc.DrawLine(wp, new Point(rect.X + px, mid - amplitude), new Point(rect.X + px, mid + amplitude));
                        }
                    }
                }
                var fadePen = new Pen(Brush("#E4E4E4"), 1);
                if (clip.FadeInFrames > 0) dc.DrawLine(fadePen, rect.BottomLeft, new Point(rect.Left + Math.Min(rect.Width, Project.FrameRate.Seconds(clip.FadeInFrames) * PixelsPerSecond), rect.Y + 20));
                if (clip.FadeOutFrames > 0) dc.DrawLine(fadePen, rect.BottomRight, new Point(rect.Right - Math.Min(rect.Width, Project.FrameRate.Seconds(clip.FadeOutFrames) * PixelsPerSecond), rect.Y + 20));
                dc.Pop();
            }
        }
        dc.Pop();
        dc.DrawRectangle(Brush("#262626"), null, new Rect(0, 0, HeaderWidth, ActualHeight));
        Text(dc, FormatTimecode(Position, Project.FrameRate), 116, 5, size: 20, maxWidth: 130);
        dc.PushClip(new RectangleGeometry(new Rect(0, RulerHeight, HeaderWidth, Math.Max(0, ActualHeight - RulerHeight))));
        for (var i = 0; i < Project.Tracks.Count; i++)
        {
            var t = Project.Tracks[i]; var y = RulerHeight + i * TrackHeight - VerticalOffset;
            dc.DrawRectangle(Brush(t.IsLocked ? "#292929" : "#303030"), new Pen(Brush("#181818"), 1), new Rect(3, y + 2, HeaderWidth - 7, TrackHeight - 3));
            if (IsKeyboardFocusWithin && i == ActiveTrackIndex)
                dc.DrawRectangle(null, new Pen(Brush("#83C9FF"), 1), new Rect(4, y + 3, HeaderWidth - 9, TrackHeight - 5));
            Text(dc, $"{i + 1}   {t.Name}", 12, y + 10, maxWidth: 155);
            foreach (var (label, on, x) in new[] { ("M", t.IsMuted, 169), ("S", t.IsSolo, 194), ("L", t.IsLocked, 219) })
            {
                dc.DrawRectangle(Brush(on ? "#438AC8" : "#404040"), null, new Rect(x, y + 8, 21, 23));
                Text(dc, label, x + 5, y + 11);
            }
            Text(dc, t.Kind == TrackKind.Video ? "Video   •   Composite" : "Audio   •   Stereo", 14, y + 44, Brush("#B8B8B8"), 11);
            if (t.Kind == TrackKind.Video)
            {
                dc.DrawRectangle(Brush(t.IsVisible ? "#438AC8" : "#404040"), null, TrackButtonBounds(t.Id, "Visible"));
                Text(dc, "V", 224, y + 43);
            }
        }
        dc.Pop();
        var playX = X(Position);
        if (playX >= HeaderWidth && playX < ActualWidth)
        {
            dc.DrawLine(new Pen(Brush("#F2D46A"), 1), new Point(playX, 28), new Point(playX, ActualHeight));
            dc.DrawGeometry(Brush("#F2D46A"), null, Geometry.Parse($"M {playX.ToString(CultureInfo.InvariantCulture)},27 l -5,-8 10,0 Z"));
        }
        if (Project.Tracks.Count == 0) Text(dc, "Import media, then add it to the timeline", HeaderWidth + 30, RulerHeight + 35, Brush("#B8B8B8"));
    }

    public static string FormatTimecode(long frame, Rational rate)
    {
        var fps = Math.Max(1, (int)Math.Round(rate.Numerator / (double)rate.Denominator));
        var total = frame / fps;
        return $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}:{frame % fps:00}";
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); Focus(); origin = e.GetPosition(this);
        if (origin.X < HeaderWidth)
        {
            var track = TrackAt(origin.Y);
            if (track is not null)
            {
                ActivateTrack(Project.Tracks.IndexOf(track));
                foreach (var action in new[] { "Mute", "Solo", "Lock", "Visible" })
                    if ((action != "Visible" || track.Kind == TrackKind.Video) && TrackButtonBounds(track.Id, action).Contains(origin))
                    { InvokeTrackAction(track.Id, action); break; }
            }
            return;
        }
        dragTrack = origin.Y >= RulerHeight ? TrackAt(origin.Y) : null;
        dragging = dragTrack?.Clips.LastOrDefault(c => X(c.StartFrame) <= origin.X && X(c.EndFrame) >= origin.X);
        if (dragging is null)
        {
            seeking = true; SeekRequested?.Invoke(FrameAt(origin.X)); CaptureMouse(); return;
        }
        SelectClip(dragging.Id, Keyboard.Modifiers.HasFlag(ModifierKeys.Control), ensureVisible: false);
        if (dragTrack!.IsLocked) { dragging = null; return; }
        mode = origin.X - X(dragging.StartFrame) < 7 ? "TrimLeft" : X(dragging.EndFrame) - origin.X < 7 ? "TrimRight" : "Move";
        destination = Project.Tracks.IndexOf(dragTrack); delta = 0; CaptureMouse();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); var p = e.GetPosition(this);
        if (seeking) { SeekRequested?.Invoke(FrameAt(p.X)); return; }
        if (dragging is null) return;
        delta = Project.FrameRate.Frames((p.X - origin.X) / PixelsPerSecond);
        if (mode == "Move")
        {
            delta = Math.Max(-dragging.StartFrame, delta);
            var target = TrackAt(p.Y);
            if (target is not null && target.Kind == dragTrack!.Kind && !target.IsLocked) destination = Project.Tracks.IndexOf(target);
            if (Snapping)
            {
                var points = Project.Tracks.SelectMany(t => t.Clips).Where(c => !Selection.Contains(c.Id)).SelectMany(c => new[] { c.StartFrame, c.EndFrame }).Append(Position).Append(0);
                var start = dragging.StartFrame + delta;
                var threshold = Math.Max(1, Project.FrameRate.Frames(8 / PixelsPerSecond));
                var nearest = points.SelectMany(f => new[] { f - start, f - start - dragging.DurationFrames }).OrderBy(Math.Abs).FirstOrDefault();
                if (Math.Abs(nearest) <= threshold) delta = Math.Max(-dragging.StartFrame, delta + nearest);
            }
        }
        else if (mode == "TrimLeft") delta = Math.Clamp(delta, Math.Max(-dragging.SourceInFrame, -dragging.StartFrame), dragging.DurationFrames - 1);
        else
        {
            var media = Project.Media.First(m => m.Id == dragging.MediaId);
            delta = Math.Clamp(delta, 1 - dragging.DurationFrames, Math.Max(0, Project.FrameRate.Frames(media.DurationSeconds) - dragging.SourceInFrame - dragging.DurationFrames));
        }
        InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e); ReleaseMouseCapture(); seeking = false;
        var clip = dragging; dragging = null;
        if (clip is not null && (delta != 0 || mode == "Move" && Project.Tracks[destination].Id != dragTrack?.Id))
        {
            var left = mode == "TrimLeft" ? delta : 0;
            EditRequested?.Invoke(new ClipGesture(clip.Id, Project.Tracks[destination].Id,
                mode == "Move" ? clip.StartFrame + delta : clip.StartFrame + left,
                clip.SourceInFrame + left, clip.DurationFrames + (mode == "TrimRight" ? delta : -left), mode));
        }
        InvalidateVisual();
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
}
