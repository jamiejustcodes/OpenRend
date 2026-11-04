using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Project;
using OpenRend.Timeline;

namespace OpenRend.App;

public sealed class MediaItem(MediaAsset asset) : ObservableObject
{
    public MediaAsset Asset { get; private set; } = asset;
    public string Name => Asset.Name;
    public string Details => $"{(Asset.HasVideo ? $"{Asset.Width} \u00D7 {Asset.Height}  \u2022  {Asset.FrameRate:0.###} fps" : "Audio")}  \u2022  {Asset.DurationSeconds:0.00} s";
    public string Type => Asset.IsGenerated ? "Generated" : Asset.HasVideo ? "Video" : "Audio";
    public string FrameSize => Asset.HasVideo ? $"{Asset.Width} \u00D7 {Asset.Height}" : "\u2014";
    public string FrameRateText => Asset.HasVideo ? $"{Asset.FrameRate:0.###} fps" : "\u2014";
    public string DurationText => TimeSpan.FromSeconds(Asset.DurationSeconds).ToString(@"hh\:mm\:ss\.ff");
    public string Path => Asset.Path;
    public string AudioInfo => Asset.HasAudio ? "Stereo, 48 kHz" : "\u2014";
    private int useCount;
    public int UseCount { get => useCount; set => SetProperty(ref useCount, value); }
    public bool HasEffects => Asset.VideoEffects.Count > 0;
    public System.Windows.Media.Brush FxBadgeBrush => HasEffects
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x33, 0x33, 0x33));
    private System.Windows.Media.ImageSource? thumbnail;
    public System.Windows.Media.ImageSource? Thumbnail { get => thumbnail; set => SetProperty(ref thumbnail, value); }
    public void Rebind(MediaAsset current, int count = 0)
    {
        if (!string.Equals(Asset.Path, current.Path, StringComparison.OrdinalIgnoreCase)) Thumbnail = null;
        Asset = current;
        UseCount = count;
        OnPropertyChanged(nameof(Asset)); OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(Type)); OnPropertyChanged(nameof(FrameSize)); OnPropertyChanged(nameof(FrameRateText));
        OnPropertyChanged(nameof(DurationText)); OnPropertyChanged(nameof(Path)); OnPropertyChanged(nameof(AudioInfo));
        OnPropertyChanged(nameof(UseCount));
        OnPropertyChanged(nameof(HasEffects)); OnPropertyChanged(nameof(FxBadgeBrush));
    }
}

public sealed class EditorViewModel : ObservableObject
{
    public MediaService Media { get; }
    public TimelineRenderService Renderer { get; }
    public ExportService Exporter { get; }
    public TimelineEditor Editor { get; private set; }
    public ProjectDocument Project => Editor.Project;
    public ObservableCollection<MediaItem> Library { get; } = [];
    public HashSet<Guid> Selection { get; } = [];
    public event Action? Changed;
    private string status = "Import media to start editing.";
    public string Status { get => status; set => SetProperty(ref status, value); }
    private string? projectPath;
    public string? ProjectPath { get => projectPath; private set { SetProperty(ref projectPath, value); OnPropertyChanged(nameof(Title)); } }
    private bool dirty;
    public bool Dirty { get => dirty; private set { SetProperty(ref dirty, value); OnPropertyChanged(nameof(Title)); } }
    public bool CreateProjectBackups { get; set; } = true;
    public string Title => $"{Project.Name}{(Dirty ? " *" : "")} \u2014 OpenRend";
    private long position;
    public long Position { get => position; set => SetProperty(ref position, Math.Clamp(value, 0, Project.DurationFrames)); }
    private bool busy;
    public bool Busy { get => busy; set => SetProperty(ref busy, value); }
    private double progress;
    public double Progress { get => progress; set => SetProperty(ref progress, value); }
    private bool ripple;
    public bool Ripple { get => ripple; set => SetProperty(ref ripple, value); }
    private AutoRippleMode rippleMode = AutoRippleMode.AffectedTracks;
    public AutoRippleMode RippleMode { get => rippleMode; set => SetProperty(ref rippleMode, value); }
    public bool IgnoreGroups { get; set; }
    public int ActiveTrackIndex { get; set; }
    private EditToolMode editTool = EditToolMode.Normal;
    public EditToolMode EditTool
    {
        get => editTool;
        set
        {
            if (SetProperty(ref editTool, value))
            {
                Status = editTool.ToStatusHint();
                EditToolChanged?.Invoke(editTool);
                Changed?.Invoke();
            }
        }
    }
    public event Action<EditToolMode>? EditToolChanged;
    public void CycleNextEditTool() => EditTool = EditTool.NextTool();
    public void CyclePreviousEditTool() => EditTool = EditTool.PreviousTool();
    public RulerFormat RulerFormat => Project.RulerFormat;
    private FrameRange? loopRegion;
    public FrameRange? LoopRegion
    {
        get => loopRegion;
        set => SetProperty(ref loopRegion, value);
    }
    public void SetRulerFormat(RulerFormat format)
    {
        if (Project.RulerFormat == format) return;
        Project.RulerFormat = format;
        Project.RulerTimeFormat = format.ToDisplayName();
        OnPropertyChanged(nameof(RulerFormat));
        Changed?.Invoke();
        Status = $"Ruler format set to {format.ToDisplayName()}.";
    }
    public void SetTimeAtCursor(long frame)
    {
        Position = frame;
        Changed?.Invoke();
        Status = $"Moved cursor to {TimecodeFormatter.Format(frame, Project.FrameRate, Project.RulerFormat, Project.AudioSampleRate, Project.BeatsPerMinute, Project.BeatsPerMeasure, Project.RulerStartTimeFrames)}.";
    }

