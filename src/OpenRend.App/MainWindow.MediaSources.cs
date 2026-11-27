using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using OpenRend.Core;

namespace OpenRend.App;

public partial class MainWindow
{
    private void OpenSourceInTrimmer(MediaAsset media, FrameRange? range = null)
    {
        TrimmerDeck.LoadMedia(media, range);
        ShowTrimmerPane();
    }

    private void ProjectMediaMenuOpened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        var selected = (menu.PlacementTarget as Selector)?.SelectedItem as MediaItem;
        MenuItem Command(string id) => menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == id);
        var hasFile = selected is not null && File.Exists(selected.Asset.Path);
        var openParent = Command("OpenMediaParentInTrimmer");
        openParent.Visibility = selected?.Asset.SourceRange is not null ? Visibility.Visible : Visibility.Collapsed;
        openParent.IsEnabled = hasFile;
        var detect = Command("DetectScenesFromMedia");
        detect.Visibility = selected?.Asset is { HasVideo: true, IsGenerated: false } ? Visibility.Visible : Visibility.Collapsed;
        detect.IsEnabled = hasFile;
    }

    private async void OpenMediaParentInTrimmerClick(object sender, RoutedEventArgs e)
    {
        if (MediaMenuSelection(sender) is { } item)
            await GuardAsync(() => OpenParentInTrimmerAsync(item.Asset, selectRange: false));
    }

    private void DetectScenesFromMediaClick(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (MediaMenuSelection(sender) is not { Asset: { HasVideo: true, IsGenerated: false } source }) return;
        Pause();
        new SceneDetectionWindow(vm, source) { Owner = this }.ShowDialog();
    });

    private static MediaItem? MediaMenuSelection(object sender) =>
        sender is MenuItem menuItem &&
        ItemsControl.ItemsControlFromItemContainer(menuItem) is ContextMenu { PlacementTarget: Selector selector }
            ? selector.SelectedItem as MediaItem : null;

    private async Task OpenParentInTrimmerAsync(MediaAsset subclip, bool selectRange)
    {
        if (subclip.SourceRange is not { } stored) return;
        var path = subclip.Path;
        var project = vm.Project;
        var original = await vm.ImportAsync(path);
        if (!ReferenceEquals(project, vm.Project) ||
            !project.Media.Any(asset => asset.Id == subclip.Id && asset.Path == path && asset.SourceRange == stored) ||
            (selectRange && TrimmerDeck.ActiveMedia?.Id != subclip.Id)) return;

        FrameRange? selection = null;
        if (selectRange)
        {
            var rate = project.FrameRate;
            var length = Math.Max(1, rate.Frames(original.DurationSeconds));
            var mapped = stored.OnFrameGrid(rate);
            var start = Math.Clamp(mapped.StartFrame, 0, length - 1);
            var end = Math.Clamp(mapped.EndFrame, start + 1, length);
            selection = new(start, end - start);
        }
        OpenSourceInTrimmer(original, selection);
        vm.Status = selectRange ? $"Selected the subclip range in '{original.Name}'." : $"Opened '{original.Name}' in the Trimmer.";
    }

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
