using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using OpenRend.Core;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro docked Color Grading panel hosted in MainWindow.
/// Provides category navigation (LUTs, Color Wheels, Utilities, Curves),
/// dual 3D LUT slots with built-in camera/look presets, interactive color wheels,
/// curves, tone, levels, white balance, and HSL controls.
/// </summary>
public sealed class ColorGradingDockControl : UserControl
{
    private EditorViewModel? vm;
    private Action? previewCallback;
    private Action? popoutCallback;
    private TimelineClip? activeClip;
    private Guid activeEffectId = Guid.Empty;
    private bool updating;

    // Header controls
    private readonly TextBlock targetLabel;
    private readonly ToggleButton bypassBtn;
    private readonly Button resetAllBtn;
    private readonly ComboBox presetCombo;
    private readonly Button popoutBtn;

    // Navigation Tree / List
    private readonly ListBox navList;

    // Pages Container
    private readonly Grid pageContainer;
    private readonly Grid wheelsPage;
    private readonly Grid toneLevelsPage;
    private readonly Grid wbHslPage;
    private readonly Grid curvesPage;
    private readonly Grid lutsPage;

    // Color Wheels
    private readonly ColorGradingWheelControl liftWheel;
    private readonly Slider liftYSlider;
    private readonly TextBlock liftReadout;

    private readonly ColorGradingWheelControl gammaWheel;
    private readonly Slider gammaYSlider;
    private readonly TextBlock gammaReadout;

    private readonly ColorGradingWheelControl gainWheel;
    private readonly Slider gainYSlider;
    private readonly TextBlock gainReadout;

    private readonly ColorGradingWheelControl offsetWheel;
    private readonly Slider offsetYSlider;
    private readonly TextBlock offsetReadout;

    // Tone & Levels
    private readonly Slider inMinSlider, inMaxSlider, outMinSlider, outMaxSlider;
    private readonly TextBlock inMinVal, inMaxVal, outMinVal, outMaxVal;
    private readonly Slider exposureSlider, brightnessSlider, contrastSlider, contrastCtrSlider, saturationSlider;
    private readonly TextBlock exposureVal, brightnessVal, contrastVal, contrastCtrVal, saturationVal;

    // White Balance & HSL
    private readonly Slider temperatureSlider, tintSlider;
    private readonly TextBlock temperatureVal, tintVal;
    private readonly Slider hueSlider, satHslSlider, valHslSlider;
    private readonly TextBlock hueVal, satHslVal, valHslVal;

    // Curves
    private readonly ColorCurvesControl curvesControl;
    private readonly TextBlock curveReadout;

    // LUT Slots
    private readonly TextBlock cameraLutPathText;
    private readonly ComboBox cameraLutPresetCombo;
    private readonly Slider cameraLutStrengthSlider;
    private readonly TextBlock cameraLutStrengthVal;
    private readonly ComboBox cameraLutInterpCombo;
    private readonly CheckBox cameraLutBypassCheck;

    private readonly TextBlock lookLutPathText;
    private readonly ComboBox lookLutPresetCombo;
    private readonly Slider lookLutStrengthSlider;
    private readonly TextBlock lookLutStrengthVal;
    private readonly ComboBox lookLutInterpCombo;
    private readonly CheckBox lookLutBypassCheck;

    private VideoEffect? ActiveEffect => activeClip?.VideoEffects.FirstOrDefault(e => e.Id == activeEffectId);

