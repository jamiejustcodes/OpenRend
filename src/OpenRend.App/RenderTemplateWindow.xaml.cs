using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

public partial class RenderTemplateWindow : Window
{
    private readonly RenderTemplate source;
    private readonly ProjectDocument project;
    private bool suppressDimensionSync;
    private bool suppressBitrateSync;
    public RenderPreferences Preferences { get; private set; }

    public RenderTemplateWindow(ProjectDocument project, RenderPreferences preferences, RenderTemplate template)
    {
        preferences.Validate();
        this.project = project;
        source = template;
        Preferences = preferences;
        InitializeComponent();
        Style = (Style)FindResource(typeof(Window));

        Title = $"Custom Settings — {template.FormatName}";
        PresetName.Text = template.IsCustom ? template.Name : AvailableName(template.Name + " (custom)");
        DescriptionNotes.Text = template.DescriptionText ?? template.Description(project);

        Save.IsEnabled = template.IsCustom;
        Delete.IsEnabled = template.IsCustom;

        // Video Settings Setup
        IncludeVideoCheck.IsChecked = template.IncludeVideo && !template.IsAudioOnly;
        if (template.IsAudioOnly)
        {
            IncludeVideoCheck.IsEnabled = false;
        }

        // Frame Size Presets
        var projectW = project.Width;
        var projectH = project.Height;
        var currentW = template.Width ?? projectW;
        var currentH = template.Height ?? projectH;

        FrameSizeCombo.ItemsSource = new[]
        {
            "(Custom frame size)",
            $"Project settings ({projectW} × {projectH})",
            "High Definition 1080p (1920 × 1080)",
            "4K UHD (3840 × 2160)",
            "HD 720p (1280 × 720)",
            "4K DCI (4096 × 2160)",
            "Standard 480p (854 × 480)"
        };

        suppressDimensionSync = true;
        WidthBox.Text = currentW.ToString(CultureInfo.InvariantCulture);
        HeightBox.Text = currentH.ToString(CultureInfo.InvariantCulture);
        SelectFrameSizePreset(currentW, currentH);
        suppressDimensionSync = false;

        // Profile Combo
        ProfileCombo.ItemsSource = Enum.GetValues<VideoProfile>();
        ProfileCombo.SelectedItem = template.Profile;

        // Frame Rate Combo
        FrameRateCombo.ItemsSource = new[]
        {
            $"Project frame rate ({project.FrameRate.Value:0.###} fps)",
            "23.976 (IVTC Film)",
            "24.000 (Film)",
            "25.000 (PAL)",
            "29.970 (NTSC)",
            "30.000",
            "50.000 (PAL)",
            "59.940 (NTSC)",
            "60.000"
        };
        SelectFrameRatePreset(template.FrameRate);

        // Field Order
        FieldOrderCombo.ItemsSource = new[]
        {
            "None (progressive scan)",
            "Upper field first",
            "Lower field first"
        };
        FieldOrderCombo.SelectedIndex = template.FieldOrder switch
        {
            FieldOrder.UpperFieldFirst => 1,
            FieldOrder.LowerFieldFirst => 2,
            _ => 0
        };

        // Pixel Aspect Ratio
        PixelAspectCombo.ItemsSource = new[]
        {
            "1.0000 (Square)",
            "0.9091 (NTSC DV)",
            "1.0926 (PAL DV)",
            "1.3333 (HDV 1080-60i)"
        };
        SelectPixelAspectPreset(template.PixelAspectRatio);

        // Bitrate & Rate Control
        suppressBitrateSync = true;
        MaxBitrateBox.Text = template.MaxBitrateBps.ToString(CultureInfo.InvariantCulture);
        AvgBitrateBox.Text = template.AverageBitrateBps.ToString(CultureInfo.InvariantCulture);
        Quality.Text = template.VideoCrf.ToString(CultureInfo.InvariantCulture);
        UpdateBitrateLabels();

        switch (template.BitrateMode)
        {
            case BitrateMode.VBR_2Pass:
                Vbr2PassRadio.IsChecked = true;
                break;
            case BitrateMode.VBR_1Pass:
                Vbr1PassRadio.IsChecked = true;
                break;
            case BitrateMode.CBR:
                CbrRadio.IsChecked = true;
                break;
            default:
                CrfRadio.IsChecked = true;
                break;
        }
        UpdateBitrateControlStates();
        suppressBitrateSync = false;

        // Hardware Acceleration
        HwAccelCombo.ItemsSource = Enum.GetValues<HardwareAcceleration>();
        HwAccelCombo.SelectedItem = template.HardwareAcceleration;

        // Speed / Preset
        Speed.ItemsSource = Enum.GetValues<EncoderSpeed>();
        Speed.SelectedItem = template.EncoderSpeed;

        // Audio Settings Setup
        IncludeAudioCheck.IsChecked = template.IncludeAudio;
        AudioFormatCombo.ItemsSource = new[] { "AAC", "MP3 Audio", "Uncompressed PCM" };
        AudioFormatCombo.SelectedItem = template.AudioCodec == "libmp3lame" ? "MP3 Audio" :
            template.AudioCodec.StartsWith("pcm", StringComparison.OrdinalIgnoreCase) ? "Uncompressed PCM" : "AAC";

        SampleRateCombo.ItemsSource = new object[] { 44100, 48000, 96000 };
        SampleRateCombo.SelectedItem = template.AudioSampleRate;

        AudioBitrate.Text = template.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture);

