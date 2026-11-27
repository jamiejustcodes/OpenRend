using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

public sealed class VideoScopesWindow : Window
{
    internal readonly record struct ScopeOptions(VideoScopeMode Mode, VideoScopeScale Scale, double Intensity, bool FollowPlayback);
    private readonly record struct ScopeFrame(byte[]? Pixels, int Width, int Height);
    private readonly record struct RenderSettings(ScopeOptions Options, int Width, int Height, LuminanceThresholds? Limits);

    private readonly MainWindow? mainWindow;
    private readonly ComboBox modeCombo = new() { MinWidth = 112, SelectedValuePath = nameof(ComboBoxItem.Tag) };
    private readonly ComboBox scaleCombo = new() { MinWidth = 137, SelectedValuePath = nameof(ComboBoxItem.Tag) };
    private readonly Slider intensity = new() { Minimum = .4, Maximum = 2.4, Width = 75, SmallChange = .05, LargeChange = .2 };
    private readonly CheckBox followPlayback = new() { Content = "Update while playing", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock intensityText = new() { Width = 36, TextAlignment = TextAlignment.Right };
    private readonly TextBlock readout = new() { MinHeight = 16, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0) };
    private readonly Image graph = new() { Stretch = Stretch.Fill };
    private WriteableBitmap? bitmap;
    private ScopeFrame source;
    private ScopeFrame? testSource;
    private LuminanceThresholds? limits;
    private RenderSettings? displayed;
    private bool queued;
    private bool rendering;
    private bool closed;

    internal ScopeOptions Options => new((VideoScopeMode)modeCombo.SelectedValue, (VideoScopeScale)scaleCombo.SelectedValue,
        intensity.Value, followPlayback.IsChecked == true);

    public VideoScopesWindow(MainWindow? window = null) : this(window, null)
    {
    }

