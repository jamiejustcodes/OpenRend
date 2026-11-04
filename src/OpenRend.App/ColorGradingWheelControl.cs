using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenRend.App;

public enum ColorWheelDisplayMode
{
    Wheel = 0,
    Sliders = 1
}

/// <summary>
/// Vegas Pro 1:1 interactive Color Grading Wheel control.
/// Supports 2D circular HSL chromaticity disc with vector connection line and precision drag,
/// 4-channel vertical faders view (R, G, B, Y), direct numeric inputs with mouse wheel stepping,
/// black/white balance eyedropper sampling, and keyframe/channel clipboard buffer.
/// </summary>
public sealed class ColorGradingWheelControl : UserControl
{
    private const int DiscDiameter = 112;
    private const double DiscRadius = 50.0;

    // Basis vector angles extracted from Vegas Pro ColorGradingColorBall:
    // Red = 185°, Green = -40° (320°), Blue = 65°
    private static readonly Point VectorR = new(Math.Cos(185.0 * Math.PI / 180.0), Math.Sin(185.0 * Math.PI / 180.0));
    private static readonly Point VectorG = new(Math.Cos(-40.0 * Math.PI / 180.0), Math.Sin(-40.0 * Math.PI / 180.0));
    private static readonly Point VectorB = new(Math.Cos(65.0 * Math.PI / 180.0), Math.Sin(65.0 * Math.PI / 180.0));

    // Moore-Penrose pseudo-inverse coefficients mapping (x, y) coordinates to (R, G, B) projections
    private static readonly Point PinvR = new(-0.567749809160127, -0.0804745980161999);
    private static readonly Point PinvG = new(0.429248310818155, -0.509678170500369);
    private static readonly Point PinvB = new(0.249840995078461, 0.734156070630045);

    private static readonly Color[] VegasStops =
    [
        Color.FromArgb(255, 255, 0, 128),
        Color.FromArgb(255, 255, 192, 0),
        Color.FromArgb(255, 255, 255, 0),
        Color.FromArgb(255, 0, 255, 128),
        Color.FromArgb(255, 0, 255, 255),
        Color.FromArgb(255, 64, 192, 255),
        Color.FromArgb(255, 255, 0, 255),
        Color.FromArgb(255, 255, 0, 128)
    ];

    private static readonly WriteableBitmap CachedDiscBitmap = GenerateVegasColorDisc(DiscDiameter);

    private static (double R, double G, double B, double Y)? ClipboardValues;

    // Dependency Properties
    public static readonly DependencyProperty WheelNameProperty =
        DependencyProperty.Register(nameof(WheelName), typeof(string), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata("Wheel", OnVisualLayoutChanged));

    public static readonly DependencyProperty AutomationPrefixProperty =
        DependencyProperty.Register(nameof(AutomationPrefix), typeof(string), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata("ColorWheel", OnAutomationPrefixChanged));

    public static readonly DependencyProperty DisplayModeProperty =
        DependencyProperty.Register(nameof(DisplayMode), typeof(ColorWheelDisplayMode), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata(ColorWheelDisplayMode.Wheel, OnDisplayModeChanged));

    public static readonly DependencyProperty IsPickerVisibleProperty =
        DependencyProperty.Register(nameof(IsPickerVisible), typeof(bool), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata(false, OnPickerVisibilityChanged));

    public static readonly DependencyProperty PickerColorProperty =
        DependencyProperty.Register(nameof(PickerColor), typeof(Color), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata(Color.FromRgb(128, 128, 128), OnPickerColorChanged));

