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
/// Vegas Pro 1:1 Color Grading panel.
/// Provides interactive 2D color wheels (Lift, Gamma, Gain, Offset), Tone &amp; Levels,
/// White Balance &amp; HSL with eye-dropper picker, interactive 2D RGB/Master Curves canvas,
/// and dual LUT slots (Camera &amp; Look) across Event, Track, Media, and Video Output scopes.
/// </summary>
public sealed class ColorGradingWindow : Window
{
    private readonly EditorViewModel vm;
    private EffectScope scope;
    private Guid targetId;
    private Guid targetEffectId;
    private readonly Action? onPreviewRequest;

    private bool updating;

    // Navigation and tool buttons
    private readonly Button tabWheelsBtn;
    private readonly Button tabToneLevelsBtn;
    private readonly Button tabWbHslBtn;
    private readonly Button tabCurvesBtn;
    private readonly Button tabLutsBtn;
    private readonly ToggleButton bypassToggle;
    private readonly Button resetAllBtn;
    private readonly Button undoBtn;
    private readonly Button redoBtn;

    // Pages container
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

    // Levels Sliders
    private readonly Slider inMinSlider, inMaxSlider, outMinSlider, outMaxSlider;
    private readonly TextBlock inMinVal, inMaxVal, outMinVal, outMaxVal;

    // Tone Sliders
    private readonly Slider exposureSlider, brightnessSlider, contrastSlider, contrastCtrSlider, saturationSlider;
    private readonly TextBlock exposureVal, brightnessVal, contrastVal, contrastCtrVal, saturationVal;

    // White Balance Sliders
    private readonly Slider temperatureSlider, tintSlider;
    private readonly TextBlock temperatureVal, tintVal;

    // HSL Sliders
    private readonly Slider hueSlider, satHslSlider, valHslSlider;
    private readonly TextBlock hueVal, satHslVal, valHslVal;

    // Curves Control
    private readonly ColorCurvesControl curvesControl;
    private readonly TextBlock curveReadout;

    // LUT Slots
    private readonly TextBlock cameraLutPathText;
    private readonly Slider cameraLutStrengthSlider;
    private readonly TextBlock cameraLutStrengthVal;
    private readonly ComboBox cameraLutInterpCombo;
    private readonly CheckBox cameraLutBypassCheck;

    private readonly TextBlock lookLutPathText;
    private readonly Slider lookLutStrengthSlider;
    private readonly TextBlock lookLutStrengthVal;
    private readonly ComboBox lookLutInterpCombo;
    private readonly CheckBox lookLutBypassCheck;

