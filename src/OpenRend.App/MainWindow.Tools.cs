using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;
using OpenRend.Project;

namespace OpenRend.App;

public partial class MainWindow
{
    private readonly Stack<string> explorerBackStack = new();
    private readonly Stack<string> explorerForwardStack = new();
    private string currentExplorerPath = string.Empty;

    // Initialize 1:1 Vegas Pro UnifiedExplorer navigation and Video FX catalog
    private void InitializeToolsViews()
    {
        InitializeExplorerView();
        InitializeVideoFxView();
    }

    private void InitializeExplorerView()
    {
        var defaultFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (!Directory.Exists(defaultFolder))
            defaultFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        NavigateExplorer(defaultFolder, recordHistory: false);
    }

    public void NavigateExplorer(string folderPath, bool recordHistory = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

        if (recordHistory && !string.IsNullOrEmpty(currentExplorerPath) && currentExplorerPath != folderPath)
        {
            explorerBackStack.Push(currentExplorerPath);
            explorerForwardStack.Clear();
        }

        currentExplorerPath = folderPath;
        if (ExplorerPathBox is not null) ExplorerPathBox.Text = folderPath;
        if (FolderLabel is not null) FolderLabel.Text = folderPath;

        try
        {
            var dir = new DirectoryInfo(folderPath);
            var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wav", ".mp3", ".flac", ".m4a", ".mxf", ".png", ".jpg", ".jpeg"
            };

            var files = dir.EnumerateFiles()
                .Where(f => supported.Contains(f.Extension))
                .OrderBy(f => f.Name)
                .ToList();

            if (ExplorerList is not null)
                ExplorerList.ItemsSource = files;
        }
        catch (Exception ex)
        {
            vm.Status = $"Cannot access folder: {ex.Message}";
        }
    }

    private void ExplorerBackClick(object sender, RoutedEventArgs e)
    {
        if (explorerBackStack.Count > 0)
        {
            var prev = explorerBackStack.Pop();
            explorerForwardStack.Push(currentExplorerPath);
            NavigateExplorer(prev, recordHistory: false);
        }
    }

    private void ExplorerForwardClick(object sender, RoutedEventArgs e)
    {
        if (explorerForwardStack.Count > 0)
        {
            var next = explorerForwardStack.Pop();
            explorerBackStack.Push(currentExplorerPath);
            NavigateExplorer(next, recordHistory: false);
        }
    }

    private void ExplorerUpClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(currentExplorerPath)) return;
        var parent = Directory.GetParent(currentExplorerPath);
        if (parent is not null && Directory.Exists(parent.FullName))
            NavigateExplorer(parent.FullName);
    }

    private void ExplorerRefreshClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(currentExplorerPath))
            NavigateExplorer(currentExplorerPath, recordHistory: false);
    }

    private void ExplorerPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ExplorerPathBox is not null)
        {
            var path = ExplorerPathBox.Text.Trim();
            if (Directory.Exists(path))
                NavigateExplorer(path);
        }
    }

    private void ExplorerNavTreeChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is string tag)
        {
            string targetPath = tag switch
            {
                "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                "Videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                _ => tag
            };

            if (Directory.Exists(targetPath))
                NavigateExplorer(targetPath);
        }
    }

    private void ExplorerViewModeClick(object sender, RoutedEventArgs e)
    {
        // View modes: Large Icons, Details, List
    }

    private async void ExplorerItemAddClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is FileInfo file)
        {
            await ImportPathsAsync([file.FullName]);
            ToolsTabs.SelectedIndex = 1;
        }
    }

    private void ExplorerItemPreviewClick(object sender, RoutedEventArgs e)
    {
        // Preview clip playback in video preview
    }

    // Video FX catalog and preset management
    private void InitializeVideoFxView()
    {
        if (VideoFxPlugIns is null) return;
        VideoFxPlugIns.Items.Clear();

        foreach (var def in VideoEffectCatalog.Definitions.OrderBy(d => d.Name))
        {
            var item = new ListBoxItem
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                DataContext = def,
                Tag = def.Kind
            };
            AutomationProperties.SetName(item, def.Name);

            var dock = new DockPanel();
            var star = new ToggleButton
            {
                Content = "☆",
                Width = 22,
                Height = 20,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE3, 0xC4, 0x54)),
                FocusVisualStyle = (Style)FindResource("KeyboardFocus"),
                ToolTip = $"Favorite {def.Name}"
            };
            AutomationProperties.SetName(star, $"Favorite {def.Name}");

            var icon = new ContentControl
            {
                Width = 20,
                Height = 20,
                ContentTemplate = (DataTemplate)FindResource("ic_fx"),
                Style = (Style)FindResource("BaseContentControl"),
                Margin = new Thickness(0, 0, 6, 0)
            };

            var label = new TextBlock
            {
                Text = def.Name,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextColor")
            };

            dock.Children.Add(star);
            dock.Children.Add(icon);
            dock.Children.Add(label);
            item.Content = dock;
            VideoFxPlugIns.Items.Add(item);
        }

        if (VideoFxPlugIns.Items.Count > 0)
            VideoFxPlugIns.SelectedIndex = 0;
    }

    private void VideoFxCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VideoFxPlugIns is null) return;
        var selectedTab = VideoFxCategories?.SelectedItem as TabItem;
        var category = selectedTab?.Header?.ToString() ?? "All Plug-ins";

        foreach (var obj in VideoFxPlugIns.Items)
        {
            if (obj is not ListBoxItem item || item.DataContext is not VideoEffectDefinition def) continue;
            var visible = category == "All Plug-ins" ||
                          (category == "Favorites" && false) ||
                          string.Equals(def.Category, category, StringComparison.OrdinalIgnoreCase);
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void VideoFxSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (VideoFxPlugIns is null || VideoFxSearch is null) return;
        var query = VideoFxSearch.Text.Trim();

        foreach (var obj in VideoFxPlugIns.Items)
        {
            if (obj is not ListBoxItem item || item.DataContext is not VideoEffectDefinition def) continue;
            var visible = query.Length == 0 ||
                          def.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          def.Category.Contains(query, StringComparison.OrdinalIgnoreCase);
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void VideoFxPluginSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VideoFxPresets is null) return;
        VideoFxPresets.Items.Clear();

        if (VideoFxPlugIns.SelectedItem is ListBoxItem item && item.DataContext is VideoEffectDefinition def)
        {
            if (VideoFxInfoText is not null)
                VideoFxInfoText.Text = $"Applies {def.Name} ({def.Category}) to selected video events.";

            var presets = new[] { "Default", "Reset to None" };
            foreach (var preset in presets)
            {
                var card = new ListBoxItem
                {
                    Width = 124,
                    Height = 104,
                    Padding = new Thickness(4),
                    Margin = new Thickness(3),
                    DataContext = def,
                    Tag = preset
                };

                var grid = new Grid();
                var sp = new StackPanel();
                var border = new Border
                {
                    Width = 112,
                    Height = 68,
                    BorderBrush = (Brush)FindResource("ColorL"),
                    BorderThickness = new Thickness(1),
                    Background = new LinearGradientBrush(
                        Color.FromRgb(0x22, 0x33, 0x44),
                        Color.FromRgb(0x44, 0x22, 0x33),
                        new Point(0, 0), new Point(1, 1))
                };
                var nameBlock = new TextBlock
                {
                    Text = preset,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = (Brush)FindResource("TextColor"),
                    FontSize = 11
                };
                sp.Children.Add(border);
                sp.Children.Add(nameBlock);

                var addBtn = new Button
                {
                    Style = (Style)FindResource("IconButton"),
                    ContentTemplate = (DataTemplate)FindResource("ic_add"),
                    Width = 22,
                    Height = 22,
                    Background = (Brush)FindResource("ColorM"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(2),
                    ToolTip = "Add to selected events"
                };
                addBtn.Click += (_, _) => ApplyVideoFx(def.Kind);

                grid.Children.Add(sp);
                grid.Children.Add(addBtn);
                card.Content = grid;
                VideoFxPresets.Items.Add(card);
            }
        }
    }

    private void VideoFxPresetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VideoFxPresets.SelectedItem is ListBoxItem item && item.DataContext is VideoEffectDefinition def)
            ApplyVideoFx(def.Kind);
    }

    private void ApplyVideoFx(VideoEffectKind kind)
    {
        var clips = vm.Project.Tracks.SelectMany(t => t.Clips).Where(c => vm.Selection.Contains(c.Id)).ToList();
        if (clips.Count == 0 && SelectedClip is { } sel) clips.Add(sel);
        if (clips.Count == 0)
        {
            vm.Status = "Select a video event to apply the effect.";
            return;
        }

        foreach (var clip in clips)
        {
            var effects = clip.VideoEffects.ToList();
            effects.Add(VideoEffect.Create(kind));
            vm.Editor.SetVideoEffects(clip.Id, effects);
        }
        vm.Status = $"Added {kind} effect to {clips.Count} event(s).";
        RequestPreview();
    }
}
