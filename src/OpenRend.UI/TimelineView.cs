using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.UI;

public sealed record ClipGesture(Guid ClipId, Guid TrackId, long Start, long SourceIn, long Duration, string Mode);
public sealed record TrackLevelGesture(Guid TrackId, string Level, double Value);

public sealed class TimelineView : FrameworkElement
{
    private ProjectDocument project = new();
    public ProjectDocument Project
    {
        get => project;
        set
        {
            project = value ?? throw new ArgumentNullException(nameof(value));
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
    public double PixelsPerSecond { get; set; } = 13.4;
    public bool Snapping { get; set; } = true;
    public double Offset { get; set; }
    public double VerticalOffset { get; set; }
    public const double HeaderWidth = 256;
    public const double RulerHeight = 46;
    public const double TrackHeight = 69;
    public event Action<long>? SeekRequested;
    public event Action? SelectionChanged;
    public event Action<ClipGesture>? EditRequested;
    public event Action<Guid, string>? TrackAction;
    public event Action<TrackLevelGesture>? TrackLevelRequested;
    public event Action<Guid, Guid, long>? MediaDropped;
    public event Action? DeleteRequested;
    public event Action? SplitRequested;
    public event Action? PanCropRequested;
    private TimelineClip? dragging;
    private TimelineTrack? dragTrack;
    private string mode = "";
    private Point origin;
    private long delta;
    private int destination;
    private bool seeking;
    private Guid? levelTrack;
    private string? levelKind;
    private double pendingLevel;
    private bool adjustingLevel;
    private Guid? activeClipId;
    internal Guid? ActiveClipId => activeClipId;
    internal bool IsTrackLevelFocused(Guid trackId, string level) => IsKeyboardFocusWithin && levelTrack == trackId && levelKind == level;
    public void CancelTrackLevelInput()
    {
        var capturedLevel = adjustingLevel;
        adjustingLevel = false; levelTrack = null; levelKind = null;
        if (capturedLevel) ReleaseMouseCapture();
        InvalidateVisual();
    }
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
        var crop = new System.Windows.Controls.MenuItem { Header = "Video event Pan/Crop…" };
        crop.Click += (_, _) => PanCropRequested?.Invoke();
        menu.Items.Add(crop);
        menu.Opened += (_, _) => crop.IsEnabled = Project.Tracks.Any(track => track.Kind == TrackKind.Video && !track.IsLocked && track.Clips.Any(clip => Selection.Contains(clip.Id)));
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
        if (levelTrack is { } levelId && levelKind is { } level && key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            if (Project.Tracks.Single(t => t.Id == levelId).IsLocked) return true;
            var current = ReadTrackLevel(levelId, level);
            var min = level == "Pan" ? -1 : 0; var max = level == "Gain" ? Math.Max(4, current) : 1;
            var value = key switch { Key.Home => min, Key.End => max, _ => current + (key == Key.Left ? -.01 : .01) };
            SetTrackLevel(levelId, level, Math.Clamp(value, min, max));
            return true;
        }
        if (key == Key.Escape) { CancelTrackLevelInput(); return true; }
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
        return new Rect(X(clip.StartFrame), RulerHeight + index * TrackHeight - VerticalOffset + 1,
            Math.Max(2, Project.FrameRate.Seconds(clip.DurationFrames) * PixelsPerSecond), TrackHeight - 2);
    }

    internal Rect TrackButtonBounds(Guid trackId, string action)
    {
        var index = Project.Tracks.FindIndex(t => t.Id == trackId);
        var x = action switch { "Mute" => 207, "Solo" => 230, "Lock" => 184, _ => 230 };
        return new Rect(x, RulerHeight + index * TrackHeight - VerticalOffset + (action == "Visible" ? 47 : 4), 21, action == "Visible" ? 18 : 20);
    }

    internal Rect TrackLevelBounds(Guid trackId, string level)
    {
        var index = Project.Tracks.FindIndex(t => t.Id == trackId);
        var y = RulerHeight + index * TrackHeight - VerticalOffset;
        return level switch { "Opacity" => new Rect(116, y + 25, 124, 20), "Gain" => new Rect(84, y + 25, 48, 20), _ => new Rect(137, y + 46, 43, 18) };
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
        var step = PixelsPerSecond >= 130 ? 1 : PixelsPerSecond >= 50 ? 2 : PixelsPerSecond >= 25 ? 5 : PixelsPerSecond >= 10 ? 10 : 20;
        var first = Math.Max(0, (int)(Offset / PixelsPerSecond / step));
        for (var n = first; n * step * PixelsPerSecond - Offset < ActualWidth - HeaderWidth; n++)
        {
            var x = HeaderWidth + n * step * PixelsPerSecond - Offset;
            dc.DrawLine(new Pen(Brush("#444444"), 1) { DashStyle = new DashStyle([2, 3], 0) }, new Point(x, RulerHeight), new Point(x, ActualHeight));
            Text(dc, FormatTimecode(Project.FrameRate.Frames(n * step), Project.FrameRate), x + 3, 30, Brush("#B8B8B8"), 9);
            for (var minor = 1; minor < 5; minor++)
                dc.DrawLine(pen, new Point(x + minor * step * PixelsPerSecond / 5, 40), new Point(x + minor * step * PixelsPerSecond / 5, RulerHeight));
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
                var rect = new Rect(X(start), cy + 1, Math.Max(2, Project.FrameRate.Seconds(Math.Max(1, duration)) * PixelsPerSecond), TrackHeight - 2);
                if (rect.Right < HeaderWidth || rect.Left > ActualWidth) continue;
                var selected = Selection.Contains(clip.Id);
                dc.DrawRectangle(Brush(track.IsMuted ? "#414141" : track.Kind == TrackKind.Video ? "#233442" : "#365D61"),
                    new Pen(selected ? Brush("#E4DF8F") : Brush("#5E737C"), 1), rect);
                dc.PushClip(new RectangleGeometry(rect));
                dc.DrawRectangle(Brush(selected ? "#4D5356" : "#30383C"), null, new Rect(rect.X, rect.Y, rect.Width, 18));
                Text(dc, clip.Name, rect.X + 3, rect.Y + 1, size: 11, maxWidth: rect.Width - 6);
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
                    var media = Project.Media.FirstOrDefault(m => m.Id == clip.MediaId);
                    if (media is not null)
                    {
                        var mid = rect.Y + 42;
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
        Text(dc, FormatTimecode(Position, Project.FrameRate), 126, 12, size: 20, maxWidth: 130);
        dc.PushClip(new RectangleGeometry(new Rect(0, RulerHeight, HeaderWidth, Math.Max(0, ActualHeight - RulerHeight))));
        for (var i = 0; i < Project.Tracks.Count; i++)
        {
            var t = Project.Tracks[i]; var y = RulerHeight + i * TrackHeight - VerticalOffset;
            dc.DrawRectangle(Brush(t.IsLocked ? "#242424" : "#2B2B2B"), new Pen(Brush("#181818"), 1), new Rect(0, y + 1, HeaderWidth - 3, TrackHeight - 1));
            dc.DrawRectangle(Brush(t.Kind == TrackKind.Video ? "#448DBD" : "#65BECE"), null, new Rect(1, y + 1, 23, TrackHeight - 2));
            if (IsKeyboardFocusWithin && i == ActiveTrackIndex)
                dc.DrawRectangle(null, new Pen(Brush("#83C9FF"), 1), new Rect(4, y + 3, HeaderWidth - 9, TrackHeight - 5));
            Text(dc, "···", 4, y + 1, size: 12);
            Text(dc, $"{i + 1}", 5, y + 24, size: 11);
            Text(dc, t.Name, 30, y + 5, size: 11, maxWidth: 145);
            foreach (var (label, action, on) in new[] { ("M", "Mute", t.IsMuted), ("S", "Solo", t.IsSolo), ("L", "Lock", t.IsLocked) })
            {
                var bounds = TrackButtonBounds(t.Id, action);
                if (on) dc.DrawRectangle(Brush("#438AC8"), null, bounds);
                Text(dc, label, bounds.X + 5, bounds.Y + 1);
            }
            var gain = ReadTrackLevel(t.Id, "Gain"); var opacity = ReadTrackLevel(t.Id, "Opacity"); var pan = ReadTrackLevel(t.Id, "Pan");
            Text(dc, t.Kind == TrackKind.Video ? $"Level: {opacity * 100:0.0} %" : gain > 0 ? $"{20 * Math.Log10(gain):0.0} dB" : "−∞ dB", 31, y + 28, Brush("#B8B8B8"), 11, t.Kind == TrackKind.Video ? 83 : 52);
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
                dc.DrawRectangle(Brush(t.IsVisible ? "#438AC8" : "#404040"), null, TrackButtonBounds(t.Id, "Visible"));
                Text(dc, "V", 235, y + 47);
            }
        }
        dc.Pop();
        var playX = X(Position);
        if (playX >= HeaderWidth && playX < ActualWidth)
        {
            dc.DrawLine(new Pen(Brush("#F0F0F0"), 1), new Point(playX, 32), new Point(playX, ActualHeight));
            dc.DrawGeometry(Brush("#D8B85D"), null, Geometry.Parse($"M {playX.ToString(CultureInfo.InvariantCulture)},32 l -4,-6 8,0 Z"));
        }
        if (Project.Tracks.Count == 0) Text(dc, "Import media, then add it to the timeline", HeaderWidth + 30, RulerHeight + 35, Brush("#B8B8B8"));
    }

    public static string FormatTimecode(long frame, Rational rate)
    {
        var fps = Math.Max(1, (int)Math.Round(rate.Numerator / (double)rate.Denominator));
        var drop = rate.Denominator == 1001 && rate.Numerator is 30000 or 60000 ? fps / 15 : 0;
        if (drop > 0)
        {
            var tenMinutes = fps * 600 - drop * 9;
            var remainder = frame % tenMinutes;
            // Every tenth minute keeps its frame numbers; the other nine skip 2 or 4.
            frame += drop * 9 * (frame / tenMinutes) + (remainder >= drop ? drop * ((remainder - drop) / (fps * 60 - drop)) : 0);
        }
        var total = frame / fps;
        return $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}{(drop > 0 ? ';' : ':')}{frame % fps:00}";
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
                foreach (var level in track.Kind == TrackKind.Video ? new[] { "Opacity" } : new[] { "Gain", "Pan" })
                {
                    if (!TrackLevelBounds(track.Id, level).Contains(origin)) continue;
                    FocusTrackLevel(track.Id, level);
                    if (track.IsLocked) return;
                    if (e.ClickCount == 2) { SetTrackLevel(track.Id, level, level == "Pan" ? 0 : 1); return; }
                    adjustingLevel = true; pendingLevel = LevelAt(origin.X); CaptureMouse(); InvalidateVisual(); return;
                }
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
        if (adjustingLevel) { pendingLevel = LevelAt(p.X); InvalidateVisual(); return; }
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
        var commitLevel = adjustingLevel;
        adjustingLevel = false;
        base.OnMouseLeftButtonUp(e); seeking = false; ReleaseMouseCapture();
        if (commitLevel)
        {
            SetTrackLevel(levelTrack!.Value, levelKind!, pendingLevel);
            return;
        }
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
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        adjustingLevel = false; seeking = false;
        if (Mouse.LeftButton == MouseButtonState.Pressed) dragging = null;
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
