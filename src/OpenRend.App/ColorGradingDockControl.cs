using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

internal sealed record GradingPreview(EffectScope Scope, Guid Target, Guid? Grade,
    ColorCurvesData? Rgb = null, HSLCurvesData? Hsl = null, LuminanceThresholds? Thresholds = null)
{
    public void Apply(ProjectDocument snapshot)
    {
        var (effects, _) = ColorGradingDockControl.FindTarget(snapshot, Scope, Target);
        if (effects is null) return;
        var grade = effects.FirstOrDefault(effect => effect.Kind == VideoEffectKind.ColorGrading &&
            (Grade is null || effect.Id == Grade));
        if (grade is null)
        {
            grade = VideoEffect.Create(VideoEffectKind.ColorGrading);
            effects.Add(grade);
        }
        if (Rgb is not null) grade.Curves = Rgb;
        if (Hsl is not null) grade.HSLCurves = Hsl;
        if (Thresholds is { } thresholds)
        {
            grade.SetParameter(TonalRangeGrading.LowerKey, thresholds.Lower);
            grade.SetParameter(TonalRangeGrading.UpperKey, thresholds.Upper);
        }
    }
}

/// <summary>
/// Shared Color Grading controls for docked and scoped editing.
/// Provides category navigation (LUTs, Color Wheels, Utilities, Curves),
/// dual 3D LUT slots with built-in camera/look presets, interactive color wheels,
/// curves, tone, levels, white balance, and HSL controls.
/// </summary>
public sealed partial class ColorGradingDockControl : UserControl
{
    private EditorViewModel? vm;
    private Action? previewCallback;
    private Action<EffectScope, Guid, int>? popoutCallback;
    private Func<EffectScope, Guid?>? resolveTarget;
    private (EffectScope Scope, Guid Target, Guid? Grade)? pinnedTarget;
    private EffectScope scope;
    private Guid targetId;
    private Guid? effectId;
    private List<VideoEffect>? targetEffects;
    private bool updating;
    private bool showingCurvePreview;
    private bool exportingLut;

    internal event Action<GradingPreview?>? GradingPreviewRequested;
    private static readonly VideoEffect NeutralGrade = VideoEffect.Create(VideoEffectKind.ColorGrading);
    private static readonly ColorCurvesData NeutralRgb = new();
    private static readonly HSLCurvesData NeutralHsl = new();

    // Header controls
    private readonly TextBlock targetLabel;
    private readonly ComboBox scopePicker;
    private readonly ToggleButton bypassBtn;
    private readonly Button resetAllBtn;
    private readonly ComboBox presetCombo;
    private readonly Button exportLutBtn;
    private readonly Button popoutBtn;
    private readonly Button undoBtn;
    private readonly Button redoBtn;
    private readonly Button closeBtn;

    // Navigation Tree / List
    private readonly ListBox navList;