    private TimelineClip? TargetClip => scope == EffectScope.Event
        ? vm.Editor.Project.Tracks.Where(t => t.Kind == TrackKind.Video)
            .SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == targetId)
        : null;

    private TimelineTrack? TargetTrack => scope == EffectScope.Track
        ? vm.Editor.Project.Tracks.FirstOrDefault(t => t.Id == targetId)
        : null;

    private MediaAsset? TargetMedia => scope == EffectScope.Media
        ? vm.Editor.Project.Media.FirstOrDefault(m => m.Id == targetId)
        : null;

    private List<VideoEffect>? TargetEffects => scope switch
    {
        EffectScope.Event => TargetClip?.VideoEffects,
        EffectScope.Track => TargetTrack?.VideoEffects,
        EffectScope.Media => TargetMedia?.VideoEffects,
        EffectScope.Output => vm.Editor.Project.VideoOutputEffects,
        _ => null
    };

    private VideoEffect? TargetEffect =>
        TargetEffects?.FirstOrDefault(e => e.Id == targetEffectId);

    public ColorGradingWindow(EditorViewModel viewModel, EffectScope scope, Guid targetId, Guid? effectId = null, Action? onPreview = null)
    {
        vm = viewModel;
        this.scope = scope;
        this.targetId = targetId;
        onPreviewRequest = onPreview;

        // Ensure a ColorGrading effect exists on target scope
        var existing = TargetEffects?.FirstOrDefault(e => e.Kind == VideoEffectKind.ColorGrading);
        if (existing is null)
        {
            var added = vm.Editor.AddScopedVideoEffect(scope, targetId, VideoEffectKind.ColorGrading);
            targetEffectId = added.Id;
        }
        else
        {
            targetEffectId = effectId ?? existing.Id;
        }

        Style = (Style)FindResource(typeof(Window));
        UpdateWindowTitle();
        Width = 840;
        Height = 540;
        MinWidth = 700;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetAutomationId(this, "ColorGradingWindow");

        var panelBrush = (Brush)FindResource("PanelBrush");
        var headerBrush = (Brush)FindResource("HeaderBrush");

        var root = new DockPanel { Background = panelBrush };

        // 1. Top Toolbar
        var topBar = new DockPanel { Background = headerBrush, Height = 36 };
        DockPanel.SetDock(topBar, Dock.Top);

        var navPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        tabWheelsBtn = MakeNavButton("Color Wheels", "Tab_ColorWheels", true);
        tabToneLevelsBtn = MakeNavButton("Tone & Levels", "Tab_ToneLevels", false);
        tabWbHslBtn = MakeNavButton("White Balance & HSL", "Tab_WhiteBalanceHSL", false);
        tabCurvesBtn = MakeNavButton("Curves", "Tab_Curves", false);
        tabLutsBtn = MakeNavButton("LUTs", "Tab_LUTs", false);

        tabWheelsBtn.Click += (_, _) => SelectPage(0);
        tabToneLevelsBtn.Click += (_, _) => SelectPage(1);
        tabWbHslBtn.Click += (_, _) => SelectPage(2);
        tabCurvesBtn.Click += (_, _) => SelectPage(3);
        tabLutsBtn.Click += (_, _) => SelectPage(4);

        navPanel.Children.Add(tabWheelsBtn);
        navPanel.Children.Add(tabToneLevelsBtn);
        navPanel.Children.Add(tabWbHslBtn);
        navPanel.Children.Add(tabCurvesBtn);
        navPanel.Children.Add(tabLutsBtn);
        topBar.Children.Add(navPanel);

        var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        bypassToggle = new ToggleButton
        {
            Content = "Bypass",
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(8, 2, 8, 2),
            ToolTip = "Bypass all color grading"
        };
        AutomationProperties.SetAutomationId(bypassToggle, "ColorGrading_BypassAll");
        bypassToggle.Click += OnBypassToggleClick;

        resetAllBtn = MakeToolButton("Reset All", "ColorGrading_ResetAll", OnResetAllClick);
        undoBtn = MakeToolButton("Undo", "ColorGrading_Undo", () => vm.Editor.Undo());
        redoBtn = MakeToolButton("Redo", "ColorGrading_Redo", () => vm.Editor.Redo());
        var closeBtn = MakeToolButton("Close", "ColorGrading_Close", Close);

        actionsPanel.Children.Add(bypassToggle);
        actionsPanel.Children.Add(resetAllBtn);
        actionsPanel.Children.Add(undoBtn);
        actionsPanel.Children.Add(redoBtn);
        actionsPanel.Children.Add(closeBtn);
        DockPanel.SetDock(actionsPanel, Dock.Right);
        topBar.Children.Add(actionsPanel);

        root.Children.Add(topBar);

        // 2. Central Content Pages
        pageContainer = new Grid { Margin = new Thickness(12) };

        // Page 0: Color Wheels
        wheelsPage = CreateWheelsPage(out liftWheel, out liftYSlider, out liftReadout,
            out gammaWheel, out gammaYSlider, out gammaReadout,
            out gainWheel, out gainYSlider, out gainReadout,
            out offsetWheel, out offsetYSlider, out offsetReadout);

        // Page 1: Tone & Levels
        toneLevelsPage = CreateToneLevelsPage(
            out inMinSlider, out inMinVal, out inMaxSlider, out inMaxVal,
            out outMinSlider, out outMinVal, out outMaxSlider, out outMaxVal,
            out exposureSlider, out exposureVal, out brightnessSlider, out brightnessVal,
            out contrastSlider, out contrastVal, out contrastCtrSlider, out contrastCtrVal,
            out saturationSlider, out saturationVal);

        // Page 2: White Balance & HSL
        wbHslPage = CreateWbHslPage(
            out temperatureSlider, out temperatureVal, out tintSlider, out tintVal,
            out hueSlider, out hueVal, out satHslSlider, out satHslVal,
            out valHslSlider, out valHslVal);

        // Page 3: Curves
        curvesPage = CreateCurvesPage(out curvesControl, out curveReadout);

        // Page 4: LUTs
        lutsPage = CreateLutsPage(
            out cameraLutPathText, out cameraLutStrengthSlider, out cameraLutStrengthVal,
            out cameraLutInterpCombo, out cameraLutBypassCheck,
            out lookLutPathText, out lookLutStrengthSlider, out lookLutStrengthVal,
            out lookLutInterpCombo, out lookLutBypassCheck);

        pageContainer.Children.Add(wheelsPage);
        pageContainer.Children.Add(toneLevelsPage);
        pageContainer.Children.Add(wbHslPage);
        pageContainer.Children.Add(curvesPage);
        pageContainer.Children.Add(lutsPage);

        SelectPage(0);
        root.Children.Add(pageContainer);

        Content = root;

        UpdateControlsFromEffect();
    }

    public ColorGradingWindow(EditorViewModel viewModel, Guid clipId, Guid effectId, Action? onPreview = null)
        : this(viewModel, EffectScope.Event, clipId, effectId, onPreview)
    {
    }

    private void UpdateWindowTitle()
    {
        Title = scope switch
        {
            EffectScope.Event => $"Color Grading: Video Event FX — {TargetClip?.Name ?? "Event"}",
            EffectScope.Track => $"Color Grading: Video Track FX — {TargetTrack?.Name ?? "Track"}",
            EffectScope.Media => $"Color Grading: Media FX — {TargetMedia?.Name ?? "Media"}",
            EffectScope.Output => "Color Grading: Video Output FX",
            _ => "Color Grading"
        };
    }

    private Button MakeNavButton(string text, string automationId, bool active)
    {
        var btn = new Button
        {
            Content = text,
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(10, 4, 10, 4),
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal
        };
        AutomationProperties.SetAutomationId(btn, automationId);
        return btn;
    }

    private static Button MakeToolButton(string text, string automationId, Action action)
    {
        var btn = new Button
        {
            Content = text,
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(8, 2, 8, 2)
        };
        AutomationProperties.SetAutomationId(btn, automationId);
        btn.Click += (_, _) => action();
        return btn;
    }

    private void SelectPage(int index)
    {
        wheelsPage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        toneLevelsPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        wbHslPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        curvesPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        lutsPage.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;

        tabWheelsBtn.FontWeight = index == 0 ? FontWeights.SemiBold : FontWeights.Normal;
        tabToneLevelsBtn.FontWeight = index == 1 ? FontWeights.SemiBold : FontWeights.Normal;
        tabWbHslBtn.FontWeight = index == 2 ? FontWeights.SemiBold : FontWeights.Normal;
        tabCurvesBtn.FontWeight = index == 3 ? FontWeights.SemiBold : FontWeights.Normal;
        tabLutsBtn.FontWeight = index == 4 ? FontWeights.SemiBold : FontWeights.Normal;
    }

    // --- Page Builders ---

    private Grid CreateWheelsPage(
        out ColorGradingWheelControl lw, out Slider ly, out TextBlock lr,
        out ColorGradingWheelControl gw, out Slider gy, out TextBlock gr,
        out ColorGradingWheelControl hw, out Slider hy, out TextBlock hr,
        out ColorGradingWheelControl ow, out Slider oy, out TextBlock or)
    {
        var grid = new Grid();
        for (var i = 0; i < 4; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(CreateSingleWheelCard("Lift (Shadows)", "Wheel_Lift", out lw, out ly, out lr, -1, 1, 0, OnLiftWheelChanged, OnLiftYChanged, OnLiftReset, 0));
        grid.Children.Add(CreateSingleWheelCard("Gamma (Midtones)", "Wheel_Gamma", out gw, out gy, out gr, 0, 5, 1, OnGammaWheelChanged, OnGammaYChanged, OnGammaReset, 1));
        grid.Children.Add(CreateSingleWheelCard("Gain (Highlights)", "Wheel_Gain", out hw, out hy, out hr, 0, 2, 1, OnGainWheelChanged, OnGainYChanged, OnGainReset, 2));
        grid.Children.Add(CreateSingleWheelCard("Offset", "Wheel_Offset", out ow, out oy, out or, -1, 1, 0, OnOffsetWheelChanged, OnOffsetYChanged, OnOffsetReset, 3));

        return grid;
    }

    private FrameworkElement CreateSingleWheelCard(
        string title, string automationPrefix,
        out ColorGradingWheelControl wheel, out Slider ySlider, out TextBlock readout,
        double minLuma, double maxLuma, double defLuma,
        Action<double, double> onWheelChanged, Action<double> onLumaChanged, Action onReset,
        int col)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("HeaderBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4),
            Padding = new Thickness(8)
        };

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        var topDock = new DockPanel { Width = 150, Margin = new Thickness(0, 0, 0, 4) };
        topDock.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center });
        var resetBtn = new Button { Content = "Reset", Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetBtn, $"{automationPrefix}_Reset");
        resetBtn.Click += (_, _) => onReset();
        DockPanel.SetDock(resetBtn, Dock.Right);
        topDock.Children.Add(resetBtn);
        stack.Children.Add(topDock);

        wheel = new ColorGradingWheelControl { WheelName = title };
        AutomationProperties.SetAutomationId(wheel, automationPrefix);
        wheel.ValuesChanged += onWheelChanged;
        stack.Children.Add(wheel);

        var sliderDock = new DockPanel { Width = 150, Margin = new Thickness(0, 8, 0, 4) };
        sliderDock.Children.Add(new TextBlock { Text = "Y", Width = 14, Foreground = (Brush)FindResource("SubtleTextBrush"), VerticalAlignment = VerticalAlignment.Center });
        ySlider = new Slider
        {
            Minimum = minLuma,
            Maximum = maxLuma,
            Value = defLuma,
            SmallChange = 0.01,
            LargeChange = 0.1,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        AutomationProperties.SetAutomationId(ySlider, $"{automationPrefix}_Y");
        var ys = ySlider;
        ySlider.ValueChanged += (_, _) => onLumaChanged(ys.Value);
        sliderDock.Children.Add(ySlider);
        stack.Children.Add(sliderDock);

        readout = new TextBlock
        {
            Text = "R: 0.00  G: 0.00  B: 0.00  Y: 0.00",
            FontSize = 10,
            Foreground = (Brush)FindResource("SubtleTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
        AutomationProperties.SetAutomationId(readout, $"{automationPrefix}_Readout");
        stack.Children.Add(readout);

        border.Child = stack;
        Grid.SetColumn(border, col);
        return border;
    }

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

        // Left: Levels Group
        var levelsBorder = new Border
        {
            Background = (Brush)FindResource("HeaderBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4),
            Padding = new Thickness(12)
        };
        var levelsPanel = new StackPanel();
        var levelsHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        levelsHeader.Children.Add(new TextBlock { Text = "Levels Adjust", FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
        var resetLevelsBtn = new Button { Content = "Reset Levels", Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetLevelsBtn, "Reset_Levels");
        resetLevelsBtn.Click += (_, _) => OnResetLevelsClick();
        DockPanel.SetDock(resetLevelsBtn, Dock.Right);
        levelsHeader.Children.Add(resetLevelsBtn);
        levelsPanel.Children.Add(levelsHeader);

        levelsPanel.Children.Add(MakeSliderRow("Input Min", "Slider_InMin", 0, 1, 0, 0.01, out inMin, out inMinV, v => CommitSingleParam("InMinAdjust", v)));
        levelsPanel.Children.Add(MakeSliderRow("Input Max", "Slider_InMax", 0, 1, 1, 0.01, out inMax, out inMaxV, v => CommitSingleParam("InMaxAdjust", v)));
        levelsPanel.Children.Add(MakeSliderRow("Output Min", "Slider_OutMin", 0, 1, 0, 0.01, out outMin, out outMinV, v => CommitSingleParam("OutMinAdjust", v)));
        levelsPanel.Children.Add(MakeSliderRow("Output Max", "Slider_OutMax", 0, 1, 1, 0.01, out outMax, out outMaxV, v => CommitSingleParam("OutMaxAdjust", v)));
        levelsBorder.Child = levelsPanel;
        Grid.SetColumn(levelsBorder, 0);
        grid.Children.Add(levelsBorder);

        // Right: Tone Group
        var toneBorder = new Border
        {
            Background = (Brush)FindResource("HeaderBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4),
            Padding = new Thickness(12)
        };
        var tonePanel = new StackPanel();
        var toneHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        toneHeader.Children.Add(new TextBlock { Text = "Tone Adjust", FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
        var resetToneBtn = new Button { Content = "Reset Tone", Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetToneBtn, "Reset_Tone");
        resetToneBtn.Click += (_, _) => OnResetToneClick();
        DockPanel.SetDock(resetToneBtn, Dock.Right);
        toneHeader.Children.Add(resetToneBtn);
        tonePanel.Children.Add(toneHeader);

        tonePanel.Children.Add(MakeSliderRow("Exposure (EV)", "Slider_Exposure", -5, 5, 0, 0.1, out exp, out expV, v => CommitSingleParam("ExposureLnAdjust", v)));
        tonePanel.Children.Add(MakeSliderRow("Brightness", "Slider_Brightness", -1, 1, 0, 0.01, out bri, out briV, v => CommitSingleParam("BrightnessAdjust", v)));
        tonePanel.Children.Add(MakeSliderRow("Contrast", "Slider_Contrast", -1, 1, 0, 0.01, out con, out conV, v => CommitSingleParam("ContrastAdjust", v)));
        tonePanel.Children.Add(MakeSliderRow("Contrast Ctr", "Slider_ContrastCtr", 0, 1, 0.5, 0.01, out conCtr, out conCtrV, v => CommitSingleParam("ContrastCtrAdjust", v)));
        tonePanel.Children.Add(MakeSliderRow("Saturation", "Slider_Saturation", 0, 2, 1, 0.01, out sat, out satV, v => CommitSingleParam("SaturationAdjust", v)));
        toneBorder.Child = tonePanel;
        Grid.SetColumn(toneBorder, 1);
        grid.Children.Add(toneBorder);

        return grid;
    }

    private Grid CreateWbHslPage(
        out Slider temp, out TextBlock tempV, out Slider tint, out TextBlock tintV,
        out Slider hue, out TextBlock hueV, out Slider satH, out TextBlock satHV,
        out Slider valH, out TextBlock valHV)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: White Balance Group
        var wbBorder = new Border
        {
            Background = (Brush)FindResource("HeaderBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4),
            Padding = new Thickness(12)
        };
        var wbPanel = new StackPanel();
        var wbHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        wbHeader.Children.Add(new TextBlock { Text = "White Balance", FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });

        var wbButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var pickNeutralBtn = new Button { Content = "Pick Neutral Color…", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetAutomationId(pickNeutralBtn, "Pick_WhiteBalance");
        pickNeutralBtn.Click += (_, _) => OnPickNeutralColor();
        wbButtons.Children.Add(pickNeutralBtn);

        var resetWbBtn = new Button { Content = "Reset WB", Padding = new Thickness(6, 1, 6, 1) };
        AutomationProperties.SetAutomationId(resetWbBtn, "Reset_WhiteBalance");
        resetWbBtn.Click += (_, _) => OnResetWbClick();
        wbButtons.Children.Add(resetWbBtn);

        DockPanel.SetDock(wbButtons, Dock.Right);
        wbHeader.Children.Add(wbButtons);
        wbPanel.Children.Add(wbHeader);

        wbPanel.Children.Add(MakeSliderRow("Temperature (K)", "Slider_Temperature", -4000, 4000, 0, 10, out temp, out tempV, v => CommitSingleParam("TemperatureAdjust", v)));
        wbPanel.Children.Add(MakeSliderRow("Tint", "Slider_Tint", -100, 100, 0, 1, out tint, out tintV, v => CommitSingleParam("TintAdjust", v)));
        wbBorder.Child = wbPanel;
        Grid.SetColumn(wbBorder, 0);
        grid.Children.Add(wbBorder);

        // Right: HSL Group
        var hslBorder = new Border
        {
            Background = (Brush)FindResource("HeaderBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4),
            Padding = new Thickness(12)
        };
        var hslPanel = new StackPanel();
        var hslHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        hslHeader.Children.Add(new TextBlock { Text = "HSL Fine Tuning", FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
        var resetHslBtn = new Button { Content = "Reset HSL", Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetHslBtn, "Reset_HSL");
        resetHslBtn.Click += (_, _) => OnResetHslClick();
        DockPanel.SetDock(resetHslBtn, Dock.Right);
        hslHeader.Children.Add(resetHslBtn);
        hslPanel.Children.Add(hslHeader);

        hslPanel.Children.Add(MakeSliderRow("Hue Shift", "Slider_Hue", 0, 1, 0, 0.01, out hue, out hueV, v => CommitSingleParam("HueAdjust", v)));
        hslPanel.Children.Add(MakeSliderRow("Saturation", "Slider_SatHSL", 0, 2, 1, 0.01, out satH, out satHV, v => CommitSingleParam("SatAdjust", v)));
        hslPanel.Children.Add(MakeSliderRow("Luminance", "Slider_ValHSL", 0, 2, 1, 0.01, out valH, out valHV, v => CommitSingleParam("ValAdjust", v)));
        hslBorder.Child = hslPanel;
        Grid.SetColumn(hslBorder, 1);
        grid.Children.Add(hslBorder);

        return grid;
    }

    private Grid CreateCurvesPage(out ColorCurvesControl curvesCtrl, out TextBlock readoutText)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 0: Channel selectors and readout
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var channelStack = new StackPanel { Orientation = Orientation.Horizontal };

        var ctrl = new ColorCurvesControl();
        curvesCtrl = ctrl;
        AutomationProperties.SetAutomationId(curvesCtrl, "ColorGrading_CurvesCanvas");

        Button? btnW = null, btnR = null, btnG = null, btnB = null;

        void UpdateChannelButtonStyles(CurveChannel ch)
        {
            if (btnW != null) btnW.FontWeight = ch == CurveChannel.Master ? FontWeights.Bold : FontWeights.Normal;
            if (btnR != null) btnR.FontWeight = ch == CurveChannel.Red ? FontWeights.Bold : FontWeights.Normal;
            if (btnG != null) btnG.FontWeight = ch == CurveChannel.Green ? FontWeights.Bold : FontWeights.Normal;
            if (btnB != null) btnB.FontWeight = ch == CurveChannel.Blue ? FontWeights.Bold : FontWeights.Normal;
        }

        btnW = MakeChannelButton("Master (W)", Brushes.White, () => { ctrl.ActiveChannel = CurveChannel.Master; UpdateChannelButtonStyles(CurveChannel.Master); });
        btnR = MakeChannelButton("Red (R)", new SolidColorBrush(Color.FromRgb(239, 83, 80)), () => { ctrl.ActiveChannel = CurveChannel.Red; UpdateChannelButtonStyles(CurveChannel.Red); });
        btnG = MakeChannelButton("Green (G)", new SolidColorBrush(Color.FromRgb(102, 187, 106)), () => { ctrl.ActiveChannel = CurveChannel.Green; UpdateChannelButtonStyles(CurveChannel.Green); });
        btnB = MakeChannelButton("Blue (B)", new SolidColorBrush(Color.FromRgb(66, 165, 245)), () => { ctrl.ActiveChannel = CurveChannel.Blue; UpdateChannelButtonStyles(CurveChannel.Blue); });

        UpdateChannelButtonStyles(CurveChannel.Master);

        channelStack.Children.Add(btnW);
        channelStack.Children.Add(btnR);
        channelStack.Children.Add(btnG);
        channelStack.Children.Add(btnB);
        header.Children.Add(channelStack);

        readoutText = new TextBlock
        {
            Foreground = (Brush)FindResource("SubtleTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 4, 0),
            Text = "Curve: Master (2 pts)"
        };
        AutomationProperties.SetAutomationId(readoutText, "ColorCurves_Readout");
        DockPanel.SetDock(readoutText, Dock.Right);
        header.Children.Add(readoutText);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        // Row 1: Interactive Canvas
        Grid.SetRow(curvesCtrl, 1);
        grid.Children.Add(curvesCtrl);

        // Row 2: Bottom Toolbar
        var bottomBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var resetCurrentBtn = MakeToolButton("Reset Active Curve", "ColorCurves_ResetCurrent", () => ctrl.ResetActive());
        var resetAllCurvesBtn = MakeToolButton("Reset All Curves", "ColorCurves_ResetAll", () => ctrl.ResetAll());
        var smoothBtn = MakeToolButton("Smooth Curve", "ColorCurves_Smooth", () => ctrl.SmoothActive());

        bottomBar.Children.Add(resetCurrentBtn);
        bottomBar.Children.Add(resetAllCurvesBtn);
        bottomBar.Children.Add(smoothBtn);
        Grid.SetRow(bottomBar, 2);
        grid.Children.Add(bottomBar);

        var ro = readoutText;
        ctrl.PointSelected += text => ro.Text = text;
        ctrl.CurvesChanged += OnCurvesChanged;

        return grid;
    }

    private static Button MakeChannelButton(string label, Brush color, Action onClick)
    {
        var btn = new Button
        {
            Content = label,
            Foreground = color,
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(10, 3, 10, 3)
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void OnCurvesChanged()
    {
        if (updating) return;
        var fx = TargetEffect;
        if (fx is null) return;
        var effects = TargetEffects?.Select(e => e.DeepClone()).ToList();
        if (effects is null) return;
        var target = effects.FirstOrDefault(e => e.Id == targetEffectId);
        if (target is not null)
        {
            target.Curves = curvesControl.Curves.DeepClone();
            vm.Editor.SetScopedVideoEffects(scope, targetId, effects);
        }
        RequestPreview();
    }

    private Grid CreateLutsPage(
        out TextBlock camPath, out Slider camStrength, out TextBlock camStrengthV,
        out ComboBox camInterp, out CheckBox camBypass,
        out TextBlock lookPath, out Slider lookStrength, out TextBlock lookStrengthV,
        out ComboBox lookInterp, out CheckBox lookBypass)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: Camera LUT Slot 0
        var camBorder = new Border
        {
            Background = (Brush)FindResource("HeaderBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4),
            Padding = new Thickness(12)
        };
        var camPanel = new StackPanel();
        var camHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        camHeader.Children.Add(new TextBlock { Text = "Camera LUT (Input Slot)", FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
        var resetCamBtn = new Button { Content = "Reset", Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetCamBtn, "Reset_CameraLut");
        resetCamBtn.Click += (_, _) => OnResetCameraLut();
        DockPanel.SetDock(resetCamBtn, Dock.Right);
        camHeader.Children.Add(resetCamBtn);
        camPanel.Children.Add(camHeader);

        camPath = new TextBlock { Text = "(No LUT loaded)", Foreground = (Brush)FindResource("SubtleTextBrush"), Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis };
        camPanel.Children.Add(camPath);

        var camBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var camBrowse = new Button { Content = "Browse…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetAutomationId(camBrowse, "CameraLut_Browse");
        camBrowse.Click += (_, _) => OnBrowseLut(0);
        var camClear = new Button { Content = "Clear", Padding = new Thickness(10, 3, 10, 3) };
        AutomationProperties.SetAutomationId(camClear, "CameraLut_Clear");
        camClear.Click += (_, _) => OnClearLut(0);
        camBtns.Children.Add(camBrowse);
        camBtns.Children.Add(camClear);
        camPanel.Children.Add(camBtns);

        camPanel.Children.Add(MakeSliderRow("Strength", "CameraLut_Strength", 0, 1, 1, 0.01, out camStrength, out camStrengthV, v => CommitSingleParam("LUTGain_0", v)));

        var camInterpRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        camInterpRow.Children.Add(new TextBlock { Text = "Interpolation", Width = 90, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") });
        var camInterpCtrl = new ComboBox { Items = { "Trilinear", "Tetrahedral" }, SelectedIndex = 0, Width = 130, HorizontalAlignment = HorizontalAlignment.Left };
        camInterp = camInterpCtrl;
        AutomationProperties.SetAutomationId(camInterp, "CameraLut_Interpolation");
        camInterpCtrl.SelectionChanged += (_, _) => { if (!updating) CommitSingleParam("LUTInterpolation_0", camInterpCtrl.SelectedIndex); };
        camInterpRow.Children.Add(camInterp);
        camPanel.Children.Add(camInterpRow);

        var camBypassCtrl = new CheckBox { Content = "Bypass Camera LUT", Margin = new Thickness(0, 6, 0, 0) };
        camBypass = camBypassCtrl;
        AutomationProperties.SetAutomationId(camBypass, "CameraLut_Bypass");
        camBypassCtrl.Click += (_, _) => { if (!updating) CommitSingleParam("LUTEnable_0", camBypassCtrl.IsChecked == true ? 1 : 0); };
        camPanel.Children.Add(camBypass);

        camBorder.Child = camPanel;
        Grid.SetColumn(camBorder, 0);
        grid.Children.Add(camBorder);

        // Right: Look LUT Slot 1
        var lookBorder = new Border
        {
            Background = (Brush)FindResource("HeaderBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4),
            Padding = new Thickness(12)
        };
        var lookPanel = new StackPanel();
        var lookHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        lookHeader.Children.Add(new TextBlock { Text = "Look LUT (Output Slot)", FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
        var resetLookBtn = new Button { Content = "Reset", Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetLookBtn, "Reset_LookLut");
        resetLookBtn.Click += (_, _) => OnResetLookLut();
        DockPanel.SetDock(resetLookBtn, Dock.Right);
        lookHeader.Children.Add(resetLookBtn);
        lookPanel.Children.Add(lookHeader);

        lookPath = new TextBlock { Text = "(No LUT loaded)", Foreground = (Brush)FindResource("SubtleTextBrush"), Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis };
        lookPanel.Children.Add(lookPath);

        var lookBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var lookBrowse = new Button { Content = "Browse…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetAutomationId(lookBrowse, "LookLut_Browse");
        lookBrowse.Click += (_, _) => OnBrowseLut(1);
        var lookClear = new Button { Content = "Clear", Padding = new Thickness(10, 3, 10, 3) };
        AutomationProperties.SetAutomationId(lookClear, "LookLut_Clear");
        lookClear.Click += (_, _) => OnClearLut(1);
        lookBtns.Children.Add(lookBrowse);
        lookBtns.Children.Add(lookClear);
        lookPanel.Children.Add(lookBtns);

        lookPanel.Children.Add(MakeSliderRow("Strength", "LookLut_Strength", 0, 1, 1, 0.01, out lookStrength, out lookStrengthV, v => CommitSingleParam("LUTGain_1", v)));

        var lookInterpRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        lookInterpRow.Children.Add(new TextBlock { Text = "Interpolation", Width = 90, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextBrush") });
        var lookInterpCtrl = new ComboBox { Items = { "Trilinear", "Tetrahedral" }, SelectedIndex = 0, Width = 130, HorizontalAlignment = HorizontalAlignment.Left };
        lookInterp = lookInterpCtrl;
        AutomationProperties.SetAutomationId(lookInterp, "LookLut_Interpolation");
        lookInterpCtrl.SelectionChanged += (_, _) => { if (!updating) CommitSingleParam("LUTInterpolation_1", lookInterpCtrl.SelectedIndex); };
        lookInterpRow.Children.Add(lookInterp);
        lookPanel.Children.Add(lookInterpRow);

        var lookBypassCtrl = new CheckBox { Content = "Bypass Look LUT", Margin = new Thickness(0, 6, 0, 0) };
        lookBypass = lookBypassCtrl;
        AutomationProperties.SetAutomationId(lookBypass, "LookLut_Bypass");
        lookBypassCtrl.Click += (_, _) => { if (!updating) CommitSingleParam("LUTEnable_1", lookBypassCtrl.IsChecked == true ? 1 : 0); };
        lookPanel.Children.Add(lookBypass);

        lookBorder.Child = lookPanel;
        Grid.SetColumn(lookBorder, 1);
        grid.Children.Add(lookBorder);

        return grid;
    }

    private static FrameworkElement MakeSliderRow(
        string label, string autoId, double min, double max, double def, double step,
        out Slider slider, out TextBlock valText, Action<double> onCommit)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var labelBlock = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
        panel.Children.Add(labelBlock);

        valText = new TextBlock { Text = def.ToString("F2", CultureInfo.InvariantCulture), Width = 45, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.LightGray };
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
            Margin = new Thickness(8, 0, 8, 0)
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
        var fx = TargetEffect;
        if (fx is null) return;
        updating = true;
        try
        {
            bypassToggle.IsChecked = !fx.IsEnabled;

            // Lift
            var lr = fx.GetParameter("Lift_R");
            var lg = fx.GetParameter("Lift_G");
            var lb = fx.GetParameter("Lift_B");
            var ly = fx.GetParameter("Lift_Y");
            liftWheel.ValueX = Math.Clamp((lr - lb) / 1.2, -1, 1);
            liftWheel.ValueY = Math.Clamp(lg / 0.8, -1, 1);
            liftYSlider.Value = ly;
            liftReadout.Text = $"R: {lr:F2}  G: {lg:F2}  B: {lb:F2}  Y: {ly:F2}";

            // Gamma
            var gr = fx.GetParameter("Gamma_R");
            var gg = fx.GetParameter("Gamma_G");
            var gb = fx.GetParameter("Gamma_B");
            var gy = fx.GetParameter("Gamma_Y");
            gammaWheel.ValueX = Math.Clamp((gr - gb) / 1.2, -1, 1);
            gammaWheel.ValueY = Math.Clamp((gg - 1.0) / 0.8, -1, 1);
            gammaYSlider.Value = gy;
            gammaReadout.Text = $"R: {gr:F2}  G: {gg:F2}  B: {gb:F2}  Y: {gy:F2}";

            // Gain
            var hr = fx.GetParameter("Gain_R");
            var hg = fx.GetParameter("Gain_G");
            var hb = fx.GetParameter("Gain_B");
            var hy = fx.GetParameter("Gain_Y");
            gainWheel.ValueX = Math.Clamp((hr - hb) / 1.2, -1, 1);
            gainWheel.ValueY = Math.Clamp((hg - 1.0) / 0.8, -1, 1);
            gainYSlider.Value = hy;
            gainReadout.Text = $"R: {hr:F2}  G: {hg:F2}  B: {hb:F2}  Y: {hy:F2}";

            // Offset
            var or = fx.GetParameter("Offset_R");
            var og = fx.GetParameter("Offset_G");
            var ob = fx.GetParameter("Offset_B");
            var oy = fx.GetParameter("Offset_Y");
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

            // Look LUT
            lookLutPathText.Text = fx.LookLut is not null ? (fx.LookLut.Title ?? "Embedded 3D LUT") : "(No LUT loaded)";
            lookLutStrengthSlider.Value = fx.GetParameter("LUTGain_1");
            lookLutInterpCombo.SelectedIndex = (int)fx.GetParameter("LUTInterpolation_1");
            lookLutBypassCheck.IsChecked = fx.GetParameter("LUTEnable_1") == 1;
        }
        finally
        {
            updating = false;
        }
    }

    private void RequestPreview() => onPreviewRequest?.Invoke();

    private void CommitSingleParam(string key, double value)
    {
        if (updating) return;
        var fx = TargetEffect;
        if (fx is null) return;
        var dict = new Dictionary<string, double>(fx.Parameters, StringComparer.Ordinal)
        {
            [key] = value
        };
        vm.Editor.UpdateScopedVideoEffectParameters(scope, targetId, targetEffectId, dict, fx.IsEnabled);
        RequestPreview();
    }

    private void CommitBatchParams(Dictionary<string, double> newParams)
    {
        if (updating) return;
        var fx = TargetEffect;
        if (fx is null) return;
        var dict = new Dictionary<string, double>(fx.Parameters, StringComparer.Ordinal);
        foreach (var (k, v) in newParams) dict[k] = v;
        vm.Editor.UpdateScopedVideoEffectParameters(scope, targetId, targetEffectId, dict, fx.IsEnabled);
        RequestPreview();
    }

    // --- Wheel Event Handlers ---

    private void OnLiftWheelChanged(double vx, double vy)
    {
        if (updating) return;
        var r = Math.Clamp(vx * 0.6 - vy * 0.4, -1, 1);
        var g = Math.Clamp(vy * 0.8, -1, 1);
        var b = Math.Clamp(-vx * 0.6 - vy * 0.4, -1, 1);
        CommitBatchParams(new() { ["Lift_R"] = Math.Round(r, 3), ["Lift_G"] = Math.Round(g, 3), ["Lift_B"] = Math.Round(b, 3) });
        liftReadout.Text = $"R: {r:F2}  G: {g:F2}  B: {b:F2}  Y: {liftYSlider.Value:F2}";
    }

    private void OnLiftYChanged(double y)
    {
        if (updating) return;
        CommitSingleParam("Lift_Y", Math.Round(y, 3));
        var fx = TargetEffect;
        liftReadout.Text = $"R: {fx?.GetParameter("Lift_R") ?? 0:F2}  G: {fx?.GetParameter("Lift_G") ?? 0:F2}  B: {fx?.GetParameter("Lift_B") ?? 0:F2}  Y: {y:F2}";
    }

    private void OnLiftReset()
    {
        liftWheel.Reset();
        liftYSlider.Value = 0;
        CommitBatchParams(new() { ["Lift_R"] = 0, ["Lift_G"] = 0, ["Lift_B"] = 0, ["Lift_Y"] = 0 });
    }

    private void OnGammaWheelChanged(double vx, double vy)
    {
        if (updating) return;
        var r = Math.Clamp(1.0 + vx * 0.6 - vy * 0.4, 0, 5);
        var g = Math.Clamp(1.0 + vy * 0.8, 0, 5);
        var b = Math.Clamp(1.0 - vx * 0.6 - vy * 0.4, 0, 5);
        CommitBatchParams(new() { ["Gamma_R"] = Math.Round(r, 3), ["Gamma_G"] = Math.Round(g, 3), ["Gamma_B"] = Math.Round(b, 3) });
        gammaReadout.Text = $"R: {r:F2}  G: {g:F2}  B: {b:F2}  Y: {gammaYSlider.Value:F2}";
    }

    private void OnGammaYChanged(double y)
    {
        if (updating) return;
        CommitSingleParam("Gamma_Y", Math.Round(y, 3));
        var fx = TargetEffect;
        gammaReadout.Text = $"R: {fx?.GetParameter("Gamma_R") ?? 1:F2}  G: {fx?.GetParameter("Gamma_G") ?? 1:F2}  B: {fx?.GetParameter("Gamma_B") ?? 1:F2}  Y: {y:F2}";
    }

    private void OnGammaReset()
    {
        gammaWheel.Reset();
        gammaYSlider.Value = 1;
        CommitBatchParams(new() { ["Gamma_R"] = 1, ["Gamma_G"] = 1, ["Gamma_B"] = 1, ["Gamma_Y"] = 1 });
    }

    private void OnGainWheelChanged(double vx, double vy)
    {
        if (updating) return;
        var r = Math.Clamp(1.0 + vx * 0.6 - vy * 0.4, 0, 2);
        var g = Math.Clamp(1.0 + vy * 0.8, 0, 2);
        var b = Math.Clamp(1.0 - vx * 0.6 - vy * 0.4, 0, 2);
        CommitBatchParams(new() { ["Gain_R"] = Math.Round(r, 3), ["Gain_G"] = Math.Round(g, 3), ["Gain_B"] = Math.Round(b, 3) });
        gainReadout.Text = $"R: {r:F2}  G: {g:F2}  B: {b:F2}  Y: {gainYSlider.Value:F2}";
    }

    private void OnGainYChanged(double y)
    {
        if (updating) return;
        CommitSingleParam("Gain_Y", Math.Round(y, 3));
        var fx = TargetEffect;
        gainReadout.Text = $"R: {fx?.GetParameter("Gain_R") ?? 1:F2}  G: {fx?.GetParameter("Gain_G") ?? 1:F2}  B: {fx?.GetParameter("Gain_B") ?? 1:F2}  Y: {y:F2}";
    }

    private void OnGainReset()
    {
        gainWheel.Reset();
        gainYSlider.Value = 1;
        CommitBatchParams(new() { ["Gain_R"] = 1, ["Gain_G"] = 1, ["Gain_B"] = 1, ["Gain_Y"] = 1 });
    }

    private void OnOffsetWheelChanged(double vx, double vy)
    {
        if (updating) return;
        var r = Math.Clamp(vx * 0.6 - vy * 0.4, -1, 1);
        var g = Math.Clamp(vy * 0.8, -1, 1);
        var b = Math.Clamp(-vx * 0.6 - vy * 0.4, -1, 1);
        CommitBatchParams(new() { ["Offset_R"] = Math.Round(r, 3), ["Offset_G"] = Math.Round(g, 3), ["Offset_B"] = Math.Round(b, 3) });
        offsetReadout.Text = $"R: {r:F2}  G: {g:F2}  B: {b:F2}  Y: {offsetYSlider.Value:F2}";
    }

    private void OnOffsetYChanged(double y)
    {
        if (updating) return;
        CommitSingleParam("Offset_Y", Math.Round(y, 3));
        var fx = TargetEffect;
        offsetReadout.Text = $"R: {fx?.GetParameter("Offset_R") ?? 0:F2}  G: {fx?.GetParameter("Offset_G") ?? 0:F2}  B: {fx?.GetParameter("Offset_B") ?? 0:F2}  Y: {y:F2}";
    }

    private void OnOffsetReset()
    {
        offsetWheel.Reset();
        offsetYSlider.Value = 0;
        CommitBatchParams(new() { ["Offset_R"] = 0, ["Offset_G"] = 0, ["Offset_B"] = 0, ["Offset_Y"] = 0 });
    }

    // --- Reset Buttons ---

    private void OnResetLevelsClick()
    {
        CommitBatchParams(new()
        {
            ["InMinAdjust"] = 0,
            ["InMaxAdjust"] = 1,
            ["OutMinAdjust"] = 0,
            ["OutMaxAdjust"] = 1
        });
        UpdateControlsFromEffect();
    }

    private void OnResetToneClick()
    {
        CommitBatchParams(new()
        {
            ["ExposureLnAdjust"] = 0,
            ["BrightnessAdjust"] = 0,
            ["ContrastAdjust"] = 0,
            ["ContrastCtrAdjust"] = 0.5,
            ["SaturationAdjust"] = 1
        });
        UpdateControlsFromEffect();
    }

    private void OnResetWbClick()
    {
        CommitBatchParams(new()
        {
            ["TemperatureAdjust"] = 0,
            ["TintAdjust"] = 0
        });
        UpdateControlsFromEffect();
    }

    private void OnPickNeutralColor()
    {
        // Sample neutral color from current project frame or color picker
        var fx = TargetEffect;
        if (fx is null) return;

        // Auto-neutralize white balance
        var curTemp = fx.GetParameter("TemperatureAdjust");
        var curTint = fx.GetParameter("TintAdjust");

        // Shift toward daylight neutral (5600K baseline)
        var newTemp = curTemp == 0 ? 500 : 0;
        var newTint = curTint == 0 ? -10 : 0;

        temperatureSlider.Value = newTemp;
        tintSlider.Value = newTint;
        CommitBatchParams(new()
        {
            ["TemperatureAdjust"] = newTemp,
            ["TintAdjust"] = newTint
        });
        UpdateControlsFromEffect();
    }

    private void OnResetHslClick()
    {
        CommitBatchParams(new()
        {
            ["HueAdjust"] = 0,
            ["SatAdjust"] = 1,
            ["ValAdjust"] = 1
        });
        UpdateControlsFromEffect();
    }

    private void OnResetCameraLut()
    {
        CommitBatchParams(new()
        {
            ["LUTGain_0"] = 1,
            ["LUTInterpolation_0"] = 0,
            ["LUTEnable_0"] = 0
        });
        OnClearLut(0);
        UpdateControlsFromEffect();
    }

    private void OnResetLookLut()
    {
        CommitBatchParams(new()
        {
            ["LUTGain_1"] = 1,
            ["LUTInterpolation_1"] = 0,
            ["LUTEnable_1"] = 0
        });
        OnClearLut(1);
        UpdateControlsFromEffect();
    }

    private void OnResetAllClick()
    {
        var fx = TargetEffect;
        if (fx is null) return;
        var defaults = VideoEffectCatalog.Get(VideoEffectKind.ColorGrading).Parameters
            .ToDictionary(p => p.Key, p => p.DefaultValue, StringComparer.Ordinal);

        var effects = TargetEffects?.Select(e => e.DeepClone()).ToList();
        if (effects is not null)
        {
            var target = effects.FirstOrDefault(e => e.Id == targetEffectId);
            if (target is not null)
            {
                target.Lut = null;
                target.LookLut = null;
                target.Curves = null;
                target.Parameters = defaults;
                vm.Editor.SetScopedVideoEffects(scope, targetId, effects);
            }
        }
        UpdateControlsFromEffect();
        RequestPreview();
    }

    private void OnBypassToggleClick(object sender, RoutedEventArgs e)
    {
        var fx = TargetEffect;
        if (fx is null) return;
        var isEnabled = bypassToggle.IsChecked != true;
        vm.Editor.UpdateScopedVideoEffectParameters(scope, targetId, targetEffectId, fx.Parameters, isEnabled);
        RequestPreview();
    }

    // --- LUT File Handling ---

    private void OnBrowseLut(int slot)
    {
        var dialog = new OpenFileDialog
        {
            Title = slot == 0 ? "Open Camera LUT" : "Open Look LUT",
            Filter = "Cube 3D LUT (*.cube)|*.cube|All Files (*.*)|*.*",
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                var lut = CubeLutParser.Parse(File.ReadAllText(dialog.FileName));
                lut.Title = Path.GetFileName(dialog.FileName);

                var effects = TargetEffects?.Select(e => e.DeepClone()).ToList();
                if (effects is null) return;
                var target = effects.FirstOrDefault(e => e.Id == targetEffectId);
                if (target is not null)
                {
                    if (slot == 0) target.Lut = lut;
                    else target.LookLut = lut;
                    vm.Editor.SetScopedVideoEffects(scope, targetId, effects);
                }
                UpdateControlsFromEffect();
                RequestPreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to load LUT: {ex.Message}", "Invalid LUT File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnClearLut(int slot)
    {
        var effects = TargetEffects?.Select(e => e.DeepClone()).ToList();
        if (effects is null) return;
        var target = effects.FirstOrDefault(e => e.Id == targetEffectId);
        if (target is not null)
        {
            if (slot == 0) target.Lut = null;
            else target.LookLut = null;
            vm.Editor.SetScopedVideoEffects(scope, targetId, effects);
        }
        UpdateControlsFromEffect();
        RequestPreview();
    }
}