    public MediaBin CreateMediaBin(string name, Guid? parentBinId = null)
    {
        var bin = Editor.CreateMediaBin(name, parentBinId);
        Changed?.Invoke();
        Status = $"Created media bin \"{name}\".";
        return bin;
    }

    public void RenameMediaBin(Guid binId, string newName)
    {
        Editor.RenameMediaBin(binId, newName);
        Changed?.Invoke();
        Status = $"Renamed bin to \"{newName}\".";
    }

    public void DeleteMediaBin(Guid binId)
    {
        Editor.DeleteMediaBin(binId);
        Changed?.Invoke();
        Status = "Deleted media bin.";
    }

    public void AddMediaToBin(Guid binId, Guid mediaId)
    {
        Editor.AddMediaToBin(binId, mediaId);
        Changed?.Invoke();
        var bin = Project.MediaBins.FirstOrDefault(b => b.Id == binId);
        Status = $"Added media to bin \"{bin?.Name ?? "Bin"}\".";
    }

    public void RemoveMediaFromBin(Guid binId, Guid mediaId)
    {
        Editor.RemoveMediaFromBin(binId, mediaId);
        Changed?.Invoke();
        var bin = Project.MediaBins.FirstOrDefault(b => b.Id == binId);
        Status = $"Removed media from bin \"{bin?.Name ?? "Bin"}\".";
    }

    public void SetMediaTags(Guid mediaId, IEnumerable<string> tags)
    {
        Editor.SetMediaTags(mediaId, tags);
        Changed?.Invoke();
        Status = "Updated media tags.";
    }

    public void MakeCompositingChild(Guid trackId)
    {
        Editor.MakeCompositingChild(trackId);
        Changed?.Invoke();
        Status = "Indented track as compositing child.";
    }

    public void MakeCompositingParent(Guid trackId)
    {
        Editor.MakeCompositingParent(trackId);
        Changed?.Invoke();
        Status = "Outdented track as compositing parent.";
    }

    public void ToggleTrackAdjustment(Guid trackId)
    {
        Editor.ToggleTrackAdjustment(trackId);
        Changed?.Invoke();
        var track = Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        Status = track?.IsAdjustmentTrack == true ? "Track enabled as adjustment track." : "Track converted to normal video track.";
    }

    public void SetTrackAudioBus(Guid trackId, Guid? busId)
    {
        Editor.SetTrackAudioBus(trackId, busId);
        Changed?.Invoke();
        var bus = busId.HasValue ? Project.AudioBuses.FirstOrDefault(b => b.Id == busId.Value) : null;
        Status = bus is not null ? $"Track routed to {bus.Name}." : "Track routed to Master bus.";
    }

    public AudioBusTrack CreateAudioBus(string? name = null)
    {
        var bus = Editor.CreateAudioBus(name);
        Changed?.Invoke();
        Status = $"Created {bus.Name}.";
        return bus;
    }

