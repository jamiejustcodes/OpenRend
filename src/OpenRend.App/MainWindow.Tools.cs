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

    // Initialize 1:1 Vegas Pro UnifiedExplorer navigation, Video FX, and Media Generator catalog
    private void InitializeToolsViews()
    {
        InitializeExplorerView();
        InitializeVideoFxView();
        InitializeMediaGeneratorView();
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

    private readonly HashSet<VideoEffectKind> favoriteVideoFx = [];

    private record VideoFxPreset(string Name, Dictionary<string, double>? Parameters = null);

    private static string GetFxCategory(VideoEffectDefinition def) => def.Category switch
    {
        "AI" => "AI/ML",
        "Color" => "Color",
        "Blur" => "Blur",
        "Glow" => "Light",
        "Stylize" => "Creative",
        "Distort" => "Creative",
        "Keying" or "Detail" or "Render" => "Utility",
        _ => "Utility"
    };

    private static IReadOnlyList<VideoFxPreset> GetPresetsForEffect(VideoEffectKind kind) => kind switch
    {
        VideoEffectKind.BlackAndWhite =>
        [
            new("(Default)", new() { ["Amount"] = 1.0 }),
            new("50% Blend", new() { ["Amount"] = 0.5 }),
            new("High Contrast B&W", new() { ["Amount"] = 1.0 }),
            new("Subtle Faded", new() { ["Amount"] = 0.75 })
        ],
        VideoEffectKind.GaussianBlur =>
        [
            new("(Default)", new() { ["Amount"] = 0.4 }),
            new("Light Blur", new() { ["Amount"] = 0.15 }),
            new("Medium Blur", new() { ["Amount"] = 0.6 }),
            new("Heavy Blur", new() { ["Amount"] = 1.2 }),
            new("Extreme Dream", new() { ["Amount"] = 2.0 })
        ],
        VideoEffectKind.BrightnessContrast =>
        [
            new("(Default)", new() { ["Amount"] = 0.0, ["SecondaryAmount"] = 0.0 }),
            new("Brighter (+20%)", new() { ["Amount"] = 0.2, ["SecondaryAmount"] = 0.0 }),
            new("Darker (-20%)", new() { ["Amount"] = -0.2, ["SecondaryAmount"] = 0.0 }),
            new("High Contrast", new() { ["Amount"] = 0.05, ["SecondaryAmount"] = 0.4 }),
            new("Low Contrast", new() { ["Amount"] = 0.0, ["SecondaryAmount"] = -0.3 }),
            new("Punchy Colors", new() { ["Amount"] = 0.1, ["SecondaryAmount"] = 0.25 })
        ],
        VideoEffectKind.Sepia =>
        [
            new("(Default)", new() { ["Amount"] = 1.0 }),
            new("Subtle Warmth", new() { ["Amount"] = 0.35 }),
            new("Vintage 1890s", new() { ["Amount"] = 0.85 }),
            new("Deep Copper", new() { ["Amount"] = 1.0 })
        ],
        VideoEffectKind.Vignette =>
        [
            new("(Default)", new() { ["Amount"] = 0.65, ["Radius"] = 0.4, ["Softness"] = 0.5 }),
            new("Subtle Falloff", new() { ["Amount"] = 0.35, ["Radius"] = 0.6, ["Softness"] = 0.4 }),
            new("Dramatic Spotlight", new() { ["Amount"] = 0.9, ["Radius"] = 0.25, ["Softness"] = 0.6 }),
            new("Heavy Lens", new() { ["Amount"] = 0.8, ["Radius"] = 0.35, ["Softness"] = 0.3 })
        ],
        VideoEffectKind.Glow =>
        [
            new("(Default)", new() { ["Amount"] = 0.5, ["Radius"] = 0.8, ["Threshold"] = 0.6 }),
            new("Soft Dreamy", new() { ["Amount"] = 0.8, ["Radius"] = 1.2, ["Threshold"] = 0.4 }),
            new("Subtle Warmth", new() { ["Amount"] = 0.3, ["Radius"] = 0.5, ["Threshold"] = 0.7 }),
            new("Intense Bloom", new() { ["Amount"] = 1.5, ["Radius"] = 1.5, ["Threshold"] = 0.3 })
        ],
        VideoEffectKind.FilmGrain =>
        [
            new("(Default)", new() { ["Amount"] = 0.15, ["Size"] = 1 }),
            new("Fine 35mm Grain", new() { ["Amount"] = 0.08, ["Size"] = 1 }),
            new("Gritty 16mm Grain", new() { ["Amount"] = 0.25, ["Size"] = 2 }),
            new("Vintage Super8", new() { ["Amount"] = 0.4, ["Size"] = 3 })
        ],
        VideoEffectKind.Border =>
        [
            new("(Default)", new() { ["Amount"] = 0.05, ["Red"] = 1, ["Green"] = 1, ["Blue"] = 1 }),
            new("Thin White", new() { ["Amount"] = 0.015, ["Red"] = 1, ["Green"] = 1, ["Blue"] = 1 }),
            new("Thick Black", new() { ["Amount"] = 0.08, ["Red"] = 0, ["Green"] = 0, ["Blue"] = 0 }),
            new("Golden Frame", new() { ["Amount"] = 0.04, ["Red"] = 0.85, ["Green"] = 0.7, ["Blue"] = 0.2 })
        ],
        VideoEffectKind.Levels =>
        [
            new("(Default)", new() { ["Gamma"] = 1.0, ["InputBlack"] = 0, ["InputWhite"] = 1 }),
            new("Computer RGB to Studio RGB", new() { ["OutputBlack"] = 0.063, ["OutputWhite"] = 0.922 }),
            new("Studio RGB to Computer RGB", new() { ["InputBlack"] = 0.063, ["InputWhite"] = 0.922 }),
            new("Punch Midtones", new() { ["Gamma"] = 1.3 }),
            new("Crush Blacks", new() { ["InputBlack"] = 0.1, ["Gamma"] = 0.9 })
        ],
        VideoEffectKind.Invert =>
        [
            new("(Default)", new() { ["Amount"] = 1.0 }),
            new("Solarized 50%", new() { ["Amount"] = 0.5 })
        ],
        VideoEffectKind.Sharpen =>
        [
            new("(Default)", new() { ["Amount"] = 0.5 }),
            new("Subtle Sharpen", new() { ["Amount"] = 0.25 }),
            new("Medium Sharpen", new() { ["Amount"] = 0.8 }),
            new("Crisp Detail", new() { ["Amount"] = 1.5 })
        ],
        _ =>
        [
            new("(Default)"),
            new("Subtle Effect"),
            new("Intense Effect")
        ]
    };

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
            star.Checked += (_, _) => { favoriteVideoFx.Add(def.Kind); star.Content = "★"; };
            star.Unchecked += (_, _) => { favoriteVideoFx.Remove(def.Kind); star.Content = "☆"; };

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
                          (category == "Favorites" && favoriteVideoFx.Contains(def.Kind)) ||
                          string.Equals(GetFxCategory(def), category, StringComparison.OrdinalIgnoreCase);
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
            {
                // 1:1 Vegas Pro OFX capability string & description
                VideoFxInfoText.Text = $"Vegas {def.Name}: OFX, 32-bit floating point, GPU Accelerated, Grouping Vegas\\{GetFxCategory(def)}, Version 1.0 — Professional visual processing filter.";
            }

            var presets = GetPresetsForEffect(def.Kind);
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

                // Stylized preview background brush based on effect kind
                Brush previewBrush = def.Kind switch
                {
                    VideoEffectKind.BlackAndWhite => new LinearGradientBrush(Color.FromRgb(30, 30, 30), Color.FromRgb(220, 220, 220), new Point(0, 0), new Point(1, 1)),
                    VideoEffectKind.Sepia => new LinearGradientBrush(Color.FromRgb(60, 40, 20), Color.FromRgb(210, 180, 120), new Point(0, 0), new Point(1, 1)),
                    VideoEffectKind.Invert => new LinearGradientBrush(Color.FromRgb(240, 240, 240), Color.FromRgb(20, 20, 40), new Point(0, 0), new Point(1, 1)),
                    VideoEffectKind.GaussianBlur or VideoEffectKind.QuickBlur => new LinearGradientBrush(Color.FromRgb(40, 60, 80), Color.FromRgb(100, 130, 160), new Point(0, 0), new Point(1, 1)),
                    VideoEffectKind.Glow => new LinearGradientBrush(Color.FromRgb(40, 30, 60), Color.FromRgb(240, 200, 100), new Point(0, 0), new Point(1, 1)),
                    VideoEffectKind.ChromaKeyer => new SolidColorBrush(Color.FromRgb(30, 180, 40)),
                    _ => new LinearGradientBrush(Color.FromRgb(0x22, 0x33, 0x44), Color.FromRgb(0x44, 0x22, 0x33), new Point(0, 0), new Point(1, 1))
                };

                var border = new Border
                {
                    Width = 112,
                    Height = 68,
                    BorderBrush = (Brush)FindResource("ColorL"),
                    BorderThickness = new Thickness(1),
                    Background = previewBrush
                };
                var nameBlock = new TextBlock
                {
                    Text = preset.Name,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = (Brush)FindResource("TextColor"),
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis
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
                    ToolTip = $"Add {preset.Name} to selected events"
                };
                addBtn.Click += (_, _) => ApplyVideoFx(def.Kind, preset.Parameters);

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
        {
            var preset = item.Tag as VideoFxPreset;
            ApplyVideoFx(def.Kind, preset?.Parameters);
        }
    }

    private void ApplyVideoFx(VideoEffectKind kind, Dictionary<string, double>? parameters = null)
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
            var fx = VideoEffect.Create(kind);
            if (parameters != null)
            {
                foreach (var (k, v) in parameters)
                {
                    fx.Parameters[k] = v;
                }
            }
            effects.Add(fx);
            vm.Editor.SetVideoEffects(clip.Id, effects);
        }
        vm.Status = $"Added {kind} effect to {clips.Count} event(s).";
        RequestPreview();
    }
    private record MediaGeneratorDef(string Name, string Category, string[] Presets, string Description);

    private static readonly MediaGeneratorDef[] MediaGenerators =
    [
        new("Checkerboard", "Utility", ["(Default)", "Large Tiles", "Small Tiles", "Horizontal Blinds", "Vertical Blinds", "Grate", "Fence", "Ridges", "Bumps", "Plaid", "Split Screen", "Horizon"], "Vegas Checkerboard: OFX, 32-bit floating point, GPU Accelerated, Grouping: Vegas, Version 1.0"),
        new("Color Gradient", "Creative", ["(Default)", "Linear White to Black", "Soft Contrast", "Radial White to Black", "Linear Black to White", "Sunset", "Sunrise", "Blue Sky", "Sepia Tone"], "Vegas Color Gradient: Generates multi-color smooth gradients with customizable angle and falloff."),
        new("Credit Roll", "Titles and Text", ["(Default)", "Scrolling On Black", "Crawling On Black", "Subtitled Credits", "Dual Column Credits"], "Vegas Credit Roll: Automated scrolling credits generator with multiple text styles."),
        new("Noise Texture", "Creative", ["(Default)", "Fine Grain", "Coarse Grain", "Perlin Noise", "Simplex Fog", "Turbulence"], "Vegas Noise Texture: Procedural animated noise pattern generator."),
        new("Solid Color", "Utility", ["(Default)", "Black", "White", "Red", "Green", "Blue", "Yellow", "Cyan", "Magenta"], "Vegas Solid Color: Generates solid uniform color frames."),
        new("Test Pattern", "Utility", ["(Default)", "SMPTE Bars (NTSC)", "SMPTE Bars (PAL)", "Color Bars (NTSC)", "Color Bars (PAL)", "White Porches", "Yellow Porches", "Pluge and Porches", "Ramp", "Gradient"], "Vegas Test Pattern: OFX, 32-bit floating point, GPU Accelerated, Grouping: Vegas, Version 1.0"),
        new("Titles & Text", "Titles and Text", ["(Default)", "Title 01", "Subtitle", "Lower Third", "Banner Title", "Pop In", "Action Title", "Film Noir"], "Vegas Titles & Text: OFX high quality vector typography generator.")
    ];

    private void InitializeMediaGeneratorView()
    {
        if (MediaGenPlugIns is null) return;
        MediaGenPlugIns.Items.Clear();

        foreach (var def in MediaGenerators)
        {
            var item = new ListBoxItem
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                DataContext = def,
                Tag = def.Name
            };
            AutomationProperties.SetName(item, def.Name);

            var dock = new DockPanel();
            var icon = new ContentControl
            {
                Width = 20,
                Height = 20,
                ContentTemplate = (DataTemplate)FindResource("ic_media_generator"),
                Style = (Style)FindResource("BaseContentControl"),
                Margin = new Thickness(0, 0, 6, 0)
            };

            var label = new TextBlock
            {
                Text = def.Name,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextColor")
            };

            dock.Children.Add(icon);
            dock.Children.Add(label);
            item.Content = dock;
            MediaGenPlugIns.Items.Add(item);
        }

        if (MediaGenPlugIns.Items.Count > 0)
            MediaGenPlugIns.SelectedIndex = 0;
    }

    private void MediaGenCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MediaGenPlugIns is null) return;
        var selectedTab = MediaGenCategories?.SelectedItem as TabItem;
        var category = selectedTab?.Header?.ToString() ?? "All Plug-ins";

        foreach (var obj in MediaGenPlugIns.Items)
        {
            if (obj is not ListBoxItem item || item.DataContext is not MediaGeneratorDef def) continue;
            var visible = category == "All Plug-ins" ||
                          (category == "Favorites" && false) ||
                          string.Equals(def.Category, category, StringComparison.OrdinalIgnoreCase);
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void MediaGenSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (MediaGenPlugIns is null || MediaGenSearch is null) return;
        var query = MediaGenSearch.Text.Trim();

        foreach (var obj in MediaGenPlugIns.Items)
        {
            if (obj is not ListBoxItem item || item.DataContext is not MediaGeneratorDef def) continue;
            var visible = query.Length == 0 ||
                          def.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          def.Category.Contains(query, StringComparison.OrdinalIgnoreCase);
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void MediaGenPluginSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MediaGenPresets is null) return;
        MediaGenPresets.Items.Clear();

        if (MediaGenPlugIns.SelectedItem is ListBoxItem item && item.DataContext is MediaGeneratorDef def)
        {
            if (MediaGenInfoText is not null)
                MediaGenInfoText.Text = def.Description;

            foreach (var preset in def.Presets)
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
                    Background = GetPresetThumbnailBrush(def.Name, preset)
                };
                var nameBlock = new TextBlock
                {
                    Text = preset,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = (Brush)FindResource("TextColor"),
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis
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
                    ToolTip = "Add to timeline at playhead"
                };
                addBtn.Click += (_, _) => InsertMediaGenerator(def.Name, preset);

                grid.Children.Add(sp);
                grid.Children.Add(addBtn);
                card.Content = grid;
                MediaGenPresets.Items.Add(card);
            }
        }
    }

    private static Brush GetPresetThumbnailBrush(string plugin, string preset)
    {
        return plugin switch
        {
            "Solid Color" => preset switch
            {
                "White" => Brushes.White,
                "Red" => new SolidColorBrush(Color.FromRgb(0xEE, 0x33, 0x33)),
                "Green" => new SolidColorBrush(Color.FromRgb(0x33, 0xCC, 0x33)),
                "Blue" => new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0xEE)),
                "Yellow" => new SolidColorBrush(Color.FromRgb(0xFC, 0xBF, 0x45)),
                "Cyan" => new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xFF)),
                "Magenta" => new SolidColorBrush(Color.FromRgb(0xEE, 0x33, 0xCC)),
                _ => new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18))
            },
            "Checkerboard" => new DrawingBrush
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 16, 16),
                ViewportUnits = BrushMappingMode.Absolute,
                Drawing = new DrawingGroup
                {
                    Children =
                    {
                        new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))),
                        new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(new Rect(0, 0, 8, 8))),
                        new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(new Rect(8, 8, 8, 8)))
                    }
                }
            },
            "Color Gradient" => new LinearGradientBrush(
                Color.FromRgb(0xFF, 0x85, 0x7A),
                Color.FromRgb(0x46, 0x7D, 0x96),
                new Point(0, 0), new Point(1, 1)),
            "Test Pattern" => new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Colors.White, 0.0),
                    new(Colors.Yellow, 0.166),
                    new(Colors.Cyan, 0.333),
                    new(Colors.Green, 0.5),
                    new(Colors.Magenta, 0.666),
                    new(Colors.Red, 0.833),
                    new(Colors.Blue, 1.0)
                },
                new Point(0, 0), new Point(1, 0)),
            _ => new LinearGradientBrush(
                Color.FromRgb(0x22, 0x33, 0x44),
                Color.FromRgb(0x11, 0x18, 0x24),
                new Point(0, 0), new Point(1, 1))
        };
    }

    private void MediaGenPresetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MediaGenPresets.SelectedItem is ListBoxItem item && item.DataContext is MediaGeneratorDef def && item.Tag is string preset)
        {
            InsertMediaGenerator(def.Name, preset);
        }
    }

    private void InsertMediaGenerator(string plugin, string preset)
    {
        Pause();
        var kind = plugin switch
        {
            "Test Pattern" => MediaGeneratorKind.TestPattern,
            "Solid Color" => MediaGeneratorKind.SolidColor,
            "Checkerboard" => MediaGeneratorKind.Checkerboard,
            "Color Gradient" => MediaGeneratorKind.ColorGradient,
            "Noise Texture" => MediaGeneratorKind.NoiseTexture,
            "Credit Roll" => MediaGeneratorKind.CreditRoll,
            "Titles & Text" => MediaGeneratorKind.TitlesAndText,
            _ => MediaGeneratorKind.SolidColor
        };

        var videoTracks = vm.Project.Tracks.Where(t => t.Kind == TrackKind.Video).ToList();
        var targetTrack = videoTracks.ElementAtOrDefault(Timeline.ActiveTrackIndex)
            ?? videoTracks.FirstOrDefault();

        if (targetTrack is null)
        {
            targetTrack = vm.Editor.AddTrack(TrackKind.Video);
        }

        var startFrame = Math.Max(0, vm.Position);
        var duration = vm.Project.FrameRate.Frames(5.0);
        vm.Editor.AddGeneratedMediaClip(targetTrack.Id, startFrame, kind, preset, duration);

        vm.Status = $"Inserted {plugin} ({preset}) at {OpenRend.UI.TimelineView.FormatTimecode(startFrame, vm.Project.FrameRate)}.";
        RequestPreview();
        Timeline.InvalidateVisual();
    }
}
