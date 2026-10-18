using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

/// <summary>
/// Vegas Pro 1:1 Motion Tracking panel.
/// Provides forward, backward, frame-step, and full-range optical flow feature tracking,
/// multi-region mask management, precision and motion model selection, live progress feedback,
/// and motion data transfer to Picture-in-Picture, Event Pan/Crop (Stabilize/Follow), and Masks.
/// </summary>
public sealed class MotionTrackWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly Guid clipId;
    private readonly Action? onPreviewRequest;

    private TimelineClip? currentClip;
    private MotionTrackData? selectedRegion;
    private CancellationTokenSource? trackingCts;

    // UI elements
    private readonly TextBlock lClipName;
    private readonly ComboBox cbMasks;
    private readonly Button bAdd;
    private readonly Button bRemove;
    private readonly CheckBox chkMaskInvert;

    private readonly RadioButton rbRect;
    private readonly RadioButton rbOval;
    private readonly RadioButton rbBezier;

    private readonly ComboBox cbPrecision;
    private readonly ComboBox cbMode;
    private readonly CheckBox chkShowFeatures;

    private readonly Button bBackTrack;
    private readonly Button bBackOne;
    private readonly Button bTrackAll;
    private readonly Button bForwardOne;
    private readonly Button bForTrack;
    private readonly Button bStop;

    private readonly ProgressBar progressBar;
    private readonly TextBlock sbStatusText;
    private readonly TextBlock keyframeCountText;

    private readonly Button bAttach;
    private readonly Button bAttachDrag;
    private readonly Button bClearKeyframes;

    private readonly TextBlock txtCenterX;
    private readonly TextBlock txtCenterY;
    private readonly TextBlock txtWidth;
    private readonly TextBlock txtHeight;
    private readonly TextBlock txtAngle;
    private readonly TextBlock txtScale;
    private readonly ProgressBar pbConfidence;

    public MotionTrackWindow(EditorViewModel vm, Guid clipId, Action? onPreviewRequest = null)
    {
        this.vm = vm ?? throw new ArgumentNullException(nameof(vm));
        this.clipId = clipId;
        this.onPreviewRequest = onPreviewRequest;

        Title = "Motion Tracking - OpenRend";
        Width = 520;
        Height = 650;
        MinWidth = 460;
        MinHeight = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(28, 28, 28));
        Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220));

        var mainGrid = new Grid { Margin = new Thickness(12) };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header / Clip name
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Region selector bar
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Shape & Options
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Tracking Controls
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Progress & Status
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Live Readout & Confidence
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Transfer Motion bar
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Keyframes list / info

        // 1. Header
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        lClipName = new TextBlock
        {
            Text = "Event: Loading...",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
            VerticalAlignment = VerticalAlignment.Center
        };
        headerGrid.Children.Add(lClipName);

        var bRefresh = new Button
        {
            Content = "Refresh",
            Padding = new Thickness(8, 2, 8, 2),
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            Cursor = Cursors.Hand
        };
        bRefresh.Click += (_, _) => ReloadClip();
        Grid.SetColumn(bRefresh, 1);
        headerGrid.Children.Add(bRefresh);
        headerBorder.Child = headerGrid;
        Grid.SetRow(headerBorder, 0);
        mainGrid.Children.Add(headerBorder);

        // 2. Region Selector Bar
        var regionBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var regionStack = new StackPanel();

        var regionHeaderGrid = new Grid();
        regionHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        regionHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        regionHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        regionHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        regionHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lblRegion = new TextBlock
        {
            Text = "Tracking Region:",
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(lblRegion, 0);
        regionHeaderGrid.Children.Add(lblRegion);

        cbMasks = new ComboBox
        {
            Height = 26,
            Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Foreground = Brushes.White
        };
        cbMasks.SelectionChanged += CbMasks_SelectionChanged;
        Grid.SetColumn(cbMasks, 1);
        regionHeaderGrid.Children.Add(cbMasks);

        bAdd = new Button
        {
            Content = " + Add ",
            Height = 26,
            Margin = new Thickness(0, 0, 4, 0),
            Background = new SolidColorBrush(Color.FromRgb(42, 90, 42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 120, 60)),
            ToolTip = "Add a new tracking region"
        };
        bAdd.Click += BAdd_Click;
        Grid.SetColumn(bAdd, 2);
        regionHeaderGrid.Children.Add(bAdd);

        bRemove = new Button
        {
            Content = " - Delete ",
            Height = 26,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(90, 42, 42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(120, 60, 60)),
            ToolTip = "Delete selected tracking region"
        };
        bRemove.Click += BRemove_Click;
        Grid.SetColumn(bRemove, 3);
        regionHeaderGrid.Children.Add(bRemove);

        chkMaskInvert = new CheckBox
        {
            Content = "Invert",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200))
        };
        chkMaskInvert.Click += ChkMaskInvert_Click;
        Grid.SetColumn(chkMaskInvert, 4);
        regionHeaderGrid.Children.Add(chkMaskInvert);

        regionStack.Children.Add(regionHeaderGrid);
        regionBorder.Child = regionStack;
        Grid.SetRow(regionBorder, 1);
        mainGrid.Children.Add(regionBorder);

        // 3. Shape & Options
        var optionsBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var optionsGrid = new Grid();
        optionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        optionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        optionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        optionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Shape row
        var lblShape = new TextBlock { Text = "Mask Shape:", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(lblShape, 0); Grid.SetColumn(lblShape, 0);
        optionsGrid.Children.Add(lblShape);

        var shapeStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        rbRect = new RadioButton { Content = "Rectangle", IsChecked = true, Margin = new Thickness(0, 0, 12, 0), Foreground = Brushes.White };
        rbOval = new RadioButton { Content = "Oval", Margin = new Thickness(0, 0, 12, 0), Foreground = Brushes.White };
        rbBezier = new RadioButton { Content = "Freehand Bezier", Foreground = Brushes.White };
        rbRect.Checked += (_, _) => SetShape(TrackingShape.Rectangle);
        rbOval.Checked += (_, _) => SetShape(TrackingShape.Oval);
        rbBezier.Checked += (_, _) => SetShape(TrackingShape.Bezier);
        shapeStack.Children.Add(rbRect);
        shapeStack.Children.Add(rbOval);
        shapeStack.Children.Add(rbBezier);
        Grid.SetRow(shapeStack, 0); Grid.SetColumn(shapeStack, 1);
        optionsGrid.Children.Add(shapeStack);

        // Mode row
        var lblMode = new TextBlock { Text = "Mode:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        Grid.SetRow(lblMode, 1); Grid.SetColumn(lblMode, 0);
        optionsGrid.Children.Add(lblMode);

        cbMode = new ComboBox { Height = 24, Margin = new Thickness(0, 4, 0, 4) };
        cbMode.Items.Add("Translation (Location X/Y)");
        cbMode.Items.Add("Rigid Motion (Location + Rotation)");
        cbMode.Items.Add("Similarity (Location + Rotation + Scale)");
        cbMode.Items.Add("Affine (Location, Rotation, Scale, Shear)");
        cbMode.Items.Add("Homography (Perspective 4-corner)");
        cbMode.SelectedIndex = 2; // Similarity default
        cbMode.SelectionChanged += CbMode_SelectionChanged;
        Grid.SetRow(cbMode, 1); Grid.SetColumn(cbMode, 1);
        optionsGrid.Children.Add(cbMode);

        // Precision row
        var lblPrecision = new TextBlock { Text = "Precision:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        Grid.SetRow(lblPrecision, 2); Grid.SetColumn(lblPrecision, 0);
        optionsGrid.Children.Add(lblPrecision);

        var precStack = new Grid();
        precStack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        precStack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        cbPrecision = new ComboBox { Height = 24, Margin = new Thickness(0, 4, 8, 4) };
        cbPrecision.Items.Add("High");
        cbPrecision.Items.Add("Medium");
        cbPrecision.Items.Add("Low");
        cbPrecision.Items.Add("Fast");
        cbPrecision.SelectedIndex = 0; // High default
        cbPrecision.SelectionChanged += CbPrecision_SelectionChanged;
        Grid.SetColumn(cbPrecision, 0);
        precStack.Children.Add(cbPrecision);

        chkShowFeatures = new CheckBox
        {
            Content = "Show Motion Vectors & Features",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200))
        };
        Grid.SetColumn(chkShowFeatures, 1);
        precStack.Children.Add(chkShowFeatures);

        Grid.SetRow(precStack, 2); Grid.SetColumn(precStack, 1);
        optionsGrid.Children.Add(precStack);

        optionsBorder.Child = optionsGrid;
        Grid.SetRow(optionsBorder, 2);
        mainGrid.Children.Add(optionsBorder);

        // 4. Tracking Controls (5 buttons)
        var trackControlBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var trackStack = new StackPanel();

        var trackButtonsGrid = new Grid();
        trackButtonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        trackButtonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        trackButtonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        trackButtonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        trackButtonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        trackButtonsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Button CreateNavBtn(string text, string tip, Action action)
        {
            var btn = new Button
            {
                Content = text,
                Height = 32,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Color.FromRgb(48, 48, 48)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                BorderThickness = new Thickness(1),
                ToolTip = tip,
                Cursor = Cursors.Hand
            };
            btn.Click += (_, _) => action();
            return btn;
        }

        bBackTrack = CreateNavBtn("<<", "Track Backward to Start", () => StartTracking(TrackingDirection.Backward));
        bBackOne = CreateNavBtn("< 1", "Step 1 Frame Backward", () => StartTracking(TrackingDirection.StepBackward));
        bTrackAll = CreateNavBtn("<< ALL >>", "Track Full Range (Both Directions)", () => StartTracking(TrackingDirection.All));
        bForwardOne = CreateNavBtn("1 >", "Step 1 Frame Forward", () => StartTracking(TrackingDirection.StepForward));
        bForTrack = CreateNavBtn(">>", "Track Forward to End", () => StartTracking(TrackingDirection.Forward));

        bStop = new Button
        {
            Content = " Stop ",
            Height = 32,
            Margin = new Thickness(4, 2, 2, 2),
            Background = new SolidColorBrush(Color.FromRgb(120, 40, 40)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            BorderBrush = new SolidColorBrush(Color.FromRgb(150, 60, 60)),
            BorderThickness = new Thickness(1),
            IsEnabled = false,
            ToolTip = "Stop Tracking"
        };
        bStop.Click += (_, _) => StopTracking();

        Grid.SetColumn(bBackTrack, 0); trackButtonsGrid.Children.Add(bBackTrack);
        Grid.SetColumn(bBackOne, 1); trackButtonsGrid.Children.Add(bBackOne);
        Grid.SetColumn(bTrackAll, 2); trackButtonsGrid.Children.Add(bTrackAll);
        Grid.SetColumn(bForwardOne, 3); trackButtonsGrid.Children.Add(bForwardOne);
        Grid.SetColumn(bForTrack, 4); trackButtonsGrid.Children.Add(bForTrack);
        Grid.SetColumn(bStop, 5); trackButtonsGrid.Children.Add(bStop);

        trackStack.Children.Add(trackButtonsGrid);
        trackControlBorder.Child = trackStack;
        Grid.SetRow(trackControlBorder, 3);
        mainGrid.Children.Add(trackControlBorder);

        // 5. Progress & Status
        var progressBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var progressStack = new StackPanel();

        progressBar = new ProgressBar
        {
            Height = 12,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = new SolidColorBrush(Color.FromRgb(60, 140, 220)),
            Background = new SolidColorBrush(Color.FromRgb(20, 20, 20))
        };
        progressStack.Children.Add(progressBar);

        var statusGrid = new Grid();
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        sbStatusText = new TextBlock
        {
            Text = "Ready to track.",
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            FontSize = 11
        };
        Grid.SetColumn(sbStatusText, 0);
        statusGrid.Children.Add(sbStatusText);

        keyframeCountText = new TextBlock
        {
            Text = "0 keyframes",
            Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
            FontSize = 11
        };
        Grid.SetColumn(keyframeCountText, 1);
        statusGrid.Children.Add(keyframeCountText);

        progressStack.Children.Add(statusGrid);
        progressBorder.Child = progressStack;
        Grid.SetRow(progressBorder, 4);
        mainGrid.Children.Add(progressBorder);

        // 6. Live Readout & Confidence
        var readoutBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var readoutGrid = new Grid();
        for (var i = 0; i < 4; i++) readoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        readoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        readoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock Val(string label, out TextBlock valBlock)
        {
            var p = new StackPanel { Margin = new Thickness(2) };
            p.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130)) });
            valBlock = new TextBlock { Text = "0.00", FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Brushes.White };
            p.Children.Add(valBlock);
            return valBlock;
        }

        Val("Center X:", out txtCenterX); Grid.SetColumn((FrameworkElement)txtCenterX.Parent, 0); Grid.SetRow((FrameworkElement)txtCenterX.Parent, 0); readoutGrid.Children.Add((FrameworkElement)txtCenterX.Parent);
        Val("Center Y:", out txtCenterY); Grid.SetColumn((FrameworkElement)txtCenterY.Parent, 1); Grid.SetRow((FrameworkElement)txtCenterY.Parent, 0); readoutGrid.Children.Add((FrameworkElement)txtCenterY.Parent);
        Val("Width:", out txtWidth); Grid.SetColumn((FrameworkElement)txtWidth.Parent, 2); Grid.SetRow((FrameworkElement)txtWidth.Parent, 0); readoutGrid.Children.Add((FrameworkElement)txtWidth.Parent);
        Val("Height:", out txtHeight); Grid.SetColumn((FrameworkElement)txtHeight.Parent, 3); Grid.SetRow((FrameworkElement)txtHeight.Parent, 0); readoutGrid.Children.Add((FrameworkElement)txtHeight.Parent);

        Val("Angle:", out txtAngle); Grid.SetColumn((FrameworkElement)txtAngle.Parent, 0); Grid.SetRow((FrameworkElement)txtAngle.Parent, 1); readoutGrid.Children.Add((FrameworkElement)txtAngle.Parent);
        Val("Scale:", out txtScale); Grid.SetColumn((FrameworkElement)txtScale.Parent, 1); Grid.SetRow((FrameworkElement)txtScale.Parent, 1); readoutGrid.Children.Add((FrameworkElement)txtScale.Parent);

        var confPanel = new StackPanel { Margin = new Thickness(2) };
        confPanel.Children.Add(new TextBlock { Text = "Confidence:", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130)) });
        pbConfidence = new ProgressBar { Height = 10, Maximum = 100, Value = 100, Foreground = new SolidColorBrush(Color.FromRgb(50, 180, 50)), Margin = new Thickness(0, 4, 0, 0) };
        confPanel.Children.Add(pbConfidence);
        Grid.SetColumn(confPanel, 2); Grid.SetRow(confPanel, 1); Grid.SetColumnSpan(confPanel, 2);
        readoutGrid.Children.Add(confPanel);

        readoutBorder.Child = readoutGrid;
        Grid.SetRow(readoutBorder, 5);
        mainGrid.Children.Add(readoutBorder);

        // 7. Transfer Motion Bar
        var transferBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var transferGrid = new Grid();
        transferGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        transferGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        transferGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        bAttach = new Button
        {
            Content = "Transfer Motion Data ▼",
            Height = 30,
            Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(40, 90, 140)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 120, 180)),
            Cursor = Cursors.Hand
        };
        bAttach.Click += BAttach_Click;
        Grid.SetColumn(bAttach, 0);
        transferGrid.Children.Add(bAttach);

        bAttachDrag = new Button
        {
            Content = " 🎯 Drag to Event ",
            Height = 30,
            Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            ToolTip = "Click and drag to drop motion data onto any event on the timeline",
            Cursor = Cursors.Hand
        };
        bAttachDrag.PreviewMouseDown += BAttachDrag_PreviewMouseDown;
        Grid.SetColumn(bAttachDrag, 1);
        transferGrid.Children.Add(bAttachDrag);

        bClearKeyframes = new Button
        {
            Content = "Clear Keyframes",
            Height = 30,
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Foreground = new SolidColorBrush(Color.FromRgb(200, 160, 160)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            ToolTip = "Clear all tracked keyframes for this region"
        };
        bClearKeyframes.Click += BClearKeyframes_Click;
        Grid.SetColumn(bClearKeyframes, 2);
        transferGrid.Children.Add(bClearKeyframes);

        transferBorder.Child = transferGrid;
        Grid.SetRow(transferBorder, 6);
        mainGrid.Children.Add(transferBorder);

        Content = mainGrid;

        Loaded += (_, _) => ReloadClip();
        Closed += (_, _) => StopTracking();
    }

    private void ReloadClip()
    {
        currentClip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
        if (currentClip == null)
        {
            lClipName.Text = "Event: Not found";
            IsEnabled = false;
            return;
        }

        var asset = vm.Project.Media.FirstOrDefault(m => m.Id == currentClip.MediaId);
        lClipName.Text = $"Event: {currentClip.Name} ({asset?.Name ?? "Media"}) [{currentClip.StartFrame} – {currentClip.EndFrame}]";

        // Populate regions
        cbMasks.Items.Clear();
        foreach (var r in currentClip.MotionTracks)
        {
            cbMasks.Items.Add(r.Name);
        }

        if (currentClip.MotionTracks.Count == 0)
        {
            // Create default region 1
            var defaultRegion = new MotionTrackData
            {
                Name = "Region 1",
                Shape = TrackingShape.Rectangle,
                Model = TrackingModel.Similarity,
                Precision = TrackingPrecision.High
            };
            // Default center box
            var kf = new TrackingKeyframe(currentClip.StartFrame, 0.5, 0.5, 0.25, 0.25);
            defaultRegion.Keyframes.Add(kf);
            vm.Editor.AddMotionTrack(clipId, defaultRegion);

            currentClip = vm.Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == clipId);
            cbMasks.Items.Add(defaultRegion.Name);
        }

        cbMasks.SelectedIndex = 0;
    }

    private void CbMasks_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (currentClip == null || cbMasks.SelectedIndex < 0 || cbMasks.SelectedIndex >= currentClip.MotionTracks.Count)
        {
            selectedRegion = null;
            return;
        }

        selectedRegion = currentClip.MotionTracks[cbMasks.SelectedIndex];

        // Update UI controls to match selected region
        chkMaskInvert.IsChecked = selectedRegion.IsInverted;
        rbRect.IsChecked = selectedRegion.Shape == TrackingShape.Rectangle;
        rbOval.IsChecked = selectedRegion.Shape == TrackingShape.Oval;
        rbBezier.IsChecked = selectedRegion.Shape == TrackingShape.Bezier;

        cbMode.SelectedIndex = (int)selectedRegion.Model;
        cbPrecision.SelectedIndex = (int)selectedRegion.Precision;

        UpdateReadout(selectedRegion.Evaluate(vm.Position));
    }

    private void SetShape(TrackingShape shape)
    {
        if (selectedRegion == null || currentClip == null) return;
        selectedRegion.Shape = shape;
        vm.Editor.UpdateMotionTrack(clipId, selectedRegion);
        onPreviewRequest?.Invoke();
    }

    private void CbMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selectedRegion == null || currentClip == null || cbMode.SelectedIndex < 0) return;
        selectedRegion.Model = (TrackingModel)cbMode.SelectedIndex;
        vm.Editor.UpdateMotionTrack(clipId, selectedRegion);
    }

    private void CbPrecision_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selectedRegion == null || currentClip == null || cbPrecision.SelectedIndex < 0) return;
        selectedRegion.Precision = (TrackingPrecision)cbPrecision.SelectedIndex;
        vm.Editor.UpdateMotionTrack(clipId, selectedRegion);
    }

    private void ChkMaskInvert_Click(object sender, RoutedEventArgs e)
    {
        if (selectedRegion == null || currentClip == null) return;
        selectedRegion.IsInverted = chkMaskInvert.IsChecked == true;
        vm.Editor.UpdateMotionTrack(clipId, selectedRegion);
        onPreviewRequest?.Invoke();
    }

    private void BAdd_Click(object sender, RoutedEventArgs e)
    {
        if (currentClip == null) return;
        var newNum = currentClip.MotionTracks.Count + 1;
        var newRegion = new MotionTrackData
        {
            Name = $"Region {newNum}",
            Shape = TrackingShape.Rectangle,
            Model = TrackingModel.Similarity,
            Precision = TrackingPrecision.High
        };
        newRegion.Keyframes.Add(new TrackingKeyframe(vm.Position, 0.5, 0.5, 0.25, 0.25));

        vm.Editor.AddMotionTrack(clipId, newRegion);
        ReloadClip();
        cbMasks.SelectedIndex = cbMasks.Items.Count - 1;
        onPreviewRequest?.Invoke();
    }

    private void BRemove_Click(object sender, RoutedEventArgs e)
    {
        if (currentClip == null || selectedRegion == null) return;
        if (currentClip.MotionTracks.Count <= 1)
        {
            MessageBox.Show("At least one tracking region must be retained.", "Motion Tracking", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        vm.Editor.RemoveMotionTrack(clipId, selectedRegion.Id);
        ReloadClip();
        onPreviewRequest?.Invoke();
    }

    private void BClearKeyframes_Click(object sender, RoutedEventArgs e)
    {
        if (currentClip == null || selectedRegion == null) return;
        var result = MessageBox.Show($"Clear all {selectedRegion.Keyframes.Count} keyframes for {selectedRegion.Name}?", "Clear Keyframes", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        selectedRegion.Keyframes.Clear();
        selectedRegion.Keyframes.Add(new TrackingKeyframe(currentClip.StartFrame, 0.5, 0.5, 0.25, 0.25));
        vm.Editor.UpdateMotionTrack(clipId, selectedRegion);

        UpdateReadout(selectedRegion.Keyframes[0]);
        sbStatusText.Text = "Keyframes cleared.";
        onPreviewRequest?.Invoke();
    }

    private enum TrackingDirection { Forward, Backward, StepForward, StepBackward, All }

    private async void StartTracking(TrackingDirection direction)
    {
        if (currentClip == null || selectedRegion == null) return;

        var asset = vm.Project.Media.FirstOrDefault(m => m.Id == currentClip.MediaId);
        if (asset == null || !File.Exists(asset.Path))
        {
            sbStatusText.Text = "Source media file not found.";
            return;
        }

        var startFrame = vm.Position;
        var endFrame = startFrame;

        switch (direction)
        {
            case TrackingDirection.Forward:
                startFrame = Math.Clamp(vm.Position, currentClip.StartFrame, currentClip.EndFrame);
                endFrame = currentClip.EndFrame;
                break;
            case TrackingDirection.Backward:
                startFrame = Math.Clamp(vm.Position, currentClip.StartFrame, currentClip.EndFrame);
                endFrame = currentClip.StartFrame;
                break;
            case TrackingDirection.StepForward:
                startFrame = Math.Clamp(vm.Position, currentClip.StartFrame, currentClip.EndFrame);
                endFrame = Math.Min(currentClip.EndFrame, startFrame + 1);
                break;
            case TrackingDirection.StepBackward:
                startFrame = Math.Clamp(vm.Position, currentClip.StartFrame, currentClip.EndFrame);
                endFrame = Math.Max(currentClip.StartFrame, startFrame - 1);
                break;
            case TrackingDirection.All:
                startFrame = currentClip.StartFrame;
                endFrame = currentClip.EndFrame;
                break;
        }

        SetTrackingUiState(isTracking: true);
        trackingCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<TrackingProgress>(p =>
            {
                progressBar.Value = Math.Clamp(p.Percent * 100.0, 0.0, 100.0);
                sbStatusText.Text = p.StatusText;
                if (p.LatestKeyframe is not null)
                {
                    UpdateReadout(p.LatestKeyframe);
                    vm.Position = p.CurrentFrame;
                    onPreviewRequest?.Invoke();
                }
            });

            // Decode frames using the timeline render engine
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

            // Apply keyframes atomically to TimelineEditor
            vm.Editor.SetMotionTrackKeyframes(clipId, selectedRegion.Id, trackedKeyframes);

            sbStatusText.Text = $"Tracking complete. {trackedKeyframes.Count} keyframes.";
            keyframeCountText.Text = $"{trackedKeyframes.Count} keyframes";
            onPreviewRequest?.Invoke();
        }
        catch (OperationCanceledException)
        {
            sbStatusText.Text = "Tracking stopped by user.";
        }
        catch (Exception ex)
        {
            sbStatusText.Text = $"Tracking error: {ex.Message}";
        }
        finally
        {
            SetTrackingUiState(isTracking: false);
            trackingCts = null;
        }
    }

    private void StopTracking()
    {
        trackingCts?.Cancel();
    }

    private void SetTrackingUiState(bool isTracking)
    {
        bBackTrack.IsEnabled = !isTracking;
        bBackOne.IsEnabled = !isTracking;
        bTrackAll.IsEnabled = !isTracking;
        bForwardOne.IsEnabled = !isTracking;
        bForTrack.IsEnabled = !isTracking;
        bStop.IsEnabled = isTracking;
        cbMasks.IsEnabled = !isTracking;
        bAdd.IsEnabled = !isTracking;
        bRemove.IsEnabled = !isTracking;
    }

    private void UpdateReadout(TrackingKeyframe? kf)
    {
        if (kf == null) return;
        txtCenterX.Text = kf.CenterX.ToString("F3", CultureInfo.InvariantCulture);
        txtCenterY.Text = kf.CenterY.ToString("F3", CultureInfo.InvariantCulture);
        txtWidth.Text = kf.Width.ToString("F3", CultureInfo.InvariantCulture);
        txtHeight.Text = kf.Height.ToString("F3", CultureInfo.InvariantCulture);
        txtAngle.Text = $"{kf.Angle:F1}°";
        txtScale.Text = $"{Math.Max(kf.ScaleX, kf.ScaleY):F2}x";

        pbConfidence.Value = Math.Clamp(kf.Confidence * 100.0, 0.0, 100.0);
        pbConfidence.Foreground = kf.Confidence > 0.7
            ? new SolidColorBrush(Color.FromRgb(50, 180, 50))
            : kf.Confidence > 0.4
                ? new SolidColorBrush(Color.FromRgb(200, 160, 40))
                : new SolidColorBrush(Color.FromRgb(200, 50, 50));

        if (selectedRegion != null)
        {
            keyframeCountText.Text = $"{selectedRegion.Keyframes.Count} keyframes";
        }
    }

    private void BAttach_Click(object sender, RoutedEventArgs e)
    {
        if (currentClip == null || selectedRegion == null) return;

        var menu = new ContextMenu();

        var pipItem = new MenuItem { Header = "Attach to Picture in Picture (Match Position, Scale & Angle)" };
        pipItem.Click += (_, _) =>
        {
            vm.Editor.TransferMotionToPictureInPicture(clipId, selectedRegion.Id, clipId);
            sbStatusText.Text = "Transferred motion to Picture-in-Picture effect.";
            onPreviewRequest?.Invoke();
        };
        menu.Items.Add(pipItem);

        var cropFollowItem = new MenuItem { Header = "Attach to Event Pan/Crop (Follow Subject)" };
        cropFollowItem.Click += (_, _) =>
        {
            vm.Editor.TransferMotionToCrop(clipId, selectedRegion.Id, clipId, stabilize: false);
            sbStatusText.Text = "Transferred motion to Event Pan/Crop (Follow Subject).";
            onPreviewRequest?.Invoke();
        };
        menu.Items.Add(cropFollowItem);

        var cropStabilizeItem = new MenuItem { Header = "Attach to Event Pan/Crop (Stabilize Video)" };
        cropStabilizeItem.Click += (_, _) =>
        {
            vm.Editor.TransferMotionToCrop(clipId, selectedRegion.Id, clipId, stabilize: true);
            sbStatusText.Text = "Applied motion stabilization to Event Pan/Crop.";
            onPreviewRequest?.Invoke();
        };
        menu.Items.Add(cropStabilizeItem);

        var maskItem = new MenuItem { Header = "Attach to Mask Generator (Tracked Matte)" };
        maskItem.Click += (_, _) =>
        {
            vm.Editor.TransferMotionToMask(clipId, selectedRegion.Id, clipId);
            sbStatusText.Text = "Transferred motion to Mask Generator.";
            onPreviewRequest?.Invoke();
        };
        menu.Items.Add(maskItem);

        menu.Items.Add(new Separator());

        // Target another video event on the timeline
        var otherClips = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Video)
            .SelectMany(t => t.Clips.Where(c => c.Id != clipId)).ToArray();

        if (otherClips.Length > 0)
        {
            var otherMenu = new MenuItem { Header = "Transfer to Another Event on Timeline" };
            foreach (var other in otherClips)
            {
                var targetId = other.Id;
                var clipMenuItem = new MenuItem { Header = $"{other.Name} [{other.StartFrame} – {other.EndFrame}]" };
                clipMenuItem.Click += (_, _) =>
                {
                    vm.Editor.TransferMotionToPictureInPicture(clipId, selectedRegion.Id, targetId);
                    sbStatusText.Text = $"Transferred motion to '{other.Name}' (Picture-in-Picture).";
                    onPreviewRequest?.Invoke();
                };
                otherMenu.Items.Add(clipMenuItem);
            }
            menu.Items.Add(otherMenu);
        }

        menu.PlacementTarget = bAttach;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void BAttachDrag_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (currentClip == null || selectedRegion == null) return;

        // Initiate Drag & Drop transfer with motion tracking payload
        var data = new DataObject("OpenRendMotionTrack", new MotionTrackDragPayload(clipId, selectedRegion.Id));
        DragDrop.DoDragDrop(bAttachDrag, data, DragDropEffects.Link);
    }
}

public readonly record struct MotionTrackDragPayload(Guid SourceClipId, Guid RegionId);