    public void DeleteAudioBus(Guid busId)
    {
        Editor.DeleteAudioBus(busId);
        Changed?.Invoke();
        Status = "Deleted audio bus.";
    }
    private long editRevision;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private List<CopiedClip> clipboard = [];
    private List<MediaAsset> clipboardMedia = [];
    private Rational clipboardRate = new(30, 1);
    public bool CanPaste => clipboard.Count > 0;
    private sealed record CopiedClip(Guid TrackId, string TrackName, TrackKind Kind, TimelineClip Clip);

    public EditorViewModel(MediaService media, TimelineRenderService renderer, ExportService exporter)
    {
        Media = media; Renderer = renderer; Exporter = exporter;
        Editor = new TimelineEditor(EmptyProject());
        Editor.Changed += OnEdit;
    }
    private static ProjectDocument EmptyProject() => new()
    {
        Tracks = [new() { Name = "Video 1", Kind = TrackKind.Video }, new() { Name = "Audio 1", Kind = TrackKind.Audio }]
    };
    private void OnEdit(object? sender, EventArgs e)
    {
        editRevision++;
        Dirty = true;
        Selection.IntersectWith(Project.Tracks.SelectMany(t => t.Clips).Select(c => c.Id));
        Position = Position;
        RefreshLibrary();
        Changed?.Invoke();
    }
    public void NewProject() => Replace(EmptyProject(), null);
    public void Replace(ProjectDocument project, string? path)
    {
        Editor.Changed -= OnEdit;
        Editor = new TimelineEditor(project);
        editRevision++;
        Editor.Changed += OnEdit;
        ProjectPath = path; Position = 0; Selection.Clear(); Dirty = path is null && project.Media.Count > 0;
        RefreshLibrary(); OnPropertyChanged(nameof(Title)); Changed?.Invoke();
    }
    public void RefreshLibrary()
    {
        var counts = Project.Tracks.SelectMany(t => t.Clips).GroupBy(c => c.MediaId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var item in Library.Where(i => Project.Media.All(m => m.Id != i.Asset.Id)).ToList()) Library.Remove(item);
        foreach (var item in Library)
        {
            var count = counts.GetValueOrDefault(item.Asset.Id, 0);
            item.Rebind(Project.Media.Single(m => m.Id == item.Asset.Id), count);
        }
        foreach (var asset in Project.Media.Where(m => Library.All(i => i.Asset.Id != m.Id)))
        {
            var item = new MediaItem(asset);
            item.UseCount = counts.GetValueOrDefault(asset.Id, 0);
            Library.Add(item);
        }
    }
    public async Task<MediaAsset> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(path);
        var existing = Project.Media.FirstOrDefault(m => string.Equals(m.Path, full, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var editor = Editor;
        Status = $"Importing {Path.GetFileName(path)}…";
        var media = await Media.ProbeAsync(full, cancellationToken);
        if (!ReferenceEquals(Editor, editor)) throw new OperationCanceledException("The project changed while importing media.");
        Editor.AddMedia(media);
        Status = $"Imported {media.Name}. Double-click to add at the cursor, or drag to a track.";
        return media;
    }
    public void Place(Guid mediaId, Guid? trackId = null, long? at = null, bool advanceCursor = true)
    {
        var asset = Project.Media.Single(m => m.Id == mediaId);
        var start = at ?? Position;
        var duration = Math.Max(1, Project.FrameRate.Frames(asset.DurationSeconds));
        var chosen = trackId.HasValue ? Project.Tracks.Single(t => t.Id == trackId) : null;
        Editor.Execute(p =>
        {
            var primaryKind = chosen?.Kind ?? (asset.HasVideo ? TrackKind.Video : TrackKind.Audio);
            var target = chosen ?? p.Tracks.FirstOrDefault(t => t.Kind == primaryKind && !t.IsLocked);
            if (target is null)
            {
                target = new TimelineTrack { Kind = primaryKind, Name = $"{primaryKind} {p.Tracks.Count(t => t.Kind == primaryKind) + 1}" };
                if (primaryKind == TrackKind.Video) p.Tracks.Insert(0, target); else p.Tracks.Add(target);
            }
            if (target.IsLocked) throw new InvalidOperationException("Unlock the track before placing media.");
            if (primaryKind == TrackKind.Video && !asset.HasVideo || primaryKind == TrackKind.Audio && !asset.HasAudio)
                throw new InvalidOperationException("This media has no stream for the selected track.");
            var clip = new TimelineClip { MediaId = mediaId, Name = asset.Name, StartFrame = start, DurationFrames = duration };
            target.Clips.Add(clip); Selection.Clear(); Selection.Add(clip.Id);
            if (primaryKind == TrackKind.Video && asset.HasAudio)
            {
                clip.GroupId = Guid.NewGuid();
                var audio = p.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Audio && !t.IsLocked);
                if (audio is null) { audio = new TimelineTrack { Kind = TrackKind.Audio, Name = "Audio" }; p.Tracks.Add(audio); }
                var sound = clip.DeepClone(); sound.Id = Guid.NewGuid(); audio.Clips.Add(sound); Selection.Add(sound.Id);
            }
        });
        if (advanceCursor) { Position = start + duration; Changed?.Invoke(); }
    }

