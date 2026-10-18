using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Project;
using OpenRend.UI;

namespace OpenRend.App;

public partial class MainWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly DispatcherTimer autosave = new() { Interval = TimeSpan.FromSeconds(30) };
    private PlaybackSession? playback;
    private CancellationTokenSource? exportCancellation;
    private long revision;
    private bool previewRunning;
    private bool previewRequested;
    private bool playbackStarting;
    private bool safeAreas;
    private bool masterMuted;
    private string mediaFilter = "All";
    private int previewWidth, previewHeight;
    private long playbackGeneration;
    private bool initialized;
    private bool allowClose;
    private long lastPreview = -1;
    private bool isDraggingTextInPreview;
    private Guid? previewDraggedClipId;
    private SubtitleStyle? previewDragOriginalStyle;
    private string? previewDragOriginalPath;
    private Point previewDragStartMouse;
    private (float X, float Y) previewDragStartLocation;
    private TextInteractHandle previewDragHandle;
    private float previewDragStartFontSize;
    private System.Drawing.PointF previewDragInitialHandlePos;
    private System.Drawing.PointF previewDragCenter;
    private readonly string recoveryPath = Path.Combine(Environment.GetEnvironmentVariable("OPENREND_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRend"), "recovery.openrend");
    private readonly Dictionary<Guid, string> cachedMediaPaths = [];
    public MainWindow(EditorViewModel viewModel)
    {
        vm = viewModel;
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var available = Math.Max(320, ActualWidth - 420);
            if (PreviewWidth.Width.Value > available) PreviewWidth.Width = new GridLength(available);
            var height = Math.Max(230, ActualHeight - 261);
            if (WorkspaceHeight.Height.Value > height) WorkspaceHeight.Height = new GridLength(height);
        };
        SourceInitialized += (_, _) =>
        {
            var enabled = 1;
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            System.Windows.Interop.HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
            DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        };
        DataContext = vm;
        Timeline.SeekRequested += frame => { Pause(); vm.Position = frame; UpdatePosition(); RequestPreview(); };
        Timeline.SelectionChanged += () =>
        {
            vm.Selection.Clear(); vm.Selection.UnionWith(vm.Editor.ExpandSelection(Timeline.Selection, vm.IgnoreGroups));
            Timeline.Selection.UnionWith(vm.Selection); Timeline.InvalidateVisual(); UpdateInspector();
            CutButton.IsEnabled = CopyButton.IsEnabled = vm.Selection.Count > 0;
        };
        Timeline.EditRequested += gesture => Guard(() => ApplyGesture(gesture));
        Timeline.TrackLevelRequested += gesture => Guard(() =>
        {
            Pause(); vm.Editor.SetTrackLevels(gesture.TrackId, gain: gesture.Level == "Gain" ? gesture.Value : null,
                opacity: gesture.Level == "Opacity" ? gesture.Value : null, pan: gesture.Level == "Pan" ? gesture.Value : null);
        });
        Timeline.TrackAction += (id, action) => Guard(() =>
        {
            Pause(); var track = vm.Project.Tracks.Single(t => t.Id == id);
            vm.Editor.SetTrackState(id, isMuted: action == "Mute" ? !track.IsMuted : null,
                isSolo: action == "Solo" ? !track.IsSolo : null, isLocked: action == "Lock" ? !track.IsLocked : null,
                isVisible: action == "Visible" ? !track.IsVisible : null);
        });
        Timeline.MediaDropped += (media, track, frame) => Guard(() => { Pause(); vm.Place(media, track, frame, advanceCursor: false); });
        Timeline.TransitionDropped += gesture => Guard(() =>
        {
            gesture.Transition.Validate();
            Pause();
            if (gesture.IsCut)
            {
                var settings = EditingPreferencesStore.Load(EditingPreferencesPath);
                vm.Editor.ConvertCutToDissolve(gesture.OutgoingClipId, gesture.IncomingClipId,
                    settings.OverlapFrames(vm.Project.FrameRate), settings.CutOverlapAlignment, gesture.Transition);
            }
            else vm.Editor.ApplyDissolve(gesture.OutgoingClipId, gesture.IncomingClipId, gesture.Transition);
            vm.Status = gesture.IsCut
                ? (gesture.Transition.Kind == VideoTransitionKind.Dissolve && gesture.Transition.Preset == "Default"
                    ? "Cut converted to Default Dissolve."
                    : $"Cut converted to {gesture.Transition.Kind} ({gesture.Transition.Preset}).")
                : (gesture.Transition.Kind == VideoTransitionKind.Dissolve && gesture.Transition.Preset == "Default"
                    ? "Default Dissolve applied to the dropped overlap."
                    : $"{gesture.Transition.Kind} ({gesture.Transition.Preset}) applied to the dropped overlap.");
        });
        Timeline.SplitRequested += () => Guard(vm.Split);
        Timeline.OverlapToCutRequested += (outgoing, incoming) => Guard(() =>
        {
            Pause();
            var settings = EditingPreferencesStore.Load(EditingPreferencesPath);
            vm.Editor.ConvertOverlapToCut(outgoing, incoming, settings.CutOverlapAlignment);
            vm.Status = "Overlap converted to cut.";
        });
        Timeline.PanCropRequested += () => PanCropClick(this, new RoutedEventArgs());
        Timeline.VideoEffectsRequested += () => VideoEffectsClick(this, new RoutedEventArgs());
        Timeline.TrackFxRequested += id => OpenTrackEffects(id);
        Timeline.TrackColorGradingRequested += id => OpenTrackColorGrading(id);
        Timeline.ColorGradingRequested += () => ColorGradingClick(this, new RoutedEventArgs());
        Timeline.MotionTrackingRequested += id => OpenMotionTracking(id);
        Timeline.TextBasedEditingRequested += id => OpenTextBasedEditing(id);
        Timeline.TransitionPropertiesRequested += id => Guard(() =>
        {
            Pause();
            new TransitionPropertiesWindow(vm, id) { Owner = this }.ShowDialog();
        });
        Timeline.VideoSourceModeRequested += (id, mode) => Guard(() =>
        {
            Pause();
            vm.Editor.SetVideoSourceMode(id, mode);
            vm.Status = mode == VideoSourceMode.Loop ? "Video event looping enabled." : "Video event holds its last frame beyond the source.";
        });
        Timeline.VideoFadeCurveRequested += (id, curve, reciprocal, fadeOut) => Guard(() =>
        {
            Pause();
            vm.Editor.SetVideoFadeCurve(id, curve, reciprocal, fadeOut);
            vm.Status = fadeOut
                ? $"Video fade-out curve set to {curve}."
                : reciprocal.HasValue && reciprocal.Value != curve
                    ? $"Video crossfade curve set to In {curve}, Out {reciprocal.Value}."
                    : $"Video fade-in curve set to {curve}.";
        });
        Timeline.VideoEventActionRequested += async (id, action) =>
        {
            if (action == "PanCrop") await OpenPanCropAsync(id);
            else if (action == "VideoEffects") OpenVideoEffects(id);
            else if (action == "EditTextMedia") OpenEditTextMedia(id);
            else if (action == "ColorGrading") OpenColorGrading(id);
        };
        Timeline.ClipLockRequested += (id, locked) => Guard(() =>
        {
            Pause();
            vm.Editor.SetClipLock(id, locked);
            vm.Status = locked ? "Locked event." : "Unlocked event.";
            UpdateInspector();
        });
        Timeline.ClipMuteRequested += (id, muted) => Guard(() =>
        {
            Pause();
            vm.Editor.SetClipMute(id, muted);
            vm.Status = muted ? "Muted event." : "Unmuted event.";
            UpdateInspector();
            RequestPreview();
        });
        Timeline.DeleteRequested += () => Guard(vm.Delete);
        Timeline.InsertVideoTrackRequested += idx => Guard(() => vm.Editor.InsertTrack(idx, TrackKind.Video));
        Timeline.InsertAudioTrackRequested += idx => Guard(() => vm.Editor.InsertTrack(idx, TrackKind.Audio));
        Timeline.DuplicateTrackRequested += id => Guard(() => vm.Editor.DuplicateTrack(id));
        Timeline.DeleteTrackRequested += id => Guard(() => vm.Editor.DeleteTrack(id));

        Preview.CanvasMouseDown += OnPreviewMouseDown;
        Preview.CanvasMouseMove += OnPreviewMouseMove;
        Preview.CanvasMouseUp += OnPreviewMouseUp;
        Preview.CanvasMouseDoubleClick += OnPreviewMouseDoubleClick;
        Preview.Ready += () => { PreviewInfo.Text = PreviewDescription(); RequestPreview(); };
        vm.Changed += Refresh;
        timer.Tick += async (_, _) =>
        {
            if (playback is null) return;
            try
            {
                await playback.CheckAsync();
                vm.Position = playback.CurrentFrame; UpdatePosition();
                var meterHeight = Math.Max(0, MeterArea.ActualHeight);
                static double MeterPosition(double peak) => peak <= .001 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);
                LeftMeter.Height = meterHeight * MeterPosition(playback.PeakLeft);
                RightMeter.Height = meterHeight * MeterPosition(playback.PeakRight);
                if (vm.Position != lastPreview) RequestPreview();
                if (playback.IsFinished || vm.Position >= vm.Project.DurationFrames) Pause();
            }
            catch (Exception ex) { Pause(); ShowError(ex); }
        };
        autosave.Tick += async (_, _) =>
        {
            if (!vm.Dirty || vm.Busy) return;
            try { await ProjectStore.SaveAsync(vm.Project.DeepClone(), recoveryPath); }
            catch (Exception ex) { vm.Status = "Recovery snapshot could not be saved: " + ex.Message; }
        };
        Loaded += (_, _) => { initialized = true; LoadWorkspace(); InitializeTransitionBrowser(); Refresh(); autosave.Start(); };
    }

    private void Guard(Action action)
    {
        if (vm.Busy) return;
        try { action(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private async Task GuardAsync(Func<Task> action)
    {
        if (vm.Busy) return;
        try { await action(); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ShowError(Exception ex)
    {
        vm.Status = ex.Message;
        MessageBox.Show(this, ex.Message, "OpenRend", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    private void Refresh()
    {
        revision++;
        Timeline.Project = vm.Project;
        Timeline.Selection.Clear(); Timeline.Selection.UnionWith(vm.Selection);
        Timeline.InvalidateVisual(); UpdatePosition(); UpdateInspector();
        UndoMenu.IsEnabled = UndoButton.IsEnabled = vm.Editor.CanUndo;
        RedoMenu.IsEnabled = RedoButton.IsEnabled = vm.Editor.CanRedo;
        CutButton.IsEnabled = CopyButton.IsEnabled = vm.Selection.Count > 0;
        PasteButton.IsEnabled = vm.CanPaste;
        RenderButton.IsEnabled = vm.Project.DurationFrames > 0 && !vm.Busy;
        EmptyLibrary.Visibility = vm.Library.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MediaCount.Text = $"{vm.Library.Count} source files";
        UpdateMediaFilter();
        TimelineScroll.Maximum = Math.Max(1, vm.Project.FrameRate.Seconds(vm.Project.DurationFrames) * Timeline.PixelsPerSecond);
        TimelineScroll.ViewportSize = Math.Max(1, Timeline.ActualWidth - TimelineView.HeaderWidth);
        PreviewInfo.Text = PreviewDescription();
        if (vm.Project.Media.Any(m => !cachedMediaPaths.TryGetValue(m.Id, out var path) || path != m.Path)) _ = PopulateMediaVisualsAsync();
        RequestPreview();
    }
    private string PreviewDescription()
    {
        var backend = $"Composite: {(vm.Renderer.IsHardwareCompositing ? "GPU" : vm.Renderer.IsD3DCompositing ? "WARP" : "CPU")}; display: Direct3D 11 {(Preview.IsHardware ? "hardware" : "software")}";
        PreviewInfo.ToolTip = backend;
        System.Windows.Automation.AutomationProperties.SetHelpText(PreviewInfo, Preview.IsReady ? backend : "Preview is starting.");
        return $"Project: {vm.Project.Width}×{vm.Project.Height}, {vm.Project.FrameRate.Value:0.000}p     Preview: {previewWidth}×{previewHeight}     Frame: {(lastPreview < 0 ? "—" : lastPreview.ToString(CultureInfo.InvariantCulture))}     Display: {Preview.ActualWidth:0}×{Preview.ActualHeight:0}";
    }
    private void MediaFilterChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        mediaFilter = (e.NewValue as TreeViewItem)?.Tag as string ?? "All";
        if (MediaList is not null) UpdateMediaFilter();
    }
    private void UpdateMediaFilter()
    {
        var view = CollectionViewSource.GetDefaultView(vm.Library);
        view.Filter = item => item is MediaItem media && mediaFilter switch
        {
            "Video" => media.Asset.HasVideo,
            "Audio" => media.Asset.HasAudio,
            "Missing" => !File.Exists(media.Asset.Path),
            _ => true
        };
        EmptyLibrary.Text = vm.Library.Count == 0 ? "Import video or audio\nCtrl+I or drop files here" : "No media matches this view";
        EmptyLibrary.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }
    private void UpdatePosition()
    {
        Timeline.Position = vm.Position; Timeline.InvalidateVisual();
        Timecode.Text = TimelineView.FormatTimecode(vm.Position, vm.Project.FrameRate);
    }
    private async void RequestPreview()
    {
        if (!initialized) return;
        previewRequested = true;
        if (previewRunning) return;
        previewRunning = true;
        try
        {
            while (previewRequested && IsLoaded)
            {
                previewRequested = false;
                var project = vm.Project.DeepClone(); var frame = vm.Position;
                var activeClip = SelectedClip;
                if (activeClip != null && frame >= activeClip.StartFrame && frame < activeClip.EndFrame &&
                    vm.Project.Media.Any(m => m.Id == activeClip.MediaId && (m.Path.StartsWith("openrend://text", StringComparison.OrdinalIgnoreCase) || m.Path.StartsWith("openrend://subtitles", StringComparison.OrdinalIgnoreCase))))
                {
                    vm.Renderer.ActiveSubtitleClipId = activeClip.Id;
                }
                else
                {
                    vm.Renderer.ActiveSubtitleClipId = null;
                }
                var request = new PreviewFrameRequest(revision, playbackGeneration, frame);
                var zoom = PreviewZoom.SelectedIndex switch { 1 => 2.0, 2 => .5, _ => 1.0 }; var guides = safeAreas;
                var (width, height) = PreviewQuality.SelectedIndex switch
                {
                    0 => FitSize(project, 320),
                    2 => (project.Width, project.Height),
                    _ => FitSize(project, 640)
                };
                var pixels = await Task.Run(() => PreviewDisplay.Apply(vm.Renderer.RenderFrame(project, frame, width, height), width, height, zoom, guides));
                // The audio clock can advance during decoding. Show completed playback frames,
                // then catch up to its latest position instead of discarding every late frame.
                if (IsLoaded && request.CanPresent(revision, playbackGeneration, vm.Position, playback is not null))
                {
                    Preview.Present(pixels, width, height); previewWidth = width; previewHeight = height; lastPreview = frame; PreviewInfo.Text = PreviewDescription();
                }
                else previewRequested = true;
            }
        }
        catch (Exception ex) { vm.Status = "Preview: " + ex.Message; }
        finally { previewRunning = false; }
    }
    private static (int, int) FitSize(ProjectDocument p, int maxWidth)
    {
        var width = Math.Min(maxWidth, p.Width);
        return (width, Math.Max(1, (int)Math.Round(width * p.Height / (double)p.Width)));
    }
    public async Task OpenPathAsync(string path)
    {
        if (!await ConfirmUnsavedAsync()) return;
        Pause();
        var project = await ProjectStore.LoadAsync(path);
        vm.Replace(project, path);
        RecordRecentProject(path);
        await PopulateMediaVisualsAsync();
        var missing = ProjectStore.FindMissingMedia(project);
        vm.Status = missing.Count > 0 ? $"{missing.Count} missing media files. File → Relink missing media." : $"Opened {Path.GetFileName(path)}.";
    }
    private async Task PopulateMediaVisualsAsync()
    {
        foreach (var item in vm.Library.ToArray())
        {
            if (cachedMediaPaths.TryGetValue(item.Asset.Id, out var cachedPath) && cachedPath == item.Asset.Path) continue;
            var asset = item.Asset;
            cachedMediaPaths[asset.Id] = asset.Path;
            Timeline.Thumbnails.Remove(asset.Id); Timeline.Waveforms.Remove(asset.Id);
            if (!File.Exists(item.Asset.Path)) continue;
            try
            {
                if (item.Asset.HasVideo)
                {
                    var data = await vm.Media.GetThumbnailAsync(item.Asset.Path, 0, 160, 90);
                    if (vm.Project.Media.All(m => m.Id != asset.Id || m.Path != asset.Path)) continue;
                    var bitmap = BitmapSource.Create(160, 90, 96, 96, PixelFormats.Bgra32, null, data, 160 * 4); bitmap.Freeze();
                    item.Thumbnail = bitmap; Timeline.Thumbnails[item.Asset.Id] = bitmap;
                }
                if (asset.HasAudio)
                {
                    var waveform = await vm.Media.GetWaveformAsync(asset.Path, 512);
                    if (vm.Project.Media.Any(m => m.Id == asset.Id && m.Path == asset.Path)) Timeline.Waveforms[asset.Id] = waveform;
                }
                Timeline.InvalidateVisual();
            }
            catch (Exception ex) { cachedMediaPaths.Remove(asset.Id); vm.Status = "Media cache: " + ex.Message; }
        }
    }
    private async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        Pause();
        foreach (var path in paths) await vm.ImportAsync(path);
        await PopulateMediaVisualsAsync();
    }
    private async Task<bool> ConfirmUnsavedAsync()
    {
        if (!vm.Dirty) return true;
        var answer = MessageBox.Show(this, "Save changes to the current project?", "OpenRend", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.No) return true;
        return await SaveProjectAsync(false);
    }
    private async Task<bool> SaveProjectAsync(bool saveAs)
    {
        var path = vm.ProjectPath;
        if (path is null || saveAs)
        {
            var dialog = new SaveFileDialog { Filter = "OpenRend project|*.openrend", DefaultExt = ".openrend", FileName = vm.Project.Name };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        await vm.SaveAsync(path); RecordRecentProject(path); return true;
    }
    private async void NewClick(object sender, RoutedEventArgs e) => await GuardAsync(ShowWelcomeAsync);
    private async void OpenClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "OpenRend project|*.openrend|All files|*.*" };
        if (dialog.ShowDialog(this) == true) await OpenPathAsync(dialog.FileName);
    });
    private async void SaveClick(object sender, RoutedEventArgs e) => await GuardAsync(async () => await SaveProjectAsync(false));
    private async void SaveAsClick(object sender, RoutedEventArgs e) => await GuardAsync(async () => await SaveProjectAsync(true));
    private async void ImportClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "Video and audio|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.mxf;*.wav;*.mp3;*.flac;*.aac;*.ogg;*.m4a|All files|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) == true) await ImportPathsAsync(dialog.FileNames);
    });
    private async void WindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await GuardAsync(() => ImportPathsAsync(paths));
    }
    private async void RecoveryClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (!File.Exists(recoveryPath)) { vm.Status = "No recovery project is available."; return; }
        if (!await ConfirmUnsavedAsync()) return;
        Pause(); vm.Replace(await ProjectStore.LoadAsync(recoveryPath), null); await PopulateMediaVisualsAsync();
        vm.Status = "Opened recovery snapshot. Save as a project to keep these edits.";
    });
    private async void ImportLegacyClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "Legacy project|*.opentrim|JSON project|*.json" };
        if (dialog.ShowDialog(this) != true || !await ConfirmUnsavedAsync()) return;
        var result = await LegacyProjectImporter.LoadAsync(dialog.FileName);
        Pause(); vm.Replace(result.Project, null); await PopulateMediaVisualsAsync();
        vm.Status = result.Warnings.Count == 0 ? "Imported project. Save to the OpenRend project format." : string.Join(" ", result.Warnings);
    });
    private async void RelinkClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        Pause(); var missing = ProjectStore.FindMissingMedia(vm.Project);
        if (missing.Count == 0) { vm.Status = "All source media is available."; return; }
        foreach (var asset in missing)
        {
            var dialog = new OpenFileDialog { Title = $"Locate {asset.Name}", FileName = asset.Name };
            if (dialog.ShowDialog(this) != true) break;
            var probe = await vm.Media.ProbeAsync(dialog.FileName);
            vm.Editor.Execute(p =>
            {
                var current = p.Media.Single(m => m.Id == asset.Id);
                current.Path = probe.Path; current.DurationSeconds = probe.DurationSeconds;
                current.HasVideo = probe.HasVideo; current.HasAudio = probe.HasAudio;
                current.Width = probe.Width; current.Height = probe.Height; current.FrameRate = probe.FrameRate;
            });
        }
        await PopulateMediaVisualsAsync();
    });
    private void PlaceSelectedClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (MediaList.SelectedItem is not MediaItem item) { vm.Status = "Select a source in Project Media."; return; }
        Pause(); vm.Place(item.Asset.Id);
    });
    private void RemoveUnusedClick(object sender, RoutedEventArgs e) => Guard(() => vm.Editor.Execute(p =>
    {
        var used = p.Tracks.SelectMany(t => t.Clips).Select(c => c.MediaId).ToHashSet();
        p.Media.RemoveAll(m => !used.Contains(m.Id));
    }));
    private void MediaDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MediaList.SelectedItem is MediaItem item && item.Asset.Path.StartsWith("openrend://text", StringComparison.OrdinalIgnoreCase))
        {
            var clip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.MediaId == item.Asset.Id);
            Pause();
            var win = new TitlesAndTextWindow(vm, clip, item.Asset) { Owner = this };
            win.ShowDialog();
            RequestPreview();
            return;
        }
        PlaceSelectedClick(sender, e);
    }
    private void MediaMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && MediaList.SelectedItem is MediaItem item)
            DragDrop.DoDragDrop(MediaList, new DataObject("OpenRend.Media", item.Asset.Id), DragDropEffects.Copy);
    }
    private void MediaSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MediaList.SelectedItem is MediaItem item) { MediaDetails.Text = item.Details; vm.Status = $"{item.Name}  •  {item.Details}"; }
    }
    private void BrowseFolderClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) != true) return;
        FolderLabel.Text = dialog.FolderName;
        ExplorerList.ItemsSource = new DirectoryInfo(dialog.FolderName).EnumerateFiles()
            .Where(f => new[] { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wav", ".mp3", ".flac", ".m4a", ".mxf" }.Contains(f.Extension.ToLowerInvariant())).OrderBy(f => f.Name).ToList();
    });
    private async void ExplorerDoubleClick(object sender, MouseButtonEventArgs e) => await GuardAsync(async () =>
    {
        if (ExplorerList.SelectedItem is FileInfo file) { await ImportPathsAsync([file.FullName]); ToolsTabs.SelectedIndex = 1; }
    });
    private void ApplyGesture(ClipGesture gesture)
    {
        Pause();
        if (gesture.Mode == "Move")
        {
            if (gesture.NewTrackIndex is { } newIdx && gesture.NewTrackKind is { } newKind)
            {
                var newTrack = vm.Editor.MoveSelectionToNewTrack(vm.Selection, gesture.ClipId, newIdx, newKind, gesture.Start, vm.IgnoreGroups);
                var trackIndex = vm.Project.Tracks.IndexOf(newTrack);
                if (trackIndex >= 0) Timeline.ActivateTrack(trackIndex);
                vm.Status = $"Created new {newKind} layer '{newTrack.Name}' and moved event.";
            }
            else
            {
                vm.Editor.MoveSelection(vm.Selection, gesture.ClipId, gesture.TrackId, gesture.Start, vm.IgnoreGroups);
                vm.Status = $"Moved event to {TimelineView.FormatTimecode(gesture.Start, vm.Project.FrameRate)}.";
            }
        }
        else
        {
            vm.Editor.TrimSelection(gesture.ClipId, gesture.Start, gesture.SourceIn, gesture.Duration, vm.IgnoreGroups);
            vm.Status = $"Trimmed event duration to {gesture.Duration} frames.";
        }
    }
    private TimelineClip? SelectedClip => vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => vm.Selection.Contains(c.Id));
    private void UpdateInspector()
    {
        var clip = SelectedClip; Inspector.IsEnabled = clip is not null; ClipName.Text = clip?.Name ?? "Select a timeline clip";
        if (clip is null)
        {
            EditTextMediaButton.Visibility = Visibility.Collapsed;
            vm.Renderer.ActiveSubtitleClipId = null;
            return;
        }
        ClipStart.Text = clip.StartFrame.ToString(); ClipIn.Text = clip.SourceInFrame.ToString(); ClipDuration.Text = clip.DurationFrames.ToString();
        ClipOpacity.Text = clip.Opacity.ToString(CultureInfo.CurrentCulture); ClipGain.Text = clip.Gain.ToString(CultureInfo.CurrentCulture);
        ClipPan.Text = clip.Pan.ToString(CultureInfo.CurrentCulture); ClipFadeIn.Text = clip.FadeInFrames.ToString(); ClipFadeOut.Text = clip.FadeOutFrames.ToString();
        var isText = vm.Project.Media.Any(m => m.Id == clip.MediaId && (m.Path.StartsWith("openrend://text", StringComparison.OrdinalIgnoreCase) || m.Path.StartsWith("openrend://subtitles", StringComparison.OrdinalIgnoreCase)));
        EditTextMediaButton.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        vm.Renderer.ActiveSubtitleClipId = isText ? clip.Id : null;
    }
    private void ApplyPropertiesClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (SelectedClip is not { } clip) return;
        if (vm.Project.Tracks.Single(t => t.Clips.Contains(clip)).IsLocked) throw new InvalidOperationException("Unlock this track before changing clip properties.");
        if (clip.IsLocked) throw new InvalidOperationException("Unlock this event before changing clip properties.");
        Pause();
        vm.Editor.Execute(p =>
        {
            var start = long.Parse(ClipStart.Text); var sourceIn = long.Parse(ClipIn.Text); var duration = long.Parse(ClipDuration.Text);
            var fadeIn = long.Parse(ClipFadeIn.Text); var fadeOut = long.Parse(ClipFadeOut.Text);
            var moved = start != clip.StartFrame || sourceIn != clip.SourceInFrame || duration != clip.DurationFrames;
            var fadesChanged = fadeIn != clip.FadeInFrames || fadeOut != clip.FadeOutFrames;
            if (moved)
            {
                var startDelta = start - clip.StartFrame; var sourceDelta = sourceIn - clip.SourceInFrame; var durationDelta = duration - clip.DurationFrames;
                var members = vm.Editor.ExpandSelection([clip.Id], vm.IgnoreGroups);
                foreach (var track in p.Tracks)
                    foreach (var member in track.Clips.Where(c => members.Contains(c.Id)))
                    {
                        if (track.IsLocked) throw new InvalidOperationException("A linked track is locked.");
                        member.StartFrame = checked(member.StartFrame + startDelta); member.SourceInFrame = checked(member.SourceInFrame + sourceDelta);
                        member.DurationFrames = checked(member.DurationFrames + durationDelta); member.FadeOffsetFrames = 0; member.FadeDurationFrames = null;
                        member.FadeInFrames = Math.Min(member.FadeInFrames, member.DurationFrames);
                        member.FadeOutFrames = Math.Min(member.FadeOutFrames, member.DurationFrames - member.FadeInFrames);
                    }
            }
            clip.Opacity = double.Parse(ClipOpacity.Text); clip.Gain = double.Parse(ClipGain.Text); clip.Pan = double.Parse(ClipPan.Text);
            if (moved || fadesChanged)
            {
                clip.FadeInFrames = fadeIn; clip.FadeOutFrames = fadeOut; clip.FadeOffsetFrames = 0; clip.FadeDurationFrames = null;
            }
        });
    });
    private async void ExportClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (vm.Project.DurationFrames == 0) { vm.Status = "Place media on the timeline before rendering."; return; }
        Pause();
        var preferencesPath = Path.Combine(Path.GetDirectoryName(recoveryPath)!, "render.json");
        var dialog = new RenderAsWindow(vm.Project, Timeline.LoopRegion, RenderPreferencesStore.Load(preferencesPath)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        RenderPreferencesStore.Save(dialog.Preferences, preferencesPath);
        exportCancellation = new CancellationTokenSource(); vm.Busy = true; vm.Progress = 0;
        CancelRender.Visibility = Visibility.Visible; RenderButton.IsEnabled = false;
        try
        {
            vm.Status = "Rendering edited timeline…";
            var progress = new Progress<double>(p => { vm.Progress = p; vm.Status = $"Rendering timeline: {p:P0}"; });
            await vm.Exporter.ExportAsync(vm.Project.DeepClone(), dialog.OutputPath, dialog.Options, progress, exportCancellation.Token);
            vm.Status = $"Rendered {Path.GetFileName(dialog.OutputPath)}.";
        }
        catch (OperationCanceledException) { vm.Status = "Render cancelled."; }
        finally { vm.Busy = false; CancelRender.Visibility = Visibility.Collapsed; exportCancellation.Dispose(); exportCancellation = null; Refresh(); }
    });
    private void CancelExportClick(object sender, RoutedEventArgs e) => exportCancellation?.Cancel();
    private async void PlayClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (playbackStarting) { Pause(); return; }
        if (playback is not null) { Pause(); return; }
        if (vm.Project.DurationFrames == 0) return;
        if (vm.Position >= vm.Project.DurationFrames) vm.Position = 0;
        var project = vm.Project.DeepClone(); var start = vm.Position;
        var masterGain = masterMuted ? 0 : MasterGain.Value;
        var generation = ++playbackGeneration; playbackStarting = true;
        try
        {
            var session = await Task.Run(() => new PlaybackSession(vm.Renderer, project, start, masterGain));
            if (generation != playbackGeneration || !IsLoaded) { await session.DisposeAsync(); return; }
            playback = session;
            PlayButton.Content = new Image { Source = (ImageSource)FindResource("Icon.Pause"), Width = 16, Height = 16 };
            PlaybackRate.Text = "Rate: 1.00"; timer.Start(); vm.Status = "Playing timeline.";
        }
        finally { if (generation == playbackGeneration) playbackStarting = false; }
    });
    private void Pause()
    {
        timer.Stop();
        playbackGeneration++; playbackStarting = false;
        if (playback is not null)
        {
            playback.Dispose(); vm.Position = playback.CurrentFrame; playback = null; UpdatePosition();
        }
        PlayButton.Content = new Image { Source = (ImageSource)FindResource("Icon.Play"), Width = 16, Height = 16 };
        PlaybackRate.Text = "Rate: 0.00";
        LeftMeter.Height = RightMeter.Height = 0;
        RequestPreview();
    }
    private void PauseClick(object sender, RoutedEventArgs e) => Pause();
    private void PlayFromStartClick(object sender, RoutedEventArgs e) { StartClick(sender, e); PlayClick(sender, e); }
    private void StopClick(object sender, RoutedEventArgs e) { Pause(); vm.Position = 0; UpdatePosition(); RequestPreview(); }
    private void StartClick(object sender, RoutedEventArgs e) { Pause(); vm.Position = 0; UpdatePosition(); RequestPreview(); }
    private void EndClick(object sender, RoutedEventArgs e) { Pause(); vm.Position = vm.Project.DurationFrames; UpdatePosition(); RequestPreview(); }
    private void PreviousClick(object sender, RoutedEventArgs e) { Pause(); vm.Position--; UpdatePosition(); RequestPreview(); }
    private void NextClick(object sender, RoutedEventArgs e) { Pause(); vm.Position++; UpdatePosition(); RequestPreview(); }
    private void MasterGainChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MasterLevel is not null) MasterLevel.Text = masterMuted ? "Muted" : e.NewValue > 0 ? $"{20 * Math.Log10(e.NewValue):0.0} dB" : "−∞ dB";
        if (playback is not null) playback.MasterGain = masterMuted ? 0 : e.NewValue;
    }
    private void ResetMasterClick(object sender, RoutedEventArgs e) { masterMuted = false; MasterGain.Value = 1; MasterGainChanged(sender, new(0, MasterGain.Value)); }
    private void MuteMasterClick(object sender, RoutedEventArgs e) { masterMuted = !masterMuted; MasterGainChanged(sender, new(MasterGain.Value, MasterGain.Value)); }
    private void UndoClick(object sender, RoutedEventArgs e) => Guard(() => { Pause(); vm.Editor.Undo(); });
    private void RedoClick(object sender, RoutedEventArgs e) => Guard(() => { Pause(); vm.Editor.Redo(); });
    private void SplitClick(object sender, RoutedEventArgs e) => Guard(() => { Pause(); vm.Split(); });
    private void DeleteClick(object sender, RoutedEventArgs e) => Guard(() => { Pause(); vm.Delete(); });
    private void CopyClick(object sender, RoutedEventArgs e) => Guard(vm.Copy);
    private void CutClick(object sender, RoutedEventArgs e) => Guard(() => { Pause(); vm.Cut(); });
    private void PasteClick(object sender, RoutedEventArgs e) => Guard(() => { Pause(); vm.Paste(); });
    private void ClipPropertiesClick(object sender, RoutedEventArgs e) { ToolsTabs.SelectedIndex = 3; UpdateInspector(); }
    private async void PanCropClick(object sender, RoutedEventArgs e) => await OpenPanCropAsync();
    private async Task OpenPanCropAsync(Guid? requestedClip = null) => await GuardAsync(async () =>
    {
        var selected = vm.Project.Tracks.Where(track => track.Kind == TrackKind.Video)
            .SelectMany(track => track.Clips.Where(clip => requestedClip is { } id ? clip.Id == id : vm.Selection.Contains(clip.Id)).Select(clip => (Track: track, Clip: clip))).ToArray();
        if (selected.Length != 1) { vm.Status = "Select one video event to edit Pan/Crop."; return; }
        if (selected[0].Track.IsLocked) { vm.Status = "Unlock the video track before editing Pan/Crop."; return; }
        Pause();
        var editor = vm.Editor; var source = vm.Project.Media.Single(media => media.Id == selected[0].Clip.MediaId);
        if (source.Width <= 0 || source.Height <= 0)
        {
            var path = source.Path; MediaAsset metadata;
            vm.Busy = true;
            try { metadata = await vm.Media.ProbeAsync(path); }
            finally { vm.Busy = false; }
            if (!ReferenceEquals(editor, vm.Editor) || !ReferenceEquals(source, vm.Project.Media.SingleOrDefault(media => media.Id == source.Id)) || source.Path != path) return;
            if (metadata.Width <= 0 || metadata.Height <= 0) throw new InvalidOperationException("The source has no readable video dimensions.");
            editor.Execute(_ => { source.Width = metadata.Width; source.Height = metadata.Height; });
        }
        var dialog = new PanCropWindow(vm, selected[0].Clip.Id, frame => { vm.Position = frame; UpdatePosition(); RequestPreview(); }) { Owner = this };
        dialog.ShowDialog();
    });
    private void VideoEffectsClick(object sender, RoutedEventArgs e) => OpenVideoEffects();
    private void OpenVideoEffects(Guid? requestedClip = null) => Guard(() =>
    {
        var selected = vm.Project.Tracks.Where(track => track.Kind == TrackKind.Video)
            .SelectMany(track => track.Clips.Where(clip => requestedClip is { } id ? clip.Id == id : vm.Selection.Contains(clip.Id)).Select(clip => (Track: track, Clip: clip))).ToArray();
        if (selected.Length != 1) { vm.Status = "Select one video event to edit its effects."; return; }
        if (selected[0].Track.IsLocked) { vm.Status = "Unlock the video track before editing its effects."; return; }
        Pause(); vm.Position = Math.Clamp(vm.Position, selected[0].Clip.StartFrame, selected[0].Clip.EndFrame - 1); UpdatePosition(); RequestPreview();
        new VideoEffectsWindow(vm, EffectScope.Event, selected[0].Clip.Id) { Owner = this }.ShowDialog();
    });
    private void VideoTrackEffectsClick(object sender, RoutedEventArgs e) => OpenTrackEffects();
    private void OpenTrackEffects(Guid? requestedTrack = null) => Guard(() =>
    {
        var tracks = vm.Project.Tracks.Where(track => track.Kind == TrackKind.Video).ToArray();
        if (tracks.Length == 0) { vm.Status = "Project has no video tracks."; return; }
        var track = requestedTrack is { } id
            ? tracks.FirstOrDefault(t => t.Id == id)
            : (Timeline.ActiveTrackIndex >= 0 && Timeline.ActiveTrackIndex < vm.Project.Tracks.Count && vm.Project.Tracks[Timeline.ActiveTrackIndex].Kind == TrackKind.Video
                ? vm.Project.Tracks[Timeline.ActiveTrackIndex]
                : tracks.FirstOrDefault());
        if (track is null) { vm.Status = "Select a video track to edit its effects."; return; }
        if (track.IsLocked) { vm.Status = "Unlock the video track before editing its effects."; return; }
        Pause(); RequestPreview();
        new VideoEffectsWindow(vm, EffectScope.Track, track.Id) { Owner = this }.ShowDialog();
    });
    private void MediaEffectsClick(object sender, RoutedEventArgs e) => OpenMediaEffects();
    private void OpenMediaEffects(Guid? requestedMedia = null) => Guard(() =>
    {
        MediaAsset? asset = null;
        if (requestedMedia is { } id)
        {
            asset = vm.Project.Media.FirstOrDefault(m => m.Id == id);
        }
        else if (MediaList.SelectedItem is MediaItem item)
        {
            asset = item.Asset;
        }
        if (asset is null || !asset.HasVideo) { vm.Status = "Select a video media asset in Project Media to edit its effects."; return; }
        Pause(); RequestPreview();
        new VideoEffectsWindow(vm, EffectScope.Media, asset.Id) { Owner = this }.ShowDialog();
    });
    private void VideoOutputEffectsClick(object sender, RoutedEventArgs e) => OpenVideoOutputEffects();
    private void OpenVideoOutputEffects() => Guard(() =>
    {
        Pause(); RequestPreview();
        new VideoEffectsWindow(vm, EffectScope.Output, Guid.Empty) { Owner = this }.ShowDialog();
    });
    private void ColorGradingClick(object sender, RoutedEventArgs e) => OpenColorGrading();
    private void VideoTrackColorGradingClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var track = vm.Project.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Video);
        if (track is null) { vm.Status = "Create a video track before editing track color grading."; return; }
        OpenTrackColorGrading(track.Id);
    });
    private void MediaColorGradingClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var item = MediaList.SelectedItem as MediaItem ?? vm.Library.FirstOrDefault();
        if (item is null) { vm.Status = "Select a media item to edit its color grading."; return; }
        if (!item.Asset.HasVideo) { vm.Status = "Select a video media asset to edit its color grading."; return; }
        OpenMediaColorGrading(item.Asset.Id);
    });
    private void VideoOutputColorGradingClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        OpenVideoOutputColorGrading();
    });
    private void OpenColorGrading(Guid? requestedClip = null) => Guard(() =>
    {
        var selected = vm.Project.Tracks.Where(track => track.Kind == TrackKind.Video)
            .SelectMany(track => track.Clips.Where(clip => requestedClip is { } id ? clip.Id == id : vm.Selection.Contains(clip.Id)).Select(clip => (Track: track, Clip: clip))).ToArray();
        if (selected.Length != 1) { vm.Status = "Select one video event to edit its color grading."; return; }
        if (selected[0].Track.IsLocked) { vm.Status = "Unlock the video track before editing its color grading."; return; }
        Pause(); vm.Position = Math.Clamp(vm.Position, selected[0].Clip.StartFrame, selected[0].Clip.EndFrame - 1); UpdatePosition(); RequestPreview();
        var clipId = selected[0].Clip.Id;
        new ColorGradingWindow(vm, EffectScope.Event, clipId, null, () => RequestPreview()) { Owner = this }.ShowDialog();
    });
    private void OpenTrackColorGrading(Guid trackId) => Guard(() =>
    {
        var track = vm.Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null) return;
        if (track.IsLocked) { vm.Status = "Unlock the video track before editing its color grading."; return; }
        Pause(); RequestPreview();
        new ColorGradingWindow(vm, EffectScope.Track, trackId, null, () => RequestPreview()) { Owner = this }.ShowDialog();
    });
    private void OpenMediaColorGrading(Guid mediaId) => Guard(() =>
    {
        var asset = vm.Project.Media.FirstOrDefault(m => m.Id == mediaId);
        if (asset is null) return;
        Pause(); RequestPreview();
        new ColorGradingWindow(vm, EffectScope.Media, mediaId, null, () => RequestPreview()) { Owner = this }.ShowDialog();
    });
    private void OpenVideoOutputColorGrading() => Guard(() =>
    {
        Pause(); RequestPreview();
        new ColorGradingWindow(vm, EffectScope.Output, Guid.Empty, null, () => RequestPreview()) { Owner = this }.ShowDialog();
    });
    private void MotionTrackingClick(object sender, RoutedEventArgs e) => OpenMotionTracking();
    private void OpenMotionTracking(Guid? requestedClip = null) => Guard(() =>
    {
        var selected = vm.Project.Tracks.Where(track => track.Kind == TrackKind.Video)
            .SelectMany(track => track.Clips.Where(clip => requestedClip is { } id ? clip.Id == id : vm.Selection.Contains(clip.Id)).Select(clip => (Track: track, Clip: clip))).ToArray();
        if (selected.Length != 1) { vm.Status = "Select one video event to edit its motion tracking."; return; }
        if (selected[0].Track.IsLocked) { vm.Status = "Unlock the video track before editing motion tracking."; return; }
        Pause(); vm.Position = Math.Clamp(vm.Position, selected[0].Clip.StartFrame, selected[0].Clip.EndFrame - 1); UpdatePosition(); RequestPreview();
        var clipId = selected[0].Clip.Id;
        new MotionTrackWindow(vm, clipId, () => RequestPreview()) { Owner = this }.ShowDialog();
    });
    private void AiTextToSpeechClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        new TextToSpeechWindow(vm) { Owner = this }.ShowDialog();
    });
    private void AiSpeechToTextClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        new SpeechToTextWindow(vm) { Owner = this }.ShowDialog();
    });
    private void TextBasedEditingClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        OpenTextBasedEditing(vm.Selection.Count == 1 ? vm.Selection.First() : null);
    });
    public void OpenTextBasedEditing(Guid? clipId = null) => Guard(() =>
    {
        Pause();
        new TextBasedEditingWindow(vm, clipId, RequestPreview) { Owner = this }.Show();
    });
    private void AiAutoReframeClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        if (new AutoReframeWindow(vm) { Owner = this }.ShowDialog() == true)
        {
            Refresh();
            RequestPreview();
        }
    });
    private void InsertTextMediaClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        // If an existing Text Media event is selected, open editor for that event
        var selectedText = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Video)
            .SelectMany(t => t.Clips.Where(c => vm.Selection.Contains(c.Id)).Select(c => (Track: t, Clip: c)))
            .FirstOrDefault(pair =>
            {
                var m = vm.Project.Media.FirstOrDefault(media => media.Id == pair.Clip.MediaId);
                return m is not null && m.Path.StartsWith("openrend://text", StringComparison.OrdinalIgnoreCase);
            });

        if (selectedText.Clip is not null)
        {
            var media = vm.Project.Media.First(m => m.Id == selectedText.Clip.MediaId);
            new TitlesAndTextWindow(vm, selectedText.Clip, media) { Owner = this }.ShowDialog();
        }
        else
        {
            new TitlesAndTextWindow(vm) { Owner = this }.ShowDialog();
        }
        Refresh();
        RequestPreview();
    });
    private void NormalizeAudioClipClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var selectedAudio = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Audio)
            .SelectMany(t => t.Clips.Where(c => vm.Selection.Contains(c.Id)).Select(c => (Track: t, Clip: c))).ToArray();

        if (selectedAudio.Length == 0)
        {
            var cursorClip = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Audio)
                .SelectMany(t => t.Clips.Where(c => vm.Position >= c.StartFrame && vm.Position < c.EndFrame).Select(c => (Track: t, Clip: c)))
                .FirstOrDefault();
            if (cursorClip.Clip != null)
                selectedAudio = [(cursorClip.Track, cursorClip.Clip)];
        }

        if (selectedAudio.Length == 0)
        {
            vm.Status = "Select an audio event to normalize.";
            return;
        }

        var normalizedCount = 0;
        vm.Editor.Execute(_ =>
        {
            foreach (var (track, clip) in selectedAudio)
            {
                var media = vm.Project.Media.FirstOrDefault(m => m.Id == clip.MediaId);
                if (media is null || !media.HasAudio) continue;

                var startSec = vm.Project.FrameRate.Seconds(clip.SourceInFrame);
                var durSec = vm.Project.FrameRate.Seconds(clip.DurationFrames);
                var totalFrames = (int)Math.Ceiling(durSec * MediaService.SampleRate);
                if (totalFrames <= 0) continue;

                var samples = vm.Media.GetAudio(media.Path, startSec, totalFrames);
                var peak = 0f;
                for (var i = 0; i < samples.Length; i++)
                {
                    var abs = Math.Abs(samples[i]);
                    if (abs > peak) peak = abs;
                }

                if (peak > 0.00001f)
                {
                    var newGain = Math.Min(31.62, 1.0f / peak);
                    clip.Gain = newGain;
                    normalizedCount++;
                }
            }
        });

        if (normalizedCount > 0)
        {
            vm.Status = $"Normalized {normalizedCount} audio event(s) to 0.0 dB peak.";
            UpdateInspector();
        }
        else
        {
            vm.Status = "Selected audio event is silent; gain unchanged.";
        }
    });
    private void LockEventClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var selected = vm.Project.Tracks
            .SelectMany(t => t.Clips.Where(c => vm.Selection.Contains(c.Id)).Select(c => (Track: t, Clip: c))).ToArray();

        if (selected.Length == 0 && SelectedClip is { } anyClip)
        {
            var trk = vm.Project.Tracks.First(t => t.Clips.Contains(anyClip));
            selected = [(trk, anyClip)];
        }

        if (selected.Length == 0) { vm.Status = "Select an event to lock/unlock."; return; }
        Pause();
        var allLocked = selected.All(s => s.Clip.IsLocked);
        vm.Editor.SetSelectionLock(selected.Select(s => s.Clip.Id), !allLocked);
        UpdateInspector();
        vm.Status = allLocked ? "Unlocked selected event(s)." : "Locked selected event(s).";
    });

    private void MuteEventClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var selected = vm.Project.Tracks
            .SelectMany(t => t.Clips.Where(c => vm.Selection.Contains(c.Id)).Select(c => (Track: t, Clip: c))).ToArray();

        if (selected.Length == 0 && SelectedClip is { } anyClip)
        {
            var trk = vm.Project.Tracks.First(t => t.Clips.Contains(anyClip));
            selected = [(trk, anyClip)];
        }

        if (selected.Length == 0) { vm.Status = "Select an event to mute/unmute."; return; }
        Pause();
        var allMuted = selected.All(s => s.Clip.IsMuted);
        vm.Editor.SetSelectionMute(selected.Select(s => s.Clip.Id), !allMuted);
        UpdateInspector();
        RequestPreview();
        vm.Status = allMuted ? "Unmuted selected event(s)." : "Muted selected event(s).";
    });

    private void LoopEventClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var selected = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Video)
            .SelectMany(t => t.Clips.Where(c => vm.Selection.Contains(c.Id)).Select(c => (Track: t, Clip: c))).ToArray();

        if (selected.Length == 0 && SelectedClip is { } anyClip)
        {
            var trk = vm.Project.Tracks.First(t => t.Clips.Contains(anyClip));
            if (trk.Kind == TrackKind.Video) selected = [(trk, anyClip)];
        }

        if (selected.Length == 0) { vm.Status = "Select a video event to toggle loop."; return; }
        Pause();
        var allLoop = selected.All(s => s.Clip.VideoSourceMode == VideoSourceMode.Loop);
        var newMode = allLoop ? VideoSourceMode.HoldLastFrame : VideoSourceMode.Loop;
        foreach (var (_, c) in selected)
        {
            vm.Editor.SetVideoSourceMode(c.Id, newMode);
        }
        UpdateInspector();
        vm.Status = allLoop ? "Set video source mode to hold last frame." : "Set video source mode to loop.";
    });

    private void MuteAudioClipClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var selected = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Audio)
            .SelectMany(t => t.Clips.Where(c => vm.Selection.Contains(c.Id)).Select(c => (Track: t, Clip: c))).ToArray();

        if (selected.Length == 0 && SelectedClip is { } anyClip)
        {
            var trk = vm.Project.Tracks.First(t => t.Clips.Contains(anyClip));
            if (trk.Kind == TrackKind.Audio)
            {
                selected = [(trk, anyClip)];
            }
            else
            {
                var groupMembers = vm.Editor.ExpandSelection([anyClip.Id], false);
                selected = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Audio)
                    .SelectMany(t => t.Clips.Where(c => groupMembers.Contains(c.Id)).Select(c => (Track: t, Clip: c))).ToArray();
            }
        }

        if (selected.Length == 0) { vm.Status = "Select an audio event (or linked clip) to mute/unmute."; return; }
        Pause();
        var allMuted = selected.All(s => s.Clip.IsMuted);
        vm.Editor.SetSelectionMute(selected.Select(s => s.Clip.Id), !allMuted, ignoreGroups: true);
        UpdateInspector();
        RequestPreview();
        vm.Status = allMuted ? "Unmuted audio event(s)." : "Muted audio event(s).";
    });
    private void InvertPhaseClipClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var selected = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Audio)
            .SelectMany(t => t.Clips.Where(c => vm.Selection.Contains(c.Id)).Select(c => (Track: t, Clip: c))).ToArray();

        if (selected.Length == 0 && SelectedClip is { } anyClip)
        {
            var trk = vm.Project.Tracks.First(t => t.Clips.Contains(anyClip));
            selected = [(trk, anyClip)];
        }

        if (selected.Length == 0) { vm.Status = "Select an audio event to invert phase."; return; }
        Pause();
        vm.Editor.Execute(_ =>
        {
            foreach (var (_, c) in selected)
            {
                c.Gain = -c.Gain;
            }
        });
        UpdateInspector();
        vm.Status = "Inverted audio phase for selected event(s).";
    });
    private void InsertSolidColorClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        var colorAsset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Path = "openrend://color?hex=#000000",
            DurationSeconds = 86400.0,
            Width = vm.Project.Width,
            Height = vm.Project.Height,
            FrameRate = vm.Project.FrameRate.Value,
            HasVideo = true,
            HasAudio = false
        };

        var cursorFrame = vm.Position;
        // 5 seconds default matching Vegas Pro generated media length
        var durFrames = vm.Project.FrameRate.Frames(5.0);
        var colorClip = new TimelineClip
        {
            MediaId = colorAsset.Id,
            Name = "Solid Color (Black)",
            StartFrame = cursorFrame,
            DurationFrames = durFrames
        };

        vm.Editor.Execute(p =>
        {
            p.Media.Add(colorAsset);
            var vTrack = p.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Video);
            if (vTrack is null)
            {
                vTrack = new TimelineTrack { Name = "Generated Media", Kind = TrackKind.Video };
                p.Tracks.Insert(0, vTrack);
            }
            vTrack.Clips.Add(colorClip);
        });

        vm.Selection.Clear();
        vm.Selection.Add(colorClip.Id);
        Timeline.Selection.Clear();
        Timeline.Selection.Add(colorClip.Id);
        Timeline.InvalidateVisual();
        UpdateInspector();
        RequestPreview();
        vm.Status = "Inserted VEGAS Solid Color generator media.";
    });
    private void EditTextMediaClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (SelectedClip is not { } clip) return;
        OpenEditTextMedia(clip.Id);
    });
    private void OpenEditTextMedia(Guid clipId)
    {
        var clip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is null) return;
        var media = vm.Project.Media.FirstOrDefault(m => m.Id == clip.MediaId);
        if (media is null) return;
        Pause();
        var win = new TitlesAndTextWindow(vm, clip, media) { Owner = this };
        win.ShowDialog();
        RequestPreview();
    }
    private void SceneDetectionClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        if (new SceneDetectionWindow(vm) { Owner = this }.ShowDialog() == true)
        {
            Refresh();
            RequestPreview();
        }
    });
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void SelectAllClick(object sender, RoutedEventArgs e) { vm.Selection.Clear(); vm.Selection.UnionWith(vm.Project.Tracks.SelectMany(t => t.Clips).Select(c => c.Id)); Timeline.Selection.Clear(); Timeline.Selection.UnionWith(vm.Selection); Timeline.InvalidateVisual(); UpdateInspector(); CutButton.IsEnabled = CopyButton.IsEnabled = vm.Selection.Count > 0; }
    private void LinkClick(object sender, RoutedEventArgs e) => Guard(() => { Pause(); vm.Editor.LinkClips(vm.Selection); });
    private void UnlinkClick(object sender, RoutedEventArgs e) => Guard(() => { Pause(); vm.Editor.UnlinkClips(vm.Selection); });
    private void IgnoreGroupsClick(object sender, RoutedEventArgs e) { vm.IgnoreGroups = IgnoreGroupsMenu.IsChecked; }
    private void VideoTrackClick(object sender, RoutedEventArgs e) => Guard(() => vm.Editor.Execute(p => p.Tracks.Insert(0, new TimelineTrack
    { Kind = TrackKind.Video, Name = $"Video {p.Tracks.Count(t => t.Kind == TrackKind.Video) + 1}" })));
    private void AudioTrackClick(object sender, RoutedEventArgs e) => Guard(() => vm.Editor.AddTrack(TrackKind.Audio));
    private void SnapClick(object sender, RoutedEventArgs e) { Timeline.Snapping = SnapToggle.IsChecked == true; SnappingMenu.IsChecked = Timeline.Snapping; }
    private void SnapMenuClick(object sender, RoutedEventArgs e) { SnapToggle.IsChecked = SnappingMenu.IsChecked; SnapClick(sender, e); }
    private void RippleClick(object sender, RoutedEventArgs e) { vm.Ripple = RippleToggle.IsChecked == true; RippleMenu.IsChecked = vm.Ripple; }
    private void RippleMenuClick(object sender, RoutedEventArgs e) { RippleToggle.IsChecked = RippleMenu.IsChecked; RippleClick(sender, e); }
    private void ZoomInClick(object sender, RoutedEventArgs e) { Timeline.PixelsPerSecond = Math.Min(1200, Timeline.PixelsPerSecond * 1.25); Refresh(); }
    private void ZoomOutClick(object sender, RoutedEventArgs e) { Timeline.PixelsPerSecond = Math.Max(5, Timeline.PixelsPerSecond / 1.25); Refresh(); }
    private void FitClick(object sender, RoutedEventArgs e) { Timeline.Offset = 0; TimelineScroll.Value = 0; Timeline.PixelsPerSecond = Math.Clamp((Timeline.ActualWidth - TimelineView.HeaderWidth - 25) / Math.Max(1, vm.Project.FrameRate.Seconds(vm.Project.DurationFrames)), 5, 1200); Refresh(); }
    private void TimelineScrollChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (Timeline is null) return; Timeline.Offset = e.NewValue; Timeline.InvalidateVisual(); }
    private void QualityChanged(object sender, SelectionChangedEventArgs e) { if (initialized) { revision++; RequestPreview(); } }
    private void GuidesClick(object sender, RoutedEventArgs e) { safeAreas = !safeAreas; revision++; RequestPreview(); }
    private async Task<BitmapSource> CaptureFrameAsync()
    {
        var project = vm.Project.DeepClone(); var frame = vm.Position;
        var pixels = await Task.Run(() => vm.Renderer.RenderFrame(project, frame, project.Width, project.Height));
        var image = BitmapSource.Create(project.Width, project.Height, 96, 96, PixelFormats.Bgra32, null, pixels, project.Width * 4);
        image.Freeze(); return image;
    }
    private async void CopySnapshotClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        Clipboard.SetImage(await CaptureFrameAsync()); vm.Status = "Copied the current frame.";
    });
    private async void SaveSnapshotClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var dialog = new SaveFileDialog { Filter = "PNG image|*.png", DefaultExt = ".png", FileName = "Frame.png" };
        if (dialog.ShowDialog(this) != true) return;
        if (vm.Project.Media.Any(m => string.Equals(Path.GetFullPath(m.Path), Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose a snapshot destination different from the original media.");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(await CaptureFrameAsync()));
        var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(temporary)) encoder.Save(file);
            File.Move(temporary, dialog.FileName, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        vm.Status = "Saved " + Path.GetFileName(dialog.FileName) + ".";
    });
    private void ResetWorkspaceClick(object sender, RoutedEventArgs e) { WorkspaceHeight.Height = new GridLength(Math.Min(600, Math.Max(230, ActualHeight - 260))); MediaWidth.Width = new GridLength(1, GridUnitType.Star); PreviewWidth.Width = new GridLength(Math.Min(844, Math.Max(320, ActualWidth - 420))); ToolsTabs.SelectedIndex = 1; Timeline.PixelsPerSecond = 13.4; Timeline.Offset = 0; Refresh(); }
    private void SettingsClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var dialog = new ProjectSettingsWindow(vm.Project) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Pause(); vm.Editor.Execute(p => { p.Width = dialog.ProjectWidth; p.Height = dialog.ProjectHeight; p.FrameRate = dialog.Rate; p.Name = dialog.ProjectName; });
        vm.Status = "Project settings updated.";
    });
    private void ShortcutsClick(object sender, RoutedEventArgs e) => MessageBox.Show(this, "Ctrl+I  Import media\nCtrl+O / Ctrl+S  Open / save project\nCtrl+Shift+E  Render timeline\nSpace  Play / pause\nEnter  Pause\nEsc  Stop\nAlt+Left / Alt+Right  Step one frame\nCtrl+Home / Ctrl+End  Timeline bounds\nS  Split selected clips at cursor\nDelete  Delete selected clips\nCtrl+Z / Ctrl+Y  Undo / redo\nCtrl+A  Select all clips\nN  Toggle snapping\nCtrl+wheel  Zoom timeline\nShift+wheel  Scroll time\nWheel  Scroll tracks\nDrag clip edges  Trim\nCtrl+click  Add to selection", "Keyboard shortcuts");
    private void AboutClick(object sender, RoutedEventArgs e) => MessageBox.Show(this, "OpenRend 0.1\nWindows video and audio editor\nGPL-3.0-or-later\n\nUses FFmpeg, CommunityToolkit.Mvvm, NAudio and Microsoft.Extensions.DependencyInjection.\nSee THIRD-PARTY-NOTICES.md in the distribution.", "About OpenRend");
    private void ExitClick(object sender, RoutedEventArgs e) => Close();
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (vm.Busy) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control); var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift); var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.FocusedElement is TextBox && !(ctrl && key is Key.S or Key.O or Key.I or Key.N || ctrl && shift && key is Key.E or Key.M or Key.L)) return;
        Action? action = key switch
        {
            Key.I when ctrl => () => ImportClick(this, e),
            Key.O when ctrl => () => OpenClick(this, e),
            Key.S when ctrl => () => { if (shift) SaveAsClick(this, e); else SaveClick(this, e); }
            ,
            Key.N when ctrl && shift => () => NewEmptyClick(this, e),
            Key.N when ctrl => () => NewClick(this, e),
            Key.Z when ctrl => () => { if (shift) RedoClick(this, e); else UndoClick(this, e); }
            ,
            Key.Y when ctrl => () => RedoClick(this, e),
            Key.E when ctrl && shift => () => ExportClick(this, e),
            Key.A when ctrl && shift => () => AudioTrackClick(this, e),
            Key.V when ctrl && shift => () => VideoTrackClick(this, e),
            Key.T when ctrl && shift => () => InsertTextMediaClick(this, e),
            Key.L when ctrl && shift => () => LockEventClick(this, e),
            Key.M when ctrl && shift => () => MuteAudioClipClick(this, e),
            Key.P when ctrl && shift => () => AiTextToSpeechClick(this, e),
            Key.G when ctrl && shift => () => ColorGradingClick(this, e),
            Key.A when ctrl => () => SelectAllClick(this, e),
            Key.C when ctrl => () => CopyClick(this, e),
            Key.X when ctrl => () => CutClick(this, e),
            Key.V when ctrl => () => PasteClick(this, e),
            Key.F when ctrl => () => FitClick(this, e),
            Key.Home when ctrl => () => StartClick(this, e),
            Key.End when ctrl => () => EndClick(this, e),
            Key.Left when alt => () => PreviousClick(this, e),
            Key.Right when alt => () => NextClick(this, e),
            Key.Enter when alt => () => SettingsClick(this, e),
            Key.Enter => Pause,
            Key.Space => () => PlayClick(this, e),
            Key.Escape => () => { Timeline.CancelPointerInput(); StopClick(this, e); },
            Key.S => () => SplitClick(this, e),
            Key.Delete => () => DeleteClick(this, e),
            Key.G => () => LinkClick(this, e),
            Key.U => () => UnlinkClick(this, e),
            Key.N => () => { SnapToggle.IsChecked = SnapToggle.IsChecked != true; SnapClick(this, e); },
            Key.Add or Key.OemPlus => () => ZoomInClick(this, e),
            Key.Subtract or Key.OemMinus => () => ZoomOutClick(this, e),
            _ => null
        };
        if (action is not null) { action(); e.Handled = true; }
    }
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (vm.Busy) { vm.Status = "Cancel the active render before closing."; return; }
        try
        {
            if (!await ConfirmUnsavedAsync()) return;
            Pause(); autosave.Stop(); SaveWorkspace(); allowClose = true;
            // Complete the cancelled Closing event before requesting the final close.
            _ = Dispatcher.BeginInvoke(Close);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private string WorkspacePath => Path.Combine(Path.GetDirectoryName(recoveryPath)!, "workspace.json");
    private void SaveWorkspace()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(WorkspacePath)!);
        File.WriteAllText(WorkspacePath, System.Text.Json.JsonSerializer.Serialize(new WorkspaceSettings(2, RestoreBounds.Width, RestoreBounds.Height, PreviewWidth.ActualWidth, WorkspaceHeight.ActualHeight, Timeline.PixelsPerSecond, WindowState == WindowState.Maximized)));
    }
    private void LoadWorkspace()
    {
        try
        {
            if (!File.Exists(WorkspacePath)) return;
            var settings = System.Text.Json.JsonSerializer.Deserialize<WorkspaceSettings>(File.ReadAllText(WorkspacePath));
            if (settings is null) return;
            if (settings.LayoutVersion != 2) return;
            Width = Math.Clamp(settings.Width, MinWidth, SystemParameters.WorkArea.Width); Height = Math.Clamp(settings.Height, MinHeight, SystemParameters.WorkArea.Height);
            WindowState = settings.Maximized ? WindowState.Maximized : WindowState.Normal;
            PreviewWidth.Width = new GridLength(Math.Clamp(settings.PreviewWidth, 320, Math.Max(320, ActualWidth - 420)));
            WorkspaceHeight.Height = new GridLength(Math.Clamp(settings.WorkspaceHeight, 230, Math.Max(230, ActualHeight - 260)));
            Timeline.PixelsPerSecond = Math.Clamp(settings.Zoom, 5, 1200);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or ArgumentException) { vm.Status = "Workspace preferences could not be restored."; }
    }
    private sealed record WorkspaceSettings(int LayoutVersion, double Width, double Height, double PreviewWidth, double WorkspaceHeight, double Zoom, bool Maximized);

    private (float u, float v, float vpWidth, float vpHeight)? PreviewWindowToVideoCoords(int mouseX, int mouseY)
    {
        var (winW, winH) = Preview.ClientSize;
        if (winW <= 0 || winH <= 0 || previewWidth <= 0 || previewHeight <= 0) return null;

        var scale = Math.Min((float)winW / previewWidth, (float)winH / previewHeight);
        var vpW = previewWidth * scale;
        var vpH = previewHeight * scale;
        var vpX = (winW - vpW) * 0.5f;
        var vpY = (winH - vpH) * 0.5f;

        var u = (mouseX - vpX) / vpW;
        var v = (mouseY - vpY) / vpH;
        return (u, v, vpW, vpH);
    }

    private void OnPreviewMouseDown(int x, int y)
    {
        if (previewWidth <= 0 || previewHeight <= 0) return;
        var norm = PreviewWindowToVideoCoords(x, y);
        if (norm is null) return;
        var (u, v, vpW, vpH) = norm.Value;

        var curFrame = vm.Position;
        var textClips = vm.Project.Tracks
            .Where(t => t.Kind == TrackKind.Video && !t.IsMuted && t.IsVisible)
            .SelectMany(t => t.Clips)
            .Where(c => curFrame >= c.StartFrame && curFrame < c.EndFrame)
            .Where(c =>
            {
                var m = vm.Project.Media.FirstOrDefault(m => m.Id == c.MediaId);
                return m != null && (m.Path.StartsWith("openrend://text", StringComparison.OrdinalIgnoreCase) ||
                                     m.Path.StartsWith("openrend://subtitles", StringComparison.OrdinalIgnoreCase) ||
                                     m.Path.EndsWith(".srt", StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        if (textClips.Count == 0) return;

        var clip = textClips.FirstOrDefault(c => vm.Selection.Contains(c.Id)) ?? textClips.First();
        var media = vm.Project.Media.FirstOrDefault(m => m.Id == clip.MediaId);
        if (media is null) return;

        var style = media.Path.StartsWith("openrend://text?", StringComparison.OrdinalIgnoreCase)
            ? SubtitleStyle.FromQuery(media.Path.Substring("openrend://text?".Length))
            : SubtitleStyle.FromPreset("Classic White");

        var bounds = SubtitleRenderer.MeasureSubtitleBounds(clip.Name, previewWidth, previewHeight, style);
        var clickPixelX = u * previewWidth;
        var clickPixelY = v * previewHeight;

        var anchorX = style.HasCustomLocation ? style.LocationX * previewWidth : bounds.X + bounds.Width * 0.5f;
        var anchorY = style.HasCustomLocation ? style.LocationY * previewHeight : bounds.Y + bounds.Height * 0.5f;

        var (winW, winH) = Preview.ClientSize;
        var scale = (winW > 0 && winH > 0) ? Math.Min((float)winW / previewWidth, (float)winH / previewHeight) : 1f;
        // Hit radius scaled to ~14 screen pixels so corners and edges are effortless to grab
        var hitRadius = Math.Clamp(14f / Math.Max(scale, 0.05f), 12f, 60f);

        var (handle, handlePos, center) = SubtitleRenderer.HitTestInteract(bounds, anchorX, anchorY, clickPixelX, clickPixelY, hitRadius);
        if (handle == TextInteractHandle.None) return;

        isDraggingTextInPreview = true;
        previewDraggedClipId = clip.Id;
        previewDragOriginalStyle = SubtitleStyle.FromQuery(style.ToQuery());
        previewDragOriginalPath = media.Path;
        previewDragStartMouse = new Point(x, y);
        previewDragStartLocation = (style.HasCustomLocation ? style.LocationX : anchorX / previewWidth,
                                    style.HasCustomLocation ? style.LocationY : anchorY / previewHeight);
        previewDragHandle = handle;
        previewDragInitialHandlePos = handlePos;
        previewDragCenter = center;

        // Resolve 0 (auto-calculated) font size to actual pixel height for proportional scaling
        previewDragStartFontSize = style.FontSize > 0f
            ? style.FontSize
            : (style.PresetName.Contains("Shorts", StringComparison.OrdinalIgnoreCase) || style.PresetName.Contains("YouTube", StringComparison.OrdinalIgnoreCase))
                ? Math.Clamp(previewHeight * 0.065f, 18f, 130f)
                : Math.Clamp(previewHeight * 0.048f, 16f, 96f);

        if (!vm.Selection.Contains(clip.Id))
        {
            vm.Selection.Clear();
            vm.Selection.Add(clip.Id);
            Timeline.Selection.Clear();
            Timeline.Selection.Add(clip.Id);
            Timeline.InvalidateVisual();
            UpdateInspector();
        }

        vm.Renderer.ActiveSubtitleClipId = clip.Id;
        RequestPreview();
    }

    private void OnPreviewMouseMove(int x, int y, bool isDown)
    {
        if (!isDraggingTextInPreview || !isDown || previewDraggedClipId is not { } clipId)
        {
            UpdatePreviewCursor(x, y);
            return;
        }

        var clip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        var media = clip is not null ? vm.Project.Media.FirstOrDefault(m => m.Id == clip.MediaId) : null;
        if (clip is null || media is null) return;

        var (winW, winH) = Preview.ClientSize;
        if (winW <= 0 || winH <= 0 || previewWidth <= 0 || previewHeight <= 0) return;

        var currentStyle = media.Path.StartsWith("openrend://text?", StringComparison.OrdinalIgnoreCase)
            ? SubtitleStyle.FromQuery(media.Path.Substring("openrend://text?".Length))
            : SubtitleStyle.FromPreset("Classic White");

        if (previewDragHandle == TextInteractHandle.Move)
        {
            // Move text position across video preview
            var scale = Math.Min((float)winW / previewWidth, (float)winH / previewHeight);
            var vpW = previewWidth * scale;
            var vpH = previewHeight * scale;

            var deltaX = (float)((x - previewDragStartMouse.X) / vpW);
            var deltaY = (float)((y - previewDragStartMouse.Y) / vpH);

            var newLocX = Math.Clamp(previewDragStartLocation.X + deltaX, 0.02f, 0.98f);
            var newLocY = Math.Clamp(previewDragStartLocation.Y + deltaY, 0.02f, 0.98f);

            currentStyle.LocationX = newLocX;
            currentStyle.LocationY = newLocY;
            currentStyle.HasCustomLocation = true;
        }
        else
        {
            // Scale font size proportionally by projecting mouse position along handle ray from text center
            var norm = PreviewWindowToVideoCoords(x, y);
            if (norm is null) return;

            var mouseVideoX = norm.Value.u * previewWidth;
            var mouseVideoY = norm.Value.v * previewHeight;

            var v0x = previewDragInitialHandlePos.X - previewDragCenter.X;
            var v0y = previewDragInitialHandlePos.Y - previewDragCenter.Y;
            var len0Sq = v0x * v0x + v0y * v0y;

            if (len0Sq > 1e-4f)
            {
                var vx = mouseVideoX - previewDragCenter.X;
                var vy = mouseVideoY - previewDragCenter.Y;

                // Dot product projection gives scale ratio relative to initial grab point
                var scaleFactor = (vx * v0x + vy * v0y) / len0Sq;

                var targetFontSize = previewDragStartFontSize * scaleFactor;
                var newFontSize = Math.Clamp((float)Math.Round(targetFontSize, 1), 8f, 300f);

                currentStyle.FontSize = newFontSize;
                // Pin center so text scales symmetrically around anchor point
                currentStyle.LocationX = previewDragCenter.X / previewWidth;
                currentStyle.LocationY = previewDragCenter.Y / previewHeight;
                currentStyle.HasCustomLocation = true;
            }
        }

        media.Path = $"openrend://text?{currentStyle.ToQuery()}";
        vm.Renderer.ActiveSubtitleClipId = clipId;
        RequestPreview();
    }

    private void UpdatePreviewCursor(int x, int y)
    {
        if (previewWidth <= 0 || previewHeight <= 0)
        {
            Preview.SetPreviewCursor(0);
            return;
        }

        var norm = PreviewWindowToVideoCoords(x, y);
        if (norm is null)
        {
            Preview.SetPreviewCursor(0);
            return;
        }

        var curFrame = vm.Position;
        var textClips = vm.Project.Tracks
            .Where(t => t.Kind == TrackKind.Video && !t.IsMuted && t.IsVisible)
            .SelectMany(t => t.Clips)
            .Where(c => curFrame >= c.StartFrame && curFrame < c.EndFrame)
            .Where(c =>
            {
                var m = vm.Project.Media.FirstOrDefault(m => m.Id == c.MediaId);
                return m != null && (m.Path.StartsWith("openrend://text", StringComparison.OrdinalIgnoreCase) ||
                                     m.Path.StartsWith("openrend://subtitles", StringComparison.OrdinalIgnoreCase) ||
                                     m.Path.EndsWith(".srt", StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        if (textClips.Count == 0)
        {
            Preview.SetPreviewCursor(0);
            return;
        }

        var clip = textClips.FirstOrDefault(c => vm.Selection.Contains(c.Id)) ?? textClips.First();
        var media = vm.Project.Media.FirstOrDefault(m => m.Id == clip.MediaId);
        if (media is null)
        {
            Preview.SetPreviewCursor(0);
            return;
        }

        var style = media.Path.StartsWith("openrend://text?", StringComparison.OrdinalIgnoreCase)
            ? SubtitleStyle.FromQuery(media.Path.Substring("openrend://text?".Length))
            : SubtitleStyle.FromPreset("Classic White");

        var bounds = SubtitleRenderer.MeasureSubtitleBounds(clip.Name, previewWidth, previewHeight, style);
        var clickPixelX = norm.Value.u * previewWidth;
        var clickPixelY = norm.Value.v * previewHeight;

        var anchorX = style.HasCustomLocation ? style.LocationX * previewWidth : bounds.X + bounds.Width * 0.5f;
        var anchorY = style.HasCustomLocation ? style.LocationY * previewHeight : bounds.Y + bounds.Height * 0.5f;

        var (winW, winH) = Preview.ClientSize;
        var scale = (winW > 0 && winH > 0) ? Math.Min((float)winW / previewWidth, (float)winH / previewHeight) : 1f;
        var hitRadius = Math.Clamp(14f / Math.Max(scale, 0.05f), 12f, 60f);

        var (handle, _, _) = SubtitleRenderer.HitTestInteract(bounds, anchorX, anchorY, clickPixelX, clickPixelY, hitRadius);
        var cursorId = handle switch
        {
            TextInteractHandle.TopLeft or TextInteractHandle.BottomRight => PreviewHost.IDC_SIZENWSE,
            TextInteractHandle.TopRight or TextInteractHandle.BottomLeft => PreviewHost.IDC_SIZENESW,
            TextInteractHandle.TopCenter or TextInteractHandle.BottomCenter => PreviewHost.IDC_SIZENS,
            TextInteractHandle.MiddleLeft or TextInteractHandle.MiddleRight => PreviewHost.IDC_SIZEWE,
            TextInteractHandle.Move => PreviewHost.IDC_SIZEALL,
            _ => 0
        };

        Preview.SetPreviewCursor(cursorId);
    }

    private void OnPreviewMouseUp(int x, int y)
    {
        if (!isDraggingTextInPreview) return;
        isDraggingTextInPreview = false;

        if (previewDraggedClipId is { } clipId && previewDragOriginalPath is { } origPath)
        {
            var clip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
            var media = clip is not null ? vm.Project.Media.FirstOrDefault(m => m.Id == clip.MediaId) : null;
            if (media != null && media.Path != origPath)
            {
                var finalPath = media.Path;
                media.Path = origPath;
                vm.Editor.Execute(p =>
                {
                    var m = p.Media.FirstOrDefault(x => x.Id == media.Id);
                    if (m != null) m.Path = finalPath;
                });
            }
        }

        previewDraggedClipId = null;
        previewDragOriginalStyle = null;
        previewDragOriginalPath = null;
        previewDragHandle = TextInteractHandle.None;
        Preview.SetPreviewCursor(0);
    }

    private void OnPreviewMouseDoubleClick(int x, int y)
    {
        if (previewWidth <= 0 || previewHeight <= 0) return;
        var norm = PreviewWindowToVideoCoords(x, y);
        if (norm is null) return;

        var curFrame = vm.Position;
        var clip = vm.Project.Tracks
            .Where(t => t.Kind == TrackKind.Video && !t.IsMuted && t.IsVisible)
            .SelectMany(t => t.Clips)
            .Where(c => curFrame >= c.StartFrame && curFrame < c.EndFrame)
            .FirstOrDefault(c =>
            {
                var m = vm.Project.Media.FirstOrDefault(m => m.Id == c.MediaId);
                return m != null && (m.Path.StartsWith("openrend://text", StringComparison.OrdinalIgnoreCase) ||
                                     m.Path.StartsWith("openrend://subtitles", StringComparison.OrdinalIgnoreCase));
            });

        if (clip is not null)
        {
            OpenEditTextMedia(clip.Id);
        }
    }

    private static nint WindowMessage(nint hwnd, int message, nint wparam, nint lparam, ref bool handled)
    {
        if (message != 0x24) return 0;
        var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return 0;
        var limits = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(lparam);
        // Borderless maximized windows must use the work area, including on secondary monitors.
        limits.MaxPosition = new NativePoint(info.Work.Left - info.Monitor.Left, info.Work.Top - info.Monitor.Top);
        limits.MaxSize = new NativePoint(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        System.Runtime.InteropServices.Marshal.StructureToPtr(limits, lparam, false);
        handled = true;
        return 0;
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint(int x, int y) { public int X = x, Y = y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
