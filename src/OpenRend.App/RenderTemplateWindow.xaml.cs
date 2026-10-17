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
    public RenderPreferences Preferences { get; private set; }

    public RenderTemplateWindow(ProjectDocument project, RenderPreferences preferences, RenderTemplate template)
    {
        preferences.Validate();
        source = template;
        Preferences = preferences;
        InitializeComponent();
        Style = (Style)FindResource(typeof(Window));
        PresetName.Text = template.IsCustom ? template.Name : AvailableName(template.Name + " (custom)");
        Quality.Text = template.VideoCrf.ToString(CultureInfo.CurrentCulture);
        AudioBitrate.Text = template.AudioBitrateKbps.ToString(CultureInfo.CurrentCulture);
        Speed.ItemsSource = Enum.GetValues<EncoderSpeed>();
        Speed.SelectedItem = template.EncoderSpeed;
        ProjectDetails.Text = $"{project.Width} × {project.Height}, {project.FrameRate.Value:0.###} fps · H.264 · Progressive · Project settings";
        Save.IsEnabled = template.IsCustom;
        Save.IsDefault = template.IsCustom;
        SaveAs.IsDefault = !template.IsCustom;
        Loaded += (_, _) => { PresetName.Focus(); PresetName.SelectAll(); };
        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
    }

    private string AvailableName(string name)
    {
        var candidate = name;
        for (var number = 2; Preferences.Templates.Any(template => template.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); number++)
            candidate = name + " " + number.ToString(CultureInfo.CurrentCulture);
        return candidate;
    }

    private void SettingsChanged(object sender, TextChangedEventArgs e) { if (Error is not null) Error.Text = ""; }
    private void SpeedChanged(object sender, SelectionChangedEventArgs e) { if (Error is not null) Error.Text = ""; }
    private void SaveClick(object sender, RoutedEventArgs e) { if (source.IsCustom) SaveTemplate(source.Id); }
    private void SaveAsClick(object sender, RoutedEventArgs e) => SaveTemplate(null);

    private void SaveTemplate(string? existingId)
    {
        if (!int.TryParse(Quality.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var crf) || crf is < 0 or > 51)
        { Error.Text = "Enter a whole-number quality value between 0 and 51."; Quality.Focus(); return; }
        if (!int.TryParse(AudioBitrate.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var bitrate) || bitrate is < 32 or > 512)
        { Error.Text = "Enter a whole-number audio bitrate between 32 and 512 kbps."; AudioBitrate.Focus(); return; }
        if (Speed.SelectedItem is not EncoderSpeed speed)
        { Error.Text = "Choose an encoder speed."; Speed.Focus(); return; }
        try
        {
            Preferences = Preferences.SaveCustomTemplate(PresetName.Text, source with
                { VideoCrf = crf, AudioBitrateKbps = bitrate, EncoderSpeed = speed }, existingId);
            DialogResult = true;
        }
        catch (InvalidDataException error) { Error.Text = error.Message; PresetName.Focus(); }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