        ChannelsCombo.ItemsSource = new[] { "Stereo (2 Channels)", "5.1 Surround (6 Channels)" };
        ChannelsCombo.SelectedIndex = template.AudioChannels == 6 ? 1 : 0;

        // System Settings Setup
        FormatDetailsText.Text = $"{template.FormatName} (*{template.Extension})";
        FastStartCheck.IsChecked = template.FastStart;

        // Project Settings Setup
        QualityCombo.ItemsSource = Enum.GetValues<RenderQuality>();
        QualityCombo.SelectedItem = template.Quality;

        ColorSpaceCombo.ItemsSource = Enum.GetValues<ColorSpaceOption>();
        ColorSpaceCombo.SelectedItem = template.ColorSpace;

        ResampleCombo.ItemsSource = Enum.GetValues<ResampleMode>();
        ResampleCombo.SelectedItem = template.ResampleMode;

        Loaded += (_, _) => { PresetName.Focus(); PresetName.SelectAll(); UpdateTabVisibility(0); };
        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
    }

    private void TabStripSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabStrip != null)
        {
            UpdateTabVisibility(TabStrip.SelectedIndex);
        }
    }

    private void UpdateTabVisibility(int selectedIndex)
    {
        SetPanelActive(VideoPanel, selectedIndex == 0);
        SetPanelActive(AudioPanel, selectedIndex == 1);
        SetPanelActive(SystemPanel, selectedIndex == 2);
        SetPanelActive(ProjectPanel, selectedIndex == 3);
    }

    private static void SetPanelActive(FrameworkElement? panel, bool active)
    {
        if (panel == null) return;
        panel.Visibility = Visibility.Visible;
        if (active)
        {
            panel.Height = double.NaN;
            panel.MaxHeight = double.PositiveInfinity;
            panel.Opacity = 1.0;
            panel.IsHitTestVisible = true;
            Panel.SetZIndex(panel, 1);
        }
        else
        {
            panel.Height = 0;
            panel.MaxHeight = 0;
            panel.Opacity = 0.0;
            panel.IsHitTestVisible = false;
            Panel.SetZIndex(panel, 0);
        }
    }

    private void FieldGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender == AudioBitrate && TabStrip != null && TabStrip.SelectedIndex != 1)
        {
            TabStrip.SelectedIndex = 1;
        }
        else if ((sender == Quality || sender == Speed) && TabStrip != null && TabStrip.SelectedIndex != 0)
        {
            TabStrip.SelectedIndex = 0;
        }
    }

    private string AvailableName(string name)
    {
        var candidate = name;
        for (var number = 2; Preferences.Templates.Any(template => template.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); number++)
            candidate = name + " " + number.ToString(CultureInfo.CurrentCulture);
        return candidate;
    }

    private void SelectFrameSizePreset(int width, int height)
    {
        if (width == project.Width && height == project.Height) FrameSizeCombo.SelectedIndex = 1;
        else if (width == 1920 && height == 1080) FrameSizeCombo.SelectedIndex = 2;
        else if (width == 3840 && height == 2160) FrameSizeCombo.SelectedIndex = 3;
        else if (width == 1280 && height == 720) FrameSizeCombo.SelectedIndex = 4;
        else if (width == 4096 && height == 2160) FrameSizeCombo.SelectedIndex = 5;
        else if (width == 854 && height == 480) FrameSizeCombo.SelectedIndex = 6;
        else FrameSizeCombo.SelectedIndex = 0;
    }

    private void SelectFrameRatePreset(Rational? fps)
    {
        if (!fps.HasValue) { FrameRateCombo.SelectedIndex = 0; return; }
        var val = fps.Value.Value;
        if (Math.Abs(val - 23.976) < 0.01) FrameRateCombo.SelectedIndex = 1;
        else if (Math.Abs(val - 24.0) < 0.01) FrameRateCombo.SelectedIndex = 2;
        else if (Math.Abs(val - 25.0) < 0.01) FrameRateCombo.SelectedIndex = 3;
        else if (Math.Abs(val - 29.97) < 0.01) FrameRateCombo.SelectedIndex = 4;
        else if (Math.Abs(val - 30.0) < 0.01) FrameRateCombo.SelectedIndex = 5;
        else if (Math.Abs(val - 50.0) < 0.01) FrameRateCombo.SelectedIndex = 6;
        else if (Math.Abs(val - 59.94) < 0.01) FrameRateCombo.SelectedIndex = 7;
        else if (Math.Abs(val - 60.0) < 0.01) FrameRateCombo.SelectedIndex = 8;
        else FrameRateCombo.SelectedIndex = 0;
    }

    private void SelectPixelAspectPreset(double aspect)
    {
        if (Math.Abs(aspect - 0.9091) < 0.001) PixelAspectCombo.SelectedIndex = 1;
        else if (Math.Abs(aspect - 1.0926) < 0.001) PixelAspectCombo.SelectedIndex = 2;
        else if (Math.Abs(aspect - 1.3333) < 0.001) PixelAspectCombo.SelectedIndex = 3;
        else PixelAspectCombo.SelectedIndex = 0;
    }

    private void SettingsChanged(object sender, TextChangedEventArgs e)
    {
        if (Error is not null) Error.Text = "";
        UpdateBitrateLabels();
    }

    private void SpeedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Error is not null) Error.Text = "";
    }

    private void DimensionTextChanged(object sender, TextChangedEventArgs e)
    {
        if (Error is not null) Error.Text = "";
        if (suppressDimensionSync) return;
        if (int.TryParse(WidthBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) &&
            int.TryParse(HeightBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
        {
            suppressDimensionSync = true;
            SelectFrameSizePreset(w, h);
            suppressDimensionSync = false;
        }
    }

    private void FrameSizeComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressDimensionSync || WidthBox == null || HeightBox == null) return;
        suppressDimensionSync = true;
        switch (FrameSizeCombo.SelectedIndex)
        {
            case 1:
                WidthBox.Text = project.Width.ToString(CultureInfo.InvariantCulture);
                HeightBox.Text = project.Height.ToString(CultureInfo.InvariantCulture);
                break;
            case 2:
                WidthBox.Text = "1920";
                HeightBox.Text = "1080";
                break;
            case 3:
                WidthBox.Text = "3840";
                HeightBox.Text = "2160";
                break;
            case 4:
                WidthBox.Text = "1280";
                HeightBox.Text = "720";
                break;
            case 5:
                WidthBox.Text = "4096";
                HeightBox.Text = "2160";
                break;
            case 6:
                WidthBox.Text = "854";
                HeightBox.Text = "480";
                break;
        }
        suppressDimensionSync = false;
        if (Error is not null) Error.Text = "";
    }

    private void ProfileComboChanged(object sender, SelectionChangedEventArgs e) { if (Error is not null) Error.Text = ""; }
    private void FrameRateComboChanged(object sender, SelectionChangedEventArgs e) { if (Error is not null) Error.Text = ""; }
    private void FieldOrderComboChanged(object sender, SelectionChangedEventArgs e) { if (Error is not null) Error.Text = ""; }
    private void PixelAspectComboChanged(object sender, SelectionChangedEventArgs e) { if (Error is not null) Error.Text = ""; }
    private void HwAccelComboChanged(object sender, SelectionChangedEventArgs e) { if (Error is not null) Error.Text = ""; }
    private void AudioFormatComboChanged(object sender, SelectionChangedEventArgs e) { if (Error is not null) Error.Text = ""; }
    private void SampleRateComboChanged(object sender, SelectionChangedEventArgs e) { if (Error is not null) Error.Text = ""; }
    private void ChannelsComboChanged(object sender, SelectionChangedEventArgs e) { if (Error is not null) Error.Text = ""; }
    private void QualityComboChanged(object sender, SelectionChangedEventArgs e) { if (Error is not null) Error.Text = ""; }
    private void ColorSpaceComboChanged(object sender, SelectionChangedEventArgs e) { if (Error is not null) Error.Text = ""; }
    private void ResampleComboChanged(object sender, SelectionChangedEventArgs e) { if (Error is not null) Error.Text = ""; }

    private void IncludeVideoChanged(object sender, RoutedEventArgs e)
    {
        var hasVideo = IncludeVideoCheck?.IsChecked == true;
        if (WidthBox != null) WidthBox.IsEnabled = hasVideo;
        if (HeightBox != null) HeightBox.IsEnabled = hasVideo;
        if (FrameSizeCombo != null) FrameSizeCombo.IsEnabled = hasVideo;
        if (ProfileCombo != null) ProfileCombo.IsEnabled = hasVideo;
        if (FrameRateCombo != null) FrameRateCombo.IsEnabled = hasVideo;
        if (FieldOrderCombo != null) FieldOrderCombo.IsEnabled = hasVideo;
        if (PixelAspectCombo != null) PixelAspectCombo.IsEnabled = hasVideo;
        if (HwAccelCombo != null) HwAccelCombo.IsEnabled = hasVideo;
        if (Speed != null) Speed.IsEnabled = hasVideo;
        UpdateBitrateControlStates();
    }

    private void IncludeAudioChanged(object sender, RoutedEventArgs e)
    {
        var hasAudio = IncludeAudioCheck?.IsChecked == true;
        if (AudioFormatCombo != null) AudioFormatCombo.IsEnabled = hasAudio;
        if (SampleRateCombo != null) SampleRateCombo.IsEnabled = hasAudio;
        if (AudioBitrate != null) AudioBitrate.IsEnabled = hasAudio;
        if (ChannelsCombo != null) ChannelsCombo.IsEnabled = hasAudio;
    }

    private void BitrateModeRadioChanged(object sender, RoutedEventArgs e)
    {
        if (suppressBitrateSync) return;
        UpdateBitrateControlStates();
    }

    private void UpdateBitrateControlStates()
    {
        if (MaxBitrateBox == null || AvgBitrateBox == null || Quality == null) return;
        var hasVideo = IncludeVideoCheck?.IsChecked == true;
        var isVbr = Vbr1PassRadio?.IsChecked == true || Vbr2PassRadio?.IsChecked == true;
        var isCbr = CbrRadio?.IsChecked == true;
        var isCrf = CrfRadio?.IsChecked == true;

        MaxBitrateBox.IsEnabled = hasVideo && isVbr;
        AvgBitrateBox.IsEnabled = hasVideo && (isVbr || isCbr);
        Quality.IsEnabled = hasVideo && isCrf;
    }

    private void UpdateBitrateLabels()
    {
        if (MaxBitrateMbpsText == null || AvgBitrateMbpsText == null) return;
        if (long.TryParse(MaxBitrateBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxBps) && maxBps > 0)
            MaxBitrateMbpsText.Text = $"{maxBps / 1_000_000.0:0.##} Mbps";
        else
            MaxBitrateMbpsText.Text = "—";

        if (long.TryParse(AvgBitrateBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var avgBps) && avgBps > 0)
            AvgBitrateMbpsText.Text = $"{avgBps / 1_000_000.0:0.##} Mbps";
        else
            AvgBitrateMbpsText.Text = "—";
    }

    private async void CheckGpuClick(object sender, RoutedEventArgs e)
    {
        CheckGpuButton.IsEnabled = false;
        GpuStatusText.Text = "Probing graphics hardware...";
        try
        {
            var support = await Task.Run(() => HardwareAccelerationHelper.Check(forceRefresh: true));
            GpuStatusText.Text = support.SummaryText;
        }
        catch (Exception ex)
        {
            GpuStatusText.Text = "GPU check failed: " + ex.Message;
        }
        finally
        {
            CheckGpuButton.IsEnabled = true;
        }
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (source.IsCustom) SaveTemplate(source.Id);
        else SaveTemplate(null);
    }

    private void SaveAsClick(object sender, RoutedEventArgs e) => SaveTemplate(null);

    private void OkClick(object sender, RoutedEventArgs e)
    {
        SaveTemplate(source.IsCustom ? source.Id : null);
    }

    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (!source.IsCustom) return;
        if (MessageBox.Show(this, $"Delete the template “{source.Name}”?", "Custom Settings",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            Preferences = Preferences.DeleteCustomTemplate(source.Id);
            DialogResult = true;
        }
        catch (InvalidDataException error)
        {
            Error.Text = error.Message;
        }
    }

    private void SaveTemplate(string? existingId)
    {
        var hasVideo = IncludeVideoCheck?.IsChecked == true && !source.IsAudioOnly;
        var hasAudio = IncludeAudioCheck?.IsChecked == true;

        int? width = null;
        int? height = null;
        if (hasVideo)
        {
            if (!int.TryParse(WidthBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) || w <= 0 || (w & 1) != 0)
            {
                Error.Text = "Width must be a positive even integer.";
                WidthBox.Focus();
                return;
            }
            if (!int.TryParse(HeightBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) || h <= 0 || (h & 1) != 0)
            {
                Error.Text = "Height must be a positive even integer.";
                HeightBox.Focus();
                return;
            }
            width = w;
            height = h;
        }

        // Bitrate / CRF evaluation
        var bitrateMode = BitrateMode.CRF;
        if (Vbr2PassRadio.IsChecked == true) bitrateMode = BitrateMode.VBR_2Pass;
        else if (Vbr1PassRadio.IsChecked == true) bitrateMode = BitrateMode.VBR_1Pass;
        else if (CbrRadio.IsChecked == true) bitrateMode = BitrateMode.CBR;

        var crf = 20;
        if (!int.TryParse(Quality.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out crf) || crf is < 0 or > 51)
        {
            Error.Text = "H.264/HEVC quality must be between 0 and 51.";
            Quality.Focus();
            return;
        }

        long avgBps = 12_000_000;
        long maxBps = 24_000_000;
        if (hasVideo && (bitrateMode == BitrateMode.VBR_1Pass || bitrateMode == BitrateMode.VBR_2Pass))
        {
            if (!long.TryParse(AvgBitrateBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out avgBps) || avgBps <= 0)
            {
                Error.Text = "Average bitrate must be greater than zero.";
                AvgBitrateBox.Focus();
                return;
            }
            if (!long.TryParse(MaxBitrateBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxBps) || maxBps <= 0)
            {
                Error.Text = "Maximum bitrate must be greater than zero.";
                MaxBitrateBox.Focus();
                return;
            }
            if (maxBps < avgBps)
            {
                Error.Text = "Maximum bitrate cannot be less than average bitrate.";
                MaxBitrateBox.Focus();
                return;
            }
        }
        else if (hasVideo && bitrateMode == BitrateMode.CBR)
        {
            if (!long.TryParse(AvgBitrateBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out avgBps) || avgBps <= 0)
            {
                Error.Text = "Constant bitrate must be greater than zero.";
                AvgBitrateBox.Focus();
                return;
            }
            maxBps = avgBps;
        }

        var audioBitrate = 192;
        if (hasAudio)
        {
            if (!int.TryParse(AudioBitrate.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out audioBitrate) || audioBitrate is < 32 or > 512)
            {
                Error.Text = "Enter a whole-number audio bitrate between 32 and 512 kbps.";
                AudioBitrate.Focus();
                return;
            }
        }

        if (Speed.SelectedItem is not EncoderSpeed speed)
        {
            Error.Text = "Choose an encoder speed.";
            Speed.Focus();
            return;
        }

        var profile = ProfileCombo.SelectedItem is VideoProfile p ? p : VideoProfile.High;

        Rational? frameRate = FrameRateCombo.SelectedIndex switch
        {
            1 => new Rational(24000, 1001),
            2 => new Rational(24, 1),
            3 => new Rational(25, 1),
            4 => new Rational(30000, 1001),
            5 => new Rational(30, 1),
            6 => new Rational(50, 1),
            7 => new Rational(60000, 1001),
            8 => new Rational(60, 1),
            _ => null
        };

        var fieldOrder = FieldOrderCombo.SelectedIndex switch
        {
            1 => FieldOrder.UpperFieldFirst,
            2 => FieldOrder.LowerFieldFirst,
            _ => FieldOrder.Progressive
        };

        var pixelAspect = PixelAspectCombo.SelectedIndex switch
        {
            1 => 0.9091,
            2 => 1.0926,
            3 => 1.3333,
            _ => 1.0
        };

        var hwAccel = HwAccelCombo.SelectedItem is HardwareAcceleration hw ? hw : HardwareAcceleration.Auto;
        var sampleRate = SampleRateCombo.SelectedItem is int sr ? sr : 48000;
        var channels = ChannelsCombo.SelectedIndex == 1 ? 6 : 2;

        var quality = QualityCombo.SelectedItem is RenderQuality rq ? rq : RenderQuality.ProjectSettings;
        var colorSpace = ColorSpaceCombo.SelectedItem is ColorSpaceOption cs ? cs : ColorSpaceOption.ProjectSettings;
        var resample = ResampleCombo.SelectedItem is ResampleMode rm ? rm : ResampleMode.ProjectSettings;

        try
        {
            var updated = source with
            {
                Width = width,
                Height = height,
                Profile = profile,
                FieldOrder = fieldOrder,
                PixelAspectRatio = pixelAspect,
                FrameRate = frameRate,
                BitrateMode = bitrateMode,
                AverageBitrateBps = avgBps,
                MaxBitrateBps = maxBps,
                VideoCrf = crf,
                HardwareAcceleration = hwAccel,
                EncoderSpeed = speed,
                IncludeVideo = hasVideo,
                IncludeAudio = hasAudio,
                AudioSampleRate = sampleRate,
                AudioBitrateKbps = audioBitrate,
                AudioChannels = channels,
                FastStart = FastStartCheck.IsChecked == true,
                Quality = quality,
                ColorSpace = colorSpace,
                ResampleMode = resample,
                DescriptionText = string.IsNullOrWhiteSpace(DescriptionNotes.Text) ? null : DescriptionNotes.Text.Trim()
            };

            Preferences = Preferences.SaveCustomTemplate(PresetName.Text, updated, existingId);
            DialogResult = true;
        }
        catch (InvalidDataException error)
        {
            Error.Text = error.Message;
            PresetName.Focus();
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
