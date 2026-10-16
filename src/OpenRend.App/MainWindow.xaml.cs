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
        Timeline.SplitRequested += () => Guard(vm.Split);
        Timeline.PanCropRequested += () => PanCropClick(this, new RoutedEventArgs());
        Timeline.DeleteRequested += () => Guard(vm.Delete);
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
        Loaded += (_, _) => { initialized = true; LoadWorkspace(); Refresh(); autosave.Start(); };
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
        await vm.SaveAsync(path); return true;
    }
    private async void NewClick(object sender, RoutedEventArgs e) => await GuardAsync(async () => { if (await ConfirmUnsavedAsync()) { Pause(); vm.NewProject(); } });
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
    private void MediaDoubleClick(object sender, MouseButtonEventArgs e) => PlaceSelectedClick(sender, e);
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
            vm.Editor.MoveSelection(vm.Selection, gesture.ClipId, gesture.TrackId, gesture.Start, vm.IgnoreGroups);
        else vm.Editor.TrimSelection(gesture.ClipId, gesture.Start, gesture.SourceIn, gesture.Duration, vm.IgnoreGroups);
    }
    private TimelineClip? SelectedClip => vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => vm.Selection.Contains(c.Id));
    private void UpdateInspector()
    {
        var clip = SelectedClip; Inspector.IsEnabled = clip is not null; ClipName.Text = clip?.Name ?? "Select a timeline clip";
        if (clip is null) return;
        ClipStart.Text = clip.StartFrame.ToString(); ClipIn.Text = clip.SourceInFrame.ToString(); ClipDuration.Text = clip.DurationFrames.ToString();
        ClipOpacity.Text = clip.Opacity.ToString(CultureInfo.CurrentCulture); ClipGain.Text = clip.Gain.ToString(CultureInfo.CurrentCulture);
        ClipPan.Text = clip.Pan.ToString(CultureInfo.CurrentCulture); ClipFadeIn.Text = clip.FadeInFrames.ToString(); ClipFadeOut.Text = clip.FadeOutFrames.ToString();
    }
    private void ApplyPropertiesClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (SelectedClip is not { } clip) return;
        if (vm.Project.Tracks.Single(t => t.Clips.Contains(clip)).IsLocked) throw new InvalidOperationException("Unlock this track before changing clip properties.");
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
        var dialog = new SaveFileDialog { Filter = "H.264 / AAC video|*.mp4", DefaultExt = ".mp4", FileName = vm.Project.Name + ".mp4" };
        if (dialog.ShowDialog(this) != true) return;
        exportCancellation = new CancellationTokenSource(); vm.Busy = true; vm.Progress = 0;
        CancelRender.Visibility = Visibility.Visible; RenderButton.IsEnabled = false;
        try
        {
            vm.Status = "Rendering edited timeline…";
            var progress = new Progress<double>(p => { vm.Progress = p; vm.Status = $"Rendering timeline: {p:P0}"; });
            await vm.Exporter.ExportAsync(vm.Project.DeepClone(), dialog.FileName, progress, exportCancellation.Token);
            vm.Status = $"Rendered {Path.GetFileName(dialog.FileName)}.";
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
    private void ClipPropertiesClick(object sender, RoutedEventArgs e) { ToolsTabs.SelectedIndex = 2; UpdateInspector(); }
    private async void PanCropClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var selected = vm.Project.Tracks.Where(track => track.Kind == TrackKind.Video)
            .SelectMany(track => track.Clips.Where(clip => vm.Selection.Contains(clip.Id)).Select(clip => (Track: track, Clip: clip))).ToArray();
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
        if (Keyboard.FocusedElement is TextBox && !(ctrl && key is Key.S or Key.O or Key.I or Key.N || ctrl && shift && key == Key.E)) return;
        Action? action = key switch
        {
            Key.I when ctrl => () => ImportClick(this, e),
            Key.O when ctrl => () => OpenClick(this, e),
            Key.S when ctrl => () => { if (shift) SaveAsClick(this, e); else SaveClick(this, e); }
            ,
            Key.N when ctrl => () => NewClick(this, e),
            Key.Z when ctrl => () => { if (shift) RedoClick(this, e); else UndoClick(this, e); }
            ,
            Key.Y when ctrl => () => RedoClick(this, e),
            Key.E when ctrl && shift => () => ExportClick(this, e),
            Key.A when ctrl && shift => () => AudioTrackClick(this, e),
            Key.V when ctrl && shift => () => VideoTrackClick(this, e),
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
