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
    public string Details => $"{(Asset.HasVideo ? $"{Asset.Width} × {Asset.Height}  •  {Asset.FrameRate:0.###} fps" : "Audio")}  •  {Asset.DurationSeconds:0.00} s";
    private System.Windows.Media.ImageSource? thumbnail;
    public System.Windows.Media.ImageSource? Thumbnail { get => thumbnail; set => SetProperty(ref thumbnail, value); }
    public void Rebind(MediaAsset current)
    {
        if (!string.Equals(Asset.Path, current.Path, StringComparison.OrdinalIgnoreCase)) Thumbnail = null;
        Asset = current;
        OnPropertyChanged(nameof(Asset)); OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Details));
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
    public string Title => $"{Project.Name}{(Dirty ? " *" : "")} — OpenRend";
    private long position;
    public long Position { get => position; set => SetProperty(ref position, Math.Clamp(value, 0, Project.DurationFrames)); }
    private bool busy;
    public bool Busy { get => busy; set => SetProperty(ref busy, value); }
    private double progress;
    public double Progress { get => progress; set => SetProperty(ref progress, value); }
    public bool Ripple { get; set; }
    public bool IgnoreGroups { get; set; }
    private long editRevision;

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
    private void RefreshLibrary()
    {
        foreach (var item in Library.Where(i => Project.Media.All(m => m.Id != i.Asset.Id)).ToList()) Library.Remove(item);
        foreach (var item in Library) item.Rebind(Project.Media.Single(m => m.Id == item.Asset.Id));
        foreach (var asset in Project.Media.Where(m => Library.All(i => i.Asset.Id != m.Id))) Library.Add(new MediaItem(asset));
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
        Editor.DeleteSelection(Selection, Ripple, IgnoreGroups);
        Selection.Clear(); Status = "Deleted selection.";
    }
    public async Task SaveAsync(string path)
    {
        var editor = Editor;
        var revision = editRevision;
        await ProjectStore.SaveAsync(Project.DeepClone(), path);
        if (!ReferenceEquals(Editor, editor)) return;
        ProjectPath = path;
        if (revision == editRevision) Dirty = false;
        Status = revision == editRevision ? $"Saved {Path.GetFileName(path)}." : "Saved the earlier revision. Newer edits still need saving.";
    }
    public void MarkSaved() => Dirty = false;
}
