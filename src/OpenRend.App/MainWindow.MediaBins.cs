using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

public partial class MainWindow
{
    private Guid? activeBinId;

    private void InitializeMediaBins()
    {
        MediaTree.ContextMenu = CreateTreeContextMenu();
        HookMediaListContextMenus();
        RebuildMediaTree();
        vm.Changed += () =>
        {
            RebuildMediaTree();
            UpdateMediaFilter();
        };
    }

    private void HookMediaListContextMenus()
    {
        if (MediaList?.ContextMenu is ContextMenu m1) m1.Opened += (_, _) => UpdateMediaContextMenu(m1);
        if (MediaDetailsList?.ContextMenu is ContextMenu m2) m2.Opened += (_, _) => UpdateMediaContextMenu(m2);
        if (MediaCompactList?.ContextMenu is ContextMenu m3) m3.Opened += (_, _) => UpdateMediaContextMenu(m3);
    }

    private void UpdateMediaContextMenu(ContextMenu menu)
    {
        // Remove dynamic items tagged with "DynamicBinItem"
        for (int i = menu.Items.Count - 1; i >= 0; i--)
        {
            if (menu.Items[i] is FrameworkElement fe && Equals(fe.Tag, "DynamicBinItem"))
            {
                menu.Items.RemoveAt(i);
            }
        }

        if (SelectedLibraryItem is not MediaItem selectedMedia) return;

        menu.Items.Add(new Separator { Tag = "DynamicBinItem" });

        // Add to Bin submenu
        if (vm.Project.MediaBins.Count > 0)
        {
            var addToBinItem = new MenuItem { Header = "Add to Bin", Tag = "DynamicBinItem" };
            foreach (var bin in vm.Project.MediaBins.OrderBy(b => b.Name))
            {
                var mi = new MenuItem { Header = bin.Name, IsCheckable = true, IsChecked = bin.MediaIds.Contains(selectedMedia.Asset.Id) };
                var targetBinId = bin.Id;
                mi.Click += (_, _) =>
                {
                    if (bin.MediaIds.Contains(selectedMedia.Asset.Id))
                        vm.RemoveMediaFromBin(targetBinId, selectedMedia.Asset.Id);
                    else
                        vm.AddMediaToBin(targetBinId, selectedMedia.Asset.Id);
                    RebuildMediaTree();
                    UpdateMediaFilter();
                };
                addToBinItem.Items.Add(mi);
            }
            menu.Items.Add(addToBinItem);
        }

        if (activeBinId.HasValue)
        {
            var removeBinItem = new MenuItem { Header = "Remove from Bin", Tag = "DynamicBinItem" };
            var currentBin = activeBinId.Value;
            removeBinItem.Click += (_, _) =>
            {
                vm.RemoveMediaFromBin(currentBin, selectedMedia.Asset.Id);
                RebuildMediaTree();
                UpdateMediaFilter();
            };
            menu.Items.Add(removeBinItem);
        }

        var tagsItem = new MenuItem { Header = "Media Tags…", Tag = "DynamicBinItem" };
        tagsItem.Click += MediaTagsClick;
        menu.Items.Add(tagsItem);
    }

    private ContextMenu CreateTreeContextMenu()
    {
        var menu = new ContextMenu();
        var newBinItem = new MenuItem { Header = "Create New &Bin" };
        newBinItem.Click += (_, _) => PromptCreateBin(activeBinId);
        menu.Items.Add(newBinItem);

        var renameItem = new MenuItem { Header = "&Rename Bin…" };
        renameItem.Click += (_, _) => PromptRenameBin(activeBinId);
        menu.Items.Add(renameItem);

        var deleteItem = new MenuItem { Header = "&Delete Bin" };
        deleteItem.Click += (_, _) => PromptDeleteBin(activeBinId);
        menu.Items.Add(deleteItem);

        menu.Opened += (_, _) =>
        {
            var isCustom = activeBinId.HasValue;
            renameItem.IsEnabled = isCustom;
            deleteItem.IsEnabled = isCustom;
        };

        return menu;
    }