    public void PlaceSubclip(Guid mediaId, long sourceIn, long duration, Guid? trackId = null, long? at = null, bool advanceCursor = true, bool? ripple = null, AutoRippleMode? rippleMode = null, TrimmerPlacementStream stream = TrimmerPlacementStream.VideoAndAudio, double? playbackRate = null)
    {
        var asset = Project.Media.Single(m => m.Id == mediaId);
        var start = at ?? Position;
        var chosen = trackId.HasValue ? Project.Tracks.Single(t => t.Id == trackId) : null;
        var useRipple = ripple ?? Ripple;
        var mode = rippleMode ?? RippleMode;
        Editor.Execute(p =>
        {
            TrackKind primaryKind;
            if (stream == TrimmerPlacementStream.AudioOnly)
            {
                primaryKind = TrackKind.Audio;
            }
            else if (stream == TrimmerPlacementStream.VideoOnly)
            {
                primaryKind = TrackKind.Video;
            }
            else
            {
                primaryKind = chosen?.Kind ?? (asset.HasVideo ? TrackKind.Video : TrackKind.Audio);
            }
            var target = chosen ?? p.Tracks.FirstOrDefault(t => t.Kind == primaryKind && !t.IsLocked);
            if (target is null)
            {
                target = new TimelineTrack { Kind = primaryKind, Name = $"{primaryKind} {p.Tracks.Count(t => t.Kind == primaryKind) + 1}" };
                if (primaryKind == TrackKind.Video) p.Tracks.Insert(0, target); else p.Tracks.Add(target);
            }
            if (target.IsLocked) throw new InvalidOperationException("Unlock the track before placing media.");
            if (primaryKind == TrackKind.Video && !asset.HasVideo || primaryKind == TrackKind.Audio && !asset.HasAudio)
                throw new InvalidOperationException("This media has no stream for the selected track.");

            TimelineTrack? audioTrack = null;
            if (stream == TrimmerPlacementStream.VideoAndAudio && primaryKind == TrackKind.Video && asset.HasAudio)
            {
                audioTrack = p.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Audio && !t.IsLocked);
                if (audioTrack is null) { audioTrack = new TimelineTrack { Kind = TrackKind.Audio, Name = "Audio" }; p.Tracks.Add(audioTrack); }
            }

            if (useRipple)
            {
                var affectedTrackIds = new HashSet<Guid> { target.Id };
                if (audioTrack is not null) affectedTrackIds.Add(audioTrack.Id);

                if (mode == AutoRippleMode.AffectedTracks)
                {
                    foreach (var t in p.Tracks.Where(tr => affectedTrackIds.Contains(tr.Id)))
                        foreach (var c in t.Clips.Where(cl => cl.StartFrame >= start))
                            c.StartFrame += duration;
                }
                else if (mode == AutoRippleMode.AffectedTracksAndMarkers)
                {
                    foreach (var t in p.Tracks.Where(tr => affectedTrackIds.Contains(tr.Id)))
                    {
                        foreach (var c in t.Clips.Where(cl => cl.StartFrame >= start))
                            c.StartFrame += duration;
                        if (t.Envelopes != null)
                            foreach (var env in t.Envelopes) env.ShiftPoints(start, duration);
                    }
                    if (p.AudioBuses != null)
                        foreach (var bus in p.AudioBuses)
                            if (bus.Envelopes != null)
                                foreach (var env in bus.Envelopes) env.ShiftPoints(start, duration);
                    foreach (var m in p.Markers.Where(mk => mk.Frame >= start))
                        m.Frame += duration;
                }
                else // AllTracksAndMarkers
                {
                    foreach (var t in p.Tracks)
                    {
                        foreach (var c in t.Clips.Where(cl => cl.StartFrame >= start))
                            c.StartFrame += duration;
                        if (t.Envelopes != null)
                            foreach (var env in t.Envelopes) env.ShiftPoints(start, duration);
                    }
                    if (p.AudioBuses != null)
                        foreach (var bus in p.AudioBuses)
                            if (bus.Envelopes != null)
                                foreach (var env in bus.Envelopes) env.ShiftPoints(start, duration);
                    if (p.MasterAudioEnvelopes != null)
                        foreach (var env in p.MasterAudioEnvelopes) env.ShiftPoints(start, duration);
                    foreach (var m in p.Markers.Where(mk => mk.Frame >= start))
                        m.Frame += duration;
                }
            }

            var rate = playbackRate.HasValue ? Math.Clamp(playbackRate.Value, 0.05, 10.0) : 1.0;
            var clip = new TimelineClip
            {
                MediaId = mediaId,
                Name = asset.Name,
                StartFrame = start,
                SourceInFrame = Math.Max(0, sourceIn),
                DurationFrames = Math.Max(1, duration),
                PlaybackRate = rate
            };
            target.Clips.Add(clip); Selection.Clear(); Selection.Add(clip.Id);
            if (audioTrack is not null)
            {
                var group = Guid.NewGuid();
                clip.GroupId = group;
                var sound = clip.DeepClone();
                sound.Id = Guid.NewGuid();
                sound.GroupId = group;
                sound.PlaybackRate = rate;
                audioTrack.Clips.Add(sound);
                Selection.Add(sound.Id);
            }
        });
        if (advanceCursor) { Position = start + duration; Changed?.Invoke(); }
    }

    public void PlaceSubclipUpToCursor(Guid mediaId, long sourceIn, long duration, Guid? trackId = null, TrimmerPlacementStream stream = TrimmerPlacementStream.VideoAndAudio)
    {
        var start = Math.Max(0, Position - duration);
        PlaceSubclip(mediaId, sourceIn, duration, trackId, at: start, advanceCursor: false, stream: stream);
    }

    public void PlaceSubclipFitToFill(Guid mediaId, long sourceIn, long sourceDuration, FrameRange targetRange, Guid? trackId = null, TrimmerPlacementStream stream = TrimmerPlacementStream.VideoAndAudio)
    {
        var targetDuration = Math.Max(1, targetRange.FrameCount);
        var rate = (double)sourceDuration / targetDuration;
        PlaceSubclip(mediaId, sourceIn, targetDuration, trackId, at: targetRange.StartFrame, advanceCursor: true, stream: stream, playbackRate: rate);
    }

    public void Split()
    {
        if (!Project.Tracks.SelectMany(t => t.Clips).Any(c => Selection.Contains(c.Id) && c.StartFrame < Position && c.EndFrame > Position))
        { Status = "Select a clip and place the cursor inside it to split."; return; }
        Editor.SplitSelection(Selection, Position, IgnoreGroups);
        Status = "Split selection at cursor.";
    }
    public void Delete()
    {
        if (Selection.Count == 0) return;
        Editor.DeleteSelection(Selection, Ripple, IgnoreGroups, RippleMode);
        Selection.Clear(); Status = "Deleted selection.";
    }

    /// <summary>
    /// Inserts empty time at the current timeline edit cursor across tracks and ripples downstream events,
    /// markers, track automation envelopes, and bus envelopes matching the specified AutoRippleMode.
    /// Aligned with Vegas Pro "Insert > Time...".
    /// </summary>
    public void InsertTime(long durationFrames, AutoRippleMode? rippleMode = null)
    {
        if (durationFrames <= 0) return;
        var mode = rippleMode ?? RippleMode;
        Editor.InsertTime(Position, durationFrames, mode);
        Status = $"Inserted {TimecodeFormatter.Format(durationFrames, Project.FrameRate, Project.RulerFormat, Project.AudioSampleRate, Project.BeatsPerMinute, Project.BeatsPerMeasure, Project.RulerStartTimeFrames)} at {TimecodeFormatter.Format(Position, Project.FrameRate, Project.RulerFormat, Project.AudioSampleRate, Project.BeatsPerMinute, Project.BeatsPerMeasure, Project.RulerStartTimeFrames)} ({mode}).";
    }

    /// <summary>
    /// Deletes the marked time range across tracks, cutting through events and rippling downstream material leftward.
    /// Uses active timeline loop region when marked, or explicit range arguments.
    /// Aligned with Vegas Pro "Edit > Delete Time" / "Ripple Delete".
    /// </summary>
    public void DeleteTime(long? startFrame = null, long? durationFrames = null, AutoRippleMode? rippleMode = null)
    {
        var start = startFrame ?? LoopRegion?.StartFrame ?? 0;
        var duration = durationFrames ?? LoopRegion?.FrameCount ?? 0;
        if (duration <= 0)
        {
            if (Selection.Count > 0)
            {
                Editor.DeleteSelection(Selection, ripple: true, ignoreGroups: IgnoreGroups, rippleMode: rippleMode ?? RippleMode);
                Selection.Clear();
                Status = "Deleted selected events with ripple.";
            }
            return;
        }

        var mode = rippleMode ?? RippleMode;
        Editor.DeleteTime(start, duration, mode);
        Status = $"Deleted {TimecodeFormatter.Format(duration, Project.FrameRate, Project.RulerFormat, Project.AudioSampleRate, Project.BeatsPerMinute, Project.BeatsPerMeasure, Project.RulerStartTimeFrames)} from {TimecodeFormatter.Format(start, Project.FrameRate, Project.RulerFormat, Project.AudioSampleRate, Project.BeatsPerMinute, Project.BeatsPerMeasure, Project.RulerStartTimeFrames)} ({mode}).";
    }

    /// <summary>
    /// Resets all envelope points on a clip to neutral/default values, matching Vegas Pro "Reset All Points".
    /// </summary>
    public void ResetClipEnvelope(Guid clipId, EnvelopeType type)
    {
        Editor.ResetClipEnvelope(clipId, type);
        Status = $"Reset {type} envelope points.";
        Changed?.Invoke();
    }

    /// <summary>
    /// Sets all points of a clip envelope to a constant value, matching Vegas Pro "Set to...".
    /// </summary>
    public void SetAllClipEnvelopePoints(Guid clipId, EnvelopeType type, double value)
    {
        Editor.SetAllClipEnvelopePoints(clipId, type, value);
        Status = $"Set all {type} envelope points to {Math.Round(value * 100)}%.";
        Changed?.Invoke();
    }

    /// <summary>
    /// Resets all envelope points on a track to neutral/default values, matching Vegas Pro "Reset All Points".
    /// </summary>
    public void ResetTrackEnvelope(Guid trackId, EnvelopeType type)
    {
        Editor.ResetTrackEnvelope(trackId, type);
        Status = $"Reset Track {type} envelope points.";
        Changed?.Invoke();
    }

    /// <summary>
    /// Sets all points of a track envelope to a constant value, matching Vegas Pro "Set to...".
    /// </summary>
    public void SetAllTrackEnvelopePoints(Guid trackId, EnvelopeType type, double value)
    {
        Editor.SetAllTrackEnvelopePoints(trackId, type, value);
        Status = $"Set all Track {type} envelope points to {Math.Round(value * 100)}%.";
        Changed?.Invoke();
    }
    public async Task SaveAsync(string path)
    {
        var editor = Editor;
        var revision = editRevision;
        var snapshot = Project.DeepClone();
        // Older snapshots must finish before a newer save can replace the same file.
        await saveGate.WaitAsync();
        try
        {
            await ProjectStore.SaveAsync(snapshot, path, createBackup: CreateProjectBackups);
            if (!ReferenceEquals(Editor, editor)) return;
            ProjectPath = path;
            if (revision == editRevision) Dirty = false;
            Status = revision == editRevision ? $"Saved {Path.GetFileName(path)}." : "Saved the earlier revision. Newer edits still need saving.";
        }
        finally { saveGate.Release(); }
    }
    public void MarkSaved() => Dirty = false;

    public void Copy()
    {
        if (Selection.Count == 0) return;
        var selected = Editor.ExpandSelection(Selection, IgnoreGroups);
        clipboard = Project.Tracks.SelectMany(t => t.Clips.Where(c => selected.Contains(c.Id))
            .Select(c => new CopiedClip(t.Id, t.Name, t.Kind, c.DeepClone()))).ToList();
        var mediaIds = clipboard.Select(c => c.Clip.MediaId).ToHashSet();
        clipboardMedia = Project.Media.Where(m => mediaIds.Contains(m.Id)).Select(m => m.DeepClone()).ToList();
        clipboardRate = Project.FrameRate;
        Status = $"Copied {clipboard.Count} clips."; Changed?.Invoke();
    }
    public void Cut()
    {
        if (Selection.Count == 0) return;
        var selected = Editor.ExpandSelection(Selection, IgnoreGroups);
        if (Project.Tracks.Any(t => t.IsLocked && t.Clips.Any(c => selected.Contains(c.Id))))
            throw new InvalidOperationException("Unlock the selected tracks before cutting.");
        Copy(); Delete();
    }
    public void Paste()
    {
        if (!CanPaste) return;
        var origin = clipboard.Min(c => c.Clip.StartFrame);
        var at = Position; var pasted = new List<TimelineClip>();
        Editor.Execute(p =>
        {
            long ConvertFrame(long frame) => checked((long)decimal.Round((decimal)frame * p.FrameRate.Numerator * clipboardRate.Denominator
                / (p.FrameRate.Denominator * (decimal)clipboardRate.Numerator), 0, MidpointRounding.AwayFromZero));
            var assets = new Dictionary<Guid, Guid>();
            foreach (var source in clipboardMedia)
            {
                var existing = p.Media.FirstOrDefault(m => string.Equals(m.Path, source.Path, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    existing = source.DeepClone();
                    if (p.Media.Any(m => m.Id == existing.Id)) existing.Id = Guid.NewGuid();
                    p.Media.Add(existing);
                }
                assets[source.Id] = existing.Id;
            }
            var destinations = new Dictionary<Guid, TimelineTrack>(); var used = new HashSet<Guid>();
            var groups = new Dictionary<Guid, Guid>();
            var copies = new Dictionary<Guid, TimelineClip>();
            foreach (var copied in clipboard)
            {
                if (!destinations.TryGetValue(copied.TrackId, out var track))
                {
                    track = p.Tracks.FirstOrDefault(t => t.Id == copied.TrackId)
                        ?? p.Tracks.FirstOrDefault(t => t.Kind == copied.Kind && !used.Contains(t.Id) && !t.IsLocked);
                    if (track is null)
                    {
                        track = new TimelineTrack { Kind = copied.Kind, Name = copied.TrackName };
                        if (track.Kind == TrackKind.Video) p.Tracks.Insert(0, track); else p.Tracks.Add(track);
                    }
                    if (track.IsLocked) throw new InvalidOperationException("Unlock the destination track before pasting.");
                    destinations.Add(copied.TrackId, track); used.Add(track.Id);
                }
            }

            var pasteSpan = ConvertFrame(clipboard.Max(c => c.Clip.EndFrame) - origin);
            if (Ripple && pasteSpan > 0)
            {
                var destinationTrackIds = destinations.Values.Select(d => d.Id).ToHashSet();
                if (RippleMode == AutoRippleMode.AffectedTracks)
                {
                    foreach (var t in p.Tracks.Where(tr => destinationTrackIds.Contains(tr.Id)))
                        foreach (var c in t.Clips.Where(cl => cl.StartFrame >= at))
                            c.StartFrame += pasteSpan;
                }
                else if (RippleMode == AutoRippleMode.AffectedTracksAndMarkers)
                {
                    foreach (var t in p.Tracks.Where(tr => destinationTrackIds.Contains(tr.Id)))
                    {
                        foreach (var c in t.Clips.Where(cl => cl.StartFrame >= at))
                            c.StartFrame += pasteSpan;
                        if (t.Envelopes != null)
                            foreach (var env in t.Envelopes) env.ShiftPoints(at, pasteSpan);
                    }
                    if (p.AudioBuses != null)
                        foreach (var bus in p.AudioBuses)
                            if (bus.Envelopes != null)
                                foreach (var env in bus.Envelopes) env.ShiftPoints(at, pasteSpan);
                    foreach (var m in p.Markers.Where(mk => mk.Frame >= at))
                        m.Frame += pasteSpan;
                }
                else // AllTracksAndMarkers
                {
                    foreach (var t in p.Tracks)
                    {
                        foreach (var c in t.Clips.Where(cl => cl.StartFrame >= at))
                            c.StartFrame += pasteSpan;
                        if (t.Envelopes != null)
                            foreach (var env in t.Envelopes) env.ShiftPoints(at, pasteSpan);
                    }
                    if (p.AudioBuses != null)
                        foreach (var bus in p.AudioBuses)
                            if (bus.Envelopes != null)
                                foreach (var env in bus.Envelopes) env.ShiftPoints(at, pasteSpan);
                    if (p.MasterAudioEnvelopes != null)
                        foreach (var env in p.MasterAudioEnvelopes) env.ShiftPoints(at, pasteSpan);
                    foreach (var m in p.Markers.Where(mk => mk.Frame >= at))
                        m.Frame += pasteSpan;
                }
            }

            foreach (var copied in clipboard)
            {
                var track = destinations[copied.TrackId];
                var clip = copied.Clip.DeepClone(); clip.Id = Guid.NewGuid(); clip.MediaId = assets[clip.MediaId];
                var offset = ConvertFrame(clip.StartFrame - origin);
                clip.DurationFrames = Math.Max(1, checked(ConvertFrame(clip.EndFrame - origin) - offset));
                clip.StartFrame = checked(at + offset);
                clip.SourceInFrame = ConvertFrame(copied.Clip.SourceInFrame);
                var available = p.FrameRate.Frames(clipboardMedia.Single(m => m.Id == copied.Clip.MediaId).DurationSeconds);
                if (clip.VideoSourceMode == VideoSourceMode.Bounded && available > 0)
                {
                    clip.DurationFrames = Math.Min(clip.DurationFrames, available);
                    clip.SourceInFrame = Math.Min(clip.SourceInFrame, available - clip.DurationFrames);
                }
                clip.FadeOffsetFrames = ConvertFrame(copied.Clip.FadeOffsetFrames);
                var envelope = copied.Clip.FadeDurationFrames is { } fadeDuration
                    ? Math.Max(checked(clip.FadeOffsetFrames + clip.DurationFrames), ConvertFrame(fadeDuration)) : clip.DurationFrames;
                clip.FadeDurationFrames = copied.Clip.FadeDurationFrames.HasValue ? envelope : null;
                clip.FadeInFrames = Math.Min(envelope, ConvertFrame(copied.Clip.FadeInFrames));
                clip.FadeOutFrames = Math.Min(envelope - clip.FadeInFrames, ConvertFrame(copied.Clip.FadeOutFrames));
                clip.CropOffsetFrames = ConvertFrame(copied.Clip.CropOffsetFrames);
                if (clip.Crop is { } crop)
                {
                    // A lower destination rate can collapse several keys onto one frame.
                    crop.Keyframes = crop.Keyframes.Select(key => new CropKeyframe { Frame = ConvertFrame(key.Frame), State = key.State, Interpolation = key.Interpolation, Smoothness = key.Smoothness })
                        .GroupBy(key => key.Frame).Select(group => group.Last()).OrderBy(key => key.Frame).ToList();
                }
                if (clip.GroupId is { } group)
                {
                    if (!groups.TryGetValue(group, out var replacement)) groups[group] = replacement = Guid.NewGuid();
                    clip.GroupId = replacement;
                }
                track.Clips.Add(clip); pasted.Add(clip);
                copies.Add(copied.Clip.Id, clip);
            }
            foreach (var incoming in clipboard.Where(item => item.Clip.FadeInTransition is not null &&
                item.Clip.FadeOffsetFrames == 0 && item.Clip.FadeDurationFrames is null))
            {
                var source = incoming.Clip;
                var outgoing = clipboard.Where(item => item.TrackId == incoming.TrackId && item.Clip.Id != source.Id &&
                    item.Clip.FadeOffsetFrames == 0 && item.Clip.FadeDurationFrames is null &&
                    item.Clip.StartFrame < source.StartFrame && item.Clip.EndFrame > source.StartFrame &&
                    item.Clip.EndFrame <= source.EndFrame && item.Clip.FadeOutFrames == source.FadeInFrames &&
                    item.Clip.EndFrame - source.StartFrame == source.FadeInFrames)
                    .OrderByDescending(item => item.Clip.StartFrame).FirstOrDefault();
                if (outgoing is null) continue;
                var pastedOutgoing = copies[outgoing.Clip.Id];
                var pastedIncoming = copies[source.Id];
                // Rounding a duration alone can lose an overlap whose converted endpoints still differ.
                var overlap = Math.Max(0, pastedOutgoing.EndFrame - pastedIncoming.StartFrame);
                pastedOutgoing.FadeOutFrames = pastedIncoming.FadeInFrames = overlap;
            }
        });
        Selection.Clear(); Selection.UnionWith(pasted.Select(c => c.Id));
        Position = pasted.Max(c => c.EndFrame); Changed?.Invoke(); Status = $"Pasted {pasted.Count} clips.";
    }
}
