using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

public readonly record struct MotionTrackDragPayload(Guid SourceClipId, Guid RegionId);

public partial class MotionTrackWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly Guid clipId;
    private readonly Action? onPreviewRequest;

    private TimelineClip? currentClip;
    private MotionTrackData? selectedRegion;
    private CancellationTokenSource? trackingCts;

    private readonly Rectangle[] rectProgress = new Rectangle[100];
    private bool isValidated;
    private bool isTracking;

    internal readonly struct GeomModelUi
    {
        internal TrackingModel Model { get; }

        internal GeomModelUi(TrackingModel model)
        {
            Model = model;
        }

        public override string ToString() => Model switch
        {
            TrackingModel.Translation => "Location",
            TrackingModel.RigidMotion => "Rigid",
            TrackingModel.Similarity => "Similarity",
            TrackingModel.Affine => "Affine",
            TrackingModel.Homography => "Homography",
            _ => Model.ToString()
        };
    }

    internal readonly struct TrackerUi
    {
        internal TrackingPrecision Precision { get; }

        internal TrackerUi(TrackingPrecision precision)
        {
            Precision = precision;
        }

        public override string ToString() => Precision switch
        {
            TrackingPrecision.Fast => "Fast processing",
            TrackingPrecision.Medium => "Balanced",
            TrackingPrecision.High => "High precision",
            _ => Precision.ToString()
        };
    }

    public MotionTrackWindow(EditorViewModel vm, Guid clipId, Action? onPreviewRequest = null)
    {
        this.vm = vm ?? throw new ArgumentNullException(nameof(vm));
        this.clipId = clipId;
        this.onPreviewRequest = onPreviewRequest;

        InitializeComponent();

        // Initialize 100-segment progress bar matching Vegas Pro
        for (int i = 0; i < 100; i++)
        {
            rectProgress[i] = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
                MinWidth = 1.0,
                Margin = new Thickness(0.0)
            };
            gProgress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            gProgress.Children.Add(rectProgress[i]);
            Grid.SetColumn(rectProgress[i], i);
        }

        // Setup Precision options (SAD, Hybrid, LK)
        cbPrecision.ItemsSource = new[]
        {
            new TrackerUi(TrackingPrecision.Fast),
            new TrackerUi(TrackingPrecision.Medium),
            new TrackerUi(TrackingPrecision.High)
        };
        cbPrecision.SelectedIndex = 1;

        // Setup Mode options (Translation, Rigid, Similarity, Affine, Homography)
        cbMode.ItemsSource = new[]
        {
            new GeomModelUi(TrackingModel.Translation),
            new GeomModelUi(TrackingModel.RigidMotion),
            new GeomModelUi(TrackingModel.Similarity),
            new GeomModelUi(TrackingModel.Affine),
            new GeomModelUi(TrackingModel.Homography)
        };
        cbMode.SelectedIndex = 4;

        LoadClipAndRegions();
    }

    private void RefreshClick(object sender, RoutedEventArgs e)
    {
        LoadClipAndRegions();
    }

    private void LoadClipAndRegions()
    {
        currentClip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (currentClip == null)
        {
            lClipName.Text = "No Clip Selected";
            IsEnabled = false;
            sbStatusText.Text = "No clip selected.";
            return;
        }

        IsEnabled = true;
        lClipName.Text = string.IsNullOrEmpty(currentClip.Name) ? "Selected Video Event" : currentClip.Name;

        if (currentClip.MotionTracks.Count == 0)
        {
            var defaultRegion = new MotionTrackData
            {
                Name = "Region 1",
                Shape = TrackingShape.Rectangle,
                Model = TrackingModel.Homography,
                Algorithm = TrackingAlgorithm.Hybrid,
                Precision = TrackingPrecision.Medium
            };
            vm.Editor.AddMotionTrack(clipId, defaultRegion);
            currentClip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        }

        isValidated = false;
        cbMasks.Items.Clear();
        if (currentClip != null)
        {
            foreach (var region in currentClip.MotionTracks)
            {
                cbMasks.Items.Add(region);
            }
        }

        cbMasks.DisplayMemberPath = nameof(MotionTrackData.Name);
        isValidated = true;

        if (cbMasks.Items.Count > 0)
        {
            cbMasks.SelectedIndex = 0;
        }

        PopulateAttachMenu();
        sbStatusText.Text = "Ready";
    }

    private void MaskSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!isValidated) return;

        selectedRegion = cbMasks.SelectedItem as MotionTrackData;
        if (selectedRegion == null)
        {
            bRemove.IsEnabled = false;
            chkMaskInvert.IsEnabled = false;
            chkShowFeatures.IsEnabled = false;
            bAttach.IsEnabled = false;
            bAttachDrag.IsEnabled = false;
            UpdateProgressBar(0, 100, FrameStatus.NotAnalyzed, setAllValues: true);
            return;
        }

        bRemove.IsEnabled = (currentClip?.MotionTracks.Count ?? 0) > 1;
        chkMaskInvert.IsEnabled = true;
        chkShowFeatures.IsEnabled = true;
        bAttach.IsEnabled = true;
        bAttachDrag.IsEnabled = true;

        chkMaskInvert.IsChecked = selectedRegion.IsInverted;

        // Select matching Mode
        for (int i = 0; i < cbMode.Items.Count; i++)
        {
            if (cbMode.Items[i] is GeomModelUi g && g.Model == selectedRegion.Model)
            {
                cbMode.SelectedIndex = i;
                break;
            }
        }

        // Select matching Precision
        for (int i = 0; i < cbPrecision.Items.Count; i++)
        {
            if (cbPrecision.Items[i] is TrackerUi t && t.Precision == selectedRegion.Precision)
            {
                cbPrecision.SelectedIndex = i;
                break;
            }
        }

        RefreshProgressBarFromKeyframes();
    }

    private void ToggleMaskInvert(object sender, RoutedEventArgs e)
    {
        if (selectedRegion == null) return;
        selectedRegion.IsInverted = chkMaskInvert.IsChecked == true;
        vm.Editor.UpdateMotionTrack(clipId, selectedRegion);
    }

    private void PrecisionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!isValidated || selectedRegion == null) return;
        if (cbPrecision.SelectedItem is TrackerUi tracker)
        {
            selectedRegion.Precision = tracker.Precision;
            selectedRegion.Algorithm = tracker.Precision switch
            {
                TrackingPrecision.Fast => TrackingAlgorithm.TemplateMatching,
                TrackingPrecision.High => TrackingAlgorithm.LucasKanade,
                _ => TrackingAlgorithm.Hybrid
            };
            vm.Editor.UpdateMotionTrack(clipId, selectedRegion);
        }
    }

    private void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!isValidated || selectedRegion == null) return;
        if (cbMode.SelectedItem is GeomModelUi geom)
        {
            selectedRegion.Model = geom.Model;
            vm.Editor.UpdateMotionTrack(clipId, selectedRegion);
        }
    }

    private void ShowMotionVectors(object sender, RoutedEventArgs e)
    {
        onPreviewRequest?.Invoke();
    }

    private void AddMask(object sender, RoutedEventArgs e)
    {
        if (currentClip == null) return;

        var newNum = currentClip.MotionTracks.Count + 1;
        var newRegion = new MotionTrackData
        {
            Name = $"Region {newNum}",
            Shape = TrackingShape.Rectangle,
            Model = TrackingModel.Homography,
            Algorithm = TrackingAlgorithm.Hybrid,
            Precision = TrackingPrecision.Medium
        };

        vm.Editor.AddMotionTrack(clipId, newRegion);
        LoadClipAndRegions();
        cbMasks.SelectedIndex = cbMasks.Items.Count - 1;
        sbStatusText.Text = $"Added {newRegion.Name}.";
    }

    private void RemoveMask(object sender, RoutedEventArgs e)
    {
        if (currentClip == null || selectedRegion == null || currentClip.MotionTracks.Count <= 1) return;

        var removingId = selectedRegion.Id;
        vm.Editor.RemoveMotionTrack(clipId, removingId);
        LoadClipAndRegions();
        sbStatusText.Text = "Removed region.";
    }

    private void UpdateProgressBar(long rangeStartFrame, long rangeEndFrame, FrameStatus status, bool setAllValues = false)
    {
        long num = Math.Max(1L, currentClip != null ? currentClip.DurationFrames : 100L);
        double num2 = 100.0 / (double)num;
        long startRel = currentClip != null ? rangeStartFrame - currentClip.StartFrame : rangeStartFrame;
        long endRel = currentClip != null ? rangeEndFrame - currentClip.StartFrame : rangeEndFrame;

        int num3 = (int)Math.Round((double)startRel * num2);
        if ((num3 < 0) || setAllValues) num3 = 0;
        int num4 = (int)Math.Round((double)endRel * num2);
        if ((num4 > 99) || setAllValues) num4 = 100;

        Brush brush = status switch
        {
            FrameStatus.NoMotionVectors => Brushes.Red,
            FrameStatus.LowPrecision => Brushes.Yellow,
            FrameStatus.HasMotionVectors or FrameStatus.ManuallyAdjusted => Brushes.LimeGreen,
            _ => (Brush)FindResource("ColorBG") ?? new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11))
        };

        for (int i = num3; i < num4 && i < 100; i++)
        {
            if (i >= 0 && rectProgress[i] != null)
            {
                rectProgress[i].Fill = brush;
            }
        }
    }

    private void RefreshProgressBarFromKeyframes()
    {
        UpdateProgressBar(0, 100, FrameStatus.NotAnalyzed, setAllValues: true);
        if (selectedRegion == null || selectedRegion.Keyframes.Count == 0 || currentClip == null) return;

        foreach (var kf in selectedRegion.Keyframes)
        {
            var status = kf.Confidence switch
            {
                < 0.3 => FrameStatus.NoMotionVectors,
                < 0.7 => FrameStatus.LowPrecision,
                _ => FrameStatus.HasMotionVectors
            };
            UpdateProgressBar(kf.ProjectFrame, kf.ProjectFrame + 1, status);
        }
    }

    private void seekOnProgressBar(object sender, MouseButtonEventArgs e)
    {
        if (currentClip == null || rectProgress == null) return;
        if (e.Source is Rectangle rectangle)
        {
            int index = Array.IndexOf(rectProgress, rectangle);
            if (index >= 0)
            {
                double pct = (index + 0.5) / 100.0;
                long targetFrame = currentClip.StartFrame + (long)(currentClip.DurationFrames * pct);
                vm.Position = Math.Clamp(targetFrame, currentClip.StartFrame, currentClip.EndFrame);
                onPreviewRequest?.Invoke();
                sbStatusText.Text = $"Frame {vm.Position}";
            }
        }
    }

    private void mouseCursor2Pointer(object sender, MouseEventArgs e)
    {
        Mouse.OverrideCursor = Cursors.Hand;
    }

    private void mouseNormalCursor(object sender, MouseEventArgs e)
    {
        Mouse.OverrideCursor = null;
    }

    private void KeyDownPress(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Z when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                vm.Editor.Undo();
                LoadClipAndRegions();
                e.Handled = true;
                break;
            case Key.Y when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                vm.Editor.Redo();
                LoadClipAndRegions();
                e.Handled = true;
                break;
            case Key.Left:
                if (vm.Position > 0)
                {
                    vm.Position--;
                    onPreviewRequest?.Invoke();
                    e.Handled = true;
                }
                break;
            case Key.Right:
                vm.Position++;
                onPreviewRequest?.Invoke();
                e.Handled = true;
                break;
        }
    }

    private void TrackForward(object sender, RoutedEventArgs e)
    {
        if (currentClip == null) return;
        _ = RunTrackingAsync(vm.Position, currentClip.EndFrame);
    }

    private void TrackNextFrame(object sender, RoutedEventArgs e)
    {
        if (currentClip == null) return;
        _ = RunTrackingAsync(vm.Position, Math.Min(currentClip.EndFrame, vm.Position + 1));
    }

    private void TrackPreviousFrame(object sender, RoutedEventArgs e)
    {
        if (currentClip == null) return;
        _ = RunTrackingAsync(vm.Position, Math.Max(currentClip.StartFrame, vm.Position - 1));
    }

    private void TrackBackward(object sender, RoutedEventArgs e)
    {
        if (currentClip == null) return;
        _ = RunTrackingAsync(vm.Position, currentClip.StartFrame);
    }

    private void TrackAll(object sender, RoutedEventArgs e)
    {
        if (currentClip == null) return;
        _ = RunTrackingAsync(currentClip.StartFrame, currentClip.EndFrame);
    }

    private async Task RunTrackingAsync(long startFrame, long endFrame)
    {
        if (currentClip == null || selectedRegion == null || isTracking) return;

        var asset = vm.Project.Media.FirstOrDefault(m => m.Id == currentClip.MediaId);
        if (asset == null || !File.Exists(asset.Path))
        {
            sbStatusText.Text = "Source media file not found.";
            return;
        }

        isTracking = true;
        bBackTrack.IsEnabled = false;
        bBackOne.IsEnabled = false;
        bTrackAll.IsEnabled = false;
        bForwardOne.IsEnabled = false;
        bForTrack.IsEnabled = false;
        sbStatusText.Text = "Tracking in progress...";

        trackingCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<TrackingProgress>(p =>
            {
                sbStatusText.Text = p.StatusText;
                if (p.LatestKeyframe is not null)
                {
                    var status = p.LatestKeyframe.Confidence switch
                    {
                        < 0.3 => FrameStatus.NoMotionVectors,
                        < 0.7 => FrameStatus.LowPrecision,
                        _ => FrameStatus.HasMotionVectors
                    };
                    UpdateProgressBar(p.CurrentFrame, p.CurrentFrame + 1, status);
                    vm.Position = p.CurrentFrame;
                    onPreviewRequest?.Invoke();
                }
            });

            byte[]? FrameDecoder(long projectFrame)
            {
                return vm.Renderer.RenderFrame(vm.Project, projectFrame, vm.Project.Width, vm.Project.Height);
            }

            var trackedKeyframes = await MotionTrackerEngine.TrackAsync(
                currentClip,
                selectedRegion,
                startFrame,
                endFrame,
                FrameDecoder,
                vm.Project.Width,
                vm.Project.Height,
                progress,
                trackingCts.Token
            );

            vm.Editor.SetMotionTrackKeyframes(clipId, selectedRegion.Id, trackedKeyframes);

            currentClip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
            selectedRegion = currentClip?.MotionTracks.FirstOrDefault(r => r.Id == selectedRegion.Id);
            RefreshProgressBarFromKeyframes();

            sbStatusText.Text = $"Tracking complete. {trackedKeyframes.Count} keyframes.";
            onPreviewRequest?.Invoke();
        }
        catch (OperationCanceledException)
        {
            sbStatusText.Text = "Tracking cancelled by user.";
        }
        catch (Exception ex)
        {
            sbStatusText.Text = $"Tracking error: {ex.Message}";
        }
        finally
        {
            isTracking = false;
            bBackTrack.IsEnabled = true;
            bBackOne.IsEnabled = true;
            bTrackAll.IsEnabled = true;
            bForwardOne.IsEnabled = true;
            bForTrack.IsEnabled = true;
        }
    }

    private void PopulateAttachMenu()
    {
        bAttach.Items.Clear();

        if (currentClip == null || selectedRegion == null) return;

        var allClips = vm.Project.Tracks.SelectMany(t => t.Clips).Where(c => c.Id != clipId).ToList();
        if (allClips.Count == 0)
        {
            var emptyItem = new MenuItem { Header = "No other timeline clips found", IsEnabled = false };
            bAttach.Items.Add(emptyItem);
            return;
        }

        foreach (var targetClip in allClips)
        {
            var clipItem = new MenuItem { Header = string.IsNullOrEmpty(targetClip.Name) ? $"Clip ({targetClip.StartFrame}-{targetClip.EndFrame})" : targetClip.Name };

            var pipItem = new MenuItem { Header = "Picture In Picture" };
            pipItem.Click += (_, _) =>
            {
                vm.Editor.TransferMotionToPictureInPicture(clipId, selectedRegion.Id, targetClip.Id);
                onPreviewRequest?.Invoke();
                sbStatusText.Text = "Transferred to Picture In Picture.";
            };
            clipItem.Items.Add(pipItem);

            var followCropItem = new MenuItem { Header = "Pan/Crop (Follow Subject)" };
            followCropItem.Click += (_, _) =>
            {
                vm.Editor.TransferMotionToCrop(clipId, selectedRegion.Id, targetClip.Id, stabilize: false);
                onPreviewRequest?.Invoke();
                sbStatusText.Text = "Transferred to Pan/Crop (Follow).";
            };
            clipItem.Items.Add(followCropItem);

            var stabilizeCropItem = new MenuItem { Header = "Pan/Crop (Stabilize)" };
            stabilizeCropItem.Click += (_, _) =>
            {
                vm.Editor.TransferMotionToCrop(clipId, selectedRegion.Id, targetClip.Id, stabilize: true);
                onPreviewRequest?.Invoke();
                sbStatusText.Text = "Transferred to Pan/Crop (Stabilize).";
            };
            clipItem.Items.Add(stabilizeCropItem);

            var maskItem = new MenuItem { Header = "Bézier Mask Generator" };
            maskItem.Click += (_, _) =>
            {
                vm.Editor.TransferMotionToMask(clipId, selectedRegion.Id, targetClip.Id);
                onPreviewRequest?.Invoke();
                sbStatusText.Text = "Transferred to Mask Generator.";
            };
            clipItem.Items.Add(maskItem);

            bAttach.Items.Add(clipItem);
        }
    }

    private void bAttach_PreviewMouseDown(object sender, MouseEventArgs e)
    {
        PopulateAttachMenu();
    }

    private void bAttach_DropCursor(object sender, MouseButtonEventArgs e)
    {
        if (currentClip == null || selectedRegion == null) return;

        var data = new DataObject("OpenRendMotionTrack", new MotionTrackDragPayload(clipId, selectedRegion.Id));
        DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
    }

    private void MotionTrackWindow_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            trackingCts?.Cancel();
        }
        catch { }
    }
}
