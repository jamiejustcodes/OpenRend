using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenRend.Media;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro Video Scopes window (Ctrl+Alt+2).
/// Displays real-time Rec. 709 Luminance/RGB Waveform, Polar Vectorscope,
/// RGB Parade, and 256-bin Histogram with authentic CRT phosphor traces.
/// </summary>
public sealed class VideoScopesWindow : Window
{
    private readonly MainWindow? mainWindow;
    private readonly ComboBox modeCombo = new() { Width = 150, Height = 24, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox scaleCombo = new() { Width = 130, Height = 24, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Slider intensitySlider = new() { Minimum = 0.4, Maximum = 2.4, Value = 1.0, Width = 90, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
    private readonly TextBlock intensityText = new() { Text = "100%", Width = 38, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Foreground = Brushes.Silver };
    private readonly TextBlock readoutText = new() { Text = "Ready", FontSize = 11, Foreground = Brushes.Silver, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image scopeImage = new() { Stretch = Stretch.Fill };
    private WriteableBitmap? writeableBmp;
    private byte[]? customPixels;
    private int customWidth, customHeight;
    private bool isUpdating;

    public VideoScopesWindow(MainWindow? window = null)
    {
        mainWindow = window;
        Style = (Style)FindResource(typeof(Window));
        Title = "Video Scopes";
        Width = 520;
        Height = 440;
        MinWidth = 380;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panelBrush = (Brush)FindResource("PanelBrush");
        var textBrush = (Brush)FindResource("TextBrush");
        var borderBrush = (Brush)FindResource("BorderBrush");

        var root = new Grid { Background = panelBrush, Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Toolbar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Scope Canvas
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bottom readout

        // 1. Toolbar
        var toolBar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        modeCombo.Items.Add(new ComboBoxItem { Content = "Waveform", Tag = VideoScopeMode.Waveform });
        modeCombo.Items.Add(new ComboBoxItem { Content = "Vectorscope", Tag = VideoScopeMode.Vectorscope });
        modeCombo.Items.Add(new ComboBoxItem { Content = "RGB Parade", Tag = VideoScopeMode.RgbParade });
        modeCombo.Items.Add(new ComboBoxItem { Content = "Histogram", Tag = VideoScopeMode.Histogram });
        modeCombo.SelectedIndex = 0;
        modeCombo.SelectionChanged += (_, _) => UpdateScope();

        scaleCombo.Items.Add(new ComboBoxItem { Content = "Studio RGB (16-235)", Tag = VideoScopeScale.StudioRgb });
        scaleCombo.Items.Add(new ComboBoxItem { Content = "Full Range (0-255)", Tag = VideoScopeScale.FullRange });
        scaleCombo.SelectedIndex = 0;
        scaleCombo.SelectionChanged += (_, _) => UpdateScope();

        intensitySlider.ValueChanged += (_, e) =>
        {
            intensityText.Text = $"{(int)Math.Round(e.NewValue * 100)}%";
            UpdateScope();
        };

        var refreshBtn = new Button
        {
            Content = "↻ Refresh",
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(8, 0, 0, 0)
        };
        refreshBtn.Click += (_, _) => UpdateScope();

        toolBar.Children.Add(new TextBlock { Text = "Scope: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Foreground = textBrush });
        toolBar.Children.Add(modeCombo);
        toolBar.Children.Add(new TextBlock { Text = "Scale: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), Foreground = textBrush });
        toolBar.Children.Add(scaleCombo);
        toolBar.Children.Add(new TextBlock { Text = "Gain: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), Foreground = textBrush });
        toolBar.Children.Add(intensitySlider);
        toolBar.Children.Add(intensityText);
        toolBar.Children.Add(refreshBtn);

        Grid.SetRow(toolBar, 0);
        root.Children.Add(toolBar);

        // 2. Scope Display Area
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(10, 14, 12)),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Child = scopeImage,
            ClipToBounds = true
        };
        border.MouseMove += ScopeMouseMove;
        border.MouseLeave += (_, _) => readoutText.Text = "Ready";

        Grid.SetRow(border, 1);
        root.Children.Add(border);

        // 3. Status Readout
        var statusPanel = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(readoutText, Dock.Left);
        statusPanel.Children.Add(readoutText);

        var hintText = new TextBlock
        {
            Text = "Rec. 709 Phosphor CRT | Real-Time",
            Foreground = Brushes.Gray,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(hintText, Dock.Right);
        statusPanel.Children.Add(hintText);

        Grid.SetRow(statusPanel, 2);
        root.Children.Add(statusPanel);

        Content = root;

        Loaded += (_, _) =>
        {
            if (mainWindow != null) mainWindow.FrameRendered += OnFrameRendered;
            UpdateScope();
        };

        Closed += (_, _) =>
        {
            if (mainWindow != null) mainWindow.FrameRendered -= OnFrameRendered;
        };

        SizeChanged += (_, _) => UpdateScope();
    }

    public void SetTestPixels(byte[] pixels, int width, int height)
    {
        customPixels = pixels;
        customWidth = width;
        customHeight = height;
        UpdateScope();
    }

    private void OnFrameRendered(byte[] pixels, int width, int height)
    {
        Dispatcher.BeginInvoke(() => UpdateScopeWithPixels(pixels, width, height));
    }

    private void UpdateScope()
    {
        if (customPixels != null && customWidth > 0 && customHeight > 0)
        {
            UpdateScopeWithPixels(customPixels, customWidth, customHeight);
            return;
        }
        var (pixels, width, height) = mainWindow != null ? mainWindow.GetCurrentFrame() : (null, 0, 0);
        UpdateScopeWithPixels(pixels, width, height);
    }

    private async void UpdateScopeWithPixels(byte[]? pixels, int width, int height)
    {
        if (isUpdating) return;
        isUpdating = true;

        try
        {
            int targetW = (int)scopeImage.ActualWidth;
            int targetH = (int)scopeImage.ActualHeight;

            if (targetW < 120 || targetH < 90)
            {
                targetW = 384;
                targetH = 288;
            }

            var mode = (modeCombo.SelectedItem as ComboBoxItem)?.Tag as VideoScopeMode? ?? VideoScopeMode.Waveform;
            var scale = (scaleCombo.SelectedItem as ComboBoxItem)?.Tag as VideoScopeScale? ?? VideoScopeScale.StudioRgb;
            double intensity = intensitySlider.Value;

            byte[]? bgraScope = await Task.Run(() =>
            {
                if (pixels == null || width <= 0 || height <= 0)
                {
                    return VideoScopeEngine.RenderScope(ReadOnlySpan<byte>.Empty, 0, 0, 0, targetW, targetH, mode, scale, intensity);
                }
                return VideoScopeEngine.RenderScope(pixels, width, height, width * 4, targetW, targetH, mode, scale, intensity);
            });

            if (bgraScope != null)
            {
                if (writeableBmp == null || writeableBmp.PixelWidth != targetW || writeableBmp.PixelHeight != targetH)
                {
                    writeableBmp = new WriteableBitmap(targetW, targetH, 96, 96, PixelFormats.Bgra32, null);
                    scopeImage.Source = writeableBmp;
                }

                writeableBmp.WritePixels(new Int32Rect(0, 0, targetW, targetH), bgraScope, targetW * 4, 0);
            }
        }
        catch (Exception ex)
        {
            readoutText.Text = "Error: " + ex.Message;
        }
        finally
        {
            isUpdating = false;
        }
    }

    private void ScopeMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(scopeImage);
        int w = (int)scopeImage.ActualWidth;
        int h = (int)scopeImage.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double normX = Math.Clamp(pos.X / w, 0.0, 1.0);
        double normY = Math.Clamp(1.0 - (pos.Y / h), 0.0, 1.0); // 0 at bottom, 1 at top

        var mode = (modeCombo.SelectedItem as ComboBoxItem)?.Tag as VideoScopeMode? ?? VideoScopeMode.Waveform;
        switch (mode)
        {
            case VideoScopeMode.Waveform:
            case VideoScopeMode.RgbParade:
                int code = (int)Math.Round(normY * 255);
                double ire = normY * 100.0;
                readoutText.Text = $"Position: X={normX * 100:0.0}%, Y={normY * 100:0.0}% | Code: {code} | IRE: {ire:0.0}%";
                break;
            case VideoScopeMode.Vectorscope:
                double u = (normX - 0.5) * 2.0;
                double v = (normY - 0.5) * 2.0;
                double sat = Math.Sqrt(u * u + v * v) * 100.0;
                double angleDeg = Math.Atan2(v, u) * 180.0 / Math.PI;
                if (angleDeg < 0) angleDeg += 360;
                readoutText.Text = $"Vectorscope: Cb/U={u:+0.00;-0.00}, Cr/V={v:+0.00;-0.00} | Sat: {sat:0.0}% | Angle: {angleDeg:0.0}°";
                break;
            case VideoScopeMode.Histogram:
                int bin = (int)Math.Round(normX * 255);
                readoutText.Text = $"Histogram Code: {bin} ({(bin * 100.0 / 255):0.0}%)";
                break;
        }
    }
}
