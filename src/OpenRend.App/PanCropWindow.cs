using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;

namespace OpenRend.App;

public enum PanCropTrack { Position, Mask }
public enum MaskTool { Normal, Pen, SplitTangent, DeleteAnchor, Rectangle, Oval }

public sealed class PanCropWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly TimelineEditor editor;
    private readonly Guid clipId;
    private readonly Action<long> seek;

    // Track state
    private PanCropTrack activeTrack = PanCropTrack.Position;
    private MaskTool activeMaskTool = MaskTool.Normal;

    // Controls
    private readonly Dictionary<string, TextBox> fields = [];
    private readonly CropWorkspace workspace = new();
    private readonly CropKeyframeLane lane = new();
    private readonly TextBox frameField;
    private readonly TextBox keyframeField;
    private readonly ComboBox interpolation;
    private readonly TextBox smoothnessField;
    private readonly Slider smoothnessSlider;
    private readonly ComboBox presets;
    private readonly CheckBox lockAspect;
    private readonly TextBlock status;
    private readonly ScrollViewer propertyScroll;
    private readonly StackPanel positionProperties;
    private readonly StackPanel maskProperties;
    private readonly StackPanel toolbar;

    // Mask controls
    private readonly ComboBox maskMode;
    private readonly CheckBox maskAntiAlias;
    private readonly TextBox maskOpacityField;
    private readonly Slider maskOpacitySlider;
    private readonly ComboBox maskFeatherType;
    private readonly TextBox maskFeatherField;
    private readonly Slider maskFeatherSlider;
    private readonly ComboBox maskInterpolation;
    private readonly TextBox maskSmoothnessField;
    private readonly Slider maskSmoothnessSlider;

    // Track headers
    private readonly Border positionTrackRow;
    private readonly Border maskTrackRow;
    private readonly CheckBox maskTrackCheck;

    // Tool buttons lists
    private readonly List<UIElement> positionTools = [];
    private readonly List<UIElement> maskTools = [];

    private static string PanCropPreferencesPath => Path.Combine(
        Environment.GetEnvironmentVariable("OPENREND_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenRend"),
        "pancrop.json");
    private PanCropPreferences panCropPrefs = new();
    private readonly List<string> presetNames = [];
    private long localFrame;
    private bool refreshing;
    private bool closed;
    private long sourceRequest;
    private bool sourceRunning;
    private bool sourcePending;
    private (string Path, long Frame, int Width, int Height, Rational Rate)? requestedSource;
    private CropState state;
    private MaskShape maskState = new();

    public PanCropWindow(EditorViewModel viewModel, Guid selectedClip, Action<long> seekPreview)
    {
        vm = viewModel; editor = vm.Editor; clipId = selectedClip; seek = seekPreview;
        Style = (Style)FindResource(typeof(Window));
        Title = "Video event Pan/Crop"; Width = 902; Height = 632;
        MinWidth = 700; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var clip = EventClip;
        localFrame = Math.Clamp(vm.Position - clip.StartFrame, 0, clip.DurationFrames - 1);
        var root = new Grid { Background = (Brush)FindResource("PanelBrush") };
        root.RowDefinitions.Add(new() { Height = new GridLength(28) });
        root.RowDefinitions.Add(new() { Height = new GridLength(30) });
        root.RowDefinitions.Add(new() { Height = new GridLength(28) });
        root.RowDefinitions.Add(new());
        root.RowDefinitions.Add(new() { Height = new GridLength(104) });
        var heading = new TextBlock { Text = "Event Pan/Crop:  " + clip.Name, Margin = new Thickness(10, 0, 0, 0) };
        root.Children.Add(heading);

        var top = new DockPanel { Background = (Brush)FindResource("HeaderBrush"), LastChildFill = false };
        Grid.SetRow(top, 1); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Pan/Crop", Background = (Brush)FindResource("SelectionBrush"), Padding = new Thickness(8, 4, 8, 4) });
        var reset = Button("Reset", "Reset Pan/Crop or Mask", () => Edit(() =>
        {
            if (activeTrack == PanCropTrack.Position) editor.ResetCrop(clipId);
            else editor.ResetMask(clipId);
        }));
        DockPanel.SetDock(reset, Dock.Right); top.Children.Add(reset);
        var undo = Button("Undo", "Undo edit", () => Edit(() => editor.Undo()));
        DockPanel.SetDock(undo, Dock.Right); top.Children.Add(undo);
        var redo = Button("Redo", "Redo edit", () => Edit(() => editor.Redo()));
        DockPanel.SetDock(redo, Dock.Right); top.Children.Add(redo);

        var presetRow = new DockPanel { LastChildFill = true, Margin = new Thickness(3, 1, 3, 1) };
        Grid.SetRow(presetRow, 2); root.Children.Add(presetRow);
        presetRow.Children.Add(new TextBlock { Text = "Preset:", Width = 72, VerticalAlignment = VerticalAlignment.Center });

        var deletePresetBtn = Button("✕", "Delete preset", DeleteCurrentPreset);
        AutomationProperties.SetAutomationId(deletePresetBtn, "DeleteCropPreset");
        DockPanel.SetDock(deletePresetBtn, Dock.Right);
        presetRow.Children.Add(deletePresetBtn);

        var savePresetBtn = Button("💾", "Save preset", SaveCurrentPreset);
        AutomationProperties.SetAutomationId(savePresetBtn, "SaveCropPreset");
        DockPanel.SetDock(savePresetBtn, Dock.Right);
        presetRow.Children.Add(savePresetBtn);

        presets = new ComboBox { IsEditable = true };
        AutomationProperties.SetName(presets, "Crop preset");
        AutomationProperties.SetAutomationId(presets, "CropPresetCombo");
        PopulatePresets();
        presets.SelectionChanged += (_, _) =>
        {
            if (refreshing || presets.SelectedIndex < 0) return;
            var selectedName = presets.SelectedItem as string ?? presets.Text;
            var identity = CropState.Identity(Source.Width, Source.Height);

            CropState CalculateAspectCrop(double targetAspect)
            {
                var sourceAspect = (double)Source.Width / Source.Height;
                var w = sourceAspect > targetAspect ? Source.Height * targetAspect : Source.Width;
                var h = sourceAspect > targetAspect ? Source.Height : Source.Width / targetAspect;
                return identity with { Width = w, Height = h, CenterX = Source.Width / 2.0, CenterY = Source.Height / 2.0 };
            }

            var preset = selectedName switch
            {
                "Full frame" => identity,
                "Half size" => identity with { Width = identity.Width / 2, Height = identity.Height / 2 },
                "Square center" => identity with { Width = Math.Min(identity.Width, identity.Height), Height = Math.Min(identity.Width, identity.Height) },
                "16:9 Widescreen TV (1.778)" => CalculateAspectCrop(16.0 / 9.0),
                "4:3 Standard TV (1.333)" => CalculateAspectCrop(4.0 / 3.0),
                "9:16 Vertical Video (0.5625)" => CalculateAspectCrop(9.0 / 16.0),
                "2.39:1 CinemaScope (2.390)" => CalculateAspectCrop(2.39),
                "Match Output Aspect" => CalculateAspectCrop((double)vm.Project.Width / vm.Project.Height),
                "Match Source Aspect" => identity,
                _ => panCropPrefs.CustomPresets.FirstOrDefault(p => string.Equals(p.Name, selectedName, StringComparison.OrdinalIgnoreCase))?.ToState() ?? identity
            };
            Edit(() => editor.SetCropKeyframe(clipId, localFrame, preset, CurrentInterpolation, CurrentSmoothness));
        };
        presetRow.Children.Add(presets);

        var body = new Grid();
        body.ColumnDefinitions.Add(new() { Width = new GridLength(244) });
        body.ColumnDefinitions.Add(new());
        Grid.SetRow(body, 3); root.Children.Add(body);

        positionProperties = new StackPanel { Margin = new Thickness(40, 2, 3, 0) };
        maskProperties = new StackPanel { Margin = new Thickness(40, 2, 3, 0) };
        propertyScroll = new ScrollViewer { Content = positionProperties, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        body.Children.Add(propertyScroll);

        void Section(StackPanel panel, string label) => panel.Children.Add(new TextBlock { Text = label, Background = new SolidColorBrush(Color.FromRgb(75, 75, 75)), Padding = new Thickness(4, 1, 0, 1), Margin = new Thickness(0, 0, 0, 2) });
        void Field(string key, string label)
        {
            var row = new Grid { Height = 20 }; row.ColumnDefinitions.Add(new() { Width = new GridLength(107) }); row.ColumnDefinitions.Add(new());
            row.Children.Add(new TextBlock { Text = label + ":", Margin = new Thickness(15, 0, 0, 0) });
            var input = new TextBox { MinHeight = 0, Margin = new Thickness(0), Padding = new Thickness(4, 0, 0, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
            AutomationProperties.SetName(input, label); AutomationProperties.SetAutomationId(input, "Crop" + key);
            input.GotKeyboardFocus += (_, _) => input.SelectAll();
            input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyField(key); e.Handled = true; } else if (e.Key == Key.Escape) { Refresh(); e.Handled = true; } };
            input.LostKeyboardFocus += (_, _) => { if (!closed) ApplyField(key); };
            Grid.SetColumn(input, 1); row.Children.Add(input); fields.Add(key, input); positionProperties.Children.Add(row);
        }

        // Setup Position Properties
        Section(positionProperties, "Position"); Field("Width", "Width"); Field("Height", "Height"); Field("CenterX", "X center"); Field("CenterY", "Y center");
        Section(positionProperties, "Rotation"); Field("RotationDegrees", "Angle"); Field("PivotX", "Rotation X center"); Field("PivotY", "Rotation Y center");
        Section(positionProperties, "Keyframe interpolation");
        interpolation = new ComboBox { ItemsSource = Enum.GetValues<CropInterpolation>(), Margin = new Thickness(12, 2, 2, 4) };
        AutomationProperties.SetName(interpolation, "Keyframe interpolation");
        interpolation.SelectionChanged += (_, _) =>
        {
            if (refreshing || interpolation.SelectedItem is not CropInterpolation value) return;
            var isSmooth = value == CropInterpolation.Smooth;
            if (smoothnessField is not null) smoothnessField.IsEnabled = isSmooth;
            if (smoothnessSlider is not null) smoothnessSlider.IsEnabled = isSmooth;
            Edit(() => editor.SetCropKeyframe(clipId, localFrame, state, value, CurrentSmoothness));
        };
        positionProperties.Children.Add(interpolation);
        var smoothRow = new Grid { Height = 22, Margin = new Thickness(0, 2, 0, 4) };
        smoothRow.ColumnDefinitions.Add(new() { Width = new GridLength(107) });
        smoothRow.ColumnDefinitions.Add(new());
        smoothRow.Children.Add(new TextBlock { Text = "Smoothness:", Margin = new Thickness(15, 2, 0, 0) });
        var smoothPanel = new DockPanel { LastChildFill = true };
        smoothnessField = new TextBox { Width = 45, TextAlignment = TextAlignment.Right, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(2, 0, 2, 0) };
        AutomationProperties.SetName(smoothnessField, "Keyframe smoothness");
        DockPanel.SetDock(smoothnessField, Dock.Right);
        smoothPanel.Children.Add(smoothnessField);
        smoothnessSlider = new Slider { Minimum = 0, Maximum = 100, Value = 100, SmallChange = 5, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        AutomationProperties.SetName(smoothnessSlider, "Keyframe smoothness slider");
        smoothPanel.Children.Add(smoothnessSlider);
        Grid.SetColumn(smoothPanel, 1);
        smoothRow.Children.Add(smoothPanel);
        positionProperties.Children.Add(smoothRow);

        smoothnessSlider.ValueChanged += (_, e) =>
        {
            if (refreshing) return;
            var val = Math.Round(e.NewValue, 1);
            smoothnessField.Text = val.ToString("0.0", CultureInfo.InvariantCulture);
            Edit(() => editor.SetCropKeyframe(clipId, localFrame, state, CurrentInterpolation, val));
        };
        smoothnessField.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (double.TryParse(smoothnessField.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    v = Math.Clamp(v, 0, 100);
                    Edit(() => editor.SetCropKeyframe(clipId, localFrame, state, CurrentInterpolation, v));
                }
                e.Handled = true;
            }
        };
        Section(positionProperties, "Source");
        lockAspect = new CheckBox { Content = "Lock aspect ratio", IsChecked = true, Margin = new Thickness(12, 4, 0, 4) };
        positionProperties.Children.Add(lockAspect);

        // Setup Mask Properties
        Section(maskProperties, "Path");
        var modeRow = new Grid { Height = 24, Margin = new Thickness(0, 1, 0, 2) };
        modeRow.ColumnDefinitions.Add(new() { Width = new GridLength(107) }); modeRow.ColumnDefinitions.Add(new());
        modeRow.Children.Add(new TextBlock { Text = "Mode:", Margin = new Thickness(15, 3, 0, 0) });
        maskMode = new ComboBox { ItemsSource = Enum.GetValues<MaskMode>(), SelectedItem = MaskMode.Positive };
        AutomationProperties.SetName(maskMode, "Mask mode");
        maskMode.SelectionChanged += (_, _) =>
        {
            if (refreshing || maskMode.SelectedItem is not MaskMode m) return;
            var p = ActiveMaskPath;
            if (p != null) { p.Mode = m; CommitMask(); }
        };
        Grid.SetColumn(maskMode, 1); modeRow.Children.Add(maskMode);
        maskProperties.Children.Add(modeRow);

        maskAntiAlias = new CheckBox { Content = "Anti-alias", IsChecked = true, Margin = new Thickness(15, 3, 0, 3) };
        AutomationProperties.SetName(maskAntiAlias, "Mask anti alias");
        maskAntiAlias.Click += (_, _) =>
        {
            if (refreshing) return;
            var p = ActiveMaskPath;
            if (p != null) { p.AntiAlias = maskAntiAlias.IsChecked == true; CommitMask(); }
        };
        maskProperties.Children.Add(maskAntiAlias);

        var opacityRow = new Grid { Height = 22, Margin = new Thickness(0, 2, 0, 3) };
        opacityRow.ColumnDefinitions.Add(new() { Width = new GridLength(107) }); opacityRow.ColumnDefinitions.Add(new());
        opacityRow.Children.Add(new TextBlock { Text = "Opacity %:", Margin = new Thickness(15, 2, 0, 0) });
        var opacPanel = new DockPanel { LastChildFill = true };
        maskOpacityField = new TextBox { Width = 45, Text = "100.0", TextAlignment = TextAlignment.Right, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(2, 0, 2, 0) };
        AutomationProperties.SetName(maskOpacityField, "Mask opacity");
        DockPanel.SetDock(maskOpacityField, Dock.Right); opacPanel.Children.Add(maskOpacityField);
        maskOpacitySlider = new Slider { Minimum = 0, Maximum = 100, Value = 100, SmallChange = 5, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        AutomationProperties.SetName(maskOpacitySlider, "Mask opacity slider");
        opacPanel.Children.Add(maskOpacitySlider);
        Grid.SetColumn(opacPanel, 1); opacityRow.Children.Add(opacPanel);
        maskProperties.Children.Add(opacityRow);
        maskOpacitySlider.ValueChanged += (_, e) =>
        {
            if (refreshing) return;
            var val = Math.Round(e.NewValue, 1);
            maskOpacityField.Text = val.ToString("0.0", CultureInfo.InvariantCulture);
            var p = ActiveMaskPath;
            if (p != null) { p.Opacity = val; CommitMask(); }
        };
        maskOpacityField.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && double.TryParse(maskOpacityField.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                var p = ActiveMaskPath;
                if (p != null) { p.Opacity = Math.Clamp(v, 0, 100); CommitMask(); }
                e.Handled = true;
            }
        };

        var featherTypeRow = new Grid { Height = 24, Margin = new Thickness(0, 2, 0, 2) };
        featherTypeRow.ColumnDefinitions.Add(new() { Width = new GridLength(107) }); featherTypeRow.ColumnDefinitions.Add(new());
        featherTypeRow.Children.Add(new TextBlock { Text = "Feather type:", Margin = new Thickness(15, 3, 0, 0) });
        maskFeatherType = new ComboBox { ItemsSource = Enum.GetValues<MaskFeatherType>(), SelectedItem = MaskFeatherType.None };
        AutomationProperties.SetName(maskFeatherType, "Mask feather type");
        maskFeatherType.SelectionChanged += (_, _) =>
        {
            if (refreshing || maskFeatherType.SelectedItem is not MaskFeatherType ft) return;
            var p = ActiveMaskPath;
            if (p != null) { p.FeatherType = ft; CommitMask(); }
        };
        Grid.SetColumn(maskFeatherType, 1); featherTypeRow.Children.Add(maskFeatherType);
        maskProperties.Children.Add(featherTypeRow);

        var featherRow = new Grid { Height = 22, Margin = new Thickness(0, 2, 0, 4) };
        featherRow.ColumnDefinitions.Add(new() { Width = new GridLength(107) }); featherRow.ColumnDefinitions.Add(new());
        featherRow.Children.Add(new TextBlock { Text = "Feather %:", Margin = new Thickness(15, 2, 0, 0) });
        var feathPanel = new DockPanel { LastChildFill = true };
        maskFeatherField = new TextBox { Width = 45, Text = "0.0", TextAlignment = TextAlignment.Right, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(2, 0, 2, 0) };
        AutomationProperties.SetName(maskFeatherField, "Mask feather amount");
        DockPanel.SetDock(maskFeatherField, Dock.Right); feathPanel.Children.Add(maskFeatherField);
        maskFeatherSlider = new Slider { Minimum = 0, Maximum = 100, Value = 0, SmallChange = 5, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        AutomationProperties.SetName(maskFeatherSlider, "Mask feather slider");
        feathPanel.Children.Add(maskFeatherSlider);
        Grid.SetColumn(feathPanel, 1); featherRow.Children.Add(feathPanel);
        maskProperties.Children.Add(featherRow);
        maskFeatherSlider.ValueChanged += (_, e) =>
        {
            if (refreshing) return;
            var val = Math.Round(e.NewValue, 1);
            maskFeatherField.Text = val.ToString("0.0", CultureInfo.InvariantCulture);
            var p = ActiveMaskPath;
            if (p != null) { p.Feather = val; CommitMask(); }
        };
        maskFeatherField.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && double.TryParse(maskFeatherField.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                var p = ActiveMaskPath;
                if (p != null) { p.Feather = Math.Clamp(v, 0, 100); CommitMask(); }
                e.Handled = true;
            }
        };

        Section(maskProperties, "Keyframe interpolation");
        maskInterpolation = new ComboBox { ItemsSource = Enum.GetValues<CropInterpolation>(), Margin = new Thickness(12, 2, 2, 4) };
        AutomationProperties.SetName(maskInterpolation, "Mask keyframe interpolation");
        maskInterpolation.SelectionChanged += (_, _) =>
        {
            if (refreshing || maskInterpolation.SelectedItem is not CropInterpolation value) return;
            var isSmooth = value == CropInterpolation.Smooth;
            if (maskSmoothnessField is not null) maskSmoothnessField.IsEnabled = isSmooth;
            if (maskSmoothnessSlider is not null) maskSmoothnessSlider.IsEnabled = isSmooth;
            CommitMask();
        };
        maskProperties.Children.Add(maskInterpolation);
        var maskSmoothRow = new Grid { Height = 22, Margin = new Thickness(0, 2, 0, 4) };
        maskSmoothRow.ColumnDefinitions.Add(new() { Width = new GridLength(107) }); maskSmoothRow.ColumnDefinitions.Add(new());
        maskSmoothRow.Children.Add(new TextBlock { Text = "Smoothness:", Margin = new Thickness(15, 2, 0, 0) });
        var maskSmoothPanel = new DockPanel { LastChildFill = true };
        maskSmoothnessField = new TextBox { Width = 45, TextAlignment = TextAlignment.Right, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(2, 0, 2, 0) };
        AutomationProperties.SetName(maskSmoothnessField, "Mask keyframe smoothness");
        DockPanel.SetDock(maskSmoothnessField, Dock.Right); maskSmoothPanel.Children.Add(maskSmoothnessField);
        maskSmoothnessSlider = new Slider { Minimum = 0, Maximum = 100, Value = 100, SmallChange = 5, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        AutomationProperties.SetName(maskSmoothnessSlider, "Mask keyframe smoothness slider");
        maskSmoothPanel.Children.Add(maskSmoothnessSlider);
        Grid.SetColumn(maskSmoothPanel, 1); maskSmoothRow.Children.Add(maskSmoothPanel);
        maskProperties.Children.Add(maskSmoothRow);
        maskSmoothnessSlider.ValueChanged += (_, e) =>
        {
            if (refreshing) return;
            var val = Math.Round(e.NewValue, 1);
            maskSmoothnessField.Text = val.ToString("0.0", CultureInfo.InvariantCulture);
            CommitMask();
        };

        // Workspace Toolbar
        toolbar = new StackPanel { Width = 32, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 4, 0, 0) };

        Button MakeToolBtn(string content, string tooltip, string autoId, Action onClick)
        {
            var btn = new Button
            {
                Content = content,
                ToolTip = tooltip,
                Padding = new Thickness(2, 1, 2, 1),
                MinWidth = 26,
                Height = 24,
                Margin = new Thickness(0, 1, 0, 1),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            AutomationProperties.SetName(btn, tooltip);
            AutomationProperties.SetAutomationId(btn, autoId);
            btn.Click += (_, _) => onClick();
            return btn;
        }

        // Position tools
        Button moveFreeBtn = null!;
        Button moveXBtn = null!;
        Button moveYBtn = null!;

        void UpdateMoveRadios()
        {
            var activeBrush = (Brush)FindResource("SelectionBrush");
            var defaultBrush = (Brush)FindResource("ButtonBackgroundBrush");
            var activePen = Brushes.White;
            var defaultPen = (Brush)FindResource("BorderBrush");

            moveFreeBtn.Background = workspace.MoveConstraint == CropMoveConstraint.Free ? activeBrush : defaultBrush;
            moveFreeBtn.BorderBrush = workspace.MoveConstraint == CropMoveConstraint.Free ? activePen : defaultPen;
            moveXBtn.Background = workspace.MoveConstraint == CropMoveConstraint.HorizontalOnly ? activeBrush : defaultBrush;
            moveXBtn.BorderBrush = workspace.MoveConstraint == CropMoveConstraint.HorizontalOnly ? activePen : defaultPen;
            moveYBtn.Background = workspace.MoveConstraint == CropMoveConstraint.VerticalOnly ? activeBrush : defaultBrush;
            moveYBtn.BorderBrush = workspace.MoveConstraint == CropMoveConstraint.VerticalOnly ? activePen : defaultPen;
        }

        moveFreeBtn = MakeToolBtn("✥", "Move freely in X and Y", "CropMoveFree", () => { workspace.MoveConstraint = CropMoveConstraint.Free; UpdateMoveRadios(); });
        moveXBtn = MakeToolBtn("↔", "Move in X only", "CropMoveXOnly", () => { workspace.MoveConstraint = CropMoveConstraint.HorizontalOnly; UpdateMoveRadios(); });
        moveYBtn = MakeToolBtn("↕", "Move in Y only", "CropMoveYOnly", () => { workspace.MoveConstraint = CropMoveConstraint.VerticalOnly; UpdateMoveRadios(); });
        UpdateMoveRadios();

        var lockAspectBtn = MakeToolBtn("🔒", "Lock aspect ratio", "CropToolbarLockAspect", () => lockAspect.IsChecked = !(lockAspect.IsChecked == true));
        Button sizeCenterBtn = null!;
        void UpdateSizeCenterButton()
        {
            sizeCenterBtn.Background = workspace.SizeAboutCenter ? (Brush)FindResource("SelectionBrush") : (Brush)FindResource("ButtonBackgroundBrush");
            sizeCenterBtn.BorderBrush = workspace.SizeAboutCenter ? Brushes.White : (Brush)FindResource("BorderBrush");
        }
        sizeCenterBtn = MakeToolBtn("⌖", "Size about center", "CropSizeAboutCenter", () =>
        {
            workspace.SizeAboutCenter = !workspace.SizeAboutCenter;
            UpdateSizeCenterButton();
        });
        UpdateSizeCenterButton();

        lockAspect.Checked += (_, _) => { lockAspectBtn.Content = "🔒"; lockAspectBtn.Background = (Brush)FindResource("SelectionBrush"); lockAspectBtn.BorderBrush = Brushes.White; };
        lockAspect.Unchecked += (_, _) => { lockAspectBtn.Content = "🔓"; lockAspectBtn.Background = (Brush)FindResource("ButtonBackgroundBrush"); lockAspectBtn.BorderBrush = (Brush)FindResource("BorderBrush"); };

        positionTools.Add(moveFreeBtn);
        positionTools.Add(moveXBtn);
        positionTools.Add(moveYBtn);
        positionTools.Add(new Separator { Margin = new Thickness(2, 2, 2, 2) });
        positionTools.Add(lockAspectBtn);
        positionTools.Add(sizeCenterBtn);

        // Mask tools
        Button normalToolBtn = null!;
        Button penToolBtn = null!;
        Button splitToolBtn = null!;
        Button deleteToolBtn = null!;
        Button rectToolBtn = null!;
        Button ovalToolBtn = null!;

        void UpdateMaskToolButtons()
        {
            var activeBrush = (Brush)FindResource("SelectionBrush");
            var defaultBrush = (Brush)FindResource("ButtonBackgroundBrush");
            var activePen = Brushes.White;
            var defaultPen = (Brush)FindResource("BorderBrush");

            normalToolBtn.Background = activeMaskTool == MaskTool.Normal ? activeBrush : defaultBrush;
            normalToolBtn.BorderBrush = activeMaskTool == MaskTool.Normal ? activePen : defaultPen;
            penToolBtn.Background = activeMaskTool == MaskTool.Pen ? activeBrush : defaultBrush;
            penToolBtn.BorderBrush = activeMaskTool == MaskTool.Pen ? activePen : defaultPen;
            splitToolBtn.Background = activeMaskTool == MaskTool.SplitTangent ? activeBrush : defaultBrush;
            splitToolBtn.BorderBrush = activeMaskTool == MaskTool.SplitTangent ? activePen : defaultPen;
            deleteToolBtn.Background = activeMaskTool == MaskTool.DeleteAnchor ? activeBrush : defaultBrush;
            deleteToolBtn.BorderBrush = activeMaskTool == MaskTool.DeleteAnchor ? activePen : defaultPen;
            rectToolBtn.Background = activeMaskTool == MaskTool.Rectangle ? activeBrush : defaultBrush;
            rectToolBtn.BorderBrush = activeMaskTool == MaskTool.Rectangle ? activePen : defaultPen;
            ovalToolBtn.Background = activeMaskTool == MaskTool.Oval ? activeBrush : defaultBrush;
            ovalToolBtn.BorderBrush = activeMaskTool == MaskTool.Oval ? activePen : defaultPen;

            workspace.ActiveMaskTool = activeMaskTool;
        }

        normalToolBtn = MakeToolBtn("⬈", "Normal Edit Tool (Arrow)", "MaskToolNormal", () => { activeMaskTool = MaskTool.Normal; UpdateMaskToolButtons(); });
        penToolBtn = MakeToolBtn("✎", "Anchor Creation Tool (Pen)", "MaskToolPen", () => { activeMaskTool = MaskTool.Pen; UpdateMaskToolButtons(); });
        splitToolBtn = MakeToolBtn("∠", "Split Tangent Tool", "MaskToolSplitTangent", () => { activeMaskTool = MaskTool.SplitTangent; UpdateMaskToolButtons(); });
        deleteToolBtn = MakeToolBtn("⌫", "Delete Anchor Tool", "MaskToolDeleteAnchor", () => { activeMaskTool = MaskTool.DeleteAnchor; UpdateMaskToolButtons(); });
        rectToolBtn = MakeToolBtn("▭", "Rectangle Mask Tool", "MaskToolRectangle", () => { activeMaskTool = MaskTool.Rectangle; UpdateMaskToolButtons(); });
        ovalToolBtn = MakeToolBtn("◯", "Oval / Ellipse Mask Tool", "MaskToolOval", () => { activeMaskTool = MaskTool.Oval; UpdateMaskToolButtons(); });
        var clearMaskBtn = MakeToolBtn("✕", "Clear Mask", "MaskToolClear", () => Edit(() => editor.ResetMask(clipId)));

        maskTools.Add(normalToolBtn);
        maskTools.Add(penToolBtn);
        maskTools.Add(splitToolBtn);
        maskTools.Add(deleteToolBtn);
        maskTools.Add(new Separator { Margin = new Thickness(2, 2, 2, 2) });
        maskTools.Add(rectToolBtn);
        maskTools.Add(ovalToolBtn);
        maskTools.Add(clearMaskBtn);

        UpdateToolbar();

        body.Children.Add(toolbar);
        Grid.SetColumn(workspace, 1); body.Children.Add(workspace);

        workspace.PreviewChanged += value => { state = value; RefreshFields(); };
        workspace.Committed += value => Edit(() => editor.SetCropKeyframe(clipId, localFrame, value, CurrentInterpolation, CurrentSmoothness));
        workspace.Cancelled += Refresh;
        workspace.LockAspect = () => lockAspect.IsChecked == true;
        workspace.OnRestoreClicked += () => Edit(() => editor.SetCropKeyframe(clipId, localFrame, CropState.Identity(Source.Width, Source.Height), CurrentInterpolation, CurrentSmoothness));
        workspace.OnMatchOutputAspectClicked += () =>
        {
            var targetAspect = (double)vm.Project.Width / vm.Project.Height;
            var sourceAspect = (double)Source.Width / Source.Height;
            var w = sourceAspect > targetAspect ? Source.Height * targetAspect : Source.Width;
            var h = sourceAspect > targetAspect ? Source.Height : Source.Width / targetAspect;
            Edit(() => editor.SetCropKeyframe(clipId, localFrame, state with { Width = w, Height = h, CenterX = Source.Width / 2.0, CenterY = Source.Height / 2.0 }, CurrentInterpolation, CurrentSmoothness));
        };
        workspace.OnMatchSourceAspectClicked += () => Edit(() => editor.SetCropKeyframe(clipId, localFrame, CropState.Identity(Source.Width, Source.Height), CurrentInterpolation, CurrentSmoothness));
        workspace.OnCenterClicked += () => Edit(() => editor.SetCropKeyframe(clipId, localFrame, state with { CenterX = Source.Width / 2.0, CenterY = Source.Height / 2.0 }, CurrentInterpolation, CurrentSmoothness));
        workspace.OnFlipHorizontalClicked += () => Edit(() => editor.SetCropKeyframe(clipId, localFrame, state with { RotationDegrees = (180.0 - state.RotationDegrees) % 360.0 }, CurrentInterpolation, CurrentSmoothness));
        workspace.OnFlipVerticalClicked += () => Edit(() => editor.SetCropKeyframe(clipId, localFrame, state with { RotationDegrees = (-state.RotationDegrees) % 360.0 }, CurrentInterpolation, CurrentSmoothness));
        workspace.OnClearAllKeyframesClicked += () => Edit(() =>
        {
            var clip = EventClip;
            if (clip.Crop == null) return;
            var first = clip.Crop.Keyframes.FirstOrDefault()?.DeepClone() ?? new CropKeyframe { Frame = 0, State = CropState.Identity(Source.Width, Source.Height) };
            first.Frame = 0;
            clip.Crop.Keyframes.Clear();
            clip.Crop.Keyframes.Add(first);
        });

        // Mask workspace events
        workspace.MaskCommitted += shape => Edit(() => editor.SetMaskKeyframe(clipId, localFrame, shape, CurrentMaskInterpolation, CurrentMaskSmoothness));

        // Bottom timeline and track list
        var bottom = new Grid();
        bottom.RowDefinitions.Add(new() { Height = new GridLength(62) });
        bottom.RowDefinitions.Add(new() { Height = new GridLength(42) });
        Grid.SetRow(bottom, 4); root.Children.Add(bottom);

        lane.Margin = new Thickness(145, 0, 8, 0); bottom.Children.Add(lane);
        lane.Seek += SetFrame;
        lane.MoveKeyframe += MoveKeyframe;
        lane.InterpolationChanged += value =>
        {
            if (activeTrack == PanCropTrack.Position) interpolation.SelectedItem = value;
            else maskInterpolation.SelectedItem = value;
        };
        lane.DeleteKeyframeRequested += f => Edit(() =>
        {
            if (activeTrack == PanCropTrack.Position) editor.RemoveCropKeyframe(clipId, f);
            else editor.RemoveMaskKeyframe(clipId, f);
        });
        lane.AddKeyframeRequested += f => Edit(() =>
        {
            if (activeTrack == PanCropTrack.Position) editor.SetCropKeyframe(clipId, f, state, CurrentInterpolation, CurrentSmoothness);
            else editor.SetMaskKeyframe(clipId, f, maskState, CurrentMaskInterpolation, CurrentMaskSmoothness);
        });
        lane.GetKeyframeState = f => TimelineEvaluator.CropAt(EventClip, Source, f);
        lane.PasteKeyframeRequested += (f, st, interp, smooth) => Edit(() => editor.SetCropKeyframe(clipId, f, st, interp, smooth));
        lane.PasteMaskKeyframeRequested += (f, shape, interp, smooth) => Edit(() => editor.SetMaskKeyframe(clipId, f, shape, interp, smooth));

        // Dual Track List on the left
        var trackHeaders = new StackPanel { Width = 140, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 18, 0, 0) };

        positionTrackRow = new Border
        {
            Height = 20,
            Padding = new Thickness(6, 1, 4, 1),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = "Position", VerticalAlignment = VerticalAlignment.Center, FontSize = 11 }
        };
        AutomationProperties.SetName(positionTrackRow, "Position track");
        positionTrackRow.MouseLeftButtonDown += (_, _) => SelectTrack(PanCropTrack.Position);

        maskTrackRow = new Border
        {
            Height = 20,
            Margin = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(2, 1, 4, 1),
            Cursor = Cursors.Hand
        };
        AutomationProperties.SetName(maskTrackRow, "Mask track");
        var maskTrackPanel = new DockPanel { LastChildFill = true };
        maskTrackCheck = new CheckBox { Margin = new Thickness(2, 1, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(maskTrackCheck, "Enable mask");
        maskTrackCheck.Click += (_, e) =>
        {
            Edit(() => editor.SetMaskEnabled(clipId, maskTrackCheck.IsChecked == true));
            e.Handled = true;
        };
        maskTrackPanel.Children.Add(maskTrackCheck);
        maskTrackPanel.Children.Add(new TextBlock { Text = "Mask", VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
        maskTrackRow.Child = maskTrackPanel;
        maskTrackRow.MouseLeftButtonDown += (_, _) => SelectTrack(PanCropTrack.Mask);

        trackHeaders.Children.Add(positionTrackRow);
        trackHeaders.Children.Add(maskTrackRow);
        bottom.Children.Add(trackHeaders);

        var transport = new DockPanel { LastChildFill = false }; Grid.SetRow(transport, 1); bottom.Children.Add(transport);
        frameField = new TextBox { Width = 105, TextAlignment = TextAlignment.Right, Margin = new Thickness(3, 5, 6, 5) };
        AutomationProperties.SetName(frameField, "Crop frame"); AutomationProperties.SetAutomationId(frameField, "CropFrame");
        frameField.GotKeyboardFocus += (_, _) => frameField.SelectAll();
        frameField.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyFrame(); e.Handled = true; } };
        frameField.LostKeyboardFocus += (_, _) => { if (!closed) ApplyFrame(); };
        DockPanel.SetDock(frameField, Dock.Right); transport.Children.Add(frameField);
        var keyTime = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        keyTime.Children.Add(new TextBlock { Text = "Keyframe:", Margin = new Thickness(4, 0, 3, 0) });
        keyframeField = new TextBox { Width = 70, TextAlignment = TextAlignment.Right };
        AutomationProperties.SetName(keyframeField, "Crop keyframe frame"); AutomationProperties.SetAutomationId(keyframeField, "CropKeyframeFrame");
        keyframeField.GotKeyboardFocus += (_, _) => keyframeField.SelectAll();
        keyframeField.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyKeyframeTime(); e.Handled = true; } else if (e.Key == Key.Escape) { Refresh(); e.Handled = true; } };
        keyframeField.LostKeyboardFocus += (_, _) => { if (!closed) ApplyKeyframeTime(); };
        keyTime.Children.Add(keyframeField); DockPanel.SetDock(keyTime, Dock.Right); transport.Children.Add(keyTime);
        transport.Children.Add(Button("|◀", "Previous keyframe", () => SeekKey(-1)));
        transport.Children.Add(Button("◀", "Previous frame", () => SetFrame(localFrame - 1)));
        transport.Children.Add(Button("▶", "Next frame", () => SetFrame(localFrame + 1)));
        transport.Children.Add(Button("▶|", "Next keyframe", () => SeekKey(1)));
        transport.Children.Add(Button("◆+", "Add keyframe", () => Edit(() =>
        {
            if (activeTrack == PanCropTrack.Position) editor.SetCropKeyframe(clipId, localFrame, state, CurrentInterpolation, CurrentSmoothness);
            else editor.SetMaskKeyframe(clipId, localFrame, maskState, CurrentMaskInterpolation, CurrentMaskSmoothness);
        })));
        transport.Children.Add(Button("◆−", "Delete keyframe", () => Edit(() =>
        {
            if (activeTrack == PanCropTrack.Position) editor.RemoveCropKeyframe(clipId, localFrame);
            else editor.RemoveMaskKeyframe(clipId, localFrame);
        })));
        status = new TextBlock { Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        transport.Children.Add(status);
        Content = root;

        SourceInitialized += (_, _) => { var dark = 1; DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); };
        editor.Changed += EditorChanged;
        Closed += (_, _) => { closed = true; sourceRequest++; editor.Changed -= EditorChanged; };
        Loaded += (_, _) => { seek(checked(EventClip.StartFrame + localFrame)); Refresh(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && Keyboard.FocusedElement is not TextBox && !workspace.IsMouseCaptured && !lane.IsMouseCaptured && lane.ContextMenu?.IsOpen != true) { Close(); e.Handled = true; }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.Z or Key.Y && Keyboard.FocusedElement is not TextBox)
            { Edit(() => { if (e.Key == Key.Z) editor.Undo(); else editor.Redo(); }); e.Handled = true; }
        };
    }

    private TimelineClip EventClip => editor.Project.Tracks.SelectMany(t => t.Clips).Single(c => c.Id == clipId);
    private MediaAsset Source => editor.Project.Media.Single(m => m.Id == EventClip.MediaId);
    private MaskPath? ActiveMaskPath => maskState.Paths.Count > 0 ? maskState.Paths[0] : null;
    private CropInterpolation CurrentInterpolation => interpolation.SelectedItem is CropInterpolation value ? value : CropInterpolation.Linear;
    private double CurrentSmoothness => double.TryParse(smoothnessField?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 100) : 100.0;
    private CropInterpolation CurrentMaskInterpolation => maskInterpolation.SelectedItem is CropInterpolation value ? value : CropInterpolation.Linear;
    private double CurrentMaskSmoothness => double.TryParse(maskSmoothnessField?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 100) : 100.0;

    public void SelectTrack(PanCropTrack track)
    {
        activeTrack = track;
        UpdateTrackVisuals();
        propertyScroll.Content = track == PanCropTrack.Position ? positionProperties : maskProperties;
        UpdateToolbar();
        workspace.Mode = track == PanCropTrack.Position ? CropWorkspaceMode.Position : CropWorkspaceMode.Mask;
        lane.TrackKind = track == PanCropTrack.Position ? CropLaneTrackKind.Position : CropLaneTrackKind.Mask;
        Refresh();
    }

    private void UpdateTrackVisuals()
    {
        var activeBrush = (Brush)FindResource("SelectionBrush");
        var activeText = Brushes.White;
        var defaultBrush = Brushes.Transparent;
        var defaultText = Brushes.Silver;

        positionTrackRow.Background = activeTrack == PanCropTrack.Position ? activeBrush : defaultBrush;
        if (positionTrackRow.Child is TextBlock tbPos) tbPos.Foreground = activeTrack == PanCropTrack.Position ? activeText : defaultText;

        maskTrackRow.Background = activeTrack == PanCropTrack.Mask ? activeBrush : defaultBrush;
        if (maskTrackRow.Child is DockPanel dp && dp.Children.Count > 1 && dp.Children[1] is TextBlock tbMask)
            tbMask.Foreground = activeTrack == PanCropTrack.Mask ? activeText : defaultText;
    }

    private void UpdateToolbar()
    {
        toolbar.Children.Clear();
        var tools = activeTrack == PanCropTrack.Position ? positionTools : maskTools;
        foreach (var t in tools) toolbar.Children.Add(t);
        toolbar.Children.Add(new Separator { Margin = new Thickness(2, 2, 2, 2) });
        toolbar.Children.Add(Button("+", "Zoom workspace in", () => workspace.Zoom = Math.Min(8, workspace.Zoom * 1.25)));
        toolbar.Children.Add(Button("−", "Zoom workspace out", () => workspace.Zoom = Math.Max(.1, workspace.Zoom / 1.25)));
        toolbar.Children.Add(Button("Fit", "Fit source in workspace", () => workspace.Zoom = 1));
        toolbar.Children.Add(Button("1:1", "1:1 Normal zoom", () => workspace.Zoom = 1.0));
    }

    private void CommitMask() => Edit(() => editor.SetMaskKeyframe(clipId, localFrame, maskState, CurrentMaskInterpolation, CurrentMaskSmoothness));

    private void PopulatePresets(string? selectName = null)
    {
        panCropPrefs = PanCropPreferencesStore.Load(PanCropPreferencesPath);
        presetNames.Clear();
        presetNames.AddRange([
            "Full frame",
            "Half size",
            "Square center",
            "16:9 Widescreen TV (1.778)",
            "4:3 Standard TV (1.333)",
            "9:16 Vertical Video (0.5625)",
            "2.39:1 CinemaScope (2.390)",
            "Match Output Aspect",
            "Match Source Aspect"
        ]);
        foreach (var custom in panCropPrefs.CustomPresets)
        {
            presetNames.Add(custom.Name);
        }
        presets.ItemsSource = null;
        presets.ItemsSource = presetNames.ToArray();

        if (selectName != null) presets.SelectedItem = selectName;
    }

    private void SaveCurrentPreset()
    {
        var name = presets.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            status.Text = "Please enter a preset name to save.";
            return;
        }
        var preset = PanCropPreset.FromState(name, state);
        var list = new List<PanCropPreset>(panCropPrefs.CustomPresets);
        var existingIndex = list.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0) list[existingIndex] = preset;
        else list.Add(preset);

        panCropPrefs = panCropPrefs with { CustomPresets = list };
        PanCropPreferencesStore.Save(panCropPrefs, PanCropPreferencesPath);
        PopulatePresets(name);
        status.Text = $"Preset '{name}' saved.";
    }

    private void DeleteCurrentPreset()
    {
        var selected = presets.SelectedItem as string ?? presets.Text?.Trim();
        if (string.IsNullOrWhiteSpace(selected)) return;
        var list = new List<PanCropPreset>(panCropPrefs.CustomPresets);
        var removed = list.RemoveAll(p => string.Equals(p.Name, selected, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            panCropPrefs = panCropPrefs with { CustomPresets = list };
            PanCropPreferencesStore.Save(panCropPrefs, PanCropPreferencesPath);
            PopulatePresets("Full frame");
            status.Text = $"Preset '{selected}' deleted.";
        }
        else
        {
            status.Text = "Built-in presets cannot be deleted.";
        }
    }

    private static Button Button(string text, string label, Action action)
    {
        var button = new Button { Content = text, ToolTip = label, Padding = new Thickness(6, 1, 6, 1), MinHeight = 23, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, label); button.Click += (_, _) => action(); return button;
    }
    private void Edit(Action action)
    {
        try { action(); status.Text = ""; Refresh(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or OverflowException) { status.Text = ex.Message; Refresh(); }
    }
    private void EditorChanged(object? sender, EventArgs e)
    {
        if (editor.Project.Tracks.SelectMany(t => t.Clips).All(c => c.Id != clipId)) { Close(); return; }
        Refresh();
    }

    private void Refresh()
    {
        if (closed) return;
        localFrame = Math.Clamp(localFrame, 0, EventClip.DurationFrames - 1);
        state = TimelineEvaluator.CropAt(EventClip, Source, localFrame);
        maskState = TimelineEvaluator.MaskAt(EventClip, localFrame) ?? new MaskShape();
        refreshing = true;
        try
        {
            RefreshFields();
            frameField.Text = localFrame.ToString(CultureInfo.InvariantCulture);
            var identity = CropState.Identity(Source.Width, Source.Height);
            if (presets.SelectedIndex < 0)
            {
                var customMatch = panCropPrefs.CustomPresets.FindIndex(p => p.ToState() == state);
                if (customMatch >= 0) presets.SelectedIndex = 9 + customMatch;
            }
            presets.SelectedIndex = state == identity ? 0 : state == (identity with { Width = identity.Width / 2, Height = identity.Height / 2 }) ? 1
                : state == (identity with { Width = Math.Min(identity.Width, identity.Height), Height = Math.Min(identity.Width, identity.Height) }) ? 2 : -1;

            var current = checked(localFrame + EventClip.CropOffsetFrames);

            // Update Mask check
            maskTrackCheck.IsChecked = EventClip.Mask?.IsEnabled == true;
            UpdateTrackVisuals();

            if (activeTrack == PanCropTrack.Position)
            {
                keyframeField.IsEnabled = EventClip.Crop?.Keyframes.Any(key => key.Frame == current) == true;
                keyframeField.Text = keyframeField.IsEnabled ? localFrame.ToString(CultureInfo.InvariantCulture) : "";
                var activeKey = EventClip.Crop?.Keyframes.FirstOrDefault(key => key.Frame == current);
                var currentKey = activeKey ?? EventClip.Crop?.Keyframes.LastOrDefault(key => key.Frame <= current);
                interpolation.SelectedItem = currentKey?.Interpolation ?? CropInterpolation.Linear;
                var currentSmoothness = currentKey?.Smoothness ?? 100.0;
                smoothnessField.Text = currentSmoothness.ToString("0.0", CultureInfo.InvariantCulture);
                smoothnessSlider.Value = currentSmoothness;
                var isSmooth = interpolation.SelectedItem is CropInterpolation ci && ci == CropInterpolation.Smooth;
                if (smoothnessField is not null) smoothnessField.IsEnabled = isSmooth;
                if (smoothnessSlider is not null) smoothnessSlider.IsEnabled = isSmooth;
                lane.Interpolation = CurrentInterpolation;
                lane.CanMove = EventClip.Crop is not null;
                lane.KeyDetails = EventClip.Crop?.Keyframes.Select(key => (Frame: (long)((Int128)key.Frame - EventClip.CropOffsetFrames), key.Interpolation, key.Smoothness)).Where(item => item.Frame >= 0 && item.Frame < EventClip.DurationFrames).ToArray() ?? [(0, CropInterpolation.Linear, 100.0)];
            }
            else
            {
                keyframeField.IsEnabled = EventClip.Mask?.Keyframes.Any(key => key.Frame == current) == true;
                keyframeField.Text = keyframeField.IsEnabled ? localFrame.ToString(CultureInfo.InvariantCulture) : "";
                var activeKey = EventClip.Mask?.Keyframes.FirstOrDefault(key => key.Frame == current);
                var currentKey = activeKey ?? EventClip.Mask?.Keyframes.LastOrDefault(key => key.Frame <= current);
                maskInterpolation.SelectedItem = currentKey?.Interpolation ?? CropInterpolation.Linear;
                var currentSmoothness = currentKey?.Smoothness ?? 100.0;
                maskSmoothnessField.Text = currentSmoothness.ToString("0.0", CultureInfo.InvariantCulture);
                maskSmoothnessSlider.Value = currentSmoothness;
                var isSmooth = maskInterpolation.SelectedItem is CropInterpolation ci && ci == CropInterpolation.Smooth;
                if (maskSmoothnessField is not null) maskSmoothnessField.IsEnabled = isSmooth;
                if (maskSmoothnessSlider is not null) maskSmoothnessSlider.IsEnabled = isSmooth;

                var activePath = ActiveMaskPath;
                if (activePath != null)
                {
                    maskMode.SelectedItem = activePath.Mode;
                    maskAntiAlias.IsChecked = activePath.AntiAlias;
                    maskOpacitySlider.Value = activePath.Opacity;
                    maskOpacityField.Text = activePath.Opacity.ToString("0.0", CultureInfo.InvariantCulture);
                    maskFeatherType.SelectedItem = activePath.FeatherType;
                    maskFeatherSlider.Value = activePath.Feather;
                    maskFeatherField.Text = activePath.Feather.ToString("0.0", CultureInfo.InvariantCulture);
                }

                lane.Interpolation = CurrentMaskInterpolation;
                lane.CanMove = EventClip.Mask is not null;
                lane.KeyDetails = EventClip.Mask?.Keyframes.Select(key => (Frame: (long)((Int128)key.Frame - EventClip.CropOffsetFrames), key.Interpolation, key.Smoothness)).Where(item => item.Frame >= 0 && item.Frame < EventClip.DurationFrames).ToArray() ?? [(0, CropInterpolation.Linear, 100.0)];
            }

            workspace.State = state;
            workspace.SourceWidth = Math.Max(1, Source.Width);
            workspace.SourceHeight = Math.Max(1, Source.Height);
            workspace.Keyframes = EventClip.Crop?.Keyframes.Select(k => k.DeepClone()).ToList();
            workspace.CropOffsetFrames = EventClip.CropOffsetFrames;
            workspace.ActiveLocalFrame = localFrame;
            workspace.MaskState = maskState;
            workspace.InvalidateVisual();

            lane.Duration = EventClip.DurationFrames;
            lane.Position = localFrame;
            lane.Rate = editor.Project.FrameRate;
            lane.InvalidateVisual();
        }
        finally { refreshing = false; }
        if (IsLoaded) _ = LoadSourceAsync();
    }

    private void RefreshFields()
    {
        var values = new[] { state.Width, state.Height, state.CenterX, state.CenterY, state.RotationDegrees, state.PivotX, state.PivotY };
        var index = 0;
        foreach (var field in fields.Values) field.Text = values[index++].ToString("0.######", CultureInfo.InvariantCulture);
    }
    private void ApplyField(string key)
    {
        if (refreshing) return;
        var displayed = key switch { "Width" => state.Width, "Height" => state.Height, "CenterX" => state.CenterX, "CenterY" => state.CenterY, "RotationDegrees" => state.RotationDegrees, "PivotX" => state.PivotX, _ => state.PivotY };
        if (fields[key].Text == displayed.ToString("0.######", CultureInfo.InvariantCulture)) return;
        if (!double.TryParse(fields[key].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
        { status.Text = "Enter a finite number."; Refresh(); return; }
        var next = key switch
        {
            "Width" => state with { Width = value, Height = lockAspect.IsChecked == true ? state.Height * value / state.Width : state.Height },
            "Height" => state with { Height = value, Width = lockAspect.IsChecked == true ? state.Width * value / state.Height : state.Width },
            "CenterX" => state with { CenterX = value }, "CenterY" => state with { CenterY = value },
            "RotationDegrees" => state with { RotationDegrees = value }, "PivotX" => state with { PivotX = value }, _ => state with { PivotY = value }
        };
        if (next != state) Edit(() => editor.SetCropKeyframe(clipId, localFrame, next, CurrentInterpolation, CurrentSmoothness));
    }
    private void ApplyFrame()
    {
        if (refreshing) return;
        if (long.TryParse(frameField.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame)) SetFrame(frame);
        else { status.Text = "Enter a whole project frame."; Refresh(); }
    }
    private void ApplyKeyframeTime()
    {
        if (refreshing || !keyframeField.IsEnabled) return;
        if (!long.TryParse(keyframeField.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame)) { status.Text = "Enter a whole project frame."; Refresh(); return; }
        if (frame != localFrame) MoveKeyframe(localFrame, frame);
    }
    private void MoveKeyframe(long from, long to) => Edit(() =>
    {
        if (activeTrack == PanCropTrack.Position)
            editor.MoveCropKeyframe(clipId, from, to, preserveDestination: true);
        else
            editor.MoveMaskKeyframe(clipId, from, to, preserveDestination: true);
        localFrame = to; seek(checked(EventClip.StartFrame + to));
    });
    private void SetFrame(long frame)
    {
        localFrame = Math.Clamp(frame, 0, EventClip.DurationFrames - 1);
        seek(checked(EventClip.StartFrame + localFrame)); Refresh();
    }
    private void SeekKey(int direction)
    {
        var keys = lane.Keys;
        SetFrame(direction < 0 ? keys.Where(frame => frame < localFrame).DefaultIfEmpty(0).Max()
            : keys.Where(frame => frame > localFrame).DefaultIfEmpty(EventClip.DurationFrames - 1).Min());
    }
    private async Task LoadSourceAsync()
    {
        var desired = (Source.Path, checked(EventClip.SourceInFrame + localFrame), Source.Width, Source.Height, editor.Project.FrameRate);
        if (requestedSource == desired) return;
        requestedSource = desired; sourceRequest++; sourcePending = true;
        if (sourceRunning) return;
        sourceRunning = true;
        try
        {
            while (sourcePending && !closed)
            {
                sourcePending = false;
                var request = sourceRequest; var source = requestedSource!.Value;
                try
                {
                    var width = Math.Min(1280, Math.Max(1, source.Width)); var height = Math.Clamp((int)Math.Round(width * Math.Max(1, source.Height) / (double)Math.Max(1, source.Width)), 1, 4096);
                    var seconds = source.Rate.Seconds(source.Frame);
                    var pixels = await Task.Run(() => vm.Media.GetFrame(source.Path, seconds, width, height));
                    if (closed || request != sourceRequest) continue;
                    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); bitmap.Freeze();
                    workspace.Image = bitmap; workspace.InvalidateVisual();
                }
                catch (Exception ex) { if (!closed && request == sourceRequest) { workspace.Image = null; workspace.InvalidateVisual(); status.Text = "Source preview: " + ex.Message; } }
            }
        }
        finally { sourceRunning = false; }
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

public enum CropMoveConstraint { Free, HorizontalOnly, VerticalOnly }
public enum CropWorkspaceMode { Position, Mask }

internal sealed class CropWorkspace : FrameworkElement
{
    public const int RotationHandleIndex = 4;
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    public ImageSource? Image { get; set; }
    public int SourceWidth { get; set; } = 1;
    public int SourceHeight { get; set; } = 1;
    public CropState State { get; set; }
    public CropWorkspaceMode Mode { get; set; } = CropWorkspaceMode.Position;
    public MaskTool ActiveMaskTool { get; set; } = MaskTool.Normal;
    public MaskShape MaskState { get; set; } = new();
    public int SelectedPathIndex { get; set; } = 0;
    public int SelectedPointIndex { get; set; } = -1;
    public int SelectedTangentIndex { get; set; } = -1; // 1 = In, 2 = Out
    public List<CropKeyframe>? Keyframes { get; set; }
    public long CropOffsetFrames { get; set; }
    public long ActiveLocalFrame { get; set; }
    public Func<bool>? LockAspect { get; set; }
    public bool SizeAboutCenter { get; set; } = true;
    public CropMoveConstraint MoveConstraint { get; set; } = CropMoveConstraint.Free;
    public event Action<CropState>? PreviewChanged;
    public event Action<CropState>? Committed;
    public event Action<MaskShape>? MaskCommitted;
    public event Action? Cancelled;
    public event Action? OnRestoreClicked;
    public event Action? OnMatchOutputAspectClicked;
    public event Action? OnMatchSourceAspectClicked;
    public event Action? OnFlipHorizontalClicked;
    public event Action? OnFlipVerticalClicked;
    public event Action? OnCenterClicked;
    public event Action? OnClearAllKeyframesClicked;

    private double zoom = 1;
    public double Zoom { get => zoom; set { zoom = value; InvalidateVisual(); } }
    private CropState? dragState;
    private Point origin;
    private Point maskDragStart;
    private int resizeCorner = -1;
    private bool draggingMask;
    private Rect SourceRect => new((ActualWidth - SourceWidth * Scale) / 2, (ActualHeight - SourceHeight * Scale) / 2, SourceWidth * Scale, SourceHeight * Scale);
    private double Scale => Math.Max(.000001, Math.Min(Math.Max(1, ActualWidth - 60) / SourceWidth, Math.Max(1, ActualHeight - 60) / SourceHeight) * zoom);

    public CropWorkspace()
    {
        Focusable = true; ClipToBounds = true;
        AutomationProperties.SetName(this, "Pan/Crop source workspace");
        AutomationProperties.SetHelpText(this, "Drag crop or mask. Position mode: drag corners to resize, circular handle to rotate. Mask mode: use tools to create and edit Bézier anchor points and tangents.");

        ContextMenu = new ContextMenu();
        AutomationProperties.SetName(ContextMenu, "Crop workspace context menu");
        var restoreItem = new MenuItem { Header = "Restore (Default)" };
        restoreItem.Click += (_, _) => OnRestoreClicked?.Invoke();
        var matchAspectItem = new MenuItem { Header = "Match Output Aspect" };
        matchAspectItem.Click += (_, _) => OnMatchOutputAspectClicked?.Invoke();
        var matchSourceAspectItem = new MenuItem { Header = "Match Source Aspect" };
        matchSourceAspectItem.Click += (_, _) => OnMatchSourceAspectClicked?.Invoke();
        var flipHItem = new MenuItem { Header = "Flip Horizontal" };
        flipHItem.Click += (_, _) => OnFlipHorizontalClicked?.Invoke();
        var flipVItem = new MenuItem { Header = "Flip Vertical" };
        flipVItem.Click += (_, _) => OnFlipVerticalClicked?.Invoke();
        var centerItem = new MenuItem { Header = "Center" };
        centerItem.Click += (_, _) => OnCenterClicked?.Invoke();
        var clearAllItem = new MenuItem { Header = "Clear All Keyframes" };
        clearAllItem.Click += (_, _) => OnClearAllKeyframesClicked?.Invoke();

        ContextMenu.Items.Add(restoreItem);
        ContextMenu.Items.Add(matchAspectItem);
        ContextMenu.Items.Add(matchSourceAspectItem);
        ContextMenu.Items.Add(new Separator());
        ContextMenu.Items.Add(flipHItem);
        ContextMenu.Items.Add(flipVItem);
        ContextMenu.Items.Add(new Separator());
        ContextMenu.Items.Add(centerItem);
        ContextMenu.Items.Add(new Separator());
        ContextMenu.Items.Add(clearAllItem);
    }

    private Point ToDisplay(Point source) => new(SourceRect.X + source.X * Scale, SourceRect.Y + source.Y * Scale);
    private Point ToSource(Point display) => new((display.X - SourceRect.X) / Scale, (display.Y - SourceRect.Y) / Scale);
    private Point Rotate(Point source)
    {
        var angle = State.RotationDegrees * Math.PI / 180; var x = source.X - State.PivotX; var y = source.Y - State.PivotY;
        return new(State.PivotX + Math.Cos(angle) * x - Math.Sin(angle) * y, State.PivotY + Math.Sin(angle) * x + Math.Cos(angle) * y);
    }
    private Point RotationAnchor() => ToDisplay(Rotate(new Point(State.CenterX, State.CenterY - State.Height / 2 - 28 / Scale)));
    private Point[] Corners() => new[] { new Point(State.CenterX - State.Width / 2, State.CenterY - State.Height / 2), new Point(State.CenterX + State.Width / 2, State.CenterY - State.Height / 2), new Point(State.CenterX + State.Width / 2, State.CenterY + State.Height / 2), new Point(State.CenterX - State.Width / 2, State.CenterY + State.Height / 2) }.Select(point => ToDisplay(Rotate(point))).ToArray();
    private int CornerAt(Point point)
    {
        var rot = RotationAnchor();
        if ((rot - point).Length <= 10) return RotationHandleIndex;
        var corners = Corners(); var nearest = Enumerable.Range(0, corners.Length).OrderBy(i => (corners[i] - point).LengthSquared).First();
        return (corners[nearest] - point).Length <= 14 ? nearest : -1;
    }

    private void UpdateCursor(Point point)
    {
        if (Mode == CropWorkspaceMode.Mask)
        {
            Cursor = ActiveMaskTool switch
            {
                MaskTool.Pen => Cursors.Pen,
                MaskTool.Rectangle => Cursors.Cross,
                MaskTool.Oval => Cursors.Cross,
                MaskTool.DeleteAnchor => Cursors.No,
                MaskTool.SplitTangent => Cursors.Hand,
                _ => Cursors.Arrow
            };
            return;
        }

        var corner = dragState is not null ? resizeCorner : CornerAt(point);
        if (corner == RotationHandleIndex) { Cursor = Cursors.Hand; return; }
        if (corner < 0) { Cursor = Cursors.SizeAll; return; }
        var angle = State.RotationDegrees + (corner is 0 or 2 ? 45 : -45);
        var direction = ((int)Math.Round(angle / 45) % 4 + 4) % 4;
        Cursor = direction switch { 0 => Cursors.SizeWE, 1 => Cursors.SizeNWSE, 2 => Cursors.SizeNS, _ => Cursors.SizeNESW };
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(225, 225, 225)), null, new Rect(RenderSize));
        if (Image is not null) dc.DrawImage(Image, SourceRect);

        if (Mode == CropWorkspaceMode.Position)
        {
            RenderPositionCrop(dc);
        }
        else
        {
            RenderMask(dc);
        }
    }

    private void RenderPositionCrop(DrawingContext dc)
    {
        var points = Corners(); var pen = new Pen(Brushes.Black, 1) { DashStyle = DashStyles.Dash };
        for (var i = 0; i < points.Length; i++)
        {
            dc.DrawLine(new Pen(Brushes.White, 1), points[i], points[(i + 1) % 4]);
            dc.DrawLine(pen, points[i], points[(i + 1) % 4]);
            dc.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 1), new Rect(points[i].X - 3, points[i].Y - 3, 6, 6));
        }

        var topCenter = ToDisplay(Rotate(new Point(State.CenterX, State.CenterY - State.Height / 2)));
        var rotAnchor = RotationAnchor();
        dc.DrawLine(new Pen(Brushes.White, 1), topCenter, rotAnchor);
        dc.DrawLine(new Pen(Brushes.Black, 1) { DashStyle = DashStyles.Dash }, topCenter, rotAnchor);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)), new Pen(Brushes.White, 1.5), rotAnchor, 4.5, 4.5);

        var pivot = ToDisplay(new Point(State.PivotX, State.PivotY)); dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 1), pivot, 4, 4);

        var mark = new[] { new Point(-.20, .35), new Point(-.20, -.35), new Point(.25, -.35), new Point(.25, -.20), new Point(-.04, -.20), new Point(-.04, -.03), new Point(.17, -.03), new Point(.17, .12), new Point(-.04, .12), new Point(-.04, .35), new Point(-.20, .35) };
        var path = mark.Select(p => ToDisplay(Rotate(new Point(State.CenterX + p.X * State.Width, State.CenterY + p.Y * State.Height)))).ToArray();
        for (var i = 1; i < path.Length; i++) { dc.DrawLine(new Pen(Brushes.White, 1), path[i - 1], path[i]); dc.DrawLine(pen, path[i - 1], path[i]); }

        if (Keyframes is not null && Keyframes.Count >= 2)
        {
            var pathPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xA2, 0xE8)), 1.5) { DashStyle = new DashStyle([4, 2], 0) };
            var anim = new CropAnimation { Keyframes = Keyframes };
            var minFrame = Keyframes[0].Frame;
            var maxFrame = Keyframes[^1].Frame;
            var totalFrames = Math.Max(1, maxFrame - minFrame);
            var step = Math.Max(1L, totalFrames / 60);

            Point prevPoint = default;
            var hasPrev = false;
            for (var f = minFrame; f <= maxFrame; f += step)
            {
                var evalState = anim.Evaluate(f);
                var pt = ToDisplay(Rotate(new Point(evalState.CenterX, evalState.CenterY)));
                if (hasPrev) dc.DrawLine(pathPen, prevPoint, pt);
                prevPoint = pt;
                hasPrev = true;
            }
            if (minFrame != maxFrame)
            {
                var lastState = anim.Evaluate(maxFrame);
                var pt = ToDisplay(Rotate(new Point(lastState.CenterX, lastState.CenterY)));
                if (hasPrev) dc.DrawLine(pathPen, prevPoint, pt);
            }

            for (var i = 0; i < Keyframes.Count; i++)
            {
                var kf = Keyframes[i];
                var pt = ToDisplay(Rotate(new Point(kf.State.CenterX, kf.State.CenterY)));
                var isCurrent = (kf.Frame - CropOffsetFrames) == ActiveLocalFrame;
                dc.PushTransform(new RotateTransform(45, pt.X, pt.Y));
                if (isCurrent)
                {
                    dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xA2, 0xE8)), 2), new Rect(pt.X - 4, pt.Y - 4, 8, 8));
                }
                else
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x00, 0xA2, 0xE8)), new Pen(Brushes.White, 1), new Rect(pt.X - 3, pt.Y - 3, 6, 6));
                }
                dc.Pop();
            }
        }
    }

    private void RenderMask(DrawingContext dc)
    {
        if (MaskState.Paths.Count == 0) return;

        var maskOutlinePen = new Pen(new SolidColorBrush(Color.FromRgb(255, 204, 0)), 1.5);
        var shadowPen = new Pen(Brushes.Black, 3);
        var tangentPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)), 1);

        for (var pIdx = 0; pIdx < MaskState.Paths.Count; pIdx++)
        {
            var path = MaskState.Paths[pIdx];
            if (path.Points.Count == 0) continue;

            // Build smooth Bézier geometry
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                var p0 = ToDisplay(new Point(path.Points[0].X, path.Points[0].Y));
                ctx.BeginFigure(p0, isFilled: path.IsClosed, isClosed: path.IsClosed);
                var segs = path.IsClosed ? path.Points.Count : path.Points.Count - 1;
                for (var i = 0; i < segs; i++)
                {
                    var cur = path.Points[i];
                    var next = path.Points[(i + 1) % path.Points.Count];
                    var dNext = ToDisplay(new Point(next.X, next.Y));

                    var hasOut = Math.Abs(cur.OutTangentX) > 1e-4 || Math.Abs(cur.OutTangentY) > 1e-4;
                    var hasIn = Math.Abs(next.InTangentX) > 1e-4 || Math.Abs(next.InTangentY) > 1e-4;

                    if (hasOut || hasIn)
                    {
                        var c1 = ToDisplay(new Point(cur.X + cur.OutTangentX, cur.Y + cur.OutTangentY));
                        var c2 = ToDisplay(new Point(next.X + next.InTangentX, next.Y + next.InTangentY));
                        ctx.BezierTo(c1, c2, dNext, isStroked: true, isSmoothJoin: true);
                    }
                    else
                    {
                        ctx.LineTo(dNext, isStroked: true, isSmoothJoin: false);
                    }
                }
            }
            geom.Freeze();

            // Draw translucent fill tint and outline
            var fillBrush = new SolidColorBrush(Color.FromArgb(35, 0, 162, 232));
            dc.DrawGeometry(fillBrush, shadowPen, geom);
            dc.DrawGeometry(null, maskOutlinePen, geom);

            // Draw anchor points and tangent handles
            for (var ptIdx = 0; ptIdx < path.Points.Count; ptIdx++)
            {
                var pt = path.Points[ptIdx];
                var anchorDisp = ToDisplay(new Point(pt.X, pt.Y));
                var isSelected = (pIdx == SelectedPathIndex && ptIdx == SelectedPointIndex);

                // If anchor is selected, draw tangent lines and knobs
                if (isSelected)
                {
                    var inDisp = ToDisplay(new Point(pt.X + pt.InTangentX, pt.Y + pt.InTangentY));
                    var outDisp = ToDisplay(new Point(pt.X + pt.OutTangentX, pt.Y + pt.OutTangentY));

                    dc.DrawLine(tangentPen, anchorDisp, inDisp);
                    dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)), 1.5), inDisp, 3.5, 3.5);

                    dc.DrawLine(tangentPen, anchorDisp, outDisp);
                    dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)), 1.5), outDisp, 3.5, 3.5);
                }

                // Draw anchor square (7x7)
                var anchorRect = new Rect(anchorDisp.X - 3.5, anchorDisp.Y - 3.5, 7, 7);
                if (isSelected)
                {
                    dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)), 2), anchorRect);
                }
                else
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(40, 40, 40)), new Pen(Brushes.White, 1.2), anchorRect);
                }
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        origin = e.GetPosition(this);

        if (Mode == CropWorkspaceMode.Position)
        {
            dragState = State;
            resizeCorner = CornerAt(origin);
            UpdateCursor(origin);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Mask mode handling
        var srcPos = ToSource(origin);
        maskDragStart = srcPos;
        draggingMask = true;

        if (ActiveMaskTool == MaskTool.Rectangle || ActiveMaskTool == MaskTool.Oval)
        {
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (ActiveMaskTool == MaskTool.Pen)
        {
            var path = MaskState.Paths.Count > 0 ? MaskState.Paths[0] : null;
            if (path == null)
            {
                path = new MaskPath { IsClosed = false };
                MaskState.Paths.Add(path);
            }

            // If clicking near first point of open path with >= 3 points, close it!
            if (!path.IsClosed && path.Points.Count >= 3)
            {
                var firstDisp = ToDisplay(new Point(path.Points[0].X, path.Points[0].Y));
                if ((firstDisp - origin).Length <= 10)
                {
                    path.IsClosed = true;
                    SelectedPointIndex = 0;
                    MaskCommitted?.Invoke(MaskState.DeepClone());
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }
            }

            // Append new anchor point
            var newPt = new MaskPoint(srcPos.X, srcPos.Y);
            path.Points.Add(newPt);
            SelectedPathIndex = 0;
            SelectedPointIndex = path.Points.Count - 1;
            SelectedTangentIndex = 2; // Dragging Out tangent
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (ActiveMaskTool == MaskTool.DeleteAnchor)
        {
            var hit = HitTestAnchor(origin);
            if (hit.HasValue)
            {
                var path = MaskState.Paths[hit.Value.PathIndex];
                path.Points.RemoveAt(hit.Value.PointIndex);
                if (path.Points.Count < 2) MaskState.Paths.Remove(path);
                SelectedPointIndex = -1;
                MaskCommitted?.Invoke(MaskState.DeepClone());
                InvalidateVisual();
            }
            e.Handled = true;
            return;
        }

        // Normal Edit Tool or Split Tangent Tool
        var tanHit = HitTestTangent(origin);
        if (tanHit.HasValue)
        {
            SelectedPathIndex = tanHit.Value.PathIndex;
            SelectedPointIndex = tanHit.Value.PointIndex;
            SelectedTangentIndex = tanHit.Value.TangentIndex;
            if (ActiveMaskTool == MaskTool.SplitTangent)
            {
                var pt = MaskState.Paths[SelectedPathIndex].Points[SelectedPointIndex];
                pt.IsSplit = true;
            }
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var ancHit = HitTestAnchor(origin);
        if (ancHit.HasValue)
        {
            SelectedPathIndex = ancHit.Value.PathIndex;
            SelectedPointIndex = ancHit.Value.PointIndex;
            SelectedTangentIndex = -1;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Click on empty canvas deselects
        SelectedPointIndex = -1;
        SelectedTangentIndex = -1;
        InvalidateVisual();
    }

    private (int PathIndex, int PointIndex)? HitTestAnchor(Point dispPos)
    {
        for (var pIdx = 0; pIdx < MaskState.Paths.Count; pIdx++)
        {
            var path = MaskState.Paths[pIdx];
            for (var ptIdx = 0; ptIdx < path.Points.Count; ptIdx++)
            {
                var ptDisp = ToDisplay(new Point(path.Points[ptIdx].X, path.Points[ptIdx].Y));
                if ((ptDisp - dispPos).Length <= 8) return (pIdx, ptIdx);
            }
        }
        return null;
    }

    private (int PathIndex, int PointIndex, int TangentIndex)? HitTestTangent(Point dispPos)
    {
        if (SelectedPathIndex < 0 || SelectedPathIndex >= MaskState.Paths.Count) return null;
        var path = MaskState.Paths[SelectedPathIndex];
        if (SelectedPointIndex < 0 || SelectedPointIndex >= path.Points.Count) return null;
        var pt = path.Points[SelectedPointIndex];

        var inDisp = ToDisplay(new Point(pt.X + pt.InTangentX, pt.Y + pt.InTangentY));
        if ((inDisp - dispPos).Length <= 8) return (SelectedPathIndex, SelectedPointIndex, 1);

        var outDisp = ToDisplay(new Point(pt.X + pt.OutTangentX, pt.Y + pt.OutTangentY));
        if ((outDisp - dispPos).Length <= 8) return (SelectedPathIndex, SelectedPointIndex, 2);

        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var curPos = e.GetPosition(this);
        if (Mode == CropWorkspaceMode.Mask)
        {
            if (!draggingMask || !IsMouseCaptured) { UpdateCursor(curPos); return; }

            var srcCur = ToSource(curPos);

            if (ActiveMaskTool == MaskTool.Rectangle)
            {
                var x = Math.Min(maskDragStart.X, srcCur.X);
                var y = Math.Min(maskDragStart.Y, srcCur.Y);
                var w = Math.Abs(srcCur.X - maskDragStart.X);
                var h = Math.Abs(srcCur.Y - maskDragStart.Y);
                MaskState = MaskShape.CreateRectangle(x, y, w, h);
                InvalidateVisual();
                return;
            }

            if (ActiveMaskTool == MaskTool.Oval)
            {
                var cx = (maskDragStart.X + srcCur.X) * 0.5;
                var cy = (maskDragStart.Y + srcCur.Y) * 0.5;
                var rx = Math.Abs(srcCur.X - maskDragStart.X) * 0.5;
                var ry = Math.Abs(srcCur.Y - maskDragStart.Y) * 0.5;
                MaskState = MaskShape.CreateOval(cx, cy, rx, ry);
                InvalidateVisual();
                return;
            }

            if (SelectedPathIndex >= 0 && SelectedPathIndex < MaskState.Paths.Count)
            {
                var path = MaskState.Paths[SelectedPathIndex];
                if (SelectedPointIndex >= 0 && SelectedPointIndex < path.Points.Count)
                {
                    var pt = path.Points[SelectedPointIndex];

                    if (SelectedTangentIndex == 1) // Dragging In tangent
                    {
                        var dx = srcCur.X - pt.X;
                        var dy = srcCur.Y - pt.Y;
                        pt.InTangentX = dx;
                        pt.InTangentY = dy;
                        if (!pt.IsSplit && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                        {
                            pt.OutTangentX = -dx;
                            pt.OutTangentY = -dy;
                        }
                    }
                    else if (SelectedTangentIndex == 2) // Dragging Out tangent
                    {
                        var dx = srcCur.X - pt.X;
                        var dy = srcCur.Y - pt.Y;
                        pt.OutTangentX = dx;
                        pt.OutTangentY = dy;
                        if (!pt.IsSplit && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                        {
                            pt.InTangentX = -dx;
                            pt.InTangentY = -dy;
                        }
                    }
                    else // Dragging anchor
                    {
                        pt.X = srcCur.X;
                        pt.Y = srcCur.Y;
                    }
                    InvalidateVisual();
                }
            }
            return;
        }

        // Position crop dragging
        if (dragState is not { } before) { UpdateCursor(curPos); return; }
        var delta = (curPos - origin) / Scale;
        if (resizeCorner == RotationHandleIndex)
        {
            var pivotDisplay = ToDisplay(new Point(before.PivotX, before.PivotY));
            var mousePos = curPos;
            var rad = Math.Atan2(mousePos.Y - pivotDisplay.Y, mousePos.X - pivotDisplay.X);
            var deg = rad * 180.0 / Math.PI + 90.0;
            while (deg > 180.0) deg -= 360.0;
            while (deg <= -180.0) deg += 360.0;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) deg = Math.Round(deg / 15.0) * 15.0;
            State = before with { RotationDegrees = deg };
        }
        else if (resizeCorner >= 0)
        {
            var angle = -before.RotationDegrees * Math.PI / 180;
            var lx = Math.Cos(angle) * delta.X - Math.Sin(angle) * delta.Y;
            var ly = Math.Sin(angle) * delta.X + Math.Cos(angle) * delta.Y;

            if (SizeAboutCenter)
            {
                var widthDelta = (resizeCorner is 0 or 3 ? -2 : 2) * lx;
                var heightDelta = (resizeCorner is 0 or 1 ? -2 : 2) * ly;
                var width = Math.Max(.001, before.Width + widthDelta);
                var height = Math.Max(.001, before.Height + heightDelta);
                if (LockAspect?.Invoke() == true)
                {
                    if (Math.Abs(heightDelta / before.Height) > Math.Abs(widthDelta / before.Width)) width = before.Width * height / before.Height;
                    else height = before.Height * width / before.Width;
                }
                State = before with { Width = width, Height = height };
            }
            else
            {
                var signX = resizeCorner is 0 or 3 ? -1 : 1;
                var signY = resizeCorner is 0 or 1 ? -1 : 1;
                var widthDelta = signX * lx;
                var heightDelta = signY * ly;
                var width = Math.Max(.001, before.Width + widthDelta);
                var height = Math.Max(.001, before.Height + heightDelta);

                if (LockAspect?.Invoke() == true)
                {
                    if (Math.Abs(heightDelta / before.Height) > Math.Abs(widthDelta / before.Width)) width = before.Width * height / before.Height;
                    else height = before.Height * width / before.Width;
                }

                var localShiftX = (signX * (width - before.Width)) * 0.5;
                var localShiftY = (signY * (height - before.Height)) * 0.5;
                var rotAngle = before.RotationDegrees * Math.PI / 180;
                var shiftX = Math.Cos(rotAngle) * localShiftX - Math.Sin(rotAngle) * localShiftY;
                var shiftY = Math.Sin(rotAngle) * localShiftX + Math.Cos(rotAngle) * localShiftY;

                State = before with { Width = width, Height = height, CenterX = before.CenterX + shiftX, CenterY = before.CenterY + shiftY };
            }
        }
        else
        {
            var effectiveDelta = delta;
            if (MoveConstraint == CropMoveConstraint.HorizontalOnly) effectiveDelta = new Vector(delta.X, 0);
            else if (MoveConstraint == CropMoveConstraint.VerticalOnly) effectiveDelta = new Vector(0, delta.Y);

            var angle = -before.RotationDegrees * Math.PI / 180;
            State = before with
            {
                CenterX = before.CenterX + Math.Cos(angle) * effectiveDelta.X - Math.Sin(angle) * effectiveDelta.Y,
                CenterY = before.CenterY + Math.Sin(angle) * effectiveDelta.X + Math.Cos(angle) * effectiveDelta.Y
            };
        }
        PreviewChanged?.Invoke(State); InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (Mode == CropWorkspaceMode.Mask)
        {
            if (draggingMask)
            {
                draggingMask = false;
                ReleaseMouseCapture();
                MaskCommitted?.Invoke(MaskState.DeepClone());
                InvalidateVisual();
                e.Handled = true;
            }
            return;
        }

        if (dragState is null) return;
        var before = dragState.Value; dragState = null; ReleaseMouseCapture();
        if (State != before) Committed?.Invoke(State);
        UpdateCursor(e.GetPosition(this)); e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (Mode == CropWorkspaceMode.Mask)
        {
            draggingMask = false;
            InvalidateVisual();
            return;
        }
        if (dragState is not { } before) return;
        dragState = null; State = before; Cancelled?.Invoke(); Cursor = null; InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); if (!IsMouseCaptured) Cursor = null; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (dragState is not null) { dragState = null; ReleaseMouseCapture(); Cancelled?.Invoke(); e.Handled = true; return; }
            if (draggingMask) { draggingMask = false; ReleaseMouseCapture(); InvalidateVisual(); e.Handled = true; return; }
        }
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var x = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
        var y = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
        if (x != 0 || y != 0)
        {
            if (Mode == CropWorkspaceMode.Position)
            {
                Committed?.Invoke(State with { CenterX = State.CenterX + x, CenterY = State.CenterY + y });
            }
            else if (SelectedPathIndex >= 0 && SelectedPathIndex < MaskState.Paths.Count)
            {
                var path = MaskState.Paths[SelectedPathIndex];
                if (SelectedPointIndex >= 0 && SelectedPointIndex < path.Points.Count)
                {
                    var pt = path.Points[SelectedPointIndex];
                    pt.X += x; pt.Y += y;
                    MaskCommitted?.Invoke(MaskState.DeepClone());
                    InvalidateVisual();
                }
            }
            e.Handled = true;
        }
    }
}

public enum CropLaneTrackKind { Position, Mask }

internal sealed class CropKeyframeLane : FrameworkElement
{
    public static (CropState State, CropInterpolation Interpolation, double Smoothness)? CopiedCropKeyframe { get; private set; }
    public static (MaskShape Shape, CropInterpolation Interpolation, double Smoothness)? CopiedMaskKeyframe { get; private set; }

    public CropLaneTrackKind TrackKind { get; set; } = CropLaneTrackKind.Position;
    public Func<long, CropState>? GetKeyframeState { get; set; }
    public Func<long, MaskShape>? GetMaskKeyframeState { get; set; }
    public long Duration { get; set; } = 1;
    public long Position { get; set; }
    private (long Frame, CropInterpolation Interpolation, double Smoothness)[] keyDetails = [(0, CropInterpolation.Linear, 100.0)];
    public (long Frame, CropInterpolation Interpolation, double Smoothness)[] KeyDetails
    {
        get => keyDetails;
        set
        {
            keyDetails = value ?? [];
            Keys = keyDetails.Select(k => k.Frame).ToArray();
            InvalidateVisual();
        }
    }
    public long[] Keys { get; set; } = [0];
    public bool CanMove { get; set; }
    public CropInterpolation Interpolation { get; set; }
    public Rational Rate { get; set; } = new(30, 1);
    public event Action<long>? Seek;
    public event Action<long, long>? MoveKeyframe;
    public event Action<CropInterpolation>? InterpolationChanged;
    public event Action<long>? DeleteKeyframeRequested;
    public event Action<long>? AddKeyframeRequested;
    public event Action<long, CropState, CropInterpolation, double>? PasteKeyframeRequested;
    public event Action<long, MaskShape, CropInterpolation, double>? PasteMaskKeyframeRequested;

    private readonly Dictionary<CropInterpolation, MenuItem> curveItems = [];
    private readonly MenuItem copyItem;
    private readonly MenuItem pasteItem;
    private readonly MenuItem deleteItem;
    private long? activeMenuKey;
    private long? dragging;
    private long pendingFrame;

    private double FrameX(long frame) => 4 + frame * Math.Max(1, ActualWidth - 10) / Math.Max(1.0, Duration);
    private long FrameAt(double x) => (long)Math.Clamp(decimal.Round((decimal)Math.Clamp((x - 4) / Math.Max(1, ActualWidth - 10), 0, 1) * Duration, 0, MidpointRounding.AwayFromZero), 0, Duration - 1);

    public CropKeyframeLane()
    {
        ClipToBounds = true; Focusable = true;
        AutomationProperties.SetName(this, "Keyframe timeline");
        AutomationProperties.SetHelpText(this, "Click to seek. Drag keyframe diamond to move; right-click for interpolation menu.");
        ContextMenu = new ContextMenu();
        AutomationProperties.SetName(ContextMenu, "Keyframe interpolation");
        AutomationProperties.SetAutomationId(ContextMenu, "CropCurveMenu");
        foreach (var curve in new[] { CropInterpolation.Linear, CropInterpolation.Fast, CropInterpolation.Slow, CropInterpolation.Smooth, CropInterpolation.Sharp, CropInterpolation.Hold })
        {
            var item = new MenuItem { Header = curve.ToString(), IsCheckable = true };
            item.Click += (_, _) => InterpolationChanged?.Invoke(curve);
            curveItems.Add(curve, item); ContextMenu.Items.Add(item);
        }
        ContextMenu.Items.Add(new Separator());
        copyItem = new MenuItem { Header = "Copy Keyframe", InputGestureText = "Ctrl+C" };
        copyItem.Click += (_, _) => CopyCurrentKeyframe();
        ContextMenu.Items.Add(copyItem);

        pasteItem = new MenuItem { Header = "Paste Keyframe", InputGestureText = "Ctrl+V" };
        pasteItem.Click += (_, _) => PasteCurrentKeyframe();
        ContextMenu.Items.Add(pasteItem);

        ContextMenu.Items.Add(new Separator());
        deleteItem = new MenuItem { Header = "Delete", InputGestureText = "Del" };
        deleteItem.Click += (_, _) =>
        {
            if (activeMenuKey is > 0) DeleteKeyframeRequested?.Invoke(activeMenuKey.Value);
        };
        ContextMenu.Items.Add(deleteItem);
        ContextMenuOpening += (_, e) =>
        {
            var key = e.CursorLeft < 0 ? Keys.Contains(Position) ? Position : (long?)null : KeyAt(new Point(e.CursorLeft, e.CursorTop));
            if (key is null) { e.Handled = true; return; }
            PrepareCurveMenu(key.Value);
            copyItem.IsEnabled = true;
            pasteItem.IsEnabled = TrackKind == CropLaneTrackKind.Position ? CopiedCropKeyframe != null : CopiedMaskKeyframe != null;
            ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            ContextMenu.HorizontalOffset = 0; ContextMenu.VerticalOffset = 0;
        };
    }

    private long? KeyAt(Point point)
    {
        if (Keys.Length == 0 || Math.Abs(point.Y - 31) > 10) return null;
        var nearest = Keys.OrderBy(frame => Math.Abs(FrameX(frame) - point.X)).First();
        return Math.Abs(FrameX(nearest) - point.X) < 7 ? nearest : null;
    }

    private void CopyCurrentKeyframe()
    {
        var frame = activeMenuKey ?? Position;
        var detail = KeyDetails.FirstOrDefault(k => k.Frame == frame);
        var interp = detail.Frame == frame ? detail.Interpolation : Interpolation;
        var smooth = detail.Frame == frame ? detail.Smoothness : 100.0;

        if (TrackKind == CropLaneTrackKind.Position)
        {
            var st = GetKeyframeState?.Invoke(frame) ?? CropState.Identity(1920, 1080);
            CopiedCropKeyframe = (st, interp, smooth);
        }
        else
        {
            var shape = GetMaskKeyframeState?.Invoke(frame) ?? new MaskShape();
            CopiedMaskKeyframe = (shape, interp, smooth);
        }
    }

    private void PasteCurrentKeyframe()
    {
        if (TrackKind == CropLaneTrackKind.Position)
        {
            if (CopiedCropKeyframe is not { } copied) return;
            PasteKeyframeRequested?.Invoke(Position, copied.State, copied.Interpolation, copied.Smoothness);
        }
        else
        {
            if (CopiedMaskKeyframe is not { } copied) return;
            PasteMaskKeyframeRequested?.Invoke(Position, copied.Shape, copied.Interpolation, copied.Smoothness);
        }
    }

    private void PrepareCurveMenu(long frame)
    {
        activeMenuKey = frame;
        deleteItem.IsEnabled = frame > 0;
        Seek?.Invoke(frame);
        var detail = KeyDetails.FirstOrDefault(k => k.Frame == frame);
        foreach (var (curve, item) in curveItems) item.IsChecked = curve == (detail.Frame == frame ? detail.Interpolation : Interpolation);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(45, 45, 45)), null, new Rect(RenderSize));
        var pen = new Pen(Brushes.Gray, 1);
        dc.DrawLine(pen, new Point(0, 19), new Point(ActualWidth, 19));
        dc.DrawLine(pen, new Point(0, 43), new Point(ActualWidth, 43));
        for (var i = 0; i <= 6; i++)
        {
            var frame = (long)((Int128)Duration * i / 6); var x = i * Math.Max(1, ActualWidth - 10) / 6;
            var text = new FormattedText(OpenRend.UI.TimelineView.FormatTimecode(frame, Rate), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, Brushes.Silver, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(x + 3, 2));
        }
        foreach (var (frame, interp, smooth) in KeyDetails)
        {
            var x = FrameX(dragging == frame ? pendingFrame : frame);
            var isSelected = frame == Position;
            var fill = interp switch
            {
                CropInterpolation.Smooth => new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                CropInterpolation.Fast => new SolidColorBrush(Color.FromRgb(0x32, 0xCD, 0x32)),
                CropInterpolation.Slow => new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)),
                CropInterpolation.Sharp => new SolidColorBrush(Color.FromRgb(0xBF, 0x5A, 0xF2)),
                CropInterpolation.Hold => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)),
                _ => isSelected ? Brushes.White : Brushes.Silver
            };
            var borderPen = new Pen(isSelected ? Brushes.White : Brushes.Black, 1);
            if (interp == CropInterpolation.Hold)
            {
                dc.DrawRectangle(fill, borderPen, new Rect(x - 3, 28, 6, 6));
            }
            else
            {
                dc.PushTransform(new RotateTransform(45, x, 31));
                dc.DrawRectangle(fill, borderPen, new Rect(x - 3, 28, 6, 6));
                dc.Pop();
            }
        }
        var cursor = FrameX(Position);
        dc.DrawLine(new Pen(Brushes.White, 1), new Point(cursor, 0), new Point(cursor, ActualHeight));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus(); var point = e.GetPosition(this); var frame = FrameAt(point.X);
        var near = Keys.OrderBy(key => Math.Abs((double)key - frame)).FirstOrDefault();
        var isNearKey = Keys.Length > 0 && Math.Abs(FrameX(near) - point.X) < 7;
        if (isNearKey)
        {
            frame = near;
            if (CanMove && Math.Abs(point.Y - 31) <= 10) { dragging = near; pendingFrame = near; CaptureMouse(); }
        }
        else if (e.ClickCount == 2)
        {
            AddKeyframeRequested?.Invoke(frame);
        }
        Seek?.Invoke(frame); e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging is null)
        {
            var pos = e.GetPosition(this);
            var nearest = KeyDetails.FirstOrDefault(k => Math.Abs(FrameX(k.Frame) - pos.X) < 7 && Math.Abs(pos.Y - 31) <= 10);
            if (nearest.Frame != 0 || (KeyDetails.Length > 0 && Math.Abs(FrameX(KeyDetails[0].Frame) - pos.X) < 7 && Math.Abs(pos.Y - 31) <= 10))
            {
                ToolTip = $"Frame {nearest.Frame}: {nearest.Interpolation}{(nearest.Interpolation == CropInterpolation.Smooth ? $" (Smoothness: {nearest.Smoothness:0.0}%)" : "")}";
            }
            else
            {
                ToolTip = null;
            }
            return;
        }
        pendingFrame = FrameAt(e.GetPosition(this).X); InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (dragging is not { } from) return;
        dragging = null; ReleaseMouseCapture();
        if (pendingFrame != from) MoveKeyframe?.Invoke(from, pendingFrame);
        InvalidateVisual(); e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) { dragging = null; InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.C)
            {
                CopyCurrentKeyframe();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.V && (CopiedCropKeyframe != null || CopiedMaskKeyframe != null))
            {
                PasteCurrentKeyframe();
                e.Handled = true;
                return;
            }
        }
        if (e.Key == Key.Delete && Position > 0 && Keys.Contains(Position))
        {
            DeleteKeyframeRequested?.Invoke(Position);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Apps || e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (Keys.Contains(Position))
            {
                PrepareCurveMenu(Position);
                ContextMenu!.PlacementTarget = this; ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint;
                ContextMenu.HorizontalOffset = FrameX(Position); ContextMenu.VerticalOffset = 43; ContextMenu.IsOpen = true;
            }
            e.Handled = true; return;
        }
        if (e.Key == Key.Escape && dragging is not null) { dragging = null; ReleaseMouseCapture(); InvalidateVisual(); e.Handled = true; return; }
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            Seek?.Invoke(e.Key switch { Key.Home => 0, Key.End => Duration - 1, Key.Left => Math.Max(0, Position - 1), _ => Math.Min(Duration - 1, Position + 1) });
            e.Handled = true;
        }
    }
}
