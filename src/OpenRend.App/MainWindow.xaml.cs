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
    private readonly DispatcherTimer autosave = new() { Interval = TimeSpan.FromMinutes(5) };
    private GeneralPreferences generalPreferences = new();
    private PlaybackSession? playback;
    private CancellationTokenSource? exportCancellation;
    private long revision;
    private bool previewRunning;
    private bool previewRequested;
    private bool playbackStarting;
    private bool safeAreas;
    private PreviewOverlayMode previewOverlay = PreviewOverlayMode.None;
    private bool loopPlayback;
    private bool autoCrossfade = true;
    private bool masterMuted;
    private string mediaFilter = "All";
    private int previewWidth, previewHeight;
    private bool splitScreenEnabled;
    private SplitScreenSource splitScreenSource = SplitScreenSource.FxBypassed;
    private SplitScreenDivision splitScreenDivision = SplitScreenDivision.LeftHalf;
    private bool retainSplitScreenSetting = true;
    private bool bypassAllFx;
    private byte[]? clipboardSnapshotPixels;
    private int clipboardSnapshotWidth, clipboardSnapshotHeight;
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
    public event Action<byte[], int, int>? FrameRendered;
    private byte[]? currentPreviewPixels;
    public (byte[]? Pixels, int Width, int Height) GetCurrentFrame() => (currentPreviewPixels, previewWidth, previewHeight);
    private VideoScopesWindow? videoScopesWindow;
    private MixingConsoleWindow? mixingConsoleWindow;
    private EditDetailsWindow? editDetailsWindow;
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
            var ignore = Timeline.IgnoreGroups || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            vm.Selection.Clear(); vm.Selection.UnionWith(vm.Editor.ExpandSelection(Timeline.Selection, ignore));
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
                isVisible: action == "Visible" ? !track.IsVisible : null,
                armRecord: action == "Arm" ? !track.ArmRecord : null);
        });
        Timeline.TrackArmRequested += (id, armed) => Guard(() =>
        {
            Pause();
            vm.Editor.SetTrackState(id, armRecord: armed);
        });
        Timeline.TrackColorRequested += (id, color) => Guard(() =>
        {
            Pause();
            vm.Editor.SetTrackColor(id, color);
        });
        Timeline.TrackAutomationModeRequested += (id, mode) => Guard(() =>
        {
            Pause();
            vm.Editor.SetTrackAutomationMode(id, mode);
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
        Timeline.TrackAudioFxRequested += id => OpenTrackAudioEffects(id);
        Timeline.TrackPhaseInvertRequested += id => Guard(() =>
        {
            var t = vm.Project.Tracks.FirstOrDefault(track => track.Id == id);
            if (t != null)
            {
                vm.Editor.SetTrackInvertPhase(id, !t.InvertPhase);
                Timeline.InvalidateVisual();
                vm.Status = $"Track phase inversion: {(t.InvertPhase ? "Inverted" : "Normal")}.";
            }
        });
        Timeline.TrackMotionRequested += id => OpenTrackMotion(id);
        Timeline.TrackColorGradingRequested += id => OpenTrackColorGrading(id);
        Timeline.TrackCompositeModeRequested += (id, mode) => Guard(() =>
        {
            Pause();
            vm.Editor.SetTrackCompositeMode(id, mode);
            Timeline.InvalidateVisual();
            RequestPreview();
            vm.Status = $"Track compositing mode set to {mode}.";
        });
        Timeline.TrackMakeCompositingChildRequested += id => Guard(() =>
        {
            Pause();
            vm.MakeCompositingChild(id);
            Timeline.InvalidateVisual();
            RequestPreview();
        });
        Timeline.TrackMakeCompositingParentRequested += id => Guard(() =>
        {
            Pause();
            vm.MakeCompositingParent(id);
            Timeline.InvalidateVisual();
            RequestPreview();
        });
        Timeline.TrackAdjustmentRequested += id => Guard(() =>
        {
            Pause();
            vm.ToggleTrackAdjustment(id);
            Timeline.InvalidateVisual();
            RequestPreview();
        });
        Timeline.TrackAudioBusRequested += (id, busId) => Guard(() =>
        {
            Pause();
            vm.SetTrackAudioBus(id, busId);
            Timeline.InvalidateVisual();
        });
        Timeline.ColorGradingRequested += () => ColorGradingClick(this, new RoutedEventArgs());
        Timeline.MotionTrackingRequested += id => OpenMotionTracking(id);
        Timeline.TextBasedEditingRequested += id => OpenTextBasedEditing(id);
        Timeline.MarkerSelected += marker => Guard(() =>
        {
            Pause();
            vm.Position = marker.Frame;
            UpdatePosition();
            RequestPreview();
            vm.Status = $"Marker {marker.Index}: {marker.Label} ({TimelineView.FormatTimecode(marker.Frame, vm.Project.FrameRate)})";
        });
        Timeline.MarkerEditRequested += marker => OpenMarkerEdit(marker);
        Timeline.MarkerDeleteRequested += id => Guard(() =>
        {
            Pause();
            vm.Editor.RemoveMarker(id);
            Timeline.InvalidateVisual();
            vm.Status = "Deleted marker.";
        });
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
        Timeline.NextTakeRequested += id => Guard(() => { Pause(); vm.Editor.NextTake(id); UpdateInspector(); RequestPreview(); Refresh(); });
        Timeline.PreviousTakeRequested += id => Guard(() => { Pause(); vm.Editor.PreviousTake(id); UpdateInspector(); RequestPreview(); Refresh(); });
        Timeline.SelectTakeRequested += (id, idx) => Guard(() => { Pause(); vm.Editor.SetActiveTake(id, idx); UpdateInspector(); RequestPreview(); Refresh(); });
        Timeline.DeleteTakeRequested += id => Guard(() => { Pause(); vm.Editor.DeleteActiveTake(id); UpdateInspector(); RequestPreview(); Refresh(); });
        Timeline.ClipReverseRequested += (id, rev) => Guard(() =>
        {
            Pause();
            vm.Editor.SetClipReverse(id, rev);
            vm.Status = rev ? "Reversed event playback." : "Restored normal event playback.";
            UpdateInspector();
            RequestPreview();
            Refresh();
        });
        Timeline.ClipPlaybackRateRequested += (id, rate) => Guard(() =>
        {
            Pause();
            vm.Editor.SetClipPlaybackRate(id, rate);
            vm.Status = $"Set event playback rate to {rate:0.##}x.";
            UpdateInspector();
            RequestPreview();
            Refresh();
        });
        Timeline.ClipUnderSampleRateRequested += (id, rate) => Guard(() =>
        {
            Pause();
            vm.Editor.SetClipUnderSampleRate(id, rate);
            vm.Status = $"Set event undersample rate to {rate:0.###}.";
            UpdateInspector();
            RequestPreview();
            Refresh();
        });
        Timeline.ClipResampleModeRequested += (id, mode) => Guard(() =>
        {
            Pause();
            vm.Editor.SetClipResampleMode(id, mode);
            vm.Status = $"Set event resample mode to {mode}.";
            UpdateInspector();
            RequestPreview();
            Refresh();
        });
        Timeline.ClipMaintainAspectRatioRequested += (id, maintain) => Guard(() =>
        {
            Pause();
            vm.Editor.SetClipMaintainAspectRatio(id, maintain);
            vm.Status = maintain ? "Enabled maintain aspect ratio." : "Disabled maintain aspect ratio.";
            UpdateInspector();
            RequestPreview();
            Refresh();
        });
        Timeline.ClipReduceInterlaceRequested += (id, red) => Guard(() =>
        {
            Pause();
            vm.Editor.SetClipReduceInterlace(id, red);
            vm.Status = red ? "Enabled reduce interlace flicker." : "Disabled reduce interlace flicker.";
            UpdateInspector();
            RequestPreview();
            Refresh();
        });
        Timeline.ClipNormalizeRequested += (id, norm) => Guard(() =>
        {
            Pause();
            vm.Editor.SetClipNormalize(id, norm);
            vm.Status = norm ? "Normalized audio event." : "Removed audio normalization.";
            UpdateInspector();
            RequestPreview();
            Refresh();
        });
        Timeline.ClipInvertPhaseRequested += (id, inv) => Guard(() =>
        {
            Pause();
            vm.Editor.SetClipInvertPhase(id, inv);
            vm.Status = inv ? "Inverted audio event phase." : "Restored audio event phase.";
            UpdateInspector();
            RequestPreview();
            Refresh();
        });
        Timeline.DeleteRequested += () => Guard(vm.Delete);
        Timeline.InsertVideoTrackRequested += idx => Guard(() => vm.Editor.InsertTrack(idx, TrackKind.Video));
        Timeline.InsertAudioTrackRequested += idx => Guard(() => vm.Editor.InsertTrack(idx, TrackKind.Audio));
        Timeline.DuplicateTrackRequested += id => Guard(() => vm.Editor.DuplicateTrack(id));
        Timeline.DeleteTrackRequested += id => Guard(() => vm.Editor.DeleteTrack(id));
        Timeline.UnlinkRequested += () => GroupRemoveClick(this, new RoutedEventArgs());
        Timeline.LinkRequested += () => GroupCreateClick(this, new RoutedEventArgs());
        Timeline.ClearGroupRequested += () => GroupClearClick(this, new RoutedEventArgs());
        Timeline.SelectAllInGroupRequested += id => GroupSelectAllInClipGroup(id);
        Timeline.ToggleEnvelopeRequested += (id, type) => Guard(() =>
        {
            Pause();
            vm.Editor.ToggleClipEnvelope(id, type);
            vm.Status = $"{(vm.Editor.HasClipEnvelope(id, type) ? "Added" : "Removed")} {type} envelope.";
            RequestPreview();
        });
        Timeline.SetEnvelopePointRequested += (id, type, frame, val, curve) => Guard(() =>
        {
            Pause();
            vm.Editor.SetEnvelopePoint(id, type, frame, val, curve);
            vm.Status = $"Set {type} envelope point at frame {frame} ({Math.Round(val * 100)}%).";
            RequestPreview();
        });
        Timeline.RemoveEnvelopePointRequested += (id, type, frame) => Guard(() =>
        {
            Pause();
            vm.Editor.RemoveEnvelopePoint(id, type, frame);
            vm.Status = $"Removed {type} envelope point at frame {frame}.";
            RequestPreview();
        });
        Timeline.SetEnvelopePointCurveRequested += (id, type, frame, curve) => Guard(() =>
        {
            Pause();
            vm.Editor.SetEnvelopePointCurve(id, type, frame, curve);
            vm.Status = $"Set {type} envelope point curve to {curve} Fade.";
            RequestPreview();
        });
        Timeline.ToggleTrackEnvelopeRequested += (id, type) => Guard(() =>
        {
            Pause();
            vm.Editor.ToggleTrackEnvelope(id, type);
            vm.Status = $"{(vm.Editor.HasTrackEnvelope(id, type) ? "Added" : "Removed")} Track {type} envelope.";
            RequestPreview();
        });
        Timeline.SetTrackEnvelopePointRequested += (id, type, frame, val, curve) => Guard(() =>
        {
            Pause();
            vm.Editor.SetTrackEnvelopePoint(id, type, frame, val, curve);
            vm.Status = $"Set Track {type} envelope point at frame {frame} ({Math.Round(val * 100)}%).";
            RequestPreview();
        });
        Timeline.RemoveTrackEnvelopePointRequested += (id, type, frame) => Guard(() =>
        {
            Pause();
            vm.Editor.RemoveTrackEnvelopePoint(id, type, frame);
            vm.Status = $"Removed Track {type} envelope point at frame {frame}.";
            RequestPreview();
        });
        Timeline.SetTrackEnvelopePointCurveRequested += (id, type, frame, curve) => Guard(() =>
        {
            Pause();
            vm.Editor.SetTrackEnvelopePointCurve(id, type, frame, curve);
            vm.Status = $"Set Track {type} envelope point curve to {curve} Fade.";
            RequestPreview();
        });

        Preview.CanvasMouseDown += OnPreviewMouseDown;
        Preview.CanvasMouseMove += OnPreviewMouseMove;
        Preview.CanvasMouseUp += OnPreviewMouseUp;
        Preview.CanvasMouseDoubleClick += OnPreviewMouseDoubleClick;
        Preview.Ready += () => { UpdatePreviewStatus(); RequestPreview(); };
        Preview.SizeChanged += (_, _) => UpdatePreviewStatus();
        DockColorGrading.Initialize(vm, () => RequestPreview(), () => OpenColorGrading());
        Timeline.LoopRegionChanged += range => vm.LoopRegion = range;
        TrimmerDeck.Initialize(vm);
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
                if (mixingConsoleWindow is { IsLoaded: true }) mixingConsoleWindow.UpdateMeters(playback.PeakLeft, playback.PeakRight);
                if (vm.Position != lastPreview) RequestPreview();
                if (playback.IsFinished || vm.Position >= vm.Project.DurationFrames)
                {
                    if (loopPlayback && vm.Project.DurationFrames > 0)
                    {
                        Pause();
                        vm.Position = 0;
                        UpdatePosition();
                        RequestPreview();
                        PlayClick(this, new RoutedEventArgs());
                    }
                    else
                    {
                        Pause();
                    }
                }
            }
            catch (Exception ex) { Pause(); ShowError(ex); }
        };
        autosave.Tick += async (_, _) =>
        {
            var dataDir = Path.GetDirectoryName(recoveryPath)!;
            SessionRecoveryManager.RecordHeartbeat(dataDir, vm.Dirty, vm.ProjectPath);
            if (!vm.Dirty || vm.Busy || !generalPreferences.EnableAutoSave) return;
            try
            {
                var clone = vm.Project.DeepClone();
                await ProjectStore.SaveAsync(clone, recoveryPath);
                if (generalPreferences.UseProjectDirectoryForAutoSave && !string.IsNullOrEmpty(vm.ProjectPath))
                {
                    await ProjectStore.SaveAutosaveAsync(clone, vm.ProjectPath);
                }
            }
            catch (Exception ex) { vm.Status = "Recovery snapshot could not be saved: " + ex.Message; }
        };
        Loaded += async (_, _) =>
        {
            initialized = true;
            LoadWorkspace();
            LoadGeneralPreferences();
            InitializeLayoutManager();
            InitializeTransitionBrowser();
            InitializeToolsViews();
            InitializeRulerEvents();
            InitializeEditToolsEvents();
            InitializeMediaBins();
            Refresh();
            autosave.Start();
            await CheckCrashRecoveryAsync();
        };
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
        Timeline.IgnoreGroups = vm.IgnoreGroups;
        Timeline.Selection.Clear(); Timeline.Selection.UnionWith(vm.Selection);
        vm.ActiveTrackIndex = Timeline.ActiveTrackIndex;
        Timeline.InvalidateVisual(); UpdatePosition(); UpdateInspector(); SyncRulerFormatMenuChecks();
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
        UpdatePreviewStatus();
        if (vm.Project.Media.Any(m => !cachedMediaPaths.TryGetValue(m.Id, out var path) || path != m.Path)) _ = PopulateMediaVisualsAsync();
        UpdateRecordTimeDisplay();
        if (editDetailsWindow != null && editDetailsWindow.IsLoaded) editDetailsWindow.RefreshGrid();
        RequestPreview();
    }
    private string PreviewDescription()
    {
        var backend = $"Composite: {(vm.Renderer.IsHardwareCompositing ? "GPU" : vm.Renderer.IsD3DCompositing ? "WARP" : "CPU")}; display: Direct3D 11 {(Preview.IsHardware ? "hardware" : "software")}";
        PreviewInfo.ToolTip = backend;
        System.Windows.Automation.AutomationProperties.SetHelpText(PreviewInfo, Preview.IsReady ? backend : "Preview is starting.");
        return $"Project: {vm.Project.Width}×{vm.Project.Height}, {vm.Project.FrameRate.Value:0.000}p     Preview: {previewWidth}×{previewHeight}     Frame: {(lastPreview < 0 ? "—" : lastPreview.ToString(CultureInfo.InvariantCulture))}     Display: {Preview.ActualWidth:0}×{Preview.ActualHeight:0}";
    }
    private void UpdatePreviewStatus()
    {
        var desc = PreviewDescription();
        System.Windows.Automation.AutomationProperties.SetName(PreviewInfo, desc);
        RenderPreviewStatusBadges();
    }
    private void RenderPreviewStatusBadges()
    {
        PreviewInfo.Inlines.Clear();

        // Project format badge (page icon + resolution + framerate)
        PreviewInfo.Inlines.Add(new System.Windows.Documents.InlineUIContainer(CreateProjectIcon()));
        PreviewInfo.Inlines.Add(new System.Windows.Documents.Run($" {vm.Project.Width}×{vm.Project.Height}, {vm.Project.FrameRate.Value:0.000}p"));

        // Segment separator
        PreviewInfo.Inlines.Add(new System.Windows.Documents.InlineUIContainer(CreateStatusDivider()));

        // Preview format badge (monitor icon + preview resolution + framerate)
        PreviewInfo.Inlines.Add(new System.Windows.Documents.InlineUIContainer(CreatePreviewIcon()));
        var pw = previewWidth > 0 ? previewWidth : vm.Project.Width;
        var ph = previewHeight > 0 ? previewHeight : vm.Project.Height;
        PreviewInfo.Inlines.Add(new System.Windows.Documents.Run($" {pw}×{ph}, {vm.Project.FrameRate.Value:0.000}p"));

        // Segment separator
        PreviewInfo.Inlines.Add(new System.Windows.Documents.InlineUIContainer(CreateStatusDivider()));

        // Frame counter badge (viewfinder corner icon + frame number)
        PreviewInfo.Inlines.Add(new System.Windows.Documents.InlineUIContainer(CreateFrameIcon()));
        PreviewInfo.Inlines.Add(new System.Windows.Documents.Run($" {(lastPreview < 0 ? "0" : lastPreview.ToString(CultureInfo.InvariantCulture))}"));

        // Segment separator
        PreviewInfo.Inlines.Add(new System.Windows.Documents.InlineUIContainer(CreateStatusDivider()));

        // Display canvas resolution badge (viewport icon + actual pixel dimensions)
        PreviewInfo.Inlines.Add(new System.Windows.Documents.InlineUIContainer(CreateDisplayIcon()));
        var dw = (int)Math.Round(Preview.ActualWidth);
        var dh = (int)Math.Round(Preview.ActualHeight);
        PreviewInfo.Inlines.Add(new System.Windows.Documents.Run($" {dw}×{dh}"));
    }
    private static FrameworkElement CreateProjectIcon()
    {
        var canvas = new Canvas { Width = 11, Height = 13, Margin = new Thickness(0, 0, 5, -2), VerticalAlignment = VerticalAlignment.Center };
        var body = new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC)),
            Data = Geometry.Parse("M 0,0 L 7,0 L 11,4 L 11,13 L 0,13 Z")
        };
        var fold = new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Data = Geometry.Parse("M 7,0 L 7,4 L 11,4 Z")
        };
        canvas.Children.Add(body);
        canvas.Children.Add(fold);
        return canvas;
    }
    private static FrameworkElement CreatePreviewIcon()
    {
        var grid = new Grid { Width = 13, Height = 12, Margin = new Thickness(0, 0, 5, -2), VerticalAlignment = VerticalAlignment.Center };
        var screen = new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            StrokeThickness = 1,
            Fill = new SolidColorBrush(Color.FromRgb(0x3A, 0x82, 0xD2)),
            Height = 10,
            VerticalAlignment = VerticalAlignment.Top
        };
        var stand = new System.Windows.Shapes.Rectangle
        {
            Fill = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
            Width = 5,
            Height = 2,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        grid.Children.Add(screen);
        grid.Children.Add(stand);
        return grid;
    }
    private static FrameworkElement CreateFrameIcon()
    {
        var canvas = new Canvas { Width = 12, Height = 12, Margin = new Thickness(0, 0, 5, -2), VerticalAlignment = VerticalAlignment.Center };
        var corners = new System.Windows.Shapes.Path
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)),
            StrokeThickness = 1,
            Data = Geometry.Parse("M 0.5,3 L 0.5,0.5 L 3,0.5  M 8.5,0.5 L 11.5,0.5 L 11.5,3  M 0.5,8.5 L 0.5,11.5 L 3,11.5  M 8.5,11.5 L 11.5,11.5 L 11.5,8.5")
        };
        var inner = new System.Windows.Shapes.Rectangle
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(Color.FromRgb(0x3A, 0x82, 0xD2))
        };
        Canvas.SetLeft(inner, 3);
        Canvas.SetTop(inner, 3);
        canvas.Children.Add(corners);
        canvas.Children.Add(inner);
        return canvas;
    }
    private static FrameworkElement CreateDisplayIcon()
    {
        var canvas = new Canvas { Width = 13, Height = 12, Margin = new Thickness(0, 0, 5, -2), VerticalAlignment = VerticalAlignment.Center };
        var corners = new System.Windows.Shapes.Path
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)),
            StrokeThickness = 1,
            Data = Geometry.Parse("M 0.5,3 L 0.5,0.5 L 3,0.5  M 9.5,0.5 L 12.5,0.5 L 12.5,3  M 0.5,8.5 L 0.5,11.5 L 3,11.5  M 9.5,11.5 L 12.5,11.5 L 12.5,8.5")
        };
        var inner = new System.Windows.Shapes.Rectangle
        {
            Width = 7,
            Height = 5,
            Stroke = new SolidColorBrush(Color.FromRgb(0xE8, 0x5D, 0x36)),
            StrokeThickness = 1
        };
        Canvas.SetLeft(inner, 3);
        Canvas.SetTop(inner, 3.5);
        canvas.Children.Add(corners);
        canvas.Children.Add(inner);
        return canvas;
    }
    private static FrameworkElement CreateStatusDivider()
    {
        return new System.Windows.Shapes.Rectangle
        {
            Width = 1,
            Height = 13,
            Fill = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)),
            Margin = new Thickness(12, 0, 12, -2),
            VerticalAlignment = VerticalAlignment.Center
        };
    }
    private MediaItem? SelectedLibraryItem =>
        (MediaList?.SelectedItem ?? MediaDetailsList?.SelectedItem ?? MediaCompactList?.SelectedItem) as MediaItem;

    private void MediaFilterChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        var tag = (e.NewValue as TreeViewItem)?.Tag;
        if (tag is Guid binId)
        {
            activeBinId = binId;
            mediaFilter = "Bin";
        }
        else
        {
            activeBinId = null;
            mediaFilter = tag as string ?? "All";
        }
        UpdateMediaFilter();
    }

    private void MediaSearchChanged(object sender, TextChangedEventArgs e) => UpdateMediaFilter();

    private void MediaViewModeClick(object sender, RoutedEventArgs e)
    {
        var source = sender as System.Windows.Controls.Primitives.ToggleButton;
        if (MediaViewThumbnails is not null) MediaViewThumbnails.IsChecked = source == MediaViewThumbnails;
        if (MediaViewDetails is not null) MediaViewDetails.IsChecked = source == MediaViewDetails;
        if (MediaViewList is not null) MediaViewList.IsChecked = source == MediaViewList;

        if (MediaList is not null) MediaList.Visibility = MediaViewThumbnails?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (MediaDetailsList is not null) MediaDetailsList.Visibility = MediaViewDetails?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (MediaCompactList is not null) MediaCompactList.Visibility = MediaViewList?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMediaFilter()
    {
        var query = MediaSearchBox?.Text?.Trim();
        var view = CollectionViewSource.GetDefaultView(vm.Library);
        if (view == null) return;
        view.Filter = item =>
        {
            if (item is not MediaItem media) return false;
            bool matchesCat;
            if (activeBinId.HasValue)
            {
                var bin = vm.Project.MediaBins.FirstOrDefault(b => b.Id == activeBinId.Value);
                matchesCat = bin != null && bin.MediaIds.Contains(media.Asset.Id);
            }
            else
            {
                matchesCat = mediaFilter switch
                {
                    "Video" => media.Asset.HasVideo,
                    "Audio" => media.Asset.HasAudio && !media.Asset.HasVideo,
                    "Images" => !media.Asset.HasVideo && !media.Asset.HasAudio || IsImageMedia(media.Asset),
                    "Missing" => !File.Exists(media.Asset.Path),
                    _ => true
                };
            }
            if (!matchesCat) return false;
            if (string.IsNullOrEmpty(query)) return true;
            return media.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || media.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
                || media.Type.Contains(query, StringComparison.OrdinalIgnoreCase)
                || media.Asset.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase));
        };
        var currentBinName = activeBinId.HasValue ? vm.Project.MediaBins.FirstOrDefault(b => b.Id == activeBinId.Value)?.Name : mediaFilter;
        if (MediaCount != null)
        {
            var matchCount = view.Cast<object>().Count();
            MediaCount.Text = $"{matchCount} items (in {currentBinName ?? "All Media"})";
        }
        if (EmptyLibrary != null)
        {
            EmptyLibrary.Text = vm.Library.Count == 0 ? "Import video or audio\nCtrl+I or drop files here" : "No media matches this view";
            EmptyLibrary.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void LoadGeneralPreferences()
    {
        try { generalPreferences = GeneralPreferencesStore.Load(GeneralPreferencesPath); }
        catch { generalPreferences = new(); }
        ApplyGeneralPreferences(generalPreferences);
    }

    private void ApplyGeneralPreferences(GeneralPreferences prefs)
    {
        generalPreferences = prefs;
        vm.CreateProjectBackups = prefs.CreateProjectBackups;
        autosave.IsEnabled = prefs.EnableAutoSave;
        autosave.Interval = TimeSpan.FromMinutes(Math.Clamp(prefs.AutoSaveIntervalMinutes, 1, 120));
    }

    private async Task CheckCrashRecoveryAsync()
    {
        var dataDir = Path.GetDirectoryName(recoveryPath)!;
        var candidate = SessionRecoveryManager.CheckCrashRecovery(dataDir);
        if (candidate is not null && File.Exists(candidate.SnapshotPath))
        {
            var dialog = new CrashRecoveryWindow(candidate, this);
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    Pause();
                    var recovered = await ProjectStore.LoadAsync(candidate.SnapshotPath);
                    vm.Replace(recovered, null);
                    await PopulateMediaVisualsAsync();
                    vm.Status = "Restored autosaved project from previous session. Save as a project to keep these edits.";
                }
                catch (Exception ex)
                {
                    vm.Status = "Could not restore autosaved project: " + ex.Message;
                }
            }
            else
            {
                SessionRecoveryManager.DiscardRecovery(dataDir);
            }
        }

        SessionRecoveryManager.StartSession(dataDir, vm.ProjectPath);
    }

    private void MediaToolbarFxClick(object sender, RoutedEventArgs e)
    {
        var selected = SelectedLibraryItem;
        if (selected is null) { vm.Status = "Select a media item in the pool to edit Media FX."; return; }
        OpenMediaEffects(selected.Asset.Id);
    }

    private void MediaToolbarPropsClick(object sender, RoutedEventArgs e)
    {
        var selected = SelectedLibraryItem;
        if (selected is null) { vm.Status = "Select a media item to view properties."; return; }
        new MediaPropertiesWindow(selected.Asset, this).ShowDialog();
    }

    private void MediaPropertiesClick(object sender, RoutedEventArgs e) => MediaToolbarPropsClick(sender, e);
    private void UpdatePosition()
    {
        Timeline.Position = vm.Position; Timeline.InvalidateVisual();
        Timecode.Text = TimecodeFormatter.Format(vm.Position, vm.Project.FrameRate, vm.Project.RulerFormat, vm.Project.AudioSampleRate, vm.Project.BeatsPerMinute, vm.Project.BeatsPerMeasure, vm.Project.RulerStartTimeFrames);
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
                var zoom = PreviewZoom.SelectedIndex switch { 1 => 2.0, 2 => .5, _ => 1.0 }; var overlay = previewOverlay;
                var (width, height) = PreviewQuality.SelectedIndex switch
                {
                    0 => FitSize(project, 320),
                    2 => (project.Width, project.Height),
                    _ => FitSize(project, 640)
                };
                byte[]? referencePixels = null;
                if (splitScreenEnabled)
                {
                    if (splitScreenSource == SplitScreenSource.FxBypassed)
                    {
                        referencePixels = await Task.Run(() => vm.Renderer.RenderFrame(project, frame, width, height, bypassVideoEffects: true));
                    }
                    else if (splitScreenSource == SplitScreenSource.Clipboard && clipboardSnapshotPixels != null)
                    {
                        referencePixels = RescaleFrame(clipboardSnapshotPixels, clipboardSnapshotWidth, clipboardSnapshotHeight, width, height);
                    }
                }
                var mainBypass = bypassAllFx;
                var pixels = await Task.Run(() =>
                {
                    var baseRender = vm.Renderer.RenderFrame(project, frame, width, height, bypassVideoEffects: mainBypass);
                    return PreviewDisplay.Apply(baseRender, width, height, zoom, overlay, referencePixels, splitScreenEnabled ? splitScreenDivision : null);
                });
                // The audio clock can advance during decoding. Show completed playback frames,
                // then catch up to its latest position instead of discarding every late frame.
                if (IsLoaded && request.CanPresent(revision, playbackGeneration, vm.Position, playback is not null))
                {
                    Preview.Present(pixels, width, height); currentPreviewPixels = pixels; previewWidth = width; previewHeight = height; lastPreview = frame; UpdatePreviewStatus(); FrameRendered?.Invoke(pixels, width, height);
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
    private async void ImportFcp7XmlClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Final Cut Pro 7 XML",
            Filter = "Final Cut Pro 7 XML (*.xml)|*.xml|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true || !await ConfirmUnsavedAsync()) return;
        Pause();
        var xml = await File.ReadAllTextAsync(dialog.FileName);
        var project = Fcp7XmlInterchange.ImportFromXml(xml);
        vm.Replace(project, null);
        await PopulateMediaVisualsAsync();
        vm.Status = $"Imported Final Cut Pro 7 XML ({project.Tracks.Count} tracks, {project.Tracks.SelectMany(t => t.Clips).Count()} clips).";
    });
    private async void ExportFcp7XmlClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var defaultName = Path.GetFileNameWithoutExtension(vm.ProjectPath) ?? vm.Project.Name ?? "Sequence";
        var dialog = new SaveFileDialog
        {
            Title = "Export Final Cut Pro 7 XML",
            Filter = "Final Cut Pro 7 XML (*.xml)|*.xml",
            FileName = $"{defaultName}.xml"
        };
        if (dialog.ShowDialog(this) == true)
        {
            var xml = Fcp7XmlInterchange.ExportToXml(vm.Project);
            await File.WriteAllTextAsync(dialog.FileName, xml, System.Text.Encoding.UTF8);
            vm.Status = $"Exported Final Cut Pro 7 XML to {Path.GetFileName(dialog.FileName)}.";
        }
    });
    private async void ImportEdlClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import CMX 3600 EDL",
            Filter = "EDL Text File (*.txt;*.edl)|*.txt;*.edl|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true || !await ConfirmUnsavedAsync()) return;
        Pause();
        var edl = await File.ReadAllTextAsync(dialog.FileName);
        var project = EdlInterchange.ImportFromEdl(edl, vm.Project.FrameRate);
        vm.Replace(project, null);
        await PopulateMediaVisualsAsync();
        vm.Status = $"Imported CMX 3600 EDL ({project.Tracks.Count} tracks, {project.Tracks.SelectMany(t => t.Clips).Count()} clips).";
    });
    private async void ExportEdlClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var defaultName = Path.GetFileNameWithoutExtension(vm.ProjectPath) ?? vm.Project.Name ?? "EDL_Sequence";
        var dialog = new SaveFileDialog
        {
            Title = "Export CMX 3600 EDL",
            Filter = "EDL Text File (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"{defaultName}.txt"
        };
        if (dialog.ShowDialog(this) == true)
        {
            var edl = EdlInterchange.ExportToEdl(vm.Project);
            await File.WriteAllTextAsync(dialog.FileName, edl, System.Text.Encoding.UTF8);
            vm.Status = $"Exported CMX 3600 EDL to {Path.GetFileName(dialog.FileName)}.";
        }
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
        if (SelectedLibraryItem is not MediaItem item) { vm.Status = "Select a source in Project Media."; return; }
        Pause(); vm.Place(item.Asset.Id);
    });
    private void RemoveUnusedClick(object sender, RoutedEventArgs e) => Guard(() => vm.Editor.Execute(p =>
    {
        var used = p.Tracks.SelectMany(t => t.Clips).Select(c => c.MediaId).ToHashSet();
        p.Media.RemoveAll(m => !used.Contains(m.Id));
    }));
    private void MediaDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedLibraryItem is MediaItem item && item.Asset.Path.StartsWith("openrend://text", StringComparison.OrdinalIgnoreCase))
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
        if (e.LeftButton == MouseButtonState.Pressed && SelectedLibraryItem is MediaItem item)
        {
            var sourceElement = sender as FrameworkElement ?? MediaList;
            DragDrop.DoDragDrop(sourceElement, new DataObject("OpenRend.Media", item.Asset.Id), DragDropEffects.Copy);
        }
    }
    private void MediaSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = SelectedLibraryItem;
        if (item is not null)
        {
            if (sender != MediaList && MediaList is not null && MediaList.SelectedItem != item) MediaList.SelectedItem = item;
            if (sender != MediaDetailsList && MediaDetailsList is not null && MediaDetailsList.SelectedItem != item) MediaDetailsList.SelectedItem = item;
            if (sender != MediaCompactList && MediaCompactList is not null && MediaCompactList.SelectedItem != item) MediaCompactList.SelectedItem = item;
            MediaDetails.Text = $"{item.Name}  â€¢  {item.Details}  â€¢  Used {item.UseCount}x  â€¢  {item.Path}";
            vm.Status = $"{item.Name}  â€¢  {item.Details}";
        }
        else
        {
            MediaDetails.Text = string.Empty;
        }
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
        var ignoreGroups = gesture.IgnoreGroups || vm.IgnoreGroups;
        var selection = vm.Selection.Contains(gesture.ClipId) ? vm.Selection : [gesture.ClipId];
        var movingIds = ignoreGroups ? new[] { gesture.ClipId } : (IEnumerable<Guid>)selection;
        if (gesture.Mode == "Move")
        {
            if (gesture.NewTrackIndex is { } newIdx && gesture.NewTrackKind is { } newKind)
            {
                var newTrack = vm.Editor.MoveSelectionToNewTrack(movingIds, gesture.ClipId, newIdx, newKind, gesture.Start, ignoreGroups, gesture.Ripple, gesture.RippleMode);
                var trackIndex = vm.Project.Tracks.IndexOf(newTrack);
                if (trackIndex >= 0) Timeline.ActivateTrack(trackIndex);
                vm.Status = $"Created new {newKind} layer '{newTrack.Name}' and moved event.";
            }
            else
            {
                vm.Editor.MoveSelection(movingIds, gesture.ClipId, gesture.TrackId, gesture.Start, ignoreGroups, gesture.Ripple, gesture.RippleMode);
                vm.Status = $"Moved event to {TimelineView.FormatTimecode(gesture.Start, vm.Project.FrameRate)}.";
            }
        }
        else
        {
            if (gesture.Mode == "Slip")
            {
                var (_, clip) = vm.Editor.FindClip(gesture.ClipId);
                var delta = gesture.SourceIn - clip.SourceInFrame;
                vm.Editor.SlipClip(gesture.ClipId, delta, ignoreGroups);
                vm.Status = $"Slipped event media in-point to {TimelineView.FormatTimecode(gesture.SourceIn, vm.Project.FrameRate)}.";
            }
            else if (gesture.Mode == "Slide")
            {
                var (_, clip) = vm.Editor.FindClip(gesture.ClipId);
                var delta = gesture.Start - clip.StartFrame;
                vm.Editor.SlideClip(gesture.ClipId, delta, ignoreGroups);
                vm.Status = $"Slid event to {TimelineView.FormatTimecode(gesture.Start, vm.Project.FrameRate)}.";
            }
            else if (gesture.Mode is "TimeStretchLeft" or "TimeStretchRight")
            {
                vm.Editor.TimeStretchClip(gesture.ClipId, gesture.Duration, gesture.NewPlaybackRate ?? 1.0, stretchFromLeft: gesture.Mode == "TimeStretchLeft", ignoreGroups);
                vm.Status = $"Time-stretched event to {gesture.Duration} frames ({gesture.NewPlaybackRate:0.###}x playback rate).";
            }
            else if (gesture.Mode == "Roll" && gesture.RollAdjacentClipId is { } adjId)
            {
                var (_, clip) = vm.Editor.FindClip(gesture.ClipId);
                var delta = gesture.Duration - clip.DurationFrames;
                vm.Editor.RollEdit(gesture.ClipId, adjId, delta);
                vm.Status = $"Rolled cut boundary by {delta} frames.";
            }
            else
            {
                vm.Editor.TrimSelection(gesture.ClipId, gesture.Start, gesture.SourceIn, gesture.Duration, ignoreGroups, gesture.Ripple, gesture.RippleMode);
            }
            vm.Status = $"Trimmed event duration to {gesture.Duration} frames.";
        }
        RequestPreview();
    }
    private TimelineClip? SelectedClip => vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => vm.Selection.Contains(c.Id));
    private void UpdateInspector()
    {
        var clip = SelectedClip; Inspector.IsEnabled = clip is not null; ClipName.Text = clip?.Name ?? "Select a timeline clip";
        DockColorGrading.BindToClip(clip);
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
            PlayButton.ContentTemplate = (DataTemplate)FindResource("ic_pause");
            if (PreviewPlayButton is not null) PreviewPlayButton.ContentTemplate = (DataTemplate)FindResource("ic_pause");
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
        PlayButton.ContentTemplate = (DataTemplate)FindResource("ic_play");
        if (PreviewPlayButton is not null) PreviewPlayButton.ContentTemplate = (DataTemplate)FindResource("ic_play");
        PlaybackRate.Text = "Rate: 0.00";
        LeftMeter.Height = RightMeter.Height = 0;
        mixingConsoleWindow?.ResetMeters();
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
    private void ClipPropertiesClick(object sender, RoutedEventArgs e) { ClipPropertiesTab.IsSelected = true; UpdateInspector(); }
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
    private void TrackAudioFxClick(object sender, RoutedEventArgs e) => OpenTrackAudioEffects();
    private void OpenTrackAudioEffects(Guid? requestedTrack = null) => Guard(() =>
    {
        var tracks = vm.Project.Tracks.Where(track => track.Kind == TrackKind.Audio).ToArray();
        if (tracks.Length == 0) { vm.Status = "Project has no audio tracks."; return; }
        var track = requestedTrack is { } id
            ? tracks.FirstOrDefault(t => t.Id == id)
            : (Timeline.ActiveTrackIndex >= 0 && Timeline.ActiveTrackIndex < vm.Project.Tracks.Count && vm.Project.Tracks[Timeline.ActiveTrackIndex].Kind == TrackKind.Audio
                ? vm.Project.Tracks[Timeline.ActiveTrackIndex]
                : tracks.FirstOrDefault());
        if (track is null) { vm.Status = "Select an audio track to edit its audio effects."; return; }
        if (track.IsLocked) { vm.Status = "Unlock the audio track before editing its audio effects."; return; }
        Pause();
        new AudioTrackFxWindow(vm, track.Id) { Owner = this }.ShowDialog();
        Timeline.InvalidateVisual();
    });

    private void MasterAudioFxClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        new AudioTrackFxWindow(vm, Guid.Empty) { Owner = this }.ShowDialog();
    });

    private void MasterDimClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var dim = !vm.Project.MasterAudioDim;
        vm.Editor.SetMasterAudioDim(dim);
        MasterDimBtn.Background = dim ? new SolidColorBrush(Color.FromRgb(200, 140, 20)) : (Brush)FindResource("ButtonBackground");
        MasterDimBtn.Foreground = dim ? Brushes.Black : (Brush)FindResource("TextColor");
        vm.Status = $"Master audio dim: {(dim ? "-20 dB active" : "off")}.";
    });

    private void MasterMuteClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var mute = !vm.Project.MasterAudioMute;
        vm.Editor.SetMasterAudioMute(mute);
        MasterMuteBtn.Background = mute ? new SolidColorBrush(Color.FromRgb(180, 40, 40)) : (Brush)FindResource("ButtonBackground");
        MasterMuteBtn.Foreground = mute ? Brushes.White : (Brush)FindResource("TextColor");
        vm.Status = $"Master audio mute: {(mute ? "muted" : "unmuted")}.";
    });

    private void TrackMotionClick(object sender, RoutedEventArgs e) => OpenTrackMotion();
    private void OpenTrackMotion(Guid? requestedTrack = null) => Guard(() =>
    {
        var tracks = vm.Project.Tracks.Where(track => track.Kind == TrackKind.Video).ToArray();
        if (tracks.Length == 0) { vm.Status = "Project has no video tracks."; return; }
        var track = requestedTrack is { } id
            ? tracks.FirstOrDefault(t => t.Id == id)
            : (Timeline.ActiveTrackIndex >= 0 && Timeline.ActiveTrackIndex < vm.Project.Tracks.Count && vm.Project.Tracks[Timeline.ActiveTrackIndex].Kind == TrackKind.Video
                ? vm.Project.Tracks[Timeline.ActiveTrackIndex]
                : tracks.FirstOrDefault());
        if (track is null) { vm.Status = "Select a video track to edit its track motion."; return; }
        if (track.IsLocked) { vm.Status = "Unlock the video track before editing its track motion."; return; }
        Pause();
        var dialog = new TrackMotionWindow(vm, track.Id, frame =>
        {
            vm.Position = frame;
            UpdatePosition();
            RequestPreview();
        }) { Owner = this };
        dialog.ShowDialog();
        Timeline.InvalidateVisual();
        RequestPreview();
    });
    private void MediaEffectsClick(object sender, RoutedEventArgs e) => OpenMediaEffects();
    private void MediaFxButtonClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: MediaItem item })
        {
            MediaList.SelectedItem = item;
            OpenMediaEffects(item.Asset.Id);
        }
    }
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
        vm.Library.FirstOrDefault(m => m.Asset.Id == asset.Id)?.Rebind(asset);
        RequestPreview();
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
        var item = SelectedLibraryItem ?? vm.Library.FirstOrDefault();
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
    private void ReverseEventClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var selected = vm.Project.Tracks.SelectMany(t => t.Clips.Where(c => vm.Selection.Contains(c.Id))).ToArray();
        if (selected.Length == 0 && SelectedClip is { } single) selected = [single];
        if (selected.Length == 0) { vm.Status = "Select an event to toggle reverse playback."; return; }
        Pause();
        var targetState = !selected.All(c => c.IsReversed);
        foreach (var c in selected) vm.Editor.SetClipReverse(c.Id, targetState);
        UpdateInspector();
        RequestPreview();
        Refresh();
        vm.Status = targetState ? "Reversed selected event(s)." : "Restored normal playback on selected event(s).";
    });
    private void MaintainAspectRatioClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var selected = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Video).SelectMany(t => t.Clips.Where(c => vm.Selection.Contains(c.Id))).ToArray();
        if (selected.Length == 0 && SelectedClip is { } single && vm.Project.Tracks.Any(t => t.Kind == TrackKind.Video && t.Clips.Contains(single))) selected = [single];
        if (selected.Length == 0) { vm.Status = "Select a video event to toggle maintain aspect ratio."; return; }
        Pause();
        var targetState = !selected.All(c => c.MaintainAspectRatio);
        foreach (var c in selected) vm.Editor.SetClipMaintainAspectRatio(c.Id, targetState);
        UpdateInspector();
        RequestPreview();
        Refresh();
        vm.Status = targetState ? "Enabled maintain aspect ratio." : "Disabled maintain aspect ratio.";
    });
    private void ReduceInterlaceClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var selected = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Video).SelectMany(t => t.Clips.Where(c => vm.Selection.Contains(c.Id))).ToArray();
        if (selected.Length == 0 && SelectedClip is { } single && vm.Project.Tracks.Any(t => t.Kind == TrackKind.Video && t.Clips.Contains(single))) selected = [single];
        if (selected.Length == 0) { vm.Status = "Select a video event to toggle reduce interlace flicker."; return; }
        Pause();
        var targetState = !selected.All(c => c.ReduceInterlace);
        foreach (var c in selected) vm.Editor.SetClipReduceInterlace(c.Id, targetState);
        UpdateInspector();
        RequestPreview();
        Refresh();
        vm.Status = targetState ? "Enabled reduce interlace flicker." : "Disabled reduce interlace flicker.";
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

    private void NextTakeClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (Timeline.Selection.Count == 0) return;
        Pause();
        vm.Editor.NextTakeOnSelection(Timeline.Selection);
        UpdateInspector();
        RequestPreview();
        Refresh();
    });

    private void PreviousTakeClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (Timeline.Selection.Count == 0) return;
        Pause();
        vm.Editor.PreviousTakeOnSelection(Timeline.Selection);
        UpdateInspector();
        RequestPreview();
        Refresh();
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
    private void InsertMarkerClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        var existing = vm.Project.Markers.FirstOrDefault(m => Math.Abs(m.Frame - vm.Position) <= 2);
        if (existing is not null)
        {
            OpenMarkerEdit(existing);
            return;
        }
        var marker = vm.Editor.AddMarker(vm.Position);
        Timeline.InvalidateVisual();
        vm.Status = $"Added Marker {marker.Index} at {TimelineView.FormatTimecode(marker.Frame, vm.Project.FrameRate)}.";
    });

    private void OpenMarkerEdit(TimelineMarker marker) => Guard(() =>
    {
        Pause();
        var dlg = new MarkerEditDialog(marker, vm.Project.FrameRate) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            vm.Editor.SetMarkerLabel(marker.Id, dlg.ResultLabel);
            vm.Editor.Execute(p =>
            {
                var m = p.Markers.FirstOrDefault(x => x.Id == marker.Id);
                if (m != null) m.ColorId = dlg.ResultColorId;
            });
            Timeline.InvalidateVisual();
            vm.Status = $"Updated Marker {marker.Index}.";
        }
    });

    private void ProjectNotesClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        new ProjectNotesWindow(vm, () =>
        {
            UpdatePosition();
            RequestPreview();
            Timeline.InvalidateVisual();
        }) { Owner = this }.ShowDialog();
    });

    private void SceneDetectionClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        if (new SceneDetectionWindow(vm) { Owner = this }.ShowDialog() == true)
        {
            Refresh();
            RequestPreview();
        }
    });
    private void CreateSlideshowClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        var dlg = new SlideshowWindow(vm, () =>
        {
            Refresh();
            RequestPreview();
            Timeline.InvalidateVisual();
        }, this);
        dlg.ShowDialog();
    });

    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void SelectAllClick(object sender, RoutedEventArgs e) { vm.Selection.Clear(); vm.Selection.UnionWith(vm.Project.Tracks.SelectMany(t => t.Clips).Select(c => c.Id)); Timeline.Selection.Clear(); Timeline.Selection.UnionWith(vm.Selection); Timeline.InvalidateVisual(); UpdateInspector(); CutButton.IsEnabled = CopyButton.IsEnabled = vm.Selection.Count > 0; }
    private void GroupCreateClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        vm.Editor.LinkClips(vm.Selection);
        Timeline.InvalidateVisual();
        vm.Status = "Created new group from selected clips.";
    });
    private void GroupRemoveClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        vm.Editor.RemoveFromGroup(vm.Selection);
        Timeline.InvalidateVisual();
        vm.Status = "Removed selected clips from group.";
    });
    private void GroupClearClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Pause();
        vm.Editor.ClearGroup(vm.Selection);
        Timeline.InvalidateVisual();
        vm.Status = "Cleared clip group(s).";
    });
    private void GroupSelectAllClick(object sender, RoutedEventArgs e)
    {
        var groupIds = vm.Project.Tracks.SelectMany(t => t.Clips)
            .Where(c => vm.Selection.Contains(c.Id) && c.GroupId.HasValue)
            .Select(c => c.GroupId!.Value)
            .ToHashSet();
        if (groupIds.Count == 0) return;
        var allInGroups = vm.Project.Tracks.SelectMany(t => t.Clips)
            .Where(c => c.GroupId.HasValue && groupIds.Contains(c.GroupId.Value))
            .Select(c => c.Id);
        vm.Selection.UnionWith(allInGroups);
        Timeline.Selection.Clear();
        Timeline.Selection.UnionWith(vm.Selection);
        Timeline.InvalidateVisual();
        UpdateInspector();
    }
    private void GroupSelectAllInClipGroup(Guid clipId)
    {
        var clip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (clip is null || !clip.GroupId.HasValue) return;
        var allInGroup = vm.Project.Tracks.SelectMany(t => t.Clips)
            .Where(c => c.GroupId == clip.GroupId.Value)
            .Select(c => c.Id);
        vm.Selection.Clear();
        vm.Selection.UnionWith(allInGroup);
        Timeline.Selection.Clear();
        Timeline.Selection.UnionWith(vm.Selection);
        Timeline.InvalidateVisual();
        UpdateInspector();
    }
    private void LinkClick(object sender, RoutedEventArgs e) => GroupCreateClick(sender, e);
    private void UnlinkClick(object sender, RoutedEventArgs e) => GroupRemoveClick(sender, e);
    private void IgnoreGroupsClick(object sender, RoutedEventArgs e)
    {
        vm.IgnoreGroups = sender == IgnoreGroupsToggle ? (IgnoreGroupsToggle.IsChecked == true) : (IgnoreGroupsMenu.IsChecked = !IgnoreGroupsMenu.IsChecked);
        if (IgnoreGroupsToggle is not null) IgnoreGroupsToggle.IsChecked = vm.IgnoreGroups;
        if (IgnoreGroupsMenu is not null) IgnoreGroupsMenu.IsChecked = vm.IgnoreGroups;
        Timeline.IgnoreGroups = vm.IgnoreGroups;
        vm.Status = vm.IgnoreGroups ? "Ignore event grouping enabled." : "Ignore event grouping disabled.";
    }
    private void VideoTrackClick(object sender, RoutedEventArgs e) => Guard(() => vm.Editor.Execute(p => p.Tracks.Insert(0, new TimelineTrack
    { Kind = TrackKind.Video, Name = $"Video {p.Tracks.Count(t => t.Kind == TrackKind.Video) + 1}" })));
    private void AudioTrackClick(object sender, RoutedEventArgs e) => Guard(() => vm.Editor.AddTrack(TrackKind.Audio));
    private void SnapClick(object sender, RoutedEventArgs e) { Timeline.Snapping = SnapToggle.IsChecked == true; SnappingMenu.IsChecked = Timeline.Snapping; }
    private void SnapMenuClick(object sender, RoutedEventArgs e) { SnapToggle.IsChecked = SnappingMenu.IsChecked; SnapClick(sender, e); }
    private void RippleClick(object sender, RoutedEventArgs e)
    {
        vm.Ripple = RippleToggle.IsChecked == true;
        if (RippleMenu is not null) RippleMenu.IsChecked = vm.Ripple;
        Timeline.Ripple = vm.Ripple;
        UpdateRippleTooltip();
        vm.Status = vm.Ripple ? $"Auto Ripple enabled ({GetRippleModeDescription(vm.RippleMode)})." : "Auto Ripple disabled.";
    }
    private void RippleMenuClick(object sender, RoutedEventArgs e)
    {
        if (RippleToggle is not null) RippleToggle.IsChecked = RippleMenu.IsChecked;
        RippleClick(sender, e);
    }
    private void RippleAffectedTracksClick(object sender, RoutedEventArgs e) => SetRippleMode(AutoRippleMode.AffectedTracks);
    private void RippleAffectedTracksMarkersClick(object sender, RoutedEventArgs e) => SetRippleMode(AutoRippleMode.AffectedTracksAndMarkers);
    private void RippleAllTracksMarkersClick(object sender, RoutedEventArgs e) => SetRippleMode(AutoRippleMode.AllTracksAndMarkers);
    private void SetRippleMode(AutoRippleMode mode)
    {
        vm.RippleMode = mode;
        Timeline.RippleMode = mode;
        if (RippleAffectedTracksMenu is not null) RippleAffectedTracksMenu.IsChecked = mode == AutoRippleMode.AffectedTracks;
        if (RippleAffectedTracksMarkersMenu is not null) RippleAffectedTracksMarkersMenu.IsChecked = mode == AutoRippleMode.AffectedTracksAndMarkers;
        if (RippleAllTracksMarkersMenu is not null) RippleAllTracksMarkersMenu.IsChecked = mode == AutoRippleMode.AllTracksAndMarkers;
        if (RippleToggleAffectedTracks is not null) RippleToggleAffectedTracks.IsChecked = mode == AutoRippleMode.AffectedTracks;
        if (RippleToggleAffectedTracksMarkers is not null) RippleToggleAffectedTracksMarkers.IsChecked = mode == AutoRippleMode.AffectedTracksAndMarkers;
        if (RippleToggleAllTracksMarkers is not null) RippleToggleAllTracksMarkers.IsChecked = mode == AutoRippleMode.AllTracksAndMarkers;
        UpdateRippleTooltip();
        vm.Status = $"Auto Ripple mode: {GetRippleModeDescription(mode)}.";
    }
    private static string GetRippleModeDescription(AutoRippleMode mode) => mode switch
    {
        AutoRippleMode.AffectedTracks => "Affected Tracks",
        AutoRippleMode.AffectedTracksAndMarkers => "Affected Tracks, Bus Tracks, Markers",
        AutoRippleMode.AllTracksAndMarkers => "All Tracks, Markers, Regions",
        _ => mode.ToString()
    };
    private void UpdateRippleTooltip()
    {
        if (RippleToggle is not null)
            RippleToggle.ToolTip = $"Auto Ripple (Ctrl+L) - {GetRippleModeDescription(vm.RippleMode)}";
    }
    private void LoopPlaybackClick(object sender, RoutedEventArgs e)
    {
        loopPlayback = !loopPlayback;
        if (LoopPlaybackToggle is not null) LoopPlaybackToggle.IsChecked = loopPlayback;
        if (LoopPlaybackMenu is not null) LoopPlaybackMenu.IsChecked = loopPlayback;
        vm.Status = loopPlayback ? "Loop playback enabled." : "Loop playback disabled.";
    }
    private void AutoCrossfadeClick(object sender, RoutedEventArgs e)
    {
        autoCrossfade = !autoCrossfade;
        if (AutoCrossfadeToggle is not null) AutoCrossfadeToggle.IsChecked = autoCrossfade;
        if (AutoCrossfadeMenu is not null) AutoCrossfadeMenu.IsChecked = autoCrossfade;
        vm.Status = autoCrossfade ? "Automatic crossfades enabled." : "Automatic crossfades disabled.";
    }
    private void UpdateRecordTimeDisplay()
    {
        if (RecordTimeDisplay is null) return;
        try
        {
            var rootDir = System.IO.Path.GetPathRoot(System.IO.Directory.GetCurrentDirectory()) ?? "C:\\";
            var drive = new System.IO.DriveInfo(rootDir);
            if (drive.IsReady)
            {
                var freeSeconds = (double)drive.AvailableFreeSpace / 288000.0;
                var timeSpan = TimeSpan.FromSeconds(Math.Min(freeSeconds, 3599999));
                var totalHours = (int)timeSpan.TotalHours;
                RecordTimeDisplay.Text = $"Record Time (2 ch): {totalHours:00}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}:00";
                return;
            }
        }
        catch { }
        RecordTimeDisplay.Text = "Record Time (2 ch): 24:00:00:00";
    }
    private void ZoomInClick(object sender, RoutedEventArgs e) { Timeline.PixelsPerSecond = Math.Min(1200, Timeline.PixelsPerSecond * 1.25); Refresh(); }
    private void ZoomOutClick(object sender, RoutedEventArgs e) { Timeline.PixelsPerSecond = Math.Max(5, Timeline.PixelsPerSecond / 1.25); Refresh(); }
    private void FitClick(object sender, RoutedEventArgs e) { Timeline.Offset = 0; TimelineScroll.Value = 0; Timeline.PixelsPerSecond = Math.Clamp((Timeline.ActualWidth - TimelineView.HeaderWidth - 25) / Math.Max(1, vm.Project.FrameRate.Seconds(vm.Project.DurationFrames)), 5, 1200); Refresh(); }
    private void TimelineScrollChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (Timeline is null) return; Timeline.Offset = e.NewValue; Timeline.InvalidateVisual(); }
    private void QualityChanged(object sender, SelectionChangedEventArgs e) { if (initialized) { revision++; RequestPreview(); } }
    private void GuidesClick(object sender, RoutedEventArgs e)
    {
        previewOverlay = previewOverlay switch
        {
            PreviewOverlayMode.None => PreviewOverlayMode.SafeAreas,
            PreviewOverlayMode.SafeAreas => PreviewOverlayMode.RuleOfThirds,
            PreviewOverlayMode.RuleOfThirds => PreviewOverlayMode.Grid4x4,
            PreviewOverlayMode.Grid4x4 => PreviewOverlayMode.GoldenRatio,
            _ => PreviewOverlayMode.None
        };
        safeAreas = previewOverlay != PreviewOverlayMode.None;
        var label = previewOverlay switch
        {
            PreviewOverlayMode.SafeAreas => "Safe Areas (Action 90% / Title 80%)",
            PreviewOverlayMode.RuleOfThirds => "Rule of Thirds (3×3)",
            PreviewOverlayMode.Grid4x4 => "Grid (4×4)",
            PreviewOverlayMode.GoldenRatio => "Golden Ratio",
            _ => "Off"
        };
        vm.Status = $"Preview overlay: {label}.";
        if (PreviewOverlayButton is not null) PreviewOverlayButton.ToolTip = $"Preview overlay: {label}";
        revision++;
        RequestPreview();
    }
    private void SplitScreenClick(object sender, RoutedEventArgs e)
    {
        splitScreenEnabled = !splitScreenEnabled;
        UpdateSplitScreenButtonVisual();
        vm.Status = splitScreenEnabled ? $"Split Screen View: {splitScreenDivision} ({splitScreenSource})" : "Split Screen View: Off";
        revision++;
        RequestPreview();
    }

    private void BypassFxClick(object sender, RoutedEventArgs e)
    {
        bypassAllFx = !bypassAllFx;
        if (BypassFxButton is not null)
        {
            BypassFxButton.Background = bypassAllFx ? (Brush)new BrushConverter().ConvertFrom("#2B6CB0")! : Brushes.Transparent;
        }
        vm.Status = bypassAllFx ? "All Video FX: Bypassed" : "All Video FX: Enabled";
        revision++;
        RequestPreview();
    }

    private void UpdateSplitScreenButtonVisual()
    {
        if (SplitScreenButton is not null)
        {
            SplitScreenButton.Background = splitScreenEnabled ? (Brush)new BrushConverter().ConvertFrom("#2B6CB0")! : Brushes.Transparent;
        }
    }

    private void SplitScreenMenuClick(object sender, RoutedEventArgs e) => OpenSplitScreenContextMenu(sender as FrameworkElement ?? SplitScreenButton);
    private void SplitScreenRightClick(object sender, MouseButtonEventArgs e) => OpenSplitScreenContextMenu(sender as FrameworkElement ?? SplitScreenButton);

    private void OpenSplitScreenContextMenu(FrameworkElement target)
    {
        var menu = new ContextMenu();
        var fxItem = new MenuItem { Header = "FX Bypassed", IsCheckable = true, IsChecked = splitScreenSource == SplitScreenSource.FxBypassed };
        fxItem.Click += (_, _) => { splitScreenSource = SplitScreenSource.FxBypassed; if (!splitScreenEnabled) splitScreenEnabled = true; UpdateSplitScreenButtonVisual(); revision++; RequestPreview(); };
        menu.Items.Add(fxItem);

        var clipItem = new MenuItem { Header = "Clipboard", IsCheckable = true, IsChecked = splitScreenSource == SplitScreenSource.Clipboard };
        clipItem.Click += (_, _) => { splitScreenSource = SplitScreenSource.Clipboard; if (!splitScreenEnabled) splitScreenEnabled = true; UpdateSplitScreenButtonVisual(); revision++; RequestPreview(); };
        menu.Items.Add(clipItem);

        menu.Items.Add(new Separator());

        var leftItem = new MenuItem { Header = "Select Left Half", IsCheckable = true, IsChecked = splitScreenDivision == SplitScreenDivision.LeftHalf };
        leftItem.Click += (_, _) => { splitScreenDivision = SplitScreenDivision.LeftHalf; if (!splitScreenEnabled) splitScreenEnabled = true; UpdateSplitScreenButtonVisual(); revision++; RequestPreview(); };
        menu.Items.Add(leftItem);

        var rightItem = new MenuItem { Header = "Select Right Half", IsCheckable = true, IsChecked = splitScreenDivision == SplitScreenDivision.RightHalf };
        rightItem.Click += (_, _) => { splitScreenDivision = SplitScreenDivision.RightHalf; if (!splitScreenEnabled) splitScreenEnabled = true; UpdateSplitScreenButtonVisual(); revision++; RequestPreview(); };
        menu.Items.Add(rightItem);

        var allItem = new MenuItem { Header = "Select All", IsCheckable = true, IsChecked = splitScreenDivision == SplitScreenDivision.All };
        allItem.Click += (_, _) => { splitScreenDivision = SplitScreenDivision.All; if (!splitScreenEnabled) splitScreenEnabled = true; UpdateSplitScreenButtonVisual(); revision++; RequestPreview(); };
        menu.Items.Add(allItem);

        menu.Items.Add(new Separator());

        var retainItem = new MenuItem { Header = "Retain Split Screen Setting", IsCheckable = true, IsChecked = retainSplitScreenSetting };
        retainItem.Click += (_, _) => { retainSplitScreenSetting = !retainSplitScreenSetting; retainItem.IsChecked = retainSplitScreenSetting; };
        menu.Items.Add(retainItem);

        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private static byte[] RescaleFrame(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        if (srcW == dstW && srcH == dstH && src.Length == dstW * dstH * 4) return src;
        if (srcW <= 0 || srcH <= 0 || dstW <= 0 || dstH <= 0) return new byte[dstW * dstH * 4];
        var dst = new byte[checked(dstW * dstH * 4)];
        for (var y = 0; y < dstH; y++)
        {
            var sy = Math.Clamp(y * srcH / dstH, 0, srcH - 1);
            for (var x = 0; x < dstW; x++)
            {
                var sx = Math.Clamp(x * srcW / dstW, 0, srcW - 1);
                var srcIdx = (sy * srcW + sx) * 4;
                var dstIdx = (y * dstW + x) * 4;
                if (srcIdx + 4 <= src.Length && dstIdx + 4 <= dst.Length)
                {
                    Buffer.BlockCopy(src, srcIdx, dst, dstIdx, 4);
                }
            }
        }
        return dst;
    }
    private void PreviewOverlayRightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        var modes = new[]
        {
            (PreviewOverlayMode.None, "Off"),
            (PreviewOverlayMode.SafeAreas, "Safe Areas (Action 90% / Title 80%)"),
            (PreviewOverlayMode.RuleOfThirds, "Rule of Thirds (3×3)"),
            (PreviewOverlayMode.Grid4x4, "Grid (4×4)"),
            (PreviewOverlayMode.GoldenRatio, "Golden Ratio")
        };
        foreach (var (m, label) in modes)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = previewOverlay == m };
            var targetMode = m;
            item.Click += (_, _) =>
            {
                previewOverlay = targetMode;
                safeAreas = previewOverlay != PreviewOverlayMode.None;
                vm.Status = $"Preview overlay: {label}.";
                if (PreviewOverlayButton is not null) PreviewOverlayButton.ToolTip = $"Preview overlay: {label}";
                revision++;
                RequestPreview();
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
        e.Handled = true;
    }
    private async Task<BitmapSource> CaptureFrameAsync()
    {
        var project = vm.Project.DeepClone(); var frame = vm.Position;
        var pixels = await Task.Run(() => vm.Renderer.RenderFrame(project, frame, project.Width, project.Height));
        var image = BitmapSource.Create(project.Width, project.Height, 96, 96, PixelFormats.Bgra32, null, pixels, project.Width * 4);
        image.Freeze(); return image;
    }
    private async void CopySnapshotClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var frame = await CaptureFrameAsync();
        Clipboard.SetImage(frame);
        if (currentPreviewPixels != null)
        {
            clipboardSnapshotPixels = (byte[])currentPreviewPixels.Clone();
            clipboardSnapshotWidth = previewWidth;
            clipboardSnapshotHeight = previewHeight;
        }
        vm.Status = "Copied snapshot to clipboard.";
        if (splitScreenEnabled && splitScreenSource == SplitScreenSource.Clipboard)
        {
            revision++;
            RequestPreview();
        }
    });
    private async void SaveSnapshotClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        var baseName = Path.GetFileNameWithoutExtension(string.IsNullOrWhiteSpace(vm.Project.Name) ? "Snapshot" : vm.Project.Name);
        var defaultName = $"{baseName}_Frame{vm.Position:D6}.png";
        var dialog = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg;*.jpeg)|*.jpg;*.jpeg|TIFF Image (*.tif;*.tiff)|*.tif;*.tiff",
            DefaultExt = ".png",
            FileName = defaultName
        };
        if (dialog.ShowDialog(this) != true) return;
        if (vm.Project.Media.Any(m => string.Equals(Path.GetFullPath(m.Path), Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose a snapshot destination different from the original media.");
        var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        BitmapEncoder encoder = ext switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".tif" or ".tiff" => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
            _ => new PngBitmapEncoder()
        };
        encoder.Frames.Add(BitmapFrame.Create(await CaptureFrameAsync()));
        var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(temporary)) encoder.Save(file);
            File.Move(temporary, dialog.FileName, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        vm.Status = "Saved " + Path.GetFileName(dialog.FileName) + ".";
    });
    private void MixingConsoleClick(object sender, RoutedEventArgs e)
    {
        if (mixingConsoleWindow == null || !mixingConsoleWindow.IsLoaded)
        {
            mixingConsoleWindow = new MixingConsoleWindow(vm, () => { Refresh(); RequestPreview(); })
            {
                Owner = this
            };
            mixingConsoleWindow.Show();
        }
        else
        {
            mixingConsoleWindow.Activate();
            mixingConsoleWindow.RefreshTracks();
        }
    }
    private void TrimmerClick(object sender, RoutedEventArgs e)
    {
        PreviewDeckTabs.SelectedItem = TrimmerTab;
        TrimmerDeck.Focus();
    }

    private void OpenInTrimmerClick(object sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItem is MediaItem item)
        {
            PreviewDeckTabs.SelectedItem = TrimmerTab;
            TrimmerDeck.LoadMedia(item.Asset);
            TrimmerDeck.Focus();
        }
    }

    private void VideoScopesClick(object sender, RoutedEventArgs e)
    {
        if (videoScopesWindow == null || !videoScopesWindow.IsLoaded)
        {
            videoScopesWindow = new VideoScopesWindow(this) { Owner = this };
            videoScopesWindow.Show();
        }
        else
        {
            videoScopesWindow.Activate();
        }
    }

    private void EditDetailsClick(object sender, RoutedEventArgs e)
    {
        if (editDetailsWindow == null || !editDetailsWindow.IsLoaded)
        {
            editDetailsWindow = new EditDetailsWindow(vm, () => { Refresh(); RequestPreview(); }, this);
            editDetailsWindow.Show();
        }
        else
        {
            editDetailsWindow.Activate();
            editDetailsWindow.RefreshGrid();
        }
    }

    private void ToggleTrackVolumeEnvelopeClick(object sender, RoutedEventArgs e)
    {
        var trackIdx = Timeline.ActiveTrackIndex;
        if (trackIdx < 0 || trackIdx >= vm.Project.Tracks.Count) return;
        var track = vm.Project.Tracks[trackIdx];
        var type = track.Kind == TrackKind.Video ? EnvelopeType.Composite : EnvelopeType.Volume;
        Guard(() =>
        {
            Pause();
            vm.Editor.ToggleTrackEnvelope(track.Id, type);
            vm.Status = $"{(vm.Editor.HasTrackEnvelope(track.Id, type) ? "Added" : "Removed")} Track {type} envelope.";
            Refresh();
            RequestPreview();
        });
    }

    private void ToggleTrackPanEnvelopeClick(object sender, RoutedEventArgs e)
    {
        var trackIdx = Timeline.ActiveTrackIndex;
        if (trackIdx < 0 || trackIdx >= vm.Project.Tracks.Count) return;
        var track = vm.Project.Tracks[trackIdx];
        if (track.Kind != TrackKind.Audio)
        {
            vm.Status = "Pan envelope is only available on audio tracks.";
            return;
        }
        Guard(() =>
        {
            Pause();
            vm.Editor.ToggleTrackEnvelope(track.Id, EnvelopeType.Pan);
            vm.Status = $"{(vm.Editor.HasTrackEnvelope(track.Id, EnvelopeType.Pan) ? "Added" : "Removed")} Track Pan envelope.";
            Refresh();
            RequestPreview();
        });
    }

    private void ToggleTrackMuteEnvelopeClick(object sender, RoutedEventArgs e)
    {
        var trackIdx = Timeline.ActiveTrackIndex;
        if (trackIdx < 0 || trackIdx >= vm.Project.Tracks.Count) return;
        var track = vm.Project.Tracks[trackIdx];
        Guard(() =>
        {
            Pause();
            vm.Editor.ToggleTrackEnvelope(track.Id, EnvelopeType.Mute);
            vm.Status = $"{(vm.Editor.HasTrackEnvelope(track.Id, EnvelopeType.Mute) ? "Added" : "Removed")} Track Mute envelope.";
            Refresh();
            RequestPreview();
        });
    }

    private void ToggleTrackFadeToColorEnvelopeClick(object sender, RoutedEventArgs e)
    {
        var trackIdx = Timeline.ActiveTrackIndex;
        if (trackIdx < 0 || trackIdx >= vm.Project.Tracks.Count) return;
        var track = vm.Project.Tracks[trackIdx];
        if (track.Kind != TrackKind.Video)
        {
            vm.Status = "Fade to Color envelope is only available on video tracks.";
            return;
        }
        Guard(() =>
        {
            Pause();
            vm.Editor.ToggleTrackEnvelope(track.Id, EnvelopeType.FadeToColor);
            vm.Status = $"{(vm.Editor.HasTrackEnvelope(track.Id, EnvelopeType.FadeToColor) ? "Added" : "Removed")} Track Fade to Color envelope.";
            Refresh();
            RequestPreview();
        });
    }
    private void ResetWorkspaceClick(object sender, RoutedEventArgs e) { DefaultLayoutClick(sender, e); Timeline.PixelsPerSecond = 13.4; Timeline.Offset = 0; Refresh(); }
    private void SettingsClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var dialog = new ProjectSettingsWindow(vm.Project) { Owner = this };
        dialog.Applied += ApplyProjectSettings;
        if (dialog.ShowDialog() == true)
        {
            ApplyProjectSettings(dialog);
            vm.Status = "Project settings updated.";
        }
    });

    private void ApplyProjectSettings(ProjectSettingsWindow dialog)
    {
        Pause();
        vm.Editor.Execute(p =>
        {
            p.Width = dialog.ProjectWidth;
            p.Height = dialog.ProjectHeight;
            p.FrameRate = dialog.Rate;
            p.Name = dialog.ProjectName;
            p.FieldOrder = dialog.FieldOrder;
            p.PixelAspectRatio = dialog.PixelAspectRatio;
            p.RenderQuality = dialog.RenderQuality;
            p.MotionBlur = dialog.MotionBlur;
            p.DeinterlaceMethod = dialog.DeinterlaceMethod;
            p.PixelFormat = dialog.PixelFormat;
            p.AudioSampleRate = dialog.AudioSampleRate;
            p.AudioBitDepth = dialog.AudioBitDepth;
            p.PrerenderFolder = dialog.PrerenderFolder;
            p.RecordedFilesFolder = dialog.RecordedFilesFolder;
            p.RulerFormat = dialog.RulerFormat;
            p.RulerTimeFormat = dialog.RulerTimeFormat;
            p.RulerStartTimeFrames = dialog.RulerStartTimeFrames;
            p.BeatsPerMinute = dialog.BeatsPerMinute;
            p.BeatsPerMeasure = dialog.BeatsPerMeasure;
            p.Author = dialog.Author;
            p.Engineer = dialog.Engineer;
            p.Copyright = dialog.Copyright;
            p.Comments = dialog.Comments;
            p.UniversalAlbumId = dialog.UniversalAlbumId;
        });
        if (dialog.StartWithSettingsChecked)
        {
            SaveDefaultProjectSettings(vm.Project);
        }
        Refresh();
    }
    private void ShortcutsClick(object sender, RoutedEventArgs e) => MessageBox.Show(this, "Ctrl+I  Import media\nCtrl+O / Ctrl+S  Open / save project\nCtrl+Shift+E  Render timeline\nSpace  Play / pause\nEnter  Pause\nEsc  Stop\nAlt+Left / Alt+Right  Step one frame\nCtrl+Home / Ctrl+End  Timeline bounds\nS  Split selected clips at cursor\nDelete  Delete selected clips\nCtrl+Z / Ctrl+Y  Undo / redo\nCtrl+A  Select all clips\nN  Toggle snapping\nCtrl+wheel  Zoom timeline\nShift+wheel  Scroll time\nWheel  Scroll tracks\nDrag clip edges  Trim\nCtrl+click  Add to selection", "Keyboard shortcuts");
    private void AboutClick(object sender, RoutedEventArgs e) => MessageBox.Show(this, "OpenRend 0.1\nWindows video and audio editor\nGPL-3.0-or-later\n\nUses FFmpeg, CommunityToolkit.Mvvm, NAudio and Microsoft.Extensions.DependencyInjection.\nSee THIRD-PARTY-NOTICES.md in the distribution.", "About OpenRend");
    private void ExitClick(object sender, RoutedEventArgs e) => Close();
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (vm.Busy) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control); var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift); var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.FocusedElement is TextBox && !(ctrl && key is Key.S or Key.O or Key.I or Key.N || ctrl && shift && key is Key.E or Key.M or Key.L)) return;
        if (HandleLayoutChordKey(e)) return;
        if (alt && !ctrl && !shift && key == Key.D)
        {
            isAltDChordPending = true;
            layoutChordTimer?.Stop();
            layoutChordTimer?.Start();
            vm.Status = "Alt+D pressed. Press D (Default), A (Audio), C (Color), S (Source), T (Timeline Top), R (Reload), W (Save), or 0–9...";
            e.Handled = true;
            return;
        }
        if (key == Key.D && !alt)
        {
            if (ctrl && !shift)
            {
                SetEditTool(EditToolMode.Normal);
                e.Handled = true;
                return;
            }
            if (!ctrl && shift)
            {
                CyclePreviousEditTool();
                e.Handled = true;
                return;
            }
            if (!ctrl && !shift)
            {
                CycleNextEditTool();
                e.Handled = true;
                return;
            }
        }
        Action? action = key switch
        {
            Key.B when shift && alt => () => SplitScreenClick(this, e),
            Key.I when ctrl => () => ImportClick(this, e),
            Key.O when ctrl => () => OpenClick(this, e),
            Key.S when ctrl && alt => () => SaveLayoutClick(this, e),
            Key.D0 when ctrl && alt => () => ActivateLayoutSlot(0),
            Key.NumPad0 when ctrl && alt => () => ActivateLayoutSlot(0),
            Key.D1 when ctrl && alt => () => ActivateLayoutSlot(1),
            Key.NumPad1 when ctrl && alt => () => ActivateLayoutSlot(1),
            Key.D3 when ctrl && alt => () => ActivateLayoutSlot(3),
            Key.NumPad3 when ctrl && alt => () => ActivateLayoutSlot(3),
            Key.D6 when ctrl && alt => () => ActivateLayoutSlot(6),
            Key.NumPad6 when ctrl && alt => () => ActivateLayoutSlot(6),
            Key.D7 when ctrl && alt => () => ActivateLayoutSlot(7),
            Key.NumPad7 when ctrl && alt => () => ActivateLayoutSlot(7),
            Key.D8 when ctrl && alt => () => ActivateLayoutSlot(8),
            Key.NumPad8 when ctrl && alt => () => ActivateLayoutSlot(8),
            Key.D9 when ctrl && alt => () => ActivateLayoutSlot(9),
            Key.NumPad9 when ctrl && alt => () => ActivateLayoutSlot(9),
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
            Key.T when !ctrl && !alt && !shift => () => NextTakeClick(this, e),
            Key.T when !ctrl && !alt && shift => () => PreviousTakeClick(this, e),
            Key.N when ctrl && shift => () => ProjectNotesClick(this, e),
            Key.M when !ctrl && !shift && !alt => () => InsertMarkerClick(this, e),
            Key.L when ctrl && shift => () => LockEventClick(this, e),
            Key.M when ctrl && shift => () => MuteAudioClipClick(this, e),
            Key.P when ctrl && shift => () => AiTextToSpeechClick(this, e),
            Key.G when ctrl && shift => () => ColorGradingClick(this, e),
            Key.D2 when !ctrl && alt && !shift => () => TrimmerClick(this, e),
            Key.NumPad2 when !ctrl && alt && !shift => () => TrimmerClick(this, e),
            Key.D2 when ctrl && alt => () => VideoScopesClick(this, e),
            Key.NumPad2 when ctrl && alt => () => VideoScopesClick(this, e),
            Key.D4 when ctrl && alt => () => EditDetailsClick(this, e),
            Key.NumPad4 when ctrl && alt => () => EditDetailsClick(this, e),
            Key.D4 when !ctrl && alt && !shift => () => EditDetailsClick(this, e),
            Key.NumPad4 when !ctrl && alt && !shift => () => EditDetailsClick(this, e),
            Key.V when !ctrl && !alt && shift => () => ToggleTrackVolumeEnvelopeClick(this, e),
            Key.P when !ctrl && !alt && shift => () => ToggleTrackPanEnvelopeClick(this, e),
            Key.D5 when ctrl && alt => () => MixingConsoleClick(this, e),
            Key.NumPad5 when ctrl && alt => () => MixingConsoleClick(this, e),
            Key.L when ctrl && !shift && !alt => () => { RippleToggle.IsChecked = RippleToggle.IsChecked != true; RippleClick(this, e); },
            Key.F when !ctrl && !shift && !alt => () => SetRippleMode(AutoRippleMode.AffectedTracks),
            Key.F when ctrl && shift && !alt => () => SetRippleMode(AutoRippleMode.AffectedTracksAndMarkers),
            Key.F when ctrl && !shift && alt => () => SetRippleMode(AutoRippleMode.AllTracksAndMarkers),
            Key.U when ctrl && shift => () => { IgnoreGroupsClick(IgnoreGroupsToggle ?? (object)this, e); },
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
            Key.G when !ctrl && !shift && !alt => () => GroupCreateClick(this, e),
            Key.U when !ctrl && !shift && !alt => () => GroupRemoveClick(this, e),
            Key.N => () => { SnapToggle.IsChecked = SnapToggle.IsChecked != true; SnapClick(this, e); },
            Key.Q when !ctrl && !alt && !shift => () => LoopPlaybackClick(this, e),
            Key.X when !ctrl && !alt && !shift => () => AutoCrossfadeClick(this, e),
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
            Pause(); autosave.Stop(); SaveWorkspace();
            SessionRecoveryManager.RecordCleanExit(Path.GetDirectoryName(recoveryPath)!);
            allowClose = true;
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