    public void RebuildMediaTree()
    {
        if (MediaTree == null) return;

        var selectedTag = (MediaTree.SelectedItem as TreeViewItem)?.Tag;

        MediaTree.Items.Clear();

        var totalMedia = vm.Project.Media.Count;
        var videoCount = vm.Project.Media.Count(m => m.HasVideo);
        var audioCount = vm.Project.Media.Count(m => m.HasAudio && !m.HasVideo);
        var imageCount = vm.Project.Media.Count(m => !m.HasVideo && !m.HasAudio || IsImageMedia(m));
        var missingCount = vm.Project.Media.Count(m => !File.Exists(m.Path));

        var root = new TreeViewItem
        {
            Header = $"All Media ({totalMedia})",
            Tag = "All",
            IsExpanded = true
        };

        var videoItem = new TreeViewItem { Header = $"Video ({videoCount})", Tag = "Video" };
        var audioItem = new TreeViewItem { Header = $"Audio ({audioCount})", Tag = "Audio" };
        var imageItem = new TreeViewItem { Header = $"Images ({imageCount})", Tag = "Images" };
        var missingItem = new TreeViewItem { Header = $"Missing Media ({missingCount})", Tag = "Missing" };

        root.Items.Add(videoItem);
        root.Items.Add(audioItem);
        root.Items.Add(imageItem);
        root.Items.Add(missingItem);

        // Build custom bins hierarchy
        var binLookup = vm.Project.MediaBins.ToLookup(b => b.ParentBinId);
        void AddChildBins(TreeViewItem parentNode, Guid? parentId)
        {
            foreach (var bin in binLookup[parentId].OrderBy(b => b.Name))
            {
                var binCount = bin.MediaIds.Count(id => vm.Project.Media.Any(m => m.Id == id));
                var binNode = new TreeViewItem
                {
                    Header = $"{bin.Name} ({binCount})",
                    Tag = bin.Id,
                    IsExpanded = true,
                    AllowDrop = true
                };

                binNode.DragOver += (s, e) =>
                {
                    if (e.Data.GetDataPresent("OpenRend.Media"))
                    {
                        e.Effects = DragDropEffects.Copy;
                        e.Handled = true;
                    }
                };

                binNode.Drop += (s, e) =>
                {
                    if (e.Data.GetDataPresent("OpenRend.Media"))
                    {
                        var mediaId = (Guid)e.Data.GetData("OpenRend.Media");
                        vm.AddMediaToBin(bin.Id, mediaId);
                        RebuildMediaTree();
                        UpdateMediaFilter();
                        e.Handled = true;
                    }
                };

                AddChildBins(binNode, bin.Id);
                parentNode.Items.Add(binNode);
            }
        }

        AddChildBins(root, null);
        MediaTree.Items.Add(root);

        // Restore selection
        bool restored = false;
        void TrySelect(ItemCollection items)
        {
            foreach (TreeViewItem item in items)
            {
                if (Equals(item.Tag, selectedTag))
                {
                    item.IsSelected = true;
                    restored = true;
                    return;
                }
                TrySelect(item.Items);
                if (restored) return;
            }
        }

        if (selectedTag != null) TrySelect(MediaTree.Items);
        if (!restored) root.IsSelected = true;
    }

    private static bool IsImageMedia(MediaAsset asset)
    {
        if (asset.IsGenerated) return true;
        var ext = Path.GetExtension(asset.Path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp";
    }

    private void PromptCreateBin(Guid? parentId)
    {
        var dlg = new BinNameDialog("New Media Bin", "New Bin") { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var bin = vm.CreateMediaBin(dlg.BinName, parentId);
            activeBinId = bin.Id;
            mediaFilter = "Bin";
            RebuildMediaTree();
            UpdateMediaFilter();
        }
    }

    private void PromptRenameBin(Guid? binId)
    {
        if (!binId.HasValue) return;
        var current = vm.Project.MediaBins.FirstOrDefault(b => b.Id == binId.Value);
        if (current == null) return;

        var dlg = new BinNameDialog("Rename Media Bin", current.Name) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            vm.RenameMediaBin(binId.Value, dlg.BinName);
            RebuildMediaTree();
            UpdateMediaFilter();
        }
    }

    private void PromptDeleteBin(Guid? binId)
    {
        if (!binId.HasValue) return;
        var current = vm.Project.MediaBins.FirstOrDefault(b => b.Id == binId.Value);
        if (current == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the bin \"{current.Name}\"?\n(Media items will remain in the project.)",
            "Delete Media Bin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            vm.DeleteMediaBin(binId.Value);
            activeBinId = null;
            mediaFilter = "All";
            RebuildMediaTree();
            UpdateMediaFilter();
        }
    }

    private void CreateBinToolbarClick(object sender, RoutedEventArgs e)
    {
        PromptCreateBin(activeBinId);
    }

    private void DeleteBinToolbarClick(object sender, RoutedEventArgs e)
    {
        PromptDeleteBin(activeBinId);
    }

    private void MediaTagsClick(object sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItem is not MediaItem media) return;
        var dlg = new MediaTagsDialog(media.Name, media.Asset.Tags) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            vm.SetMediaTags(media.Asset.Id, dlg.Tags);
            media.Rebind(media.Asset, media.UseCount);
            UpdateMediaFilter();
        }
    }
}