    public ColorGradingDockControl()
    {
        var panelBrush = new SolidColorBrush(Color.FromRgb(32, 32, 32));
        var headerBrush = new SolidColorBrush(Color.FromRgb(45, 45, 45));
        var borderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55));
        var textBrush = Brushes.White;

        Background = panelBrush;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); // Header
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Body

        // --- 1. Top Header Bar (1:1 Vegas Pro) ---
        var header = new DockPanel { Background = headerBrush, Height = 32 };
        AutomationProperties.SetAutomationId(header, "ColorGradingHeader");

        var headerLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var fxBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(50, 100, 160)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(0, 0, 6, 0)
        };
        fxBadge.Child = new TextBlock { Text = "fx", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        headerLeft.Children.Add(fxBadge);

        targetLabel = new TextBlock
        {
            Text = "Event FX:  (Not active)",
            FontWeight = FontWeights.SemiBold,
            Foreground = textBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(targetLabel, "ColorGrading_TargetLabel");
        headerLeft.Children.Add(targetLabel);
        header.Children.Add(headerLeft);

        var headerRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(headerRight, Dock.Right);

        bypassBtn = new ToggleButton
        {
            Content = "Bypass",
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 4, 0)
        };
        AutomationProperties.SetAutomationId(bypassBtn, "m_BypassButton");
        bypassBtn.Click += OnBypassClick;
        headerRight.Children.Add(bypassBtn);

        resetAllBtn = new Button
        {
            Content = "Reset All",
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 4, 0)
        };
        AutomationProperties.SetAutomationId(resetAllBtn, "m_ResetAllButton");
        resetAllBtn.Click += OnResetAllClick;
        headerRight.Children.Add(resetAllBtn);

        presetCombo = new ComboBox
        {
            Width = 140,
            Margin = new Thickness(0, 0, 6, 0)
        };
        presetCombo.Items.Add("(None)");
        foreach (var name in BuiltinLutLibrary.LookLutNames) presetCombo.Items.Add(name);
        presetCombo.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(presetCombo, "ColorGrading_PresetCombo");
        presetCombo.SelectionChanged += OnPresetComboChanged;
        headerRight.Children.Add(presetCombo);

        popoutBtn = new Button
        {
            Content = "Open Floating Panel…",
            Padding = new Thickness(8, 2, 8, 2)
        };
        AutomationProperties.SetAutomationId(popoutBtn, "ColorGrading_PopoutButton");
        popoutBtn.Click += (_, _) => popoutCallback?.Invoke();
        headerRight.Children.Add(popoutBtn);

        header.Children.Add(headerRight);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // --- 2. Main Body: Left Navigation + Right Pages ---
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left Navigation List
        navList = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(36, 36, 36)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = borderBrush,
            Padding = new Thickness(4)
        };
        AutomationProperties.SetAutomationId(navList, "ColorGradingNavigation");

        navList.Items.Add(MakeNavItem("LUTs", "LUTsItem"));
        navList.Items.Add(MakeNavItem("Color Wheels", "ColorWheelsItem"));
        navList.Items.Add(MakeNavItem("Tone & Levels", "UtilitiesItem"));
        navList.Items.Add(MakeNavItem("White Balance / HSL", "WbHslItem"));
        navList.Items.Add(MakeNavItem("Curves", "CurvesItem"));

        navList.SelectedIndex = 0;
        navList.SelectionChanged += (_, _) => SelectPage(navList.SelectedIndex);
        Grid.SetColumn(navList, 0);
        body.Children.Add(navList);

        // Right Pages Container
        pageContainer = new Grid { Margin = new Thickness(8) };

        // Page 0: LUTs
        lutsPage = CreateLutsPage(
            out cameraLutPathText, out cameraLutPresetCombo, out cameraLutStrengthSlider, out cameraLutStrengthVal,
            out cameraLutInterpCombo, out cameraLutBypassCheck,
            out lookLutPathText, out lookLutPresetCombo, out lookLutStrengthSlider, out lookLutStrengthVal,
            out lookLutInterpCombo, out lookLutBypassCheck);

        // Page 1: Color Wheels
        wheelsPage = CreateWheelsPage(
            out liftWheel, out liftYSlider, out liftReadout,
            out gammaWheel, out gammaYSlider, out gammaReadout,
            out gainWheel, out gainYSlider, out gainReadout,
            out offsetWheel, out offsetYSlider, out offsetReadout);

        // Page 2: Tone & Levels
        toneLevelsPage = CreateToneLevelsPage(
            out inMinSlider, out inMinVal, out inMaxSlider, out inMaxVal,
            out outMinSlider, out outMinVal, out outMaxSlider, out outMaxVal,
            out exposureSlider, out exposureVal, out brightnessSlider, out brightnessVal,
            out contrastSlider, out contrastVal, out contrastCtrSlider, out contrastCtrVal,
            out saturationSlider, out saturationVal);

        // Page 3: White Balance & HSL
        wbHslPage = CreateWbHslPage(
            out temperatureSlider, out temperatureVal, out tintSlider, out tintVal,
            out hueSlider, out hueVal, out satHslSlider, out satHslVal,
            out valHslSlider, out valHslVal);

        // Page 4: Curves
        curvesPage = CreateCurvesPage(out curvesControl, out curveReadout);

        pageContainer.Children.Add(lutsPage);
        pageContainer.Children.Add(wheelsPage);
        pageContainer.Children.Add(toneLevelsPage);
        pageContainer.Children.Add(wbHslPage);
        pageContainer.Children.Add(curvesPage);

        Grid.SetColumn(pageContainer, 1);
        body.Children.Add(pageContainer);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        Content = root;
        SelectPage(0);
        SetEnabled(false);
    }

    private static ListBoxItem MakeNavItem(string label, string autoId)
    {
        var item = new ListBoxItem
        {
            Content = "▶  " + label,
            Padding = new Thickness(8, 6, 8, 6),
            Foreground = Brushes.White,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        AutomationProperties.SetAutomationId(item, autoId);
        return item;
    }

    public void Initialize(EditorViewModel viewModel, Action requestPreview, Action openPopout)
    {
        vm = viewModel;
        previewCallback = requestPreview;
        popoutCallback = openPopout;
    }

    public void BindToClip(TimelineClip? clip)
    {
        activeClip = clip;
        if (clip is null || vm is null)
        {
            targetLabel.Text = "Event FX:  (Not active)";
            activeEffectId = Guid.Empty;
            SetEnabled(false);
            return;
        }

        targetLabel.Text = $"Event FX:  {clip.Name}";
        var existing = clip.VideoEffects.FirstOrDefault(e => e.Kind == VideoEffectKind.ColorGrading);
        if (existing is null)
        {
            var added = vm.Editor.AddScopedVideoEffect(EffectScope.Event, clip.Id, VideoEffectKind.ColorGrading);
            activeEffectId = added.Id;
        }
        else
        {
            activeEffectId = existing.Id;
        }

        SetEnabled(true);
        UpdateControlsFromEffect();
    }

    private void SetEnabled(bool enabled)
    {
        bypassBtn.IsEnabled = enabled;
        resetAllBtn.IsEnabled = enabled;
        presetCombo.IsEnabled = enabled;
        pageContainer.IsEnabled = enabled;
    }

    private void SelectPage(int index)
    {
        lutsPage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        wheelsPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        toneLevelsPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        wbHslPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        curvesPage.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Page 0: LUTs ---

    private Grid CreateLutsPage(
        out TextBlock camPath, out ComboBox camPreset, out Slider camStrength, out TextBlock camStrengthV,
        out ComboBox camInterp, out CheckBox camBypass,
        out TextBlock lookPath, out ComboBox lookPreset, out Slider lookStrength, out TextBlock lookStrengthV,
        out ComboBox lookInterp, out CheckBox lookBypass)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var headerBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40));
        var borderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));

        // Camera LUT Slot 0
        var camBorder = new Border
        {
            Background = headerBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4),
            Padding = new Thickness(10)
        };
        var camPanel = new StackPanel();
        var camHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        camHeader.Children.Add(new TextBlock { Text = "Camera LUT (Input Slot)", FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        var resetCamBtn = new Button { Content = "Reset", Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetCamBtn, "cameraLut_ResetButton");
        resetCamBtn.Click += (_, _) => ResetLutSlot(0);
        DockPanel.SetDock(resetCamBtn, Dock.Right);
        camHeader.Children.Add(resetCamBtn);
        camPanel.Children.Add(camHeader);

        camPath = new TextBlock { Text = "(No LUT loaded)", Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis };
        camPanel.Children.Add(camPath);

        var camPresetRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        camPresetRow.Children.Add(new TextBlock { Text = "Preset:", Width = 65, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White });
        var camPresetCtrl = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        camPresetCtrl.Items.Add("(None / Custom)");
        foreach (var name in BuiltinLutLibrary.CameraLutNames) camPresetCtrl.Items.Add(name);
        camPresetCtrl.SelectedIndex = 0;
        camPreset = camPresetCtrl;
        AutomationProperties.SetAutomationId(camPreset, "CameraLut_Preset");
        camPresetCtrl.SelectionChanged += (_, _) =>
        {
            if (updating || camPresetCtrl.SelectedIndex <= 0) return;
            var preset = BuiltinLutLibrary.CreatePreset(camPresetCtrl.SelectedItem.ToString()!);
            if (preset is not null) SetLutSlot(0, preset);
        };
        camPresetRow.Children.Add(camPreset);
        camPanel.Children.Add(camPresetRow);

        var camBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var camBrowse = new Button { Content = "Browse…", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetAutomationId(camBrowse, "cameraLut_BrowseButton");
        camBrowse.Click += (_, _) => BrowseLutSlot(0);
        var camClear = new Button { Content = "Clear", Padding = new Thickness(8, 2, 8, 2) };
        AutomationProperties.SetAutomationId(camClear, "cameraLut_Clear");
        camClear.Click += (_, _) => SetLutSlot(0, null);
        camBtns.Children.Add(camBrowse);
        camBtns.Children.Add(camClear);
        camPanel.Children.Add(camBtns);

        camPanel.Children.Add(MakeSliderRow("Strength", "cameraLut_Strength", 0, 1, 1, 0.01, out camStrength, out camStrengthV, v => CommitSingleParam("LUTGain_0", v)));

        var camInterpRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        camInterpRow.Children.Add(new TextBlock { Text = "Interpolation", Width = 90, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White });
        var camInterpCtrl = new ComboBox { Items = { "Trilinear", "Tetrahedral" }, SelectedIndex = 0, Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
        camInterp = camInterpCtrl;
        AutomationProperties.SetAutomationId(camInterp, "cameraLut_tetrahedral_ToggleButton");
        camInterpCtrl.SelectionChanged += (_, _) => { if (!updating) CommitSingleParam("LUTInterpolation_0", camInterpCtrl.SelectedIndex); };
        camInterpRow.Children.Add(camInterp);
        camPanel.Children.Add(camInterpRow);

        var camBypassCtrl = new CheckBox { Content = "Bypass Camera LUT", Margin = new Thickness(0, 4, 0, 0) };
        camBypass = camBypassCtrl;
        AutomationProperties.SetAutomationId(camBypass, "cameraLut_Bypass");
        camBypassCtrl.Click += (_, _) => { if (!updating) CommitSingleParam("LUTEnable_0", camBypassCtrl.IsChecked == true ? 1 : 0); };
        camPanel.Children.Add(camBypass);

        camBorder.Child = camPanel;
        Grid.SetColumn(camBorder, 0);
        grid.Children.Add(camBorder);

        // Look LUT Slot 1
        var lookBorder = new Border
        {
            Background = headerBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4),
            Padding = new Thickness(10)
        };
        var lookPanel = new StackPanel();
        var lookHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        lookHeader.Children.Add(new TextBlock { Text = "Look LUT (Output Slot)", FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        var resetLookBtn = new Button { Content = "Reset", Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetLookBtn, "lookLut_ResetButton");
        resetLookBtn.Click += (_, _) => ResetLutSlot(1);
        DockPanel.SetDock(resetLookBtn, Dock.Right);
        lookHeader.Children.Add(resetLookBtn);
        lookPanel.Children.Add(lookHeader);

        lookPath = new TextBlock { Text = "(No LUT loaded)", Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis };
        lookPanel.Children.Add(lookPath);

        var lookPresetRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        lookPresetRow.Children.Add(new TextBlock { Text = "Preset:", Width = 65, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White });
        var lookPresetCtrl = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        lookPresetCtrl.Items.Add("(None / Custom)");
        foreach (var name in BuiltinLutLibrary.LookLutNames) lookPresetCtrl.Items.Add(name);
        lookPresetCtrl.SelectedIndex = 0;
        lookPreset = lookPresetCtrl;
        AutomationProperties.SetAutomationId(lookPreset, "LookLut_Preset");
        lookPresetCtrl.SelectionChanged += (_, _) =>
        {
            if (updating || lookPresetCtrl.SelectedIndex <= 0) return;
            var preset = BuiltinLutLibrary.CreatePreset(lookPresetCtrl.SelectedItem.ToString()!);
            if (preset is not null) SetLutSlot(1, preset);
        };
        lookPresetRow.Children.Add(lookPreset);
        lookPanel.Children.Add(lookPresetRow);

        var lookBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var lookBrowse = new Button { Content = "Browse…", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetAutomationId(lookBrowse, "lookLut_BrowseButton");
        lookBrowse.Click += (_, _) => BrowseLutSlot(1);
        var lookClear = new Button { Content = "Clear", Padding = new Thickness(8, 2, 8, 2) };
        AutomationProperties.SetAutomationId(lookClear, "lookLut_Clear");
        lookClear.Click += (_, _) => SetLutSlot(1, null);
        lookBtns.Children.Add(lookBrowse);
        lookBtns.Children.Add(lookClear);
        lookPanel.Children.Add(lookBtns);

        lookPanel.Children.Add(MakeSliderRow("Strength", "lookLut_Strength", 0, 1, 1, 0.01, out lookStrength, out lookStrengthV, v => CommitSingleParam("LUTGain_1", v)));

        var lookInterpRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        lookInterpRow.Children.Add(new TextBlock { Text = "Interpolation", Width = 90, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White });
        var lookInterpCtrl = new ComboBox { Items = { "Trilinear", "Tetrahedral" }, SelectedIndex = 0, Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
        lookInterp = lookInterpCtrl;
        AutomationProperties.SetAutomationId(lookInterp, "lookLut_tetrahedral_ToggleButton");
        lookInterpCtrl.SelectionChanged += (_, _) => { if (!updating) CommitSingleParam("LUTInterpolation_1", lookInterpCtrl.SelectedIndex); };
        lookInterpRow.Children.Add(lookInterp);
        lookPanel.Children.Add(lookInterpRow);

        var lookBypassCtrl = new CheckBox { Content = "Bypass Look LUT", Margin = new Thickness(0, 4, 0, 0) };
        lookBypass = lookBypassCtrl;
        AutomationProperties.SetAutomationId(lookBypass, "lookLut_Bypass");
        lookBypassCtrl.Click += (_, _) => { if (!updating) CommitSingleParam("LUTEnable_1", lookBypassCtrl.IsChecked == true ? 1 : 0); };
        lookPanel.Children.Add(lookBypass);

        lookBorder.Child = lookPanel;
        Grid.SetColumn(lookBorder, 1);
        grid.Children.Add(lookBorder);

        return grid;
    }

    // --- Page 1: Color Wheels ---

    private Grid CreateWheelsPage(
        out ColorGradingWheelControl lw, out Slider lys, out TextBlock lr,
        out ColorGradingWheelControl gw, out Slider gys, out TextBlock gr,
        out ColorGradingWheelControl hw, out Slider hys, out TextBlock hr,
        out ColorGradingWheelControl ow, out Slider oys, out TextBlock orBlock)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        var liftCard = MakeWheelCard("Shadows (Lift)", "ColorWheel_Shadows", -0.5, 0.5, 0, 0.01,
            out lw, out lys, out lr, OnLiftWheelChanged, OnLiftSliderChanged, () => ResetWheelGroup("Lift"));
        Grid.SetRow(liftCard, 0); Grid.SetColumn(liftCard, 0); grid.Children.Add(liftCard);

        var gammaCard = MakeWheelCard("Midtones (Gamma)", "ColorWheel_Midtones", 0.2, 3.0, 1.0, 0.01,
            out gw, out gys, out gr, OnGammaWheelChanged, OnGammaSliderChanged, () => ResetWheelGroup("Gamma"));
        Grid.SetRow(gammaCard, 0); Grid.SetColumn(gammaCard, 1); grid.Children.Add(gammaCard);

        var gainCard = MakeWheelCard("Highlights (Gain)", "ColorWheel_Highlights", 0.0, 4.0, 1.0, 0.01,
            out hw, out hys, out hr, OnGainWheelChanged, OnGainSliderChanged, () => ResetWheelGroup("Gain"));
        Grid.SetRow(gainCard, 1); Grid.SetColumn(gainCard, 0); grid.Children.Add(gainCard);

        var offsetCard = MakeWheelCard("Offset", "ColorWheel_Log_Offset", -0.5, 0.5, 0.0, 0.01,
            out ow, out oys, out orBlock, OnOffsetWheelChanged, OnOffsetSliderChanged, () => ResetWheelGroup("Offset"));
        Grid.SetRow(offsetCard, 1); Grid.SetColumn(offsetCard, 1); grid.Children.Add(offsetCard);

        return grid;
    }

    private Border MakeWheelCard(
        string title, string autoId, double yMin, double yMax, double yDef, double yStep,
        out ColorGradingWheelControl wheel, out Slider ySlider, out TextBlock readout,
        Action<double, double> onWheel, Action<double> onSlider, Action onReset)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(3),
            Padding = new Thickness(6)
        };
        AutomationProperties.SetAutomationId(border, autoId);

        var stack = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, FontSize = 11 });
        var resetBtn = new Button { Content = "Reset", Padding = new Thickness(4, 0, 4, 0), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetBtn, autoId + "_Reset");
        resetBtn.Click += (_, _) => onReset();
        DockPanel.SetDock(resetBtn, Dock.Right);
        header.Children.Add(resetBtn);
        stack.Children.Add(header);

        var wheelRow = new DockPanel();
        wheel = new ColorGradingWheelControl { WheelName = title, Width = 110, Height = 110, Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetAutomationId(wheel, autoId + "_Wheel");
        wheel.ValuesChanged += onWheel;
        wheelRow.Children.Add(wheel);

        ySlider = new Slider
        {
            Orientation = Orientation.Vertical,
            Minimum = yMin,
            Maximum = yMax,
            Value = yDef,
            SmallChange = yStep,
            LargeChange = yStep * 5,
            Height = 100,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(ySlider, autoId + "_Y");
        var ys = ySlider;
        ySlider.ValueChanged += (_, _) => onSlider(ys.Value);
        DockPanel.SetDock(ySlider, Dock.Right);
        wheelRow.Children.Add(ySlider);
        stack.Children.Add(wheelRow);

        readout = new TextBlock
        {
            Text = "R: 0.00  G: 0.00  B: 0.00  Y: 0.00",
            FontSize = 9.5,
            Foreground = Brushes.LightGray,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        stack.Children.Add(readout);

        border.Child = stack;
        return border;
    }

    // --- Page 2: Tone & Levels ---

    private Grid CreateToneLevelsPage(
        out Slider inMin, out TextBlock inMinV, out Slider inMax, out TextBlock inMaxV,
        out Slider outMin, out TextBlock outMinV, out Slider outMax, out TextBlock outMaxV,
        out Slider exp, out TextBlock expV, out Slider bri, out TextBlock briV,
        out Slider con, out TextBlock conV, out Slider conCtr, out TextBlock conCtrV,
        out Slider sat, out TextBlock satV)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var headerBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40));
        var borderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));

        // Levels Card
        var levelsBorder = new Border
        {
            Background = headerBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(3),
            Padding = new Thickness(8)
        };
        var levelsPanel = new StackPanel();
        var levelsHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        levelsHeader.Children.Add(new TextBlock { Text = "Levels", FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        var resetLevelsBtn = new Button { Content = "Reset", Padding = new Thickness(4, 0, 4, 0), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetLevelsBtn, "levels_Reset_Button");
        resetLevelsBtn.Click += (_, _) => ResetParams(new() { ["InMinAdjust"] = 0, ["InMaxAdjust"] = 1, ["OutMinAdjust"] = 0, ["OutMaxAdjust"] = 1 });
        DockPanel.SetDock(resetLevelsBtn, Dock.Right);
        levelsHeader.Children.Add(resetLevelsBtn);
        levelsPanel.Children.Add(levelsHeader);

        levelsPanel.Children.Add(MakeSliderRow("Input Min", "inputMin", 0, 1, 0, 0.005, out inMin, out inMinV, v => CommitSingleParam("InMinAdjust", v)));
        levelsPanel.Children.Add(MakeSliderRow("Input Max", "inputMax", 0, 1, 1, 0.005, out inMax, out inMaxV, v => CommitSingleParam("InMaxAdjust", v)));
        levelsPanel.Children.Add(MakeSliderRow("Output Min", "outputMin", 0, 1, 0, 0.005, out outMin, out outMinV, v => CommitSingleParam("OutMinAdjust", v)));
        levelsPanel.Children.Add(MakeSliderRow("Output Max", "outputMax", 0, 1, 1, 0.005, out outMax, out outMaxV, v => CommitSingleParam("OutMaxAdjust", v)));
        levelsBorder.Child = levelsPanel;
        Grid.SetColumn(levelsBorder, 0);
        grid.Children.Add(levelsBorder);

        // Tone Card
        var toneBorder = new Border
        {
            Background = headerBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(3),
            Padding = new Thickness(8)
        };
        var tonePanel = new StackPanel();
        var toneHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        toneHeader.Children.Add(new TextBlock { Text = "Tone & Exposure", FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        var resetToneBtn = new Button { Content = "Reset", Padding = new Thickness(4, 0, 4, 0), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetToneBtn, "tone_Reset_Button");
        resetToneBtn.Click += (_, _) => ResetParams(new() { ["ExposureLnAdjust"] = 0, ["BrightnessAdjust"] = 0, ["ContrastAdjust"] = 0, ["ContrastCtrAdjust"] = 0.5, ["SaturationAdjust"] = 1 });
        DockPanel.SetDock(resetToneBtn, Dock.Right);
        toneHeader.Children.Add(resetToneBtn);
        tonePanel.Children.Add(toneHeader);

        tonePanel.Children.Add(MakeSliderRow("Exposure (EV)", "logExposure", -5, 5, 0, 0.05, out exp, out expV, v => CommitSingleParam("ExposureLnAdjust", v)));
        tonePanel.Children.Add(MakeSliderRow("Brightness", "brightness", -1, 1, 0, 0.01, out bri, out briV, v => CommitSingleParam("BrightnessAdjust", v)));
        tonePanel.Children.Add(MakeSliderRow("Contrast", "contrast", -1, 1, 0, 0.01, out con, out conV, v => CommitSingleParam("ContrastAdjust", v)));
        tonePanel.Children.Add(MakeSliderRow("Contrast Center", "contrastCenter", 0, 1, 0.5, 0.01, out conCtr, out conCtrV, v => CommitSingleParam("ContrastCtrAdjust", v)));
        tonePanel.Children.Add(MakeSliderRow("Saturation", "saturation", 0, 3, 1, 0.01, out sat, out satV, v => CommitSingleParam("SaturationAdjust", v)));
        toneBorder.Child = tonePanel;
        Grid.SetColumn(toneBorder, 1);
        grid.Children.Add(toneBorder);

        return grid;
    }

    // --- Page 3: White Balance & HSL ---

    private Grid CreateWbHslPage(
        out Slider temp, out TextBlock tempV, out Slider tint, out TextBlock tintV,
        out Slider hue, out TextBlock hueV, out Slider sat, out TextBlock satV,
        out Slider val, out TextBlock valV)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var headerBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40));
        var borderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));

        // White Balance Card
        var wbBorder = new Border
        {
            Background = headerBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(3),
            Padding = new Thickness(8)
        };
        var wbPanel = new StackPanel();
        var wbHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        wbHeader.Children.Add(new TextBlock { Text = "White Balance", FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        var resetWbBtn = new Button { Content = "Reset", Padding = new Thickness(4, 0, 4, 0), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetWbBtn, "whiteBalance_Reset_Button");
        resetWbBtn.Click += (_, _) => ResetParams(new() { ["TemperatureAdjust"] = 0, ["TintAdjust"] = 0 });
        DockPanel.SetDock(resetWbBtn, Dock.Right);
        wbHeader.Children.Add(resetWbBtn);
        wbPanel.Children.Add(wbHeader);

        wbPanel.Children.Add(MakeSliderRow("Temperature", "temperature", -4000, 4000, 0, 10, out temp, out tempV, v => CommitSingleParam("TemperatureAdjust", v)));
        wbPanel.Children.Add(MakeSliderRow("Tint", "tint", -100, 100, 0, 1, out tint, out tintV, v => CommitSingleParam("TintAdjust", v)));
        wbBorder.Child = wbPanel;
        Grid.SetColumn(wbBorder, 0);
        grid.Children.Add(wbBorder);

        // HSL Card
        var hslBorder = new Border
        {
            Background = headerBrush,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(3),
            Padding = new Thickness(8)
        };
        var hslPanel = new StackPanel();
        var hslHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        hslHeader.Children.Add(new TextBlock { Text = "HSL Adjust", FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        var resetHslBtn = new Button { Content = "Reset", Padding = new Thickness(4, 0, 4, 0), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetHslBtn, "HSL_Reset_Button");
        resetHslBtn.Click += (_, _) => ResetParams(new() { ["HueAdjust"] = 0, ["SatAdjust"] = 1, ["ValAdjust"] = 1 });
        DockPanel.SetDock(resetHslBtn, Dock.Right);
        hslHeader.Children.Add(resetHslBtn);
        hslPanel.Children.Add(hslHeader);

        hslPanel.Children.Add(MakeSliderRow("Hue Shift", "hue", -180, 180, 0, 1, out hue, out hueV, v => CommitSingleParam("HueAdjust", v)));
        hslPanel.Children.Add(MakeSliderRow("Saturation", "saturationHSL", 0, 3, 1, 0.01, out sat, out satV, v => CommitSingleParam("SatAdjust", v)));
        hslPanel.Children.Add(MakeSliderRow("Luminance", "luminance", 0, 2, 1, 0.01, out val, out valV, v => CommitSingleParam("ValAdjust", v)));
        hslBorder.Child = hslPanel;
        Grid.SetColumn(hslBorder, 1);
        grid.Children.Add(hslBorder);

        return grid;
    }

    // --- Page 4: Curves ---

    private Grid CreateCurvesPage(out ColorCurvesControl ctrl, out TextBlock readout)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var chPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var btnRgb = new Button { Content = "RGB", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        var btnRed = new Button { Content = "Red", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        var btnGreen = new Button { Content = "Green", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        var btnBlue = new Button { Content = "Blue", Padding = new Thickness(8, 2, 8, 2) };
        chPanel.Children.Add(btnRgb);
        chPanel.Children.Add(btnRed);
        chPanel.Children.Add(btnGreen);
        chPanel.Children.Add(btnBlue);
        top.Children.Add(chPanel);

        var rstPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var rstChBtn = new Button { Content = "Reset Channel", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        var rstAllBtn = new Button { Content = "Reset All Curves", Padding = new Thickness(8, 2, 8, 2) };
        rstPanel.Children.Add(rstChBtn);
        rstPanel.Children.Add(rstAllBtn);
        DockPanel.SetDock(rstPanel, Dock.Right);
        top.Children.Add(rstPanel);

        Grid.SetRow(top, 0);
        grid.Children.Add(top);

        ctrl = new ColorCurvesControl { Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetAutomationId(ctrl, "CurvesItem_Control");
        var c = ctrl;
        btnRgb.Click += (_, _) => c.ActiveChannel = CurveChannel.Master;
        btnRed.Click += (_, _) => c.ActiveChannel = CurveChannel.Red;
        btnGreen.Click += (_, _) => c.ActiveChannel = CurveChannel.Green;
        btnBlue.Click += (_, _) => c.ActiveChannel = CurveChannel.Blue;
        rstChBtn.Click += (_, _) => { c.ResetActive(); CommitCurves(); };
        rstAllBtn.Click += (_, _) => { c.ResetAll(); CommitCurves(); };
        ctrl.CurvesChanged += CommitCurves;

        readout = new TextBlock { Text = "Channel: Master", Foreground = Brushes.Gray, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Left };

        Grid.SetRow(ctrl, 1);
        grid.Children.Add(ctrl);

        return grid;
    }

    private static FrameworkElement MakeSliderRow(
        string label, string autoId, double min, double max, double def, double step,
        out Slider slider, out TextBlock valText, Action<double> onCommit)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var labelBlock = new TextBlock { Text = label, Width = 85, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White, FontSize = 11 };
        panel.Children.Add(labelBlock);

        valText = new TextBlock { Text = def.ToString("F2", CultureInfo.InvariantCulture), Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.LightGray, FontSize = 10.5 };
        DockPanel.SetDock(valText, Dock.Right);
        panel.Children.Add(valText);

        slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = def,
            SmallChange = step,
            LargeChange = step * 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0)
        };
        AutomationProperties.SetAutomationId(slider, autoId);
        var s = slider;
        var vt = valText;
        slider.ValueChanged += (_, _) =>
        {
            vt.Text = s.Value.ToString(Math.Abs(step) < 0.1 ? "F2" : "F0", CultureInfo.InvariantCulture);
            onCommit(Math.Round(s.Value, 3));
        };
        panel.Children.Add(slider);
        return panel;
    }

    // --- State Synchronization ---

    private void UpdateControlsFromEffect()
    {
        var fx = ActiveEffect;
        if (fx is null) return;
        updating = true;
        try
        {
            bypassBtn.IsChecked = !fx.IsEnabled;

            // Lift
            var lr = fx.GetParameter("Lift_R"); var lg = fx.GetParameter("Lift_G"); var lb = fx.GetParameter("Lift_B"); var ly = fx.GetParameter("Lift_Y");
            liftWheel.ValueX = Math.Clamp((lr - lb) / 1.2, -1, 1);
            liftWheel.ValueY = Math.Clamp(lg / 0.8, -1, 1);
            liftYSlider.Value = ly;
            liftReadout.Text = $"R: {lr:F2}  G: {lg:F2}  B: {lb:F2}  Y: {ly:F2}";

            // Gamma
            var gr = fx.GetParameter("Gamma_R"); var gg = fx.GetParameter("Gamma_G"); var gb = fx.GetParameter("Gamma_B"); var gy = fx.GetParameter("Gamma_Y");
            gammaWheel.ValueX = Math.Clamp((gr - gb) / 1.2, -1, 1);
            gammaWheel.ValueY = Math.Clamp((gg - 1.0) / 0.8, -1, 1);
            gammaYSlider.Value = gy;
            gammaReadout.Text = $"R: {gr:F2}  G: {gg:F2}  B: {gb:F2}  Y: {gy:F2}";

            // Gain
            var hr = fx.GetParameter("Gain_R"); var hg = fx.GetParameter("Gain_G"); var hb = fx.GetParameter("Gain_B"); var hy = fx.GetParameter("Gain_Y");
            gainWheel.ValueX = Math.Clamp((hr - hb) / 1.2, -1, 1);
            gainWheel.ValueY = Math.Clamp((hg - 1.0) / 0.8, -1, 1);
            gainYSlider.Value = hy;
            gainReadout.Text = $"R: {hr:F2}  G: {hg:F2}  B: {hb:F2}  Y: {hy:F2}";

            // Offset
            var or = fx.GetParameter("Offset_R"); var og = fx.GetParameter("Offset_G"); var ob = fx.GetParameter("Offset_B"); var oy = fx.GetParameter("Offset_Y");
            offsetWheel.ValueX = Math.Clamp((or - ob) / 1.2, -1, 1);
            offsetWheel.ValueY = Math.Clamp(og / 0.8, -1, 1);
            offsetYSlider.Value = oy;
            offsetReadout.Text = $"R: {or:F2}  G: {og:F2}  B: {ob:F2}  Y: {oy:F2}";

            // Levels
            inMinSlider.Value = fx.GetParameter("InMinAdjust");
            inMaxSlider.Value = fx.GetParameter("InMaxAdjust");
            outMinSlider.Value = fx.GetParameter("OutMinAdjust");
            outMaxSlider.Value = fx.GetParameter("OutMaxAdjust");

            // Tone
            exposureSlider.Value = fx.GetParameter("ExposureLnAdjust");
            brightnessSlider.Value = fx.GetParameter("BrightnessAdjust");
            contrastSlider.Value = fx.GetParameter("ContrastAdjust");
            contrastCtrSlider.Value = fx.GetParameter("ContrastCtrAdjust");
            saturationSlider.Value = fx.GetParameter("SaturationAdjust");

            // White Balance
            temperatureSlider.Value = fx.GetParameter("TemperatureAdjust");
            tintSlider.Value = fx.GetParameter("TintAdjust");

            // HSL
            hueSlider.Value = fx.GetParameter("HueAdjust");
            satHslSlider.Value = fx.GetParameter("SatAdjust");
            valHslSlider.Value = fx.GetParameter("ValAdjust");

            // Curves
            curvesControl.Curves = fx.Curves?.DeepClone() ?? new ColorCurvesData();

            // Camera LUT
            cameraLutPathText.Text = fx.Lut is not null ? (fx.Lut.Title ?? "Embedded 3D LUT") : "(No LUT loaded)";
            cameraLutStrengthSlider.Value = fx.GetParameter("LUTGain_0");
            cameraLutInterpCombo.SelectedIndex = (int)fx.GetParameter("LUTInterpolation_0");
            cameraLutBypassCheck.IsChecked = fx.GetParameter("LUTEnable_0") == 1;

            var camTitle = fx.Lut?.Title;
            var camIdx = 0;
            if (!string.IsNullOrEmpty(camTitle))
            {
                for (var i = 1; i < cameraLutPresetCombo.Items.Count; i++)
                {
                    if (string.Equals(cameraLutPresetCombo.Items[i]?.ToString(), camTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        camIdx = i;
                        break;
                    }
                }
            }
            cameraLutPresetCombo.SelectedIndex = camIdx;

            // Look LUT
            lookLutPathText.Text = fx.LookLut is not null ? (fx.LookLut.Title ?? "Embedded 3D LUT") : "(No LUT loaded)";
            lookLutStrengthSlider.Value = fx.GetParameter("LUTGain_1");
            lookLutInterpCombo.SelectedIndex = (int)fx.GetParameter("LUTInterpolation_1");
            lookLutBypassCheck.IsChecked = fx.GetParameter("LUTEnable_1") == 1;

            var lookTitle = fx.LookLut?.Title;
            var lookIdx = 0;
            if (!string.IsNullOrEmpty(lookTitle))
            {
                for (var i = 1; i < lookLutPresetCombo.Items.Count; i++)
                {
                    if (string.Equals(lookLutPresetCombo.Items[i]?.ToString(), lookTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        lookIdx = i;
                        break;
                    }
                }
            }
            lookLutPresetCombo.SelectedIndex = lookIdx;
        }
        finally
        {
            updating = false;
        }
    }

    private void CommitSingleParam(string key, double value)
    {
        if (updating || activeClip is null || vm is null) return;
        var fx = ActiveEffect;
        if (fx is null) return;
        var dict = new Dictionary<string, double>(fx.Parameters, StringComparer.Ordinal)
        {
            [key] = value
        };
        vm.Editor.UpdateScopedVideoEffectParameters(EffectScope.Event, activeClip.Id, activeEffectId, dict, fx.IsEnabled);
        previewCallback?.Invoke();
    }

    private void ResetParams(Dictionary<string, double> resetDict)
    {
        if (updating || activeClip is null || vm is null) return;
        var fx = ActiveEffect;
        if (fx is null) return;
        var dict = new Dictionary<string, double>(fx.Parameters, StringComparer.Ordinal);
        foreach (var (k, v) in resetDict) dict[k] = v;
        vm.Editor.UpdateScopedVideoEffectParameters(EffectScope.Event, activeClip.Id, activeEffectId, dict, fx.IsEnabled);
        UpdateControlsFromEffect();
        previewCallback?.Invoke();
    }

    private void CommitCurves()
    {
        if (updating || activeClip is null || vm is null) return;
        var fx = ActiveEffect;
        if (fx is null) return;
        var effects = activeClip.VideoEffects.Select(e => e.DeepClone()).ToList();
        var target = effects.FirstOrDefault(e => e.Id == activeEffectId);
        if (target is not null)
        {
            target.Curves = curvesControl.Curves.DeepClone();
            vm.Editor.SetScopedVideoEffects(EffectScope.Event, activeClip.Id, effects);
        }
        previewCallback?.Invoke();
    }

    private void SetLutSlot(int slot, CubeLut? lut)
    {
        if (activeClip is null || vm is null) return;
        var effects = activeClip.VideoEffects.Select(e => e.DeepClone()).ToList();
        var target = effects.FirstOrDefault(e => e.Id == activeEffectId);
        if (target is not null)
        {
            if (slot == 0) target.Lut = lut;
            else target.LookLut = lut;
            vm.Editor.SetScopedVideoEffects(EffectScope.Event, activeClip.Id, effects);
        }
        UpdateControlsFromEffect();
        previewCallback?.Invoke();
    }

    private void BrowseLutSlot(int slot)
    {
        var dialog = new OpenFileDialog
        {
            Title = slot == 0 ? "Open Camera LUT" : "Open Look LUT",
            Filter = "Cube 3D LUT (*.cube)|*.cube|All Files (*.*)|*.*",
            RestoreDirectory = true
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var lut = CubeLutParser.Parse(File.ReadAllText(dialog.FileName));
                lut.Title = Path.GetFileName(dialog.FileName);
                SetLutSlot(slot, lut);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load LUT: {ex.Message}", "Invalid LUT File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ResetLutSlot(int slot)
    {
        if (activeClip is null || vm is null) return;
        var prefix = slot == 0 ? "0" : "1";
        ResetParams(new()
        {
            [$"LUTGain_{prefix}"] = 1.0,
            [$"LUTInterpolation_{prefix}"] = 0,
            [$"LUTEnable_{prefix}"] = 0
        });
        SetLutSlot(slot, null);
    }

    // --- Wheel Callbacks ---

    private void OnLiftWheelChanged(double vx, double vy)
    {
        var r = Math.Clamp(vx * 0.6, -0.6, 0.6);
        var g = Math.Clamp(vy * 0.8, -0.8, 0.8);
        var b = Math.Clamp(-vx * 0.6, -0.6, 0.6);
        ResetParams(new() { ["Lift_R"] = r, ["Lift_G"] = g, ["Lift_B"] = b });
    }

    private void OnLiftSliderChanged(double y) => CommitSingleParam("Lift_Y", y);

    private void OnGammaWheelChanged(double vx, double vy)
    {
        var r = Math.Clamp(1.0 + vx * 0.6, 0.2, 3.0);
        var g = Math.Clamp(1.0 + vy * 0.8, 0.2, 3.0);
        var b = Math.Clamp(1.0 - vx * 0.6, 0.2, 3.0);
        ResetParams(new() { ["Gamma_R"] = r, ["Gamma_G"] = g, ["Gamma_B"] = b });
    }

    private void OnGammaSliderChanged(double y) => CommitSingleParam("Gamma_Y", y);

    private void OnGainWheelChanged(double vx, double vy)
    {
        var r = Math.Clamp(1.0 + vx * 0.8, 0.0, 4.0);
        var g = Math.Clamp(1.0 + vy * 0.8, 0.0, 4.0);
        var b = Math.Clamp(1.0 - vx * 0.8, 0.0, 4.0);
        ResetParams(new() { ["Gain_R"] = r, ["Gain_G"] = g, ["Gain_B"] = b });
    }

    private void OnGainSliderChanged(double y) => CommitSingleParam("Gain_Y", y);

    private void OnOffsetWheelChanged(double vx, double vy)
    {
        var r = Math.Clamp(vx * 0.5, -0.5, 0.5);
        var g = Math.Clamp(vy * 0.5, -0.5, 0.5);
        var b = Math.Clamp(-vx * 0.5, -0.5, 0.5);
        ResetParams(new() { ["Offset_R"] = r, ["Offset_G"] = g, ["Offset_B"] = b });
    }

    private void OnOffsetSliderChanged(double y) => CommitSingleParam("Offset_Y", y);

    private void ResetWheelGroup(string group)
    {
        if (group == "Lift")
            ResetParams(new() { ["Lift_R"] = 0, ["Lift_G"] = 0, ["Lift_B"] = 0, ["Lift_Y"] = 0 });
        else if (group == "Gamma")
            ResetParams(new() { ["Gamma_R"] = 1, ["Gamma_G"] = 1, ["Gamma_B"] = 1, ["Gamma_Y"] = 1 });
        else if (group == "Gain")
            ResetParams(new() { ["Gain_R"] = 1, ["Gain_G"] = 1, ["Gain_B"] = 1, ["Gain_Y"] = 1 });
        else if (group == "Offset")
            ResetParams(new() { ["Offset_R"] = 0, ["Offset_G"] = 0, ["Offset_B"] = 0, ["Offset_Y"] = 0 });
    }

    private void OnBypassClick(object sender, RoutedEventArgs e)
    {
        if (activeClip is null || vm is null) return;
        var fx = ActiveEffect;
        if (fx is null) return;
        var isEnabled = bypassBtn.IsChecked != true;
        vm.Editor.UpdateScopedVideoEffectParameters(EffectScope.Event, activeClip.Id, activeEffectId, fx.Parameters, isEnabled);
        previewCallback?.Invoke();
    }

    private void OnResetAllClick(object sender, RoutedEventArgs e)
    {
        if (activeClip is null || vm is null) return;
        var fx = ActiveEffect;
        if (fx is null) return;
        var defaults = VideoEffectCatalog.Get(VideoEffectKind.ColorGrading).Parameters
            .ToDictionary(p => p.Key, p => p.DefaultValue, StringComparer.Ordinal);
        var effects = activeClip.VideoEffects.Select(e => e.DeepClone()).ToList();
        var target = effects.FirstOrDefault(e => e.Id == activeEffectId);
        if (target is not null)
        {
            target.Lut = null;
            target.LookLut = null;
            target.Curves = null;
            target.Parameters = defaults;
            vm.Editor.SetScopedVideoEffects(EffectScope.Event, activeClip.Id, effects);
        }
        UpdateControlsFromEffect();
        previewCallback?.Invoke();
    }

    private void OnPresetComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating || activeClip is null || vm is null || presetCombo.SelectedIndex <= 0) return;
        var presetName = presetCombo.SelectedItem.ToString()!;
        var lut = BuiltinLutLibrary.CreatePreset(presetName);
        if (lut is not null) SetLutSlot(1, lut);
    }
}
