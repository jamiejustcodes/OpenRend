using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace OpenRend.App;

public partial class MainWindow
{
    private void UpdateMasterControls()
    {
        playback?.UpdateMasterControls(vm.Project);
        var background = TryFindResource("ButtonBackground") as Brush ?? TryFindResource("ButtonBackgroundBrush") as Brush ?? Brushes.DimGray;
        var foreground = TryFindResource("TextColor") as Brush ?? Brushes.White;
        var dim = vm.Project.MasterAudioDim;
        var mute = vm.Project.MasterAudioMute;
        MasterDimBtn.Background = dim ? Brushes.DarkGoldenrod : background;
        MasterDimBtn.Foreground = dim ? Brushes.Black : foreground;
        MasterMuteBtn.Background = mute ? Brushes.Firebrick : background;
        MasterMuteBtn.Foreground = mute ? Brushes.White : foreground;
    }

    private void UpdateMasterMeter(MasterMeterReading reading)
    {
        MasterMeter.Update(reading);
        MasterMomentaryLoudness.Text = $"M {FormatMeterValue(reading.MomentaryLufs)} LUFS";
        MasterShortTermLoudness.Text = $"S {FormatMeterValue(reading.ShortTermLufs)} LUFS";
        MasterTruePeak.Text = $"{FormatMeterValue(reading.TruePeakDb)} dBTP";
        var clip = (reading.LeftClipped, reading.RightClipped) switch
        {
            (true, true) => "Clip L R",
            (true, false) => "Clip L",
            (false, true) => "Clip R",
            _ => ""
        };
        MasterClipping.Content = clip;
        MasterClipping.Visibility = clip.Length == 0 ? Visibility.Hidden : Visibility.Visible;
    }

    private void ClearMasterClippingClick(object sender, RoutedEventArgs e)
    {
        if (playback is not { } session) return;
        session.ClearMeterClips();
        UpdateMasterMeter(session.MeterReading);
    }

    private static string FormatMeterValue(double value) => double.IsNaN(value) ? "—" :
        double.IsNegativeInfinity(value) ? "−∞" : value.ToString("0.0", CultureInfo.CurrentCulture);
}