    // Pages Container
    private readonly Grid pageContainer;
    private readonly Grid wheelsPage;
    private readonly Grid toneLevelsPage;
    private readonly Grid wbHslPage;
    private readonly Grid curvesPage;
    private readonly Grid lutsPage;
    private readonly CheckBox pinCurves = new()
    {
        Content = "Pin curves", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 4)
    };
    private readonly ScrollViewer toolsScroll;
    private readonly ScrollViewer curvesScroll;
    private readonly GridSplitter curvesSplitter;
    private readonly RowDefinition toolsRow = new();
    private readonly RowDefinition curvesRow = new();
    private GridLength pinnedToolsHeight = new(2, GridUnitType.Star);
    private GridLength pinnedCurvesHeight = new(3, GridUnitType.Star);

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
    private readonly HSLCurvesControl hslCurvesControl;

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

    private VideoEffect? ActiveEffect => targetEffects?.FirstOrDefault(effect =>
        effect.Kind == VideoEffectKind.ColorGrading && (effectId is null || effect.Id == effectId));

    public ColorGradingDockControl()
    {
        SetResourceReference(BackgroundProperty, "WindowBrush");
        ClipToBounds = true;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var header = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 5, 4, 1) };
        header.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        header.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        AutomationProperties.SetAutomationId(header, "ColorGradingHeader");

        var headerContent = new StackPanel();
        targetLabel = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 5)
        };
        AutomationProperties.SetAutomationId(targetLabel, "ColorGrading_TargetLabel");
        headerContent.Children.Add(targetLabel);

        var tools = new WrapPanel();
        scopePicker = new ComboBox
        {
            Width = 105, Margin = new Thickness(0, 0, 4, 4),
            SelectedValuePath = nameof(ComboBoxItem.Tag), ToolTip = "Choose which part of the project to grade",
            Visibility = Visibility.Collapsed
        };
        foreach (var (level, label) in new[]
        {
            (EffectScope.Event, "Event FX"), (EffectScope.Track, "Track FX"),
            (EffectScope.Media, "Media FX"), (EffectScope.Output, "Output FX")
        })
            scopePicker.Items.Add(new ComboBoxItem { Content = label, Tag = level });
        scopePicker.SelectedValue = EffectScope.Event;
        AutomationProperties.SetName(scopePicker, "Grading target");
        AutomationProperties.SetAutomationId(scopePicker, "ColorGrading_Scope");
        tools.Children.Add(scopePicker);

        bypassBtn = new ToggleButton { Content = "Bypass", Margin = new Thickness(0, 0, 4, 4) };
        bypassBtn.SetResourceReference(StyleProperty, "TextToggleButton");
        AutomationProperties.SetAutomationId(bypassBtn, "m_BypassButton");
        bypassBtn.Click += OnBypassClick;
        tools.Children.Add(bypassBtn);

        resetAllBtn = new Button { Content = "Reset all", Margin = new Thickness(0, 0, 4, 4) };
        AutomationProperties.SetAutomationId(resetAllBtn, "m_ResetAllButton");
        resetAllBtn.Click += OnResetAllClick;
        tools.Children.Add(resetAllBtn);

        presetCombo = MakeLutPicker(BuiltinLutLibrary.LookLutNames, "ColorGrading_PresetCombo", "Look LUT");
        presetCombo.ToolTip = "Look LUT";
        presetCombo.Width = 132;
        presetCombo.Margin = new Thickness(0, 0, 4, 4);
        presetCombo.SelectionChanged += (_, _) => SelectLut(1, presetCombo);
        tools.Children.Add(presetCombo);

        exportLutBtn = new Button { Content = "Export LUT…", Margin = new Thickness(0, 0, 4, 4) };
        exportLutBtn.ToolTip = "Save the current target's grade as a .cube file";
        AutomationProperties.SetAutomationId(exportLutBtn, "ColorGrading_ExportLut");
        exportLutBtn.Click += ExportGradeLut;
        tools.Children.Add(exportLutBtn);

        popoutBtn = new Button { Content = "Open panel…", Margin = new Thickness(0, 0, 4, 4) };
        AutomationProperties.SetAutomationId(popoutBtn, "ColorGrading_PopoutButton");
        tools.Children.Add(popoutBtn);
        undoBtn = AddWindowAction("Undo", "ColorGrading_Undo", () => MoveHistory(undo: true));
        redoBtn = AddWindowAction("Redo", "ColorGrading_Redo", () => MoveHistory(undo: false));
        closeBtn = AddWindowAction("Close", "ColorGrading_Close", () => Window.GetWindow(this)?.Close());

        headerContent.Children.Add(tools);
        header.Child = headerContent;
        root.Children.Add(header);

        const double navWidth = 114;
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(navWidth) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        navList = new ListBox { BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(3) };
        navList.SetResourceReference(BackgroundProperty, "PanelBrush");
        navList.SetResourceReference(BorderBrushProperty, "BorderBrush");
        AutomationProperties.SetAutomationId(navList, "ColorGradingNavigation");

        navList.Items.Add(MakeNavItem("LUTs", "LUTsItem"));
        navList.Items.Add(MakeNavItem("Color Wheels", "ColorWheelsItem"));
        navList.Items.Add(MakeNavItem("Tone & Levels", "UtilitiesItem"));
        navList.Items.Add(MakeNavItem("White Balance / HSL", "WbHslItem"));
        navList.Items.Add(MakeNavItem("Curves", "CurvesItem"));
        navList.Items.Add(MakeNavItem("Tonal Ranges", "TonalRangesItem"));

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
        curvesPage = CreateCurvesPage(out curvesControl, out curveReadout, out hslCurvesControl);
        rangePage = CreateRangePage(out var rangeWheelsGrid);

        pageContainer.Children.Add(lutsPage);
        pageContainer.Children.Add(wheelsPage);
        pageContainer.Children.Add(toneLevelsPage);
        pageContainer.Children.Add(wbHslPage);
        pageContainer.Children.Add(rangePage);

        toolsScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = pageContainer
        };
        curvesPage.Margin = pageContainer.Margin;
        curvesScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = curvesPage
        };
        curvesSplitter = new GridSplitter
        {
            Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ToolTip = "Resize pinned curves"
        };
        AutomationProperties.SetAutomationId(curvesSplitter, "ColorGrading_CurvesSplitter");
        AutomationProperties.SetName(curvesSplitter, "Resize pinned curves");
        curvesSplitter.DragCompleted += (_, _) =>
        {
            pinnedToolsHeight = toolsRow.Height;
            pinnedCurvesHeight = curvesRow.Height;
        };
        var pages = new Grid();
        pages.RowDefinitions.Add(toolsRow);
        pages.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pages.RowDefinitions.Add(curvesRow);
        Grid.SetRow(curvesSplitter, 1);
        Grid.SetRow(curvesScroll, 2);
        pages.Children.Add(toolsScroll);
        pages.Children.Add(curvesSplitter);
        pages.Children.Add(curvesScroll);
        Grid.SetColumn(pages, 1);
        body.Children.Add(pages);
        pinCurves.Checked += (_, _) => UpdateCurveLayout(navList.SelectedIndex);
        pinCurves.Unchecked += (_, _) => UpdateCurveLayout(navList.SelectedIndex);
        var sections = new[] { lutsPage, wheelsPage, toneLevelsPage, wbHslPage, rangeWheelsGrid };
        var columnCount = 1;
        foreach (var page in sections) ArrangeSections(page, columnCount);
        body.SizeChanged += (_, args) =>
        {
            // Leave room for page margins and a vertical scrollbar before placing sections side by side.
            var columns = args.NewSize.Width - navWidth >= 552 ? 2 : 1;
            if (columns == columnCount) return;
            columnCount = columns;
            foreach (var page in sections) ArrangeSections(page, columnCount);
        };
        navList.SelectionChanged += (_, _) => toolsScroll.ScrollToTop();

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        Content = root;
        SelectPage(0);
        popoutBtn.Click += (_, _) =>
        {
            if (targetEffects is not null) popoutCallback?.Invoke(scope, targetId, navList.SelectedIndex);
        };
        scopePicker.SelectionChanged += (_, _) => RefreshTarget();
        RefreshTarget();
        Loaded += (_, _) => ScopeLimitsChanged?.Invoke();
        Unloaded += (_, _) =>
        {
            CancelCurveDrags();
            CancelThresholdDrag();
            ScopeLimitsChanged?.Invoke();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                CancelCurveDrags();
                CancelThresholdDrag();
            }
            ScopeLimitsChanged?.Invoke();
        };

        Button AddWindowAction(string title, string id, Action action)
        {
            var button = new Button { Content = title, Margin = new Thickness(0, 0, 4, 4), Visibility = Visibility.Collapsed };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += (_, _) => action();
            tools.Children.Add(button);
            return button;
        }
    }

    private static ListBoxItem MakeNavItem(string label, string autoId)
    {
        var item = new ListBoxItem
        {
            Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
            Padding = new Thickness(5, 5, 5, 5)
        };
        AutomationProperties.SetAutomationId(item, autoId);
        AutomationProperties.SetName(item, label);
        return item;
    }

    internal void Initialize(EditorViewModel viewModel, Action requestPreview,
        Func<EffectScope, Guid?> targetResolver, Action<EffectScope, Guid, int> openPopout)
    {
        vm = viewModel;
        previewCallback = requestPreview;
        resolveTarget = targetResolver;
        popoutCallback = openPopout;
        scopePicker.Visibility = Visibility.Visible;
        RefreshTarget();
    }

    internal void RefreshContext(EffectScope level)
    {
        if (scopePicker.SelectedValue is EffectScope selected && selected == level)
            RefreshTarget();
    }

    internal bool HandlesScopePickerKey(Key key) => scopePicker.IsKeyboardFocusWithin &&
        (key is Key.Up or Key.Down or Key.Home or Key.End or Key.Enter or Key.Space ||
            key == Key.Escape && scopePicker.IsDropDownOpen);

    internal void SetWorkspaceFloating(bool floating) =>
        popoutBtn.Visibility = floating ? Visibility.Collapsed : Visibility.Visible;

    internal void BindScope(EditorViewModel viewModel, EffectScope effectScope, Guid id, Guid? gradeId, Action? requestPreview,
        Func<EffectScope, Guid?>? targetResolver, int page)
    {
        vm = viewModel;
        previewCallback = requestPreview;
        pinnedTarget = (effectScope, id, gradeId);
        resolveTarget = targetResolver;
        scopePicker.Visibility = targetResolver is null ? Visibility.Collapsed : Visibility.Visible;
        scopePicker.SelectedValue = effectScope;
        popoutBtn.Visibility = Visibility.Collapsed;
        undoBtn.Visibility = redoBtn.Visibility = closeBtn.Visibility = Visibility.Visible;
        navList.SelectedIndex = page;
        RefreshTarget();
    }

    internal void RefreshTarget()
    {
        CancelCurveDrags();
        CancelThresholdDrag();
        scope = scopePicker.SelectedValue is EffectScope selected ? selected : EffectScope.Event;
        if (pinnedTarget is { } pinned && pinned.Scope == scope)
        {
            targetId = pinned.Target;
            effectId = pinned.Grade;
        }
        else
        {
            targetId = resolveTarget?.Invoke(scope) ?? Guid.Empty;
            effectId = null;
        }
        var (effects, name) = FindTarget(vm?.Project, scope, targetId);
        var targetChanged = !ReferenceEquals(targetEffects, effects);
        targetEffects = effects;
        targetLabel.Text = name;
        targetLabel.ToolTip = name;
        bypassBtn.IsEnabled = resetAllBtn.IsEnabled = ActiveEffect is not null;
        UpdateLutExportAvailability();
        presetCombo.IsEnabled = popoutBtn.IsEnabled = pageContainer.IsEnabled = targetEffects is not null;
        curvesPage.IsEnabled = targetEffects is not null;
        undoBtn.IsEnabled = vm?.Editor.CanUndo == true;
        redoBtn.IsEnabled = vm?.Editor.CanRedo == true;
        UpdateControlsFromEffect(targetChanged);
    }

    internal static (List<VideoEffect>? Effects, string Name) FindTarget(ProjectDocument? project, EffectScope scope, Guid targetId)
    {
        if (project is null) return (null, "Select one unlocked video event");
        switch (scope)
        {
            case EffectScope.Event:
                var pair = project.Tracks.Where(track => track.Kind == TrackKind.Video)
                    .SelectMany(track => track.Clips.Where(clip => clip.Id == targetId).Select(clip => (Track: track, Clip: clip)))
                    .FirstOrDefault();
                if (pair.Clip is { IsLocked: false } clip && !pair.Track.IsLocked)
                    return (clip.VideoEffects, $"Event FX · {clip.Name}");
                return (null, "Select one unlocked video event");
            case EffectScope.Track:
                var matchedTrack = project.Tracks.FirstOrDefault(track => track.Id == targetId && track.Kind == TrackKind.Video && !track.IsLocked);
                return matchedTrack is null ? (null, "Select an unlocked video track") : (matchedTrack.VideoEffects, $"Track FX · {matchedTrack.Name}");
            case EffectScope.Media:
                var media = project.Media.FirstOrDefault(media => media.Id == targetId && media.HasVideo);
                return media is null ? (null, "Select video media or a video event") : (media.VideoEffects, $"Media FX · {media.Name}");
            case EffectScope.Output:
                return (project.VideoOutputEffects, "Video Output FX");
            default:
                return (null, "Color grading unavailable");
        }
    }

    private void MoveHistory(bool undo)
    {
        if (vm is not null && (undo ? vm.Editor.Undo() : vm.Editor.Redo()))
            previewCallback?.Invoke();
    }

    private void SelectPage(int index)
    {
        CancelCurveDrags();
        if (index != 5) CancelThresholdDrag();
        lutsPage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        wheelsPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        toneLevelsPage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        wbHslPage.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        rangePage.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCurveLayout(index);
        ScopeLimitsChanged?.Invoke();
    }

    private void UpdateCurveLayout(int index)
    {
        var curvesOnly = index == 4;
        var split = !curvesOnly && pinCurves.IsChecked == true;
        toolsScroll.Visibility = curvesOnly ? Visibility.Collapsed : Visibility.Visible;
        curvesScroll.Visibility = curvesOnly || split ? Visibility.Visible : Visibility.Collapsed;
        curvesSplitter.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        toolsRow.MinHeight = split ? 68 : 0;
        curvesRow.MinHeight = split ? 96 : 0;
        toolsRow.Height = curvesOnly ? new GridLength(0) : split ? pinnedToolsHeight : new GridLength(1, GridUnitType.Star);
        curvesRow.Height = curvesOnly ? new GridLength(1, GridUnitType.Star) : split ? pinnedCurvesHeight : new GridLength(0);
    }

    private static void ArrangeSections(Grid page, int columns)
    {
        page.ColumnDefinitions.Clear();
        page.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) page.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < page.Children.Count; i++)
        {
            if (i % columns == 0) page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(page.Children[i], i % columns);
            Grid.SetRow(page.Children[i], i / columns);
        }
    }

    // --- Page 0: LUTs ---

    private Grid CreateLutsPage(
        out TextBlock camPath, out ComboBox camPreset, out Slider camStrength, out TextBlock camStrengthV,
        out ComboBox camInterp, out CheckBox camBypass,
        out TextBlock lookPath, out ComboBox lookPreset, out Slider lookStrength, out TextBlock lookStrengthV,
        out ComboBox lookInterp, out CheckBox lookBypass)
    {
        var grid = new Grid();
        grid.Children.Add(CreateLutSlot(0, "Camera LUT", BuiltinLutLibrary.CameraLutNames,
            out camPath, out camPreset, out camStrength, out camStrengthV, out camInterp, out camBypass));
        grid.Children.Add(CreateLutSlot(1, "Look LUT", BuiltinLutLibrary.LookLutNames,
            out lookPath, out lookPreset, out lookStrength, out lookStrengthV, out lookInterp, out lookBypass));
        return grid;
    }

    private Border CreateLutSlot(int slot, string title, IReadOnlyList<string> names, out TextBlock path, out ComboBox picker,
        out Slider strength, out TextBlock strengthValue, out ComboBox interpolation, out CheckBox bypass)
    {
        var id = slot == 0 ? "cameraLut" : "lookLut";
        var panel = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var reset = new Button { Content = "Reset" };
        DockPanel.SetDock(reset, Dock.Right);
        AutomationProperties.SetAutomationId(reset, id + "_ResetButton");
        AutomationProperties.SetName(reset, $"Reset {title}");
        reset.Click += (_, _) => ResetLutSlot(slot);
        header.Children.Add(reset);
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(header);

        path = new TextBlock { Margin = new Thickness(0, 0, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis };
        path.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        AutomationProperties.SetAutomationId(path, id + "_Path");
        panel.Children.Add(path);

        var preset = MakeLutPicker(names, slot == 0 ? "CameraLut_Preset" : "LookLut_Preset", $"{title} preset");
        picker = preset;
        preset.SelectionChanged += (_, _) => SelectLut(slot, preset);
        panel.Children.Add(LabeledRow("Preset", preset));

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var browse = new Button { Content = "Browse…", Margin = new Thickness(0, 0, 4, 2) };
        AutomationProperties.SetAutomationId(browse, id + "_BrowseButton");
        AutomationProperties.SetName(browse, $"Browse for {title}");
        browse.Click += (_, _) => BrowseLutSlot(slot);
        var clear = new Button { Content = "Clear", Margin = new Thickness(0, 0, 0, 2) };
        AutomationProperties.SetAutomationId(clear, id + "_Clear");
        AutomationProperties.SetName(clear, $"Clear {title}");
        clear.Click += (_, _) => SetLutSlot(slot, null);
        buttons.Children.Add(browse);
        buttons.Children.Add(clear);
        panel.Children.Add(buttons);

        panel.Children.Add(MakeSliderRow("Strength", id + "_Strength", 0, 1, 1, 0.01,
            out strength, out strengthValue, value => CommitSingleParam($"LUTGain_{slot}", value)));

        var method = new ComboBox { Items = { "Trilinear", "Tetrahedral" }, SelectedIndex = 0 };
        interpolation = method;
        AutomationProperties.SetAutomationId(method, id + "_tetrahedral_ToggleButton");
        AutomationProperties.SetName(method, $"{title} interpolation");
        method.SelectionChanged += (_, _) =>
        {
            if (method.SelectedIndex >= 0) CommitSingleParam($"LUTInterpolation_{slot}", method.SelectedIndex);
        };
        panel.Children.Add(LabeledRow("Interpolation", method));

        var check = new CheckBox { Content = $"Bypass {title}" };
        bypass = check;
        AutomationProperties.SetAutomationId(check, id + "_Bypass");
        check.Click += (_, _) => CommitSingleParam($"LUTEnable_{slot}", check.IsChecked == true ? 1 : 0);
        panel.Children.Add(check);

        var section = new Border { Child = panel, BorderThickness = new Thickness(1), Margin = new Thickness(3), Padding = new Thickness(8) };
        section.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        section.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return section;
    }

    private static DockPanel LabeledRow(string label, Control input)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var caption = new Label { Content = label, Target = input, Width = 85, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetLabeledBy(input, caption);
        row.Children.Add(caption);
        row.Children.Add(input);
        return row;
    }

    private static ComboBox MakeLutPicker(IReadOnlyList<string> names, string id, string label)
    {
        var picker = new ComboBox();
        picker.Items.Add("None");
        foreach (var name in names) picker.Items.Add(name);
        picker.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(picker, id);
        AutomationProperties.SetName(picker, label);
        return picker;
    }

    private void SelectLut(int slot, ComboBox picker)
    {
        if (updating || picker.SelectedItem is not string name) return;
        SetLutSlot(slot, picker.SelectedIndex == 0 ? null : BuiltinLutLibrary.CreatePreset(name));
    }

    private static void SyncLutPicker(ComboBox picker, CubeLut? lut)
    {
        var imported = picker.Items[^1] as ComboBoxItem;
        var matchingPreset = lut is null ? "None" : picker.Items.OfType<string>().Skip(1)
            .FirstOrDefault(name => string.Equals(name, lut.Title, StringComparison.OrdinalIgnoreCase));
        if (matchingPreset is not null)
        {
            if (imported is not null) picker.Items.Remove(imported);
            picker.SelectedItem = matchingPreset;
            return;
        }

        if (imported is null)
        {
            imported = new ComboBoxItem { Content = "Imported LUT", IsEnabled = false };
            picker.Items.Add(imported);
        }
        picker.SelectedItem = imported;
    }

    // --- Page 1: Color Wheels ---

    private Grid CreateWheelsPage(
        out ColorGradingWheelControl lw, out Slider lys, out TextBlock lr,
        out ColorGradingWheelControl gw, out Slider gys, out TextBlock gr,
        out ColorGradingWheelControl hw, out Slider hys, out TextBlock hr,
        out ColorGradingWheelControl ow, out Slider oys, out TextBlock orBlock)
    {
        var grid = new Grid();

        var liftCard = MakeWheelCard("Shadows (Lift)", "ColorWheel_Shadows",
            -1.0, 1.0, 0.0,
            -1.0, 1.0, 0.0,
            -1.0, 1.0, 0.0,
            -1.0, 1.0, 0.0,
            0.01, true,
            out lw, out lys, out lr,
            (r, g, b, y) => OnWheelGroupChanged("Lift", r, g, b, y),
            () => ResetWheelGroup("Lift"));
        grid.Children.Add(liftCard);

        var gammaCard = MakeWheelCard("Midtones (Gamma)", "ColorWheel_Midtones",
            0.0, 5.0, 1.0,
            0.0, 5.0, 1.0,
            0.0, 5.0, 1.0,
            0.0, 5.0, 1.0,
            0.01, false,
            out gw, out gys, out gr,
            (r, g, b, y) => OnWheelGroupChanged("Gamma", r, g, b, y),
            () => ResetWheelGroup("Gamma"));
        grid.Children.Add(gammaCard);

        var gainCard = MakeWheelCard("Highlights (Gain)", "ColorWheel_Highlights",
            0.0, 2.0, 1.0,
            0.0, 2.0, 1.0,
            0.0, 2.0, 1.0,
            0.0, 2.0, 1.0,
            0.01, true,
            out hw, out hys, out hr,
            (r, g, b, y) => OnWheelGroupChanged("Gain", r, g, b, y),
            () => ResetWheelGroup("Gain"));
        grid.Children.Add(gainCard);

        var offsetCard = MakeWheelCard("Offset", "ColorWheel_Log_Offset",
            -1.0, 1.0, 0.0,
            -1.0, 1.0, 0.0,
            -1.0, 1.0, 0.0,
            -1.0, 1.0, 0.0,
            0.01, false,
            out ow, out oys, out orBlock,
            (r, g, b, y) => OnWheelGroupChanged("Offset", r, g, b, y),
            () => ResetWheelGroup("Offset"));
        grid.Children.Add(offsetCard);

        return grid;
    }

    private FrameworkElement MakeWheelCard(
        string title, string autoId,
        double rMin, double rMax, double rDef,
        double gMin, double gMax, double gDef,
        double bMin, double bMax, double bDef,
        double yMin, double yMax, double yDef,
        double yStep, bool isPickerVisible,
        out ColorGradingWheelControl wheel, out Slider ySlider, out TextBlock readout,
        Action<double, double, double, double> onAllChanged, Action onReset)
    {
        wheel = new ColorGradingWheelControl();
        wheel.Configure(title, autoId, rMin, rMax, rDef, gMin, gMax, gDef, bMin, bMax, bDef, yMin, yMax, yDef, yStep, isPickerVisible);
        ySlider = wheel.YSlider;
        readout = wheel.ReadoutTextBlock;
        wheel.AllValuesChanged += onAllChanged;
        wheel.ResetRequested += onReset;
        return wheel;
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


        // Levels Card
        var levelsBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(3),
            Padding = new Thickness(8)
        };
        var levelsPanel = new StackPanel();
        var levelsHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        levelsHeader.Children.Add(new TextBlock { Text = "Levels", FontWeight = FontWeights.SemiBold });
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
        levelsBorder.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        levelsBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        grid.Children.Add(levelsBorder);

        // Tone Card
        var toneBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(3),
            Padding = new Thickness(8)
        };
        var tonePanel = new StackPanel();
        var toneHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        toneHeader.Children.Add(new TextBlock { Text = "Tone & Exposure", FontWeight = FontWeights.SemiBold });
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
        toneBorder.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        toneBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
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


        // White Balance Card
        var wbBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(3),
            Padding = new Thickness(8)
        };
        var wbPanel = new StackPanel();
        var wbHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        wbHeader.Children.Add(new TextBlock { Text = "White Balance", FontWeight = FontWeights.SemiBold });
        var resetWbBtn = new Button { Content = "Reset", Padding = new Thickness(4, 0, 4, 0), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(resetWbBtn, "whiteBalance_Reset_Button");
        resetWbBtn.Click += (_, _) => ResetParams(new() { [WhiteBalanceSampler.TemperatureKey] = 0, [WhiteBalanceSampler.TintKey] = 0 });
        DockPanel.SetDock(resetWbBtn, Dock.Right);
        wbHeader.Children.Add(resetWbBtn);
        wbPanel.Children.Add(wbHeader);

        wbPanel.Children.Add(MakeSliderRow("Temperature", "temperature", -WhiteBalanceSampler.TemperatureLimit, WhiteBalanceSampler.TemperatureLimit,
            0, 10, out temp, out tempV, v => CommitSingleParam(WhiteBalanceSampler.TemperatureKey, v)));
        wbPanel.Children.Add(MakeSliderRow("Tint", "tint", -WhiteBalanceSampler.TintLimit, WhiteBalanceSampler.TintLimit,
            0, 1, out tint, out tintV, v => CommitSingleParam(WhiteBalanceSampler.TintKey, v)));
        var sample = new Button { Content = "Sample _neutral tone…", HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(sample, "ColorGrading_SampleNeutralTone");
        sample.Click += (_, _) => SampleNeutralTone();
        wbPanel.Children.Add(sample);
        wbBorder.Child = wbPanel;
        wbBorder.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        wbBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        grid.Children.Add(wbBorder);

        // HSL Card
        var hslBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(3),
            Padding = new Thickness(8)
        };
        var hslPanel = new StackPanel();
        var hslHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        hslHeader.Children.Add(new TextBlock { Text = "HSL Adjust", FontWeight = FontWeights.SemiBold });
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
        hslBorder.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        hslBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        grid.Children.Add(hslBorder);

        return grid;
    }

    // --- Page 4: Curves ---

    private Grid CreateCurvesPage(
        out ColorCurvesControl ctrl, out TextBlock readout,
        out HSLCurvesControl hslCtrl)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var modeBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        var btnTabColor = new Button { Content = "RGB", Margin = new Thickness(0, 0, 4, 4), FontWeight = FontWeights.Bold };
        var btnTabHsl = new Button { Content = "HSL", Margin = new Thickness(0, 0, 4, 4) };
        AutomationProperties.SetName(btnTabColor, "RGB curves");
        AutomationProperties.SetName(btnTabHsl, "HSL curves");
        modeBar.Children.Add(btnTabColor);
        modeBar.Children.Add(btnTabHsl);
        AutomationProperties.SetAutomationId(pinCurves, "ColorGrading_PinCurves");
        modeBar.Children.Add(pinCurves);
        Grid.SetRow(modeBar, 0);
        grid.Children.Add(modeBar);

        var colorGrid = new Grid();
        colorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        colorGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var hslGrid = new Grid { Visibility = Visibility.Collapsed };
        hslGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hslGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        btnTabColor.Click += (_, _) =>
        {
            CancelCurveDrags();
            colorGrid.Visibility = Visibility.Visible;
            hslGrid.Visibility = Visibility.Collapsed;
            btnTabColor.FontWeight = FontWeights.Bold;
            btnTabHsl.FontWeight = FontWeights.Normal;
        };
        btnTabHsl.Click += (_, _) =>
        {
            CancelCurveDrags();
            colorGrid.Visibility = Visibility.Collapsed;
            hslGrid.Visibility = Visibility.Visible;
            btnTabColor.FontWeight = FontWeights.Normal;
            btnTabHsl.FontWeight = FontWeights.Bold;
        };

        // --- Color Curves ---
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var btnRgb = new Button { Content = "RGB", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0), FontWeight = FontWeights.Bold };
        var btnRed = new Button { Content = "Red", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        var btnGreen = new Button { Content = "Green", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        var btnBlue = new Button { Content = "Blue", Padding = new Thickness(8, 2, 8, 2) };
        top.Children.Add(btnRgb);
        top.Children.Add(btnRed);
        top.Children.Add(btnGreen);
        top.Children.Add(btnBlue);

        var rstChBtn = new Button { Content = "Reset Channel", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        var rstAllBtn = new Button { Content = "Reset All Curves", Padding = new Thickness(8, 2, 8, 2) };
        var smoothBtn = new Button { Content = "Smooth curve", Margin = new Thickness(4, 0, 0, 0) };
        top.Children.Add(rstChBtn);
        top.Children.Add(rstAllBtn);
        top.Children.Add(smoothBtn);

        Grid.SetRow(top, 0);
        colorGrid.Children.Add(top);

        ctrl = new ColorCurvesControl { Margin = new Thickness(0, 0, 0, 4), MinHeight = 170 };
        AutomationProperties.SetAutomationId(ctrl, "CurvesItem_Control");
        AutomationProperties.SetName(ctrl, "RGB curve graph");
        var c = ctrl;
        void UpdateColorChannelStyles(CurveChannel ch)
        {
            btnRgb.FontWeight = ch == CurveChannel.Master ? FontWeights.Bold : FontWeights.Normal;
            btnRed.FontWeight = ch == CurveChannel.Red ? FontWeights.Bold : FontWeights.Normal;
            btnGreen.FontWeight = ch == CurveChannel.Green ? FontWeights.Bold : FontWeights.Normal;
            btnBlue.FontWeight = ch == CurveChannel.Blue ? FontWeights.Bold : FontWeights.Normal;
        }
        btnRgb.Click += (_, _) => { c.ActiveChannel = CurveChannel.Master; UpdateColorChannelStyles(CurveChannel.Master); };
        btnRed.Click += (_, _) => { c.ActiveChannel = CurveChannel.Red; UpdateColorChannelStyles(CurveChannel.Red); };
        btnGreen.Click += (_, _) => { c.ActiveChannel = CurveChannel.Green; UpdateColorChannelStyles(CurveChannel.Green); };
        btnBlue.Click += (_, _) => { c.ActiveChannel = CurveChannel.Blue; UpdateColorChannelStyles(CurveChannel.Blue); };
        rstChBtn.Click += (_, _) => c.ResetActive();
        rstAllBtn.Click += (_, _) => c.ResetAll();
        smoothBtn.Click += (_, _) => c.SmoothActive();
        ctrl.CurvesChanged += CommitRgbCurves;
        ctrl.CurvePreviewChanged += PreviewCurves;

        var rgbStatus = new TextBlock { Text = "Master curve", Margin = new Thickness(0, 0, 0, 4) };
        rgbStatus.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        readout = rgbStatus;
        AutomationProperties.SetAutomationId(rgbStatus, "ColorGrading_RgbPoint");
        c.PointSelected += text => rgbStatus.Text = text;
        colorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(rgbStatus, 2);
        colorGrid.Children.Add(rgbStatus);

        Grid.SetRow(ctrl, 1);
        colorGrid.Children.Add(ctrl);

        // --- HSL Curves ---
        var hslTop = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var btnHvh = new Button { Content = "Hue vs Hue", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 0) };
        var btnHvs = new Button { Content = "Hue vs Sat", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 0), FontWeight = FontWeights.Bold };
        var btnHvl = new Button { Content = "Hue vs Lum", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 0) };
        var btnSvs = new Button { Content = "Sat vs Sat", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 0) };
        var btnSvl = new Button { Content = "Sat vs Lum", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 0), ToolTip = "Saturation vs Luminance" };
        var btnLvs = new Button { Content = "Lum vs Sat", Padding = new Thickness(6, 2, 6, 2) };
        hslTop.Children.Add(btnHvh);
        hslTop.Children.Add(btnHvs);
        hslTop.Children.Add(btnHvl);
        hslTop.Children.Add(btnSvs);
        hslTop.Children.Add(btnSvl);
        hslTop.Children.Add(btnLvs);

        var rstHslModeBtn = new Button { Content = "Reset Mode", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 0) };
        var rstAllHslBtn = new Button { Content = "Reset All HSL", Padding = new Thickness(6, 2, 6, 2) };
        hslTop.Children.Add(rstHslModeBtn);
        hslTop.Children.Add(rstAllHslBtn);

        Grid.SetRow(hslTop, 0);
        hslGrid.Children.Add(hslTop);

        hslCtrl = new HSLCurvesControl { Margin = new Thickness(0, 0, 0, 4), MinHeight = 170 };
        AutomationProperties.SetAutomationId(hslCtrl, "HSLCurvesItem_Control");
        AutomationProperties.SetName(hslCtrl, "HSL curve graph");
        var hc = hslCtrl;
        var zoomReadout = new TextBlock
        {
            MinWidth = 34, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 4, 4)
        };
        zoomReadout.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        AutomationProperties.SetAutomationId(zoomReadout, "ColorGrading_CurveZoom");
        CurveGraphControl ActiveGraph() => colorGrid.Visibility == Visibility.Visible ? c : hc;
        void UpdateZoom() => zoomReadout.Text = $"{ActiveGraph().Zoom * 100:0}%";
        void AddViewButton(string text, string label, string id, Action<CurveGraphControl> action)
        {
            var button = new Button { Content = text, ToolTip = label, Margin = new Thickness(0, 0, 4, 4) };
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetAutomationId(button, id);
            button.Click += (_, _) => action(ActiveGraph());
            modeBar.Children.Add(button);
        }
        AddViewButton("−", "Zoom out", "ColorGrading_CurveZoomOut", graph => graph.StepZoom(-1));
        AddViewButton("+", "Zoom in", "ColorGrading_CurveZoomIn", graph => graph.StepZoom(1));
        AddViewButton("Fit", "Reset curve view", "ColorGrading_CurveFit", graph => graph.ResetView());
        modeBar.Children.Add(zoomReadout);
        c.ViewChanged += UpdateZoom;
        hc.ViewChanged += UpdateZoom;
        btnTabColor.Click += (_, _) => UpdateZoom();
        btnTabHsl.Click += (_, _) => UpdateZoom();
        UpdateZoom();
        void UpdateHslModeStyles(HSLCurveMode m)
        {
            btnHvh.FontWeight = m == HSLCurveMode.HueVsHue ? FontWeights.Bold : FontWeights.Normal;
            btnHvs.FontWeight = m == HSLCurveMode.HueVsSat ? FontWeights.Bold : FontWeights.Normal;
            btnHvl.FontWeight = m == HSLCurveMode.HueVsLum ? FontWeights.Bold : FontWeights.Normal;
            btnSvs.FontWeight = m == HSLCurveMode.SatVsSat ? FontWeights.Bold : FontWeights.Normal;
            btnSvl.FontWeight = m == HSLCurveMode.SatVsLum ? FontWeights.Bold : FontWeights.Normal;
            btnLvs.FontWeight = m == HSLCurveMode.LumVsSat ? FontWeights.Bold : FontWeights.Normal;
        }
        btnHvh.Click += (_, _) => { hc.ActiveMode = HSLCurveMode.HueVsHue; UpdateHslModeStyles(HSLCurveMode.HueVsHue); };
        btnHvs.Click += (_, _) => { hc.ActiveMode = HSLCurveMode.HueVsSat; UpdateHslModeStyles(HSLCurveMode.HueVsSat); };
        btnHvl.Click += (_, _) => { hc.ActiveMode = HSLCurveMode.HueVsLum; UpdateHslModeStyles(HSLCurveMode.HueVsLum); };
        btnSvs.Click += (_, _) => { hc.ActiveMode = HSLCurveMode.SatVsSat; UpdateHslModeStyles(HSLCurveMode.SatVsSat); };
        btnSvl.Click += (_, _) => { hc.ActiveMode = HSLCurveMode.SatVsLum; UpdateHslModeStyles(HSLCurveMode.SatVsLum); };
        btnLvs.Click += (_, _) => { hc.ActiveMode = HSLCurveMode.LumVsSat; UpdateHslModeStyles(HSLCurveMode.LumVsSat); };
        rstHslModeBtn.Click += (_, _) => hc.ResetActive();
        rstAllHslBtn.Click += (_, _) => hc.ResetAll();
        hslCtrl.CurvesChanged += CommitHslCurves;
        hslCtrl.CurvePreviewChanged += PreviewCurves;

        var pointFields = CreateHslPointFields(hc);
        hslGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(pointFields, 2);
        hslGrid.Children.Add(pointFields);

        Grid.SetRow(hslCtrl, 1);
        hslGrid.Children.Add(hslCtrl);

        Grid.SetRow(colorGrid, 1);
        Grid.SetRow(hslGrid, 1);
        grid.Children.Add(colorGrid);
        grid.Children.Add(hslGrid);

        return grid;
    }

    private static FrameworkElement MakeSliderRow(
        string label, string autoId, double min, double max, double def, double step,
        out Slider slider, out TextBlock valText, Action<double> onCommit)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var labelBlock = new TextBlock { Text = label, Width = 85, TextTrimming = TextTrimming.CharacterEllipsis };
        panel.Children.Add(labelBlock);

        var format = step >= 1 ? "F0" : step >= .1 ? "F1" : step >= .01 ? "F2" : "F3";
        valText = new TextBlock { Text = def.ToString(format, CultureInfo.CurrentCulture), Width = 42, TextAlignment = TextAlignment.Right };
        valText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
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
        AutomationProperties.SetName(slider, label);
        var s = slider;
        var vt = valText;
        slider.ValueChanged += (_, _) =>
        {
            vt.Text = s.Value.ToString(format, CultureInfo.CurrentCulture);
            onCommit(Math.Round(s.Value, 3));
        };
        panel.Children.Add(slider);
        return panel;
    }

    // --- State Synchronization ---

    private void UpdateControlsFromEffect(bool resetCurveSelection)
    {
        var fx = ActiveEffect ?? NeutralGrade;
        updating = true;
        try
        {
            bypassBtn.IsChecked = !fx.IsEnabled;

            // Lift
            var lr = fx.GetParameter("Lift_R"); var lg = fx.GetParameter("Lift_G"); var lb = fx.GetParameter("Lift_B"); var ly = fx.GetParameter("Lift_Y");
            liftWheel.SetValues(lr, lg, lb, ly);

            // Gamma
            var gr = fx.GetParameter("Gamma_R"); var gg = fx.GetParameter("Gamma_G"); var gb = fx.GetParameter("Gamma_B"); var gy = fx.GetParameter("Gamma_Y");
            gammaWheel.SetValues(gr, gg, gb, gy);

            // Gain
            var hr = fx.GetParameter("Gain_R"); var hg = fx.GetParameter("Gain_G"); var hb = fx.GetParameter("Gain_B"); var hy = fx.GetParameter("Gain_Y");
            gainWheel.SetValues(hr, hg, hb, hy);

            // Offset
            var or = fx.GetParameter("Offset_R"); var og = fx.GetParameter("Offset_G"); var ob = fx.GetParameter("Offset_B"); var oy = fx.GetParameter("Offset_Y");
            offsetWheel.SetValues(or, og, ob, oy);

            SyncRangeControls(fx);

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
            temperatureSlider.Value = fx.GetParameter(WhiteBalanceSampler.TemperatureKey);
            tintSlider.Value = fx.GetParameter(WhiteBalanceSampler.TintKey);

            // HSL
            hueSlider.Value = fx.GetParameter("HueAdjust");
            satHslSlider.Value = fx.GetParameter("SatAdjust");
            valHslSlider.Value = fx.GetParameter("ValAdjust");

            // Curves
            var rgb = fx.Curves ?? NeutralRgb;
            var hsl = fx.HSLCurves ?? NeutralHsl;
            if (resetCurveSelection || !curvesControl.Curves.ContentEquals(rgb)) curvesControl.Curves = rgb.DeepClone();
            if (resetCurveSelection || !hslCurvesControl.Curves.ContentEquals(hsl)) hslCurvesControl.Curves = hsl.DeepClone();

            // Camera LUT
            cameraLutPathText.Text = fx.Lut is not null ? (fx.Lut.Title ?? "Embedded 3D LUT") : "(No LUT loaded)";
            cameraLutStrengthSlider.Value = fx.GetParameter("LUTGain_0");
            cameraLutInterpCombo.SelectedIndex = (int)fx.GetParameter("LUTInterpolation_0");
            cameraLutBypassCheck.IsChecked = fx.GetParameter("LUTEnable_0") == 1;

            SyncLutPicker(cameraLutPresetCombo, fx.Lut);

            // Look LUT
            lookLutPathText.Text = fx.LookLut is not null ? (fx.LookLut.Title ?? "Embedded 3D LUT") : "(No LUT loaded)";
            lookLutStrengthSlider.Value = fx.GetParameter("LUTGain_1");
            lookLutInterpCombo.SelectedIndex = (int)fx.GetParameter("LUTInterpolation_1");
            lookLutBypassCheck.IsChecked = fx.GetParameter("LUTEnable_1") == 1;

            SyncLutPicker(lookLutPresetCombo, fx.LookLut);
            SyncLutPicker(presetCombo, fx.LookLut);
        }
        finally
        {
            updating = false;
        }
        ScopeLimitsChanged?.Invoke();
    }

    private void EditGrade(Func<VideoEffect, bool> edit)
    {
        if (updating || targetEffects is null || vm is null) return;
        var effects = vm.Editor.GetScopedVideoEffects(scope, targetId);
        var existing = ActiveEffect;
        var before = existing ?? NeutralGrade;
        var grade = before.DeepClone();
        if (!edit(grade) || before.ContentEquals(grade)) return;
        var index = existing is null ? -1 : effects.IndexOf(existing);
        if (index < 0) grade.Id = effectId ?? Guid.NewGuid();

        // Insert a new grade with its first adjustment in the same undo step.
        vm.Editor.Execute(_ =>
        {
            if (index >= 0) effects[index] = grade;
            else effects.Add(grade);
        });
        previewCallback?.Invoke();
    }

    private void SampleNeutralTone()
    {
        if (vm is not { } viewModel || targetEffects is null) return;
        var selectedScope = scope;
        var editor = viewModel.Editor;
        var selectedTarget = targetId;
        var existing = ActiveEffect;
        var snapshot = viewModel.Project.DeepClone();
        var (effects, _) = FindTarget(snapshot, selectedScope, selectedTarget);
        if (effects is null) return;

        var grade = existing is null ? null : effects.FirstOrDefault(effect => effect.Id == existing.Id);
        if (grade is null)
        {
            grade = VideoEffect.Create(VideoEffectKind.ColorGrading);
            effects.Add(grade);
        }

        var dialog = new WhiteBalanceSampleDialog(snapshot, grade, viewModel.Media, viewModel.Position)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;
        if (!ReferenceEquals(editor, viewModel.Editor) || scope != selectedScope || targetId != selectedTarget ||
            !ReferenceEquals(existing, ActiveEffect)) return;
        var adjustment = dialog.Adjustment;
        ResetParams(new()
        {
            [WhiteBalanceSampler.TemperatureKey] = adjustment.Temperature,
            [WhiteBalanceSampler.TintKey] = adjustment.Tint
        });
    }

    private void CommitSingleParam(string key, double value) => EditGrade(grade =>
    {
        if (grade.GetParameter(key) == value) return false;
        grade.SetParameter(key, value);
        return true;
    });

    private void ResetParams(Dictionary<string, double> values) => EditGrade(grade => SetParameters(grade, values));

    private static bool SetParameters(VideoEffect grade, Dictionary<string, double> values)
    {
        if (values.All(pair => grade.GetParameter(pair.Key) == pair.Value)) return false;
        foreach (var (key, value) in values) grade.SetParameter(key, value);
        return true;
    }

    private void CancelCurveDrags()
    {
        curvesControl.CancelDrag();
        hslCurvesControl.CancelDrag();
    }

    private void PreviewCurves()
    {
        if (GradingPreviewRequested is not { } request) return;
        if (targetEffects is not null && (curvesControl.IsDragging || hslCurvesControl.IsDragging))
        {
            showingCurvePreview = true;
            request(new(scope, targetId, ActiveEffect?.Id ?? effectId,
                curvesControl.IsDragging ? curvesControl.Curves.DeepClone() : null,
                hslCurvesControl.IsDragging ? hslCurvesControl.Curves.DeepClone() : null));
        }
        else if (showingCurvePreview)
        {
            showingCurvePreview = false;
            request(null);
        }
    }

    private void CommitRgbCurves() => EditGrade(grade =>
    {
        var curves = curvesControl.Curves;
        grade.Curves = curves.ContentEquals(NeutralRgb) ? null : curves.DeepClone();
        return true;
    });

    private void CommitHslCurves() => EditGrade(grade =>
    {
        var curves = hslCurvesControl.Curves;
        grade.HSLCurves = curves.ContentEquals(NeutralHsl) ? null : curves.DeepClone();
        return true;
    });

    private void SetLutSlot(int slot, CubeLut? lut) => EditGrade(grade => SetLut(grade, slot, lut));

    private static bool SetLut(VideoEffect grade, int slot, CubeLut? lut)
    {
        var current = slot == 0 ? grade.Lut : grade.LookLut;
        if (current is null ? lut is null : current.ContentEquals(lut))
            return false;
        if (slot == 0) grade.Lut = lut;
        else grade.LookLut = lut;
        return true;
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
        EditGrade(grade =>
        {
            var cleared = SetLut(grade, slot, null);
            return SetParameters(grade, new()
            {
                [$"LUTGain_{slot}"] = 1.0,
                [$"LUTInterpolation_{slot}"] = 0,
                [$"LUTEnable_{slot}"] = 0
            }) || cleared;
        });
    }

    private void UpdateLutExportAvailability() =>
        exportLutBtn.IsEnabled = !exportingLut && ActiveEffect is { IsEnabled: true };

    private async void ExportGradeLut(object sender, RoutedEventArgs e)
    {
        if (vm is not { } viewModel || ActiveEffect is not { IsEnabled: true } effect || exportingLut) return;
        if (Window.GetWindow(this) is not { } owner) return;

        var grade = effect.DeepClone();
        var protectedPaths = viewModel.Project.Media.Select(media => media.Path)
            .Where(path => !path.StartsWith("openrend://", StringComparison.OrdinalIgnoreCase))
            .Append(viewModel.ProjectPath).OfType<string>().ToArray();
        var position = viewModel.Position;
        if (scope == EffectScope.Event &&
            viewModel.Project.Tracks.SelectMany(track => track.Clips).FirstOrDefault(clip => clip.Id == targetId) is { } clip)
            position = Math.Max(0, position - clip.StartFrame);

        var dialog = new SaveFileDialog
        {
            Title = "Export Color Grading LUT",
            FileName = "Color Grade",
            DefaultExt = ".cube",
            Filter = "33-point CUBE (*.cube)|*.cube|65-point CUBE (*.cube)|*.cube",
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(owner) != true) return;

        exportingLut = true;
        UpdateLutExportAvailability();
        try
        {
            var output = Path.GetFullPath(dialog.FileName);
            if (!string.Equals(Path.GetExtension(output), ".cube", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use a .cube filename for the LUT.");
            if (protectedPaths.Any(path => string.Equals(Path.GetFullPath(path), output, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Choose a LUT destination different from the project and source media.");

            var size = dialog.FilterIndex == 2 ? 65 : 33;
            viewModel.Status = $"Exporting {size}-point LUT…";
            await Task.Run(() => GradingLutExporter.Save(grade, position, size, output));
            viewModel.Status = $"Saved {Path.GetFileName(output)}.";
        }
        catch (Exception error)
        {
            MessageBox.Show(owner, error.Message, "Export Color Grading LUT", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            exportingLut = false;
            UpdateLutExportAvailability();
        }
    }

    // --- Wheel Callbacks ---

    private void OnWheelGroupChanged(string group, double r, double g, double b, double y)
    {
        ResetParams(new()
        {
            [$"{group}_R"] = r,
            [$"{group}_G"] = g,
            [$"{group}_B"] = b,
            [$"{group}_Y"] = y
        });
    }

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
        EditGrade(grade =>
        {
            grade.IsEnabled = bypassBtn.IsChecked != true;
            return true;
        });
    }

    private void OnResetAllClick(object sender, RoutedEventArgs e)
    {
        EditGrade(grade =>
        {
            grade.Lut = null;
            grade.LookLut = null;
            grade.Curves = null;
            grade.HSLCurves = null;
            grade.Parameters = new(NeutralGrade.Parameters, StringComparer.Ordinal);
            return true;
        });
    }
}