    internal VideoScopesWindow(MainWindow? window, ScopeOptions? saved)
    {
        mainWindow = window;
        var initial = saved ?? new(VideoScopeMode.Waveform, VideoScopeScale.StudioRgb, 1, true);
        SetResourceReference(StyleProperty, typeof(Window));
        Title = "Video Scopes";
        Width = 580;
        Height = 450;
        MinWidth = 390;
        MinHeight = 310;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetAutomationId(this, "VideoScopesWindow");

        modeCombo.Items.Add(new ComboBoxItem { Content = "Waveform", Tag = VideoScopeMode.Waveform });
        modeCombo.Items.Add(new ComboBoxItem { Content = "Vectorscope", Tag = VideoScopeMode.Vectorscope });
        modeCombo.Items.Add(new ComboBoxItem { Content = "RGB Parade", Tag = VideoScopeMode.RgbParade });
        modeCombo.Items.Add(new ComboBoxItem { Content = "Histogram", Tag = VideoScopeMode.Histogram });
        modeCombo.SelectedValue = initial.Mode;
        scaleCombo.Items.Add(new ComboBoxItem { Content = "Studio RGB (16–235)", Tag = VideoScopeScale.StudioRgb });
        scaleCombo.Items.Add(new ComboBoxItem { Content = "Full range (0–255)", Tag = VideoScopeScale.FullRange });
        scaleCombo.SelectedValue = initial.Scale;
        intensity.Value = initial.Intensity;
        followPlayback.IsChecked = initial.FollowPlayback;
        AutomationProperties.SetAutomationId(modeCombo, "VideoScopeMode");
        AutomationProperties.SetAutomationId(scaleCombo, "VideoScopeScale");
        AutomationProperties.SetAutomationId(intensity, "VideoScopeIntensity");
        AutomationProperties.SetAutomationId(followPlayback, "VideoScopeFollowPlayback");
        AutomationProperties.SetAutomationId(graph, "VideoScopeGraph");
        AutomationProperties.SetAutomationId(readout, "VideoScopeReadout");
        readout.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
        toolbar.Children.Add(Field("_Scope", modeCombo));
        toolbar.Children.Add(Field("S_cale", scaleCombo));
        toolbar.Children.Add(Field("_Intensity", intensity, intensityText));
        var refresh = new Button { Content = "_Refresh", Margin = new Thickness(0) };
        AutomationProperties.SetAutomationId(refresh, "VideoScopeRefresh");

        var frame = new Border { Child = graph, BorderThickness = new Thickness(1), ClipToBounds = true };
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        frame.SetResourceReference(Border.BackgroundProperty, "PanelBrush");

        var footer = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var playbackControls = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(refresh, Dock.Right);
        playbackControls.Children.Add(refresh);
        playbackControls.Children.Add(followPlayback);
        footer.Children.Add(playbackControls);
        Grid.SetRow(readout, 1);
        footer.Children.Add(readout);
        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(footer);
        root.Children.Add(frame);
        Content = root;

        refresh.Click += (_, _) => RefreshSource();
        followPlayback.Checked += (_, _) => RefreshSource();
        followPlayback.Unchecked += (_, _) => QueueRender();
        modeCombo.SelectionChanged += (_, _) => UpdateControls();
        scaleCombo.SelectionChanged += (_, _) => QueueRender();
        intensity.ValueChanged += (_, _) => UpdateControls();
        graph.SizeChanged += (_, _) => QueueRender();
        DpiChanged += (_, _) => QueueRender();
        graph.MouseMove += (_, args) => ReadCursor(args.GetPosition(graph));
        graph.MouseLeave += (_, _) => readout.Text = "";
        PreviewKeyDown += (_, args) =>
        {
            var key = args.Key == Key.System ? args.SystemKey : args.Key;
            if (args.IsRepeat || Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Alt) || key is not (Key.D2 or Key.NumPad2)) return;
            args.Handled = true;
            Close();
        };
        Loaded += (_, _) =>
        {
            if (mainWindow is not null) mainWindow.ScopeFrameRendered += OnFrameRendered;
            RefreshSource();
        };
        Closed += (_, _) =>
        {
            closed = true;
            if (mainWindow is not null) mainWindow.ScopeFrameRendered -= OnFrameRendered;
        };
        UpdateControls();
    }

    private static StackPanel Field(string name, Control input, TextBlock? value = null)
    {
        var label = new Label { Content = name, Target = input, Padding = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        AutomationProperties.SetLabeledBy(input, label);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 5) };
        panel.Children.Add(label);
        panel.Children.Add(input);
        if (value is not null) panel.Children.Add(value);
        return panel;
    }

    public void SetTestPixels(byte[] pixels, int width, int height)
    {
        testSource = new(pixels, width, height);
        RefreshSource();
    }

    private void UpdateControls()
    {
        var options = Options;
        scaleCombo.IsEnabled = options.Mode is VideoScopeMode.Waveform or VideoScopeMode.RgbParade;
        intensity.IsEnabled = options.Mode != VideoScopeMode.Histogram;
        intensityText.Text = $"{options.Intensity * 100:0}%";
        QueueRender();
    }

    private void OnFrameRendered(byte[] pixels, int width, int height)
    {
        if (testSource.HasValue || (followPlayback.IsChecked != true && mainWindow?.IsTimelinePlaying == true)) return;
        source = new(pixels, width, height);
        QueueRender();
    }

    private void RefreshSource()
    {
        var (pixels, width, height) = mainWindow?.GetCurrentScopeFrame() ?? default;
        source = testSource ?? new(pixels, width, height);
        QueueRender();
    }

    private RenderSettings CurrentSettings()
    {
        var dpi = VisualTreeHelper.GetDpi(graph);
        return new(Options, Math.Max(64, (int)Math.Round(graph.ActualWidth * dpi.DpiScaleX)),
            Math.Max(64, (int)Math.Round(graph.ActualHeight * dpi.DpiScaleY)), limits);
    }

    internal void SetScopeLimits(LuminanceThresholds? value)
    {
        if (limits == value) return;
        limits = value;
        QueueRender();
    }

    private async void QueueRender()
    {
        if (!IsLoaded || closed) return;
        queued = true;
        if (rendering) return;
        rendering = true;
        try
        {
            while (queued && !closed)
            {
                queued = false;
                var image = source;
                var settings = CurrentSettings();
                try
                {
                    var pixels = await Task.Run(() => VideoScopeEngine.RenderScope(
                        image.Pixels, image.Width, image.Height, image.Width * 4, settings.Width, settings.Height,
                        settings.Options.Mode, settings.Options.Scale, settings.Options.Intensity, settings.Limits));
                    if (closed || settings != CurrentSettings()) continue;
                    Present(pixels, settings);
                }
                catch (Exception error)
                {
                    if (!closed && settings == CurrentSettings()) readout.Text = $"Scope render failed. {error.Message}";
                }
            }
        }
        finally { rendering = false; }
    }

    private void Present(byte[] pixels, RenderSettings settings)
    {
        var (width, height) = (settings.Width, settings.Height);
        if (bitmap is null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
        {
            bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            graph.Source = bitmap;
        }
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        displayed = settings;
        AutomationProperties.SetName(graph, settings.Options.Mode == VideoScopeMode.RgbParade ? "RGB Parade" : settings.Options.Mode.ToString());
        if (graph.IsMouseOver) ReadCursor(Mouse.GetPosition(graph));
    }

    private void ReadCursor(Point position)
    {
        if (displayed is not { } chart || graph.ActualWidth <= 0 || graph.ActualHeight <= 0) return;
        var x = Math.Clamp(position.X * chart.Width / graph.ActualWidth, 0, chart.Width - 1);
        var y = Math.Clamp(position.Y * chart.Height / graph.ActualHeight, 0, chart.Height - 1);
        switch (chart.Options.Mode)
        {
            case VideoScopeMode.Waveform:
            case VideoScopeMode.RgbParade:
                var code = VideoScopeEngine.LumaCodeAtRow((int)y, chart.Height);
                var ire = VideoScopeEngine.LumaIre(code, chart.Options.Scale);
                readout.Text = $"Code {code} · {ire:0.0} IRE";
                break;
            case VideoScopeMode.Vectorscope:
                var (cb, cr) = VideoScopeEngine.ChromaAt(x, y, chart.Width, chart.Height);
                var angle = (Math.Atan2(cr, cb) * (180 / Math.PI) + 360) % 360;
                readout.Text = $"Cb {cb:+0.00;-0.00;0.00} · Cr {cr:+0.00;-0.00;0.00} · {angle:0.0}°";
                break;
            case VideoScopeMode.Histogram:
                readout.Text = $"Code {(int)x * 255 / (chart.Width - 1)}";
                break;
        }
    }
}
