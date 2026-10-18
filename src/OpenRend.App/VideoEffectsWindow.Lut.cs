using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenRend.Core;

namespace OpenRend.App;

public sealed partial class VideoEffectsWindow
{
    private TextBlock? lutDetails;
    private Button? clearLut;

    private void AddLutControls()
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(MakeButton("Browse…", "Import a 3D cube LUT", BrowseLut, "BrowseLut"));
        clearLut = MakeButton("Clear", "Clear imported LUT", () => Edit(() => SetLut(null)), "ClearLut");
        buttons.Children.Add(clearLut);
        parameters.Children.Add(buttons);
        lutDetails = new TextBlock { Margin = new Thickness(2, 4, 2, 10), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(lutDetails, "LutDetails");
        parameters.Children.Add(lutDetails);
        UpdateLutDetails();
    }

    private void UpdateLutDetails()
    {
        if (lutDetails is null) return;
        var lut = SelectedEffect?.Lut;
        lutDetails.Text = lut is null ? "No LUT loaded. Browse to import a 3D .cube file." :
            $"{(string.IsNullOrWhiteSpace(lut.Title) ? "Imported LUT" : lut.Title)} · {lut.Size} × {lut.Size} × {lut.Size}";
        if (clearLut is not null) clearLut.IsEnabled = lut is not null;
    }

    private void BrowseLut()
    {
        if (SelectedEffect?.Kind != VideoEffectKind.Lut3D) return;
        var dialog = new OpenFileDialog { Title = "Import 3D LUT", Filter = "3D cube LUT|*.cube", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var lut = CubeLutParser.Load(dialog.FileName);
            if (string.IsNullOrWhiteSpace(lut.Title)) lut.Title = Path.GetFileName(dialog.FileName);
            Edit(() => SetLut(lut));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { status.Text = error.Message; }
    }

    private void SetLut(CubeLut? lut)
    {
        if (SelectedEffect is not { Kind: VideoEffectKind.Lut3D } selected) return;
        // Embed an independent lattice so reopening never depends on the imported file remaining in place.
        var effects = TargetEffects.Select(effect => effect.DeepClone()).ToArray();
        effects.Single(effect => effect.Id == selected.Id).Lut = lut?.DeepClone();
        editor.SetScopedVideoEffects(scope, targetId, effects);
    }
}