    public static readonly DependencyProperty ValueXProperty =
        DependencyProperty.Register(nameof(ValueX), typeof(double), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnCoordinatesChanged));

    public static readonly DependencyProperty ValueYProperty =
        DependencyProperty.Register(nameof(ValueY), typeof(double), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnCoordinatesChanged));

    public static readonly DependencyProperty RProperty =
        DependencyProperty.Register(nameof(R), typeof(double), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata(0.0, OnChannelValueChanged));

    public static readonly DependencyProperty GProperty =
        DependencyProperty.Register(nameof(G), typeof(double), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata(0.0, OnChannelValueChanged));

    public static readonly DependencyProperty BProperty =
        DependencyProperty.Register(nameof(B), typeof(double), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata(0.0, OnChannelValueChanged));

    public static readonly DependencyProperty YProperty =
        DependencyProperty.Register(nameof(Y), typeof(double), typeof(ColorGradingWheelControl),
            new FrameworkPropertyMetadata(0.0, OnChannelValueChanged));

    // Channel Bounds and Defaults
    public double R_Min { get; set; } = -1.0;
    public double R_Max { get; set; } = 1.0;
    public double R_Default { get; set; } = 0.0;

    public double G_Min { get; set; } = -1.0;
    public double G_Max { get; set; } = 1.0;
    public double G_Default { get; set; } = 0.0;

    public double B_Min { get; set; } = -1.0;
    public double B_Max { get; set; } = 1.0;
    public double B_Default { get; set; } = 0.0;

    public double Y_Min { get; set; } = -1.0;
    public double Y_Max { get; set; } = 1.0;
    public double Y_Default { get; set; } = 0.0;
    public double StepSize { get; set; } = 0.01;

    // Public Properties
    public string WheelName
    {
        get => (string)GetValue(WheelNameProperty);
        set => SetValue(WheelNameProperty, value);
    }

    public string AutomationPrefix
    {
        get => (string)GetValue(AutomationPrefixProperty);
        set => SetValue(AutomationPrefixProperty, value);
    }

    public ColorWheelDisplayMode DisplayMode
    {
        get => (ColorWheelDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public bool IsPickerVisible
    {
        get => (bool)GetValue(IsPickerVisibleProperty);
        set => SetValue(IsPickerVisibleProperty, value);
    }

    public Color PickerColor
    {
        get => (Color)GetValue(PickerColorProperty);
        set => SetValue(PickerColorProperty, value);
    }

    public double ValueX
    {
        get => (double)GetValue(ValueXProperty);
        set => SetValue(ValueXProperty, Math.Clamp(value, -1.0, 1.0));
    }

    public double ValueY
    {
        get => (double)GetValue(ValueYProperty);
        set => SetValue(ValueYProperty, Math.Clamp(value, -1.0, 1.0));
    }

    public double R
    {
        get => (double)GetValue(RProperty);
        set => SetValue(RProperty, Math.Clamp(value, R_Min, R_Max));
    }

    public double G
    {
        get => (double)GetValue(GProperty);
        set => SetValue(GProperty, Math.Clamp(value, G_Min, G_Max));
    }

    public double B
    {
        get => (double)GetValue(BProperty);
        set => SetValue(BProperty, Math.Clamp(value, B_Min, B_Max));
    }

    public double Y
    {
        get => (double)GetValue(YProperty);
        set => SetValue(YProperty, Math.Clamp(value, Y_Min, Y_Max));
    }

    // Public Events
    public event Action<double, double>? ValuesChanged;
    public event Action<double, double, double>? RgbChanged;
    public event Action<double>? YChanged;
    public event Action<double, double, double, double>? AllValuesChanged;
    public event Action<Color>? PickerColorPicked;
    public event Action? ResetRequested;

    // UI Element References
    private readonly TextBlock titleLabel;
    private readonly Button modeToggleBtn;
    private readonly Button pickerBtn;
    private readonly Border pickerPreview;
    private readonly Button resetBtn;

    private readonly Grid discContainer;
    private readonly WheelDiscElement discElement;
    private readonly Slider ySlider;

    private readonly Grid numericRow;
    private readonly TextBox rTextBox;
    private readonly TextBox gTextBox;
    private readonly TextBox bTextBox;
    private readonly TextBox yTextBox;

    private readonly Grid slidersPanel;
    private readonly Slider faderR;
    private readonly Slider faderG;
    private readonly Slider faderB;
    private readonly Slider faderY;
    private readonly TextBlock faderRVal;
    private readonly TextBlock faderGVal;
    private readonly TextBlock faderBVal;
    private readonly TextBlock faderYVal;

    private readonly TextBlock readoutBlock;

    public Slider YSlider => ySlider;
    public TextBlock ReadoutTextBlock => readoutBlock;

    private bool updatingInternals;

    public ColorGradingWheelControl()
    {
        Background = new SolidColorBrush(Color.FromRgb(36, 36, 36));
        BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55));
        BorderThickness = new Thickness(1);
        Padding = new Thickness(6);
        Margin = new Thickness(2);
        Focusable = true;

        var mainLayout = new Grid();
        mainLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Header
        mainLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: Content
        mainLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Readout

        // 0. Header Bar
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

        titleLabel = new TextBlock
        {
            Text = "Wheel",
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(titleLabel);

        var headerButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        pickerPreview = new Border
        {
            Width = 13,
            Height = 13,
            Margin = new Thickness(0, 0, 4, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(PickerColor),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        headerButtons.Children.Add(pickerPreview);

        pickerBtn = new Button
        {
            Content = "⌖",
            Width = 20,
            Height = 19,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            FontSize = 11,
            ToolTip = "Sample black/white point balance",
            Visibility = Visibility.Collapsed
        };
        pickerBtn.Click += OnPickerButtonClick;
        headerButtons.Children.Add(pickerBtn);

        modeToggleBtn = new Button
        {
            Content = "☵",
            Width = 20,
            Height = 19,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            FontSize = 10,
            ToolTip = "Toggle 2D Wheel / 4-Channel Sliders mode"
        };
        modeToggleBtn.Click += (_, _) =>
        {
            DisplayMode = DisplayMode == ColorWheelDisplayMode.Wheel ? ColorWheelDisplayMode.Sliders : ColorWheelDisplayMode.Wheel;
        };
        headerButtons.Children.Add(modeToggleBtn);

        resetBtn = new Button
        {
            Content = "↺",
            Width = 20,
            Height = 19,
            Padding = new Thickness(0),
            FontSize = 11,
            ToolTip = "Reset to default values"
        };
        resetBtn.Click += (_, _) => Reset();
        headerButtons.Children.Add(resetBtn);

        DockPanel.SetDock(headerButtons, Dock.Right);
        header.Children.Add(headerButtons);
        Grid.SetRow(header, 0);
        mainLayout.Children.Add(header);

        // 1. Content Container: 2D Wheel view + 4-Channel Sliders view
        var contentStack = new StackPanel();

        // 2D Wheel Container
        discContainer = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        discContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        discContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        discElement = new WheelDiscElement(this);
        Grid.SetColumn(discElement, 0);
        discContainer.Children.Add(discElement);

        ySlider = new Slider
        {
            Orientation = Orientation.Vertical,
            Minimum = Y_Min,
            Maximum = Y_Max,
            Value = Y_Default,
            SmallChange = StepSize,
            LargeChange = StepSize * 5,
            Height = DiscDiameter,
            Width = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Luminance (Y)"
        };
        ySlider.ValueChanged += (_, _) =>
        {
            if (updatingInternals) return;
            Y = ySlider.Value;
            SyncNumericBoxes();
            NotifyValuesChanged();
        };
        Grid.SetColumn(ySlider, 1);
        discContainer.Children.Add(ySlider);
        contentStack.Children.Add(discContainer);

        // Numeric Input Row (under wheel)
        numericRow = new Grid { Margin = new Thickness(0, 4, 0, 2) };
        for (var i = 0; i < 4; i++) numericRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        rTextBox = CreateNumericBox("R", 0, v => { R = v; SyncFromChannels(); });
        gTextBox = CreateNumericBox("G", 1, v => { G = v; SyncFromChannels(); });
        bTextBox = CreateNumericBox("B", 2, v => { B = v; SyncFromChannels(); });
        yTextBox = CreateNumericBox("Y", 3, v => { Y = v; ySlider.Value = v; SyncNumericBoxes(); NotifyValuesChanged(); });
        contentStack.Children.Add(numericRow);

        // 4-Channel Sliders Panel (visible when DisplayMode == Sliders)
        slidersPanel = new Grid { Height = DiscDiameter + 24, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 2) };
        for (var i = 0; i < 4; i++) slidersPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var faderRedCol = CreateFaderColumn("R", Color.FromRgb(229, 57, 53), 0, R_Min, R_Max, R_Default, out faderR, out faderRVal, v => { R = v; SyncFromChannels(); });
        var faderGreenCol = CreateFaderColumn("G", Color.FromRgb(67, 160, 71), 1, G_Min, G_Max, G_Default, out faderG, out faderGVal, v => { G = v; SyncFromChannels(); });
        var faderBlueCol = CreateFaderColumn("B", Color.FromRgb(30, 136, 229), 2, B_Min, B_Max, B_Default, out faderB, out faderBVal, v => { B = v; SyncFromChannels(); });
        var faderYCol = CreateFaderColumn("Y", Color.FromRgb(224, 224, 224), 3, Y_Min, Y_Max, Y_Default, out faderY, out faderYVal, v => { Y = v; ySlider.Value = v; SyncNumericBoxes(); NotifyValuesChanged(); });

        slidersPanel.Children.Add(faderRedCol);
        slidersPanel.Children.Add(faderGreenCol);
        slidersPanel.Children.Add(faderBlueCol);
        slidersPanel.Children.Add(faderYCol);
        contentStack.Children.Add(slidersPanel);

        Grid.SetRow(contentStack, 1);
        mainLayout.Children.Add(contentStack);

        // 2. Readout Text
        readoutBlock = new TextBlock
        {
            Text = "R: 0.00  G: 0.00  B: 0.00  Y: 0.00",
            FontSize = 9.5,
            Foreground = Brushes.LightGray,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        };
        Grid.SetRow(readoutBlock, 2);
        mainLayout.Children.Add(readoutBlock);

        Content = mainLayout;
        SetupContextMenu();
    }

    public void Configure(
        string title,
        string automationPrefix,
        double rMin, double rMax, double rDef,
        double gMin, double gMax, double gDef,
        double bMin, double bMax, double bDef,
        double yMin, double yMax, double yDef,
        double step = 0.01,
        bool isPickerVisible = false)
    {
        WheelName = title;
        AutomationPrefix = automationPrefix;
        R_Min = rMin; R_Max = rMax; R_Default = rDef;
        G_Min = gMin; G_Max = gMax; G_Default = gDef;
        B_Min = bMin; B_Max = bMax; B_Default = bDef;
        Y_Min = yMin; Y_Max = yMax; Y_Default = yDef;
        StepSize = step;
        IsPickerVisible = isPickerVisible;

        ySlider.Minimum = yMin;
        ySlider.Maximum = yMax;
        ySlider.SmallChange = step;
        ySlider.LargeChange = step * 5;

        faderR.Minimum = rMin; faderR.Maximum = rMax;
        faderG.Minimum = gMin; faderG.Maximum = gMax;
        faderB.Minimum = bMin; faderB.Maximum = bMax;
        faderY.Minimum = yMin; faderY.Maximum = yMax;

        SetValues(rDef, gDef, bDef, yDef);
        UpdateAutomationIds();
    }

    public void SetValues(double r, double g, double b, double y)
    {
        updatingInternals = true;
        try
        {
            R = Math.Clamp(r, R_Min, R_Max);
            G = Math.Clamp(g, G_Min, G_Max);
            B = Math.Clamp(b, B_Min, B_Max);
            Y = Math.Clamp(y, Y_Min, Y_Max);

            var (x, yCoord) = RgbToCoordinates(R, G, B);
            ValueX = x;
            ValueY = yCoord;

            ySlider.Value = Y;
            faderR.Value = R;
            faderG.Value = G;
            faderB.Value = B;
            faderY.Value = Y;

            SyncNumericBoxes();
            discElement.InvalidateVisual();
        }
        finally
        {
            updatingInternals = false;
        }
    }

    public void Reset()
    {
        SetValues(R_Default, G_Default, B_Default, Y_Default);
        NotifyValuesChanged();
        ResetRequested?.Invoke();
    }

    public void SampleColor(Color sampled)
    {
        PickerColor = sampled;
        pickerPreview.Background = new SolidColorBrush(sampled);

        var sR = sampled.R / 255.0;
        var sG = sampled.G / 255.0;
        var sB = sampled.B / 255.0;
        var luma = 0.2126 * sR + 0.7152 * sG + 0.0722 * sB;

        if (WheelName.Contains("Lift", StringComparison.OrdinalIgnoreCase) || WheelName.Contains("Shadows", StringComparison.OrdinalIgnoreCase))
        {
            // Neutralize black balance
            var dR = luma - sR;
            var dG = luma - sG;
            var dB = luma - sB;
            SetValues(R_Default + dR, G_Default + dG, B_Default + dB, Y);
        }
        else if (WheelName.Contains("Gain", StringComparison.OrdinalIgnoreCase) || WheelName.Contains("Highlights", StringComparison.OrdinalIgnoreCase))
        {
            // Neutralize white balance
            var mR = sR > 0.001 ? luma / sR : 1.0;
            var mG = sG > 0.001 ? luma / sG : 1.0;
            var mB = sB > 0.001 ? luma / sB : 1.0;
            SetValues(R_Default * mR, G_Default * mG, B_Default * mB, Y);
        }

        NotifyValuesChanged();
        PickerColorPicked?.Invoke(sampled);
    }

    private void SyncFromCoordinates(double x, double y)
    {
        if (updatingInternals) return;
        updatingInternals = true;
        try
        {
            var (r, g, b) = CoordinatesToRgb(x, y);
            R = r; G = g; B = b;
            ValueX = x; ValueY = y;

            faderR.Value = R;
            faderG.Value = G;
            faderB.Value = B;

            SyncNumericBoxes();
            discElement.InvalidateVisual();
        }
        finally
        {
            updatingInternals = false;
        }

        NotifyValuesChanged();
    }

    private void SyncFromChannels()
    {
        if (updatingInternals) return;
        updatingInternals = true;
        try
        {
            var (x, y) = RgbToCoordinates(R, G, B);
            ValueX = x;
            ValueY = y;

            faderR.Value = R;
            faderG.Value = G;
            faderB.Value = B;

            SyncNumericBoxes();
            discElement.InvalidateVisual();
        }
        finally
        {
            updatingInternals = false;
        }

        NotifyValuesChanged();
    }

    private void SyncNumericBoxes()
    {
        rTextBox.Text = R.ToString("F2", CultureInfo.InvariantCulture);
        gTextBox.Text = G.ToString("F2", CultureInfo.InvariantCulture);
        bTextBox.Text = B.ToString("F2", CultureInfo.InvariantCulture);
        yTextBox.Text = Y.ToString("F2", CultureInfo.InvariantCulture);

        faderRVal.Text = R.ToString("F2", CultureInfo.InvariantCulture);
        faderGVal.Text = G.ToString("F2", CultureInfo.InvariantCulture);
        faderBVal.Text = B.ToString("F2", CultureInfo.InvariantCulture);
        faderYVal.Text = Y.ToString("F2", CultureInfo.InvariantCulture);

        readoutBlock.Text = $"R: {R:F2}  G: {G:F2}  B: {B:F2}  Y: {Y:F2}";
    }

    private void NotifyValuesChanged()
    {
        ValuesChanged?.Invoke(ValueX, ValueY);
        RgbChanged?.Invoke(R, G, B);
        YChanged?.Invoke(Y);
        AllValuesChanged?.Invoke(R, G, B, Y);
    }

    // Coordinate Math
    public (double X, double Y) RgbToCoordinates(double r, double g, double b)
    {
        var mR = ValueToMagnitude(r, R_Min, R_Max, R_Default);
        var mG = ValueToMagnitude(g, G_Min, G_Max, G_Default);
        var mB = ValueToMagnitude(b, B_Min, B_Max, B_Default);

        var x = mR * VectorR.X + mG * VectorG.X + mB * VectorB.X;
        var y = mR * VectorR.Y + mG * VectorG.Y + mB * VectorB.Y;
        var dist = Math.Sqrt(x * x + y * y);
        if (dist > 1.0)
        {
            x /= dist;
            y /= dist;
        }
        return (x, y);
    }

    public (double R, double G, double B) CoordinatesToRgb(double x, double y)
    {
        var mR = x * PinvR.X + y * PinvR.Y;
        var mG = x * PinvG.X + y * PinvG.Y;
        var mB = x * PinvB.X + y * PinvB.Y;

        var r = MagnitudeToValue(mR, R_Min, R_Max, R_Default);
        var g = MagnitudeToValue(mG, G_Min, G_Max, G_Default);
        var b = MagnitudeToValue(mB, B_Min, B_Max, B_Default);
        return (r, g, b);
    }

    private static double ValueToMagnitude(double val, double min, double max, double def)
    {
        if (val >= def && max > def) return Math.Clamp((val - def) / (max - def), 0.0, 1.0);
        if (val < def && def > min) return Math.Clamp((val - def) / (def - min), -1.0, 0.0);
        return 0.0;
    }

    private static double MagnitudeToValue(double mag, double min, double max, double def)
    {
        if (mag >= 0) return Math.Clamp(def + mag * (max - def), min, max);
        return Math.Clamp(def + mag * (def - min), min, max);
    }

    // UI Construction Helpers
    private TextBox CreateNumericBox(string label, int col, Action<double> onCommit)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0)
        });

        var tb = new TextBox
        {
            Width = 34,
            Height = 18,
            FontSize = 9.5,
            Padding = new Thickness(1, 0, 1, 0),
            TextAlignment = TextAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center
        };

        tb.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    onCommit(parsed);
                tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        };

        tb.LostFocus += (_, _) =>
        {
            if (double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                onCommit(parsed);
        };

        tb.MouseWheel += (_, e) =>
        {
            if (double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var cur))
            {
                var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? StepSize * 0.2 : StepSize;
                var delta = e.Delta > 0 ? step : -step;
                onCommit(cur + delta);
            }
            e.Handled = true;
        };

        panel.Children.Add(tb);
        Grid.SetColumn(panel, col);
        numericRow.Children.Add(panel);
        return tb;
    }

    private Border CreateFaderColumn(
        string channel, Color accent, int col, double min, double max, double def,
        out Slider slider, out TextBlock valText, Action<double> onSlide)
    {
        var border = new Border
        {
            Margin = new Thickness(2, 0, 2, 0),
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(2)
        };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        var badge = new TextBlock
        {
            Text = channel,
            FontWeight = FontWeights.Bold,
            FontSize = 9.5,
            Foreground = new SolidColorBrush(accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        };
        stack.Children.Add(badge);

        slider = new Slider
        {
            Orientation = Orientation.Vertical,
            Minimum = min,
            Maximum = max,
            Value = def,
            Height = DiscDiameter - 26,
            SmallChange = StepSize,
            LargeChange = StepSize * 5,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var s = slider;
        slider.ValueChanged += (_, _) =>
        {
            if (!updatingInternals) onSlide(s.Value);
        };
        stack.Children.Add(slider);

        valText = new TextBlock
        {
            Text = def.ToString("F2", CultureInfo.InvariantCulture),
            FontSize = 9.0,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
        stack.Children.Add(valText);

        border.Child = stack;
        Grid.SetColumn(border, col);
        return border;
    }

    private void SetupContextMenu()
    {
        var menu = new ContextMenu();

        var resetItem = new MenuItem { Header = "Reset to Default" };
        resetItem.Click += (_, _) => Reset();
        menu.Items.Add(resetItem);

        menu.Items.Add(new Separator());

        var copyItem = new MenuItem { Header = "Copy Color Values", InputGestureText = "Ctrl+C" };
        copyItem.Click += (_, _) => CopyValues();
        menu.Items.Add(copyItem);

        var pasteItem = new MenuItem { Header = "Paste Color Values", InputGestureText = "Ctrl+V" };
        pasteItem.Click += (_, _) => PasteValues();
        menu.Items.Add(pasteItem);

        ContextMenu = menu;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CopyValues();
                e.Handled = true;
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                PasteValues();
                e.Handled = true;
            }
        };
    }

    public void CopyValues()
    {
        ClipboardValues = (R, G, B, Y);
    }

    public void PasteValues()
    {
        if (ClipboardValues is { } vals)
        {
            SetValues(vals.R, vals.G, vals.B, vals.Y);
            NotifyValuesChanged();
        }
    }

    private void OnPickerButtonClick(object sender, RoutedEventArgs e)
    {
        // Interactive screen / sample balance
        var dialog = new ColorDialogWindow(PickerColor);
        if (dialog.ShowDialog() == true)
        {
            SampleColor(dialog.SelectedColor);
        }
    }

    private void UpdateAutomationIds()
    {
        AutomationProperties.SetAutomationId(this, AutomationPrefix);
        AutomationProperties.SetAutomationId(discElement, $"{AutomationPrefix}_Wheel");
        AutomationProperties.SetAutomationId(ySlider, $"{AutomationPrefix}_Y");
        AutomationProperties.SetAutomationId(readoutBlock, $"{AutomationPrefix}_Readout");
        AutomationProperties.SetAutomationId(resetBtn, $"{AutomationPrefix}_Reset");
        AutomationProperties.SetAutomationId(modeToggleBtn, $"{AutomationPrefix}_ModeToggle");
        AutomationProperties.SetAutomationId(pickerBtn, $"{AutomationPrefix}_Picker");
        AutomationProperties.SetAutomationId(rTextBox, $"{AutomationPrefix}_RBox");
        AutomationProperties.SetAutomationId(gTextBox, $"{AutomationPrefix}_GBox");
        AutomationProperties.SetAutomationId(bTextBox, $"{AutomationPrefix}_BBox");
        AutomationProperties.SetAutomationId(yTextBox, $"{AutomationPrefix}_YBox");
    }

    // Property Changed Callbacks
    private static void OnVisualLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorGradingWheelControl ctrl) ctrl.titleLabel.Text = (string)e.NewValue;
    }

    private static void OnAutomationPrefixChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorGradingWheelControl ctrl) ctrl.UpdateAutomationIds();
    }

    private static void OnDisplayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorGradingWheelControl ctrl)
        {
            var mode = (ColorWheelDisplayMode)e.NewValue;
            ctrl.discContainer.Visibility = mode == ColorWheelDisplayMode.Wheel ? Visibility.Visible : Visibility.Collapsed;
            ctrl.numericRow.Visibility = mode == ColorWheelDisplayMode.Wheel ? Visibility.Visible : Visibility.Collapsed;
            ctrl.slidersPanel.Visibility = mode == ColorWheelDisplayMode.Sliders ? Visibility.Visible : Visibility.Collapsed;
            ctrl.modeToggleBtn.Content = mode == ColorWheelDisplayMode.Wheel ? "☵" : "◉";
        }
    }

    private static void OnPickerVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorGradingWheelControl ctrl)
        {
            var vis = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            ctrl.pickerBtn.Visibility = vis;
            ctrl.pickerPreview.Visibility = vis;
        }
    }

    private static void OnPickerColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorGradingWheelControl ctrl && e.NewValue is Color c)
        {
            ctrl.pickerPreview.Background = new SolidColorBrush(c);
        }
    }

    private static void OnCoordinatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorGradingWheelControl ctrl && !ctrl.updatingInternals)
        {
            ctrl.discElement.InvalidateVisual();
        }
    }

    private static void OnChannelValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorGradingWheelControl ctrl && !ctrl.updatingInternals)
        {
            ctrl.SyncNumericBoxes();
            ctrl.discElement.InvalidateVisual();
        }
    }

    // --- Embedded 2D Disc Canvas ---
    private sealed class WheelDiscElement : FrameworkElement
    {
        private readonly ColorGradingWheelControl parent;
        private bool isDragging;
        private Point lastMousePos;

        public WheelDiscElement(ColorGradingWheelControl parentControl)
        {
            parent = parentControl;
            Width = DiscDiameter;
            Height = DiscDiameter;
            Focusable = true;
            Cursor = Cursors.Cross;
        }

        protected override Size MeasureOverride(Size availableSize) => new(DiscDiameter, DiscDiameter);

        protected override void OnRender(DrawingContext dc)
        {
            var center = new Point(DiscDiameter / 2.0, DiscDiameter / 2.0);

            // Draw color disc bitmap
            dc.DrawImage(CachedDiscBitmap, new Rect(0, 0, DiscDiameter, DiscDiameter));

            // Outer dark boundary ring
            var ringPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), 1.0);
            dc.DrawEllipse(null, ringPen, center, DiscRadius, DiscRadius);

            // Center crosshair (subtle lines)
            var crossPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1.0);
            dc.DrawLine(crossPen, new Point(center.X - 8, center.Y), new Point(center.X + 8, center.Y));
            dc.DrawLine(crossPen, new Point(center.X, center.Y - 8), new Point(center.X, center.Y + 8));

            // Compute current puck position
            var puckPos = new Point(
                center.X + parent.ValueX * (DiscRadius - 4),
                center.Y + parent.ValueY * (DiscRadius - 4));

            // Compute dynamic tint color for connection line and puck fill
            var tintColor = GetTintColor(parent.ValueX, parent.ValueY);

            // Draw vector connection line from origin to puck
            if (Math.Abs(parent.ValueX) > 0.02 || Math.Abs(parent.ValueY) > 0.02)
            {
                var lineBrush = new SolidColorBrush(Color.FromArgb(200, tintColor.R, tintColor.G, tintColor.B));
                var linePen = new Pen(lineBrush, 1.5);
                dc.DrawLine(linePen, center, puckPos);
            }

            // Draw puck: drop shadow, outer ring, inner fill
            var shadowPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)), 3.0);
            dc.DrawEllipse(null, shadowPen, puckPos, 5.0, 5.0);

            var outerPen = new Pen(Brushes.White, 1.5);
            var fillBrush = new SolidColorBrush(tintColor);
            dc.DrawEllipse(fillBrush, outerPen, puckPos, 4.5, 4.5);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    parent.ValueX = 0;
                    parent.ValueY = 0;
                    parent.SyncFromCoordinates(0, 0);
                    return;
                }

                CaptureMouse();
                isDragging = true;
                lastMousePos = e.GetPosition(this);
                UpdatePosition(lastMousePos, false);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var curPos = e.GetPosition(this);
                var isFine = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

                if (isFine)
                {
                    // Fine drag precision: scaled delta
                    var deltaX = (curPos.X - lastMousePos.X) * 0.2;
                    var deltaY = (curPos.Y - lastMousePos.Y) * 0.2;
                    var center = new Point(DiscDiameter / 2.0, DiscDiameter / 2.0);
                    var curMappedX = parent.ValueX * (DiscRadius - 4) + deltaX;
                    var curMappedY = parent.ValueY * (DiscRadius - 4) + deltaY;
                    UpdatePosition(new Point(center.X + curMappedX, center.Y + curMappedY), false);
                }
                else
                {
                    UpdatePosition(curPos, false);
                }

                lastMousePos = curPos;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (isDragging && e.ChangedButton == MouseButton.Left)
            {
                isDragging = false;
                ReleaseMouseCapture();
            }
        }

        private void UpdatePosition(Point pos, bool isDelta)
        {
            var center = new Point(DiscDiameter / 2.0, DiscDiameter / 2.0);
            var dx = (pos.X - center.X) / (DiscRadius - 4);
            var dy = (pos.Y - center.Y) / (DiscRadius - 4);

            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > 1.0 && dist > 0)
            {
                dx /= dist;
                dy /= dist;
            }

            dx = Math.Round(dx, 4);
            dy = Math.Round(dy, 4);
            parent.SyncFromCoordinates(dx, dy);
        }

        private static Color GetTintColor(double x, double y)
        {
            var dist = Math.Sqrt(x * x + y * y);
            if (dist < 0.001) return Color.FromRgb(220, 220, 220);

            var angle = Math.Atan2(y, x) * 180.0 / Math.PI;
            if (angle < 0) angle += 360.0;
            var normAngle = ((angle - 185.0) % 360.0 + 360.0) % 360.0 / 360.0;

            var stops = VegasStops;
            var pos = normAngle * (stops.Length - 1);
            var idx = (int)Math.Floor(pos);
            var frac = pos - idx;
            idx = Math.Clamp(idx, 0, stops.Length - 2);

            var c0 = stops[idx];
            var c1 = stops[idx + 1];
            var r = (byte)Math.Clamp(c0.R + (c1.R - c0.R) * frac, 0, 255);
            var g = (byte)Math.Clamp(c0.G + (c1.G - c0.G) * frac, 0, 255);
            var b = (byte)Math.Clamp(c0.B + (c1.B - c0.B) * frac, 0, 255);

            var sat = Math.Clamp(dist, 0.0, 1.0);
            r = (byte)Math.Clamp(220 * (1 - sat) + r * sat, 0, 255);
            g = (byte)Math.Clamp(220 * (1 - sat) + g * sat, 0, 255);
            b = (byte)Math.Clamp(220 * (1 - sat) + b * sat, 0, 255);
            return Color.FromRgb(r, g, b);
        }
    }

    // --- Vegas Pro 8-Stop Color Disc Generator ---

    private static WriteableBitmap GenerateVegasColorDisc(int size)
    {
        var wb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[size * size * 4];
        var center = size / 2.0;
        var radius = DiscRadius;
        var stops = VegasStops;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var idx = (y * size + x) * 4;
                var dx = x - center;
                var dy = center - y; // Invert Y for standard math orientation
                var dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist <= radius)
                {
                    var sat = Math.Clamp(dist / radius, 0.0, 1.0);
                    var satCurve = Math.Pow(sat, 0.82);

                    var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                    if (angle < 0) angle += 360.0;

                    // Align 0° stop with Red direction (185°)
                    var normAngle = ((angle - 185.0) % 360.0 + 360.0) % 360.0 / 360.0;
                    var pos = normAngle * (stops.Length - 1);
                    var sIdx = Math.Clamp((int)Math.Floor(pos), 0, stops.Length - 2);
                    var frac = pos - sIdx;

                    var c0 = stops[sIdx];
                    var c1 = stops[sIdx + 1];
                    var specR = c0.R + (c1.R - c0.R) * frac;
                    var specG = c0.G + (c1.G - c0.G) * frac;
                    var specB = c0.B + (c1.B - c0.B) * frac;

                    // Neutral center baseline (240, 240, 240)
                    var r = (byte)Math.Clamp(240 * (1 - satCurve) + specR * satCurve, 0, 255);
                    var g = (byte)Math.Clamp(240 * (1 - satCurve) + specG * satCurve, 0, 255);
                    var b = (byte)Math.Clamp(240 * (1 - satCurve) + specB * satCurve, 0, 255);

                    // Anti-aliased outer edge
                    var alpha = dist > radius - 1.5 ? (byte)(Math.Clamp((radius - dist) / 1.5, 0.0, 1.0) * 255) : (byte)255;

                    pixels[idx] = b;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = r;
                    pixels[idx + 3] = alpha;
                }
                else
                {
                    pixels[idx] = 0;
                    pixels[idx + 1] = 0;
                    pixels[idx + 2] = 0;
                    pixels[idx + 3] = 0;
                }
            }
        }

        wb.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        wb.Freeze();
        return wb;
    }

    // Modal eyedropper / color balance selection dialog
    private sealed class ColorDialogWindow : Window
    {
        public Color SelectedColor { get; private set; }

        public ColorDialogWindow(Color initial)
        {
            SelectedColor = initial;
            Title = "Sample Balance Color";
            Width = 260;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(36, 36, 36));

            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Click a neutral preset or enter RGB:",
                Foreground = Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            presets.Children.Add(MakePresetButton("Pure Black", Color.FromRgb(10, 10, 10)));
            presets.Children.Add(MakePresetButton("Warm Gray", Color.FromRgb(135, 128, 120)));
            presets.Children.Add(MakePresetButton("Neutral Gray", Color.FromRgb(128, 128, 128)));
            presets.Children.Add(MakePresetButton("Cool Gray", Color.FromRgb(120, 128, 135)));
            presets.Children.Add(MakePresetButton("Pure White", Color.FromRgb(250, 250, 250)));
            root.Children.Add(presets);

            var cancelBtn = new Button { Content = "Close", Width = 70, HorizontalAlignment = HorizontalAlignment.Right };
            cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
            root.Children.Add(cancelBtn);

            Content = root;
        }

        private Button MakePresetButton(string label, Color c)
        {
            var btn = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(c),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                ToolTip = label
            };
            btn.Click += (_, _) =>
            {
                SelectedColor = c;
                DialogResult = true;
                Close();
            };
            return btn;
        }
    }
}
