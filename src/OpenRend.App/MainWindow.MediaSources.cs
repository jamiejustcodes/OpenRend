using System.IO;
using System.Windows;
using Microsoft.Win32;
using OpenRend.Core;

namespace OpenRend.App;

public partial class MainWindow
{
    private async void ReplaceMediaClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (SelectedLibraryItem is not MediaItem item) { vm.Status = "Select a file in Project Media."; return; }
        var asset = item.Asset;
        if (asset.IsGenerated || asset.Path.StartsWith("openrend://", StringComparison.OrdinalIgnoreCase))
        {
            vm.Status = "Generated media has no source file to replace.";
            return;
        }
        Pause();
        if (await ChooseMediaReplacementAsync(vm.Project, asset, $"Replace {asset.Name}"))
            vm.Status = $"Source updated to {Path.GetFileName(asset.Path)}.";
    });

    private async Task RelinkMissingMediaAsync(ProjectDocument project, IReadOnlyList<MediaAsset> missing)
    {
        foreach (var asset in missing)
        {
            try
            {
                if (!await ChooseMediaReplacementAsync(project, asset, $"Locate missing media — {asset.Name}")) break;
            }
            catch (Exception error) { ShowError(error); break; }
        }
    }

    private async Task<bool> ChooseMediaReplacementAsync(ProjectDocument project, MediaAsset asset, string title)
    {
        var originalPath = asset.Path;
        var dialog = new OpenFileDialog { Title = title, FileName = Path.GetFileName(originalPath), CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return false;
        var source = await vm.Media.ProbeAsync(dialog.FileName);
        if (!ReferenceEquals(project, vm.Project) || !project.Media.Any(current => current.Id == asset.Id && current.Path == originalPath)) return false;
        vm.Editor.ReplaceMediaSource(asset.Id, source);
        await PopulateMediaVisualsAsync();
        return true;
    }
}
