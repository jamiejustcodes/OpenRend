using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OpenRend.Core;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro Slideshow Creator dialog window.
/// Replicates ScriptPortal.Vegas.Slideshow.SlideshowView with picture ordering,
/// rotation, per-slide/total duration, crossfade overlap, and Ken Burns pan/zoom.
/// </summary>
public sealed class SlideshowWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly Action onProjectChanged;
    private readonly ObservableCollection<SlideshowPicture> pictures = [];

    private readonly ListBox pictureList = new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
        BorderThickness = new Thickness(1),
        SelectionMode = SelectionMode.Extended,
        Margin = new Thickness(0, 8, 0, 8)
    };

    private readonly RadioButton rbAtCursor = new() { Content = "Start at playhead cursor position", IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
    private readonly RadioButton rbAtStart = new() { Content = "Start at beginning of timeline (00:00:00:00)", Margin = new Thickness(0, 0, 0, 6) };

    private readonly RadioButton rbPerSlide = new() { Content = "Duration per slide (seconds):", IsChecked = true, Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBox txtPerSlide = new() { Text = "5.0", Width = 70, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(20, 0, 0, 8) };

    private readonly RadioButton rbTotalDuration = new() { Content = "Total slideshow length (seconds):", Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBox txtTotalDuration = new() { Text = "60.0", Width = 70, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(20, 0, 0, 8) };

    private readonly TextBox txtOverlap = new() { Text = "1.0", Width = 70, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 8) };

    private readonly ComboBox effectCombo = new()
    {
        Width = 240,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 4, 0, 8)
    };

    private readonly TextBlock summaryText = new()
    {
        Foreground = Brushes.LightGray,
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 0, 0)
    };

    public SlideshowWindow(EditorViewModel viewModel, Action projectChanged, Window? owner = null)
    {
        vm = viewModel;
        onProjectChanged = projectChanged;
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)FindResource(typeof(Window));
        Title = "Create Slideshow";
        Width = 580;
        Height = 560;
        MinWidth = 480;
        MinHeight = 440;

        var panelBrush = (Brush)FindResource("PanelBrush");
        var textBrush = (Brush)FindResource("TextBrush");
        var borderBrush = (Brush)FindResource("BorderBrush");
        var headerBrush = (Brush)FindResource("HeaderBrush");

        var root = new Grid { Background = panelBrush, Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Tabs
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bottom bar

        var tabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };

        // 1. Pictures Tab
        var picturesTab = new TabItem { Header = "Pictures" };
        var picGrid = new Grid { Margin = new Thickness(4, 8, 4, 4) };
        picGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Toolbar
        picGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List
        picGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Info

        var picToolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var addBtn = CreateButton("Add Pictures…", AddPicturesClick);
        var removeBtn = CreateButton("Remove", RemovePicturesClick);
        var rotLeftBtn = CreateButton("↺ Rotate Left", RotateLeftClick);
        var rotRightBtn = CreateButton("↻ Rotate Right", RotateRightClick);
        var moveUpBtn = CreateButton("▲ Up", MoveUpClick);
        var moveDownBtn = CreateButton("▼ Down", MoveDownClick);
        var sortAlphaBtn = CreateButton("Sort A-Z", SortAlphaClick);
        var sortDateBtn = CreateButton("Sort Date", SortDateClick);

        picToolbar.Children.Add(addBtn);
        picToolbar.Children.Add(removeBtn);
        picToolbar.Children.Add(rotLeftBtn);
        picToolbar.Children.Add(rotRightBtn);
        picToolbar.Children.Add(moveUpBtn);
        picToolbar.Children.Add(moveDownBtn);
        picToolbar.Children.Add(sortAlphaBtn);
        picToolbar.Children.Add(sortDateBtn);

        Grid.SetRow(picToolbar, 0);
        picGrid.Children.Add(picToolbar);

        // Configure picture list item template
        pictureList.ItemsSource = pictures;
        pictureList.ItemTemplate = CreatePictureItemTemplate();
        Grid.SetRow(pictureList, 1);
        picGrid.Children.Add(pictureList);

        var hintText = new TextBlock
        {
            Text = "Drag or use toolbar buttons to arrange image sequence. Supported: JPG, PNG, WebP, BMP, TIFF.",
            Foreground = Brushes.DarkGray,
            FontSize = 10,
            Margin = new Thickness(2, 2, 0, 0)
        };
        Grid.SetRow(hintText, 2);
        picGrid.Children.Add(hintText);

        picturesTab.Content = picGrid;
        tabs.Items.Add(picturesTab);

        // 2. Settings Tab
        var settingsTab = new TabItem { Header = "Settings" };
        var settingsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(4, 8, 4, 4) };
        var settingsStack = new StackPanel { Margin = new Thickness(4) };

        // Placement GroupBox
        var grpPlacement = CreateGroupBox("Placement", headerBrush, borderBrush);
        var pStack = new StackPanel { Margin = new Thickness(8) };
        pStack.Children.Add(rbAtCursor);
        pStack.Children.Add(rbAtStart);
        grpPlacement.Content = pStack;
        settingsStack.Children.Add(grpPlacement);

        // Duration GroupBox
        var grpDuration = CreateGroupBox("Slide Timing", headerBrush, borderBrush);
        var dStack = new StackPanel { Margin = new Thickness(8) };
        dStack.Children.Add(rbPerSlide);
        dStack.Children.Add(txtPerSlide);
        dStack.Children.Add(rbTotalDuration);
        dStack.Children.Add(txtTotalDuration);
        grpDuration.Content = dStack;
        settingsStack.Children.Add(grpDuration);

        // Overlap GroupBox
        var grpOverlap = CreateGroupBox("Overlap & Transitions", headerBrush, borderBrush);
        var oStack = new StackPanel { Margin = new Thickness(8) };
        oStack.Children.Add(new TextBlock { Text = "Crossfade overlap duration (seconds):", Foreground = textBrush, Margin = new Thickness(0, 0, 0, 2) });
        oStack.Children.Add(txtOverlap);
        oStack.Children.Add(new TextBlock { Text = "Transition & Motion effect:", Foreground = textBrush, Margin = new Thickness(0, 4, 0, 2) });

        effectCombo.Items.Add("Crossfade (Default Dissolve)");
        effectCombo.Items.Add("Random Transitions");
        effectCombo.Items.Add("Pan and Zoom (Ken Burns)");
        effectCombo.Items.Add("Transitions + Pan and Zoom");
        effectCombo.Items.Add("None (Cut to cut)");
        effectCombo.SelectedIndex = 0;

        oStack.Children.Add(effectCombo);
        grpOverlap.Content = oStack;
        settingsStack.Children.Add(grpOverlap);

        settingsScroll.Content = settingsStack;
        settingsTab.Content = settingsScroll;
        tabs.Items.Add(settingsTab);

        Grid.SetRow(tabs, 0);
        root.Children.Add(tabs);

        // Bottom Action Bar
        var bottomBar = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 26,
            Margin = new Thickness(6, 0, 0, 0),
            IsCancel = true
        };
        DockPanel.SetDock(cancelBtn, Dock.Right);
        cancelBtn.Click += (_, _) => Close();

        var createBtn = new Button
        {
            Name = "CreateSlideshowButton",
            Content = "Create Slideshow",
            Width = 130,
            Height = 26,
            FontWeight = FontWeights.SemiBold
        };
        AutomationProperties.SetAutomationId(createBtn, "CreateSlideshowButton");
        DockPanel.SetDock(createBtn, Dock.Right);
        createBtn.Click += CreateSlideshowClick;

        bottomBar.Children.Add(cancelBtn);
        bottomBar.Children.Add(createBtn);
        bottomBar.Children.Add(summaryText);

        Grid.SetRow(bottomBar, 1);
        root.Children.Add(bottomBar);

        Content = root;

        // Wire change events for live summary updates
        txtPerSlide.TextChanged += (_, _) => UpdateSummary();
        txtTotalDuration.TextChanged += (_, _) => UpdateSummary();
        txtOverlap.TextChanged += (_, _) => UpdateSummary();
        rbPerSlide.Checked += (_, _) => UpdateSummary();
        rbTotalDuration.Checked += (_, _) => UpdateSummary();
        effectCombo.SelectionChanged += (_, _) => UpdateSummary();

        UpdateSummary();
    }

    /// <summary>
    /// Helper to programmatically add pictures (useful for tests and automation).
    /// </summary>
    public void AddPicture(string filePath)
    {
        if (File.Exists(filePath))
        {
            var pic = new SlideshowPicture(filePath);
            try
            {
                pic.PhotoDate = File.GetCreationTime(filePath);
            }
            catch { /* Ignore IO file time read */ }
            pictures.Add(pic);
            UpdateSummary();
        }
    }

    private static Button CreateButton(string text, RoutedEventHandler handler)
    {
        var btn = new Button
        {
            Content = text,
            Height = 24,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 6, 4)
        };
        btn.Click += handler;
        return btn;
    }

    private static GroupBox CreateGroupBox(string header, Brush headerBrush, Brush borderBrush) => new()
    {
        Header = header,
        BorderBrush = borderBrush,
        BorderThickness = new Thickness(1),
        Foreground = headerBrush,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 10)
    };

    private static DataTemplate CreatePictureItemTemplate()
    {
        var template = new DataTemplate(typeof(SlideshowPicture));
        var factory = new FrameworkElementFactory(typeof(DockPanel));
        factory.SetValue(DockPanel.MarginProperty, new Thickness(2, 4, 2, 4));

        // Rotation indicator
        var rotFactory = new FrameworkElementFactory(typeof(TextBlock));
        rotFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SlideshowPicture.OrientationDegrees))
        {
            Converter = new RotationTagConverter()
        });
        rotFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xFC, 0xBF, 0x45))); // Vegas Yellow
        rotFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        rotFactory.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0));
        rotFactory.SetValue(DockPanel.DockProperty, Dock.Right);
        factory.AppendChild(rotFactory);

        // Filename
        var nameFactory = new FrameworkElementFactory(typeof(TextBlock));
        nameFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(SlideshowPicture.Filename)));
        nameFactory.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        nameFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Normal);
        factory.AppendChild(nameFactory);

        template.VisualTree = factory;
        return template;
    }

    private void AddPicturesClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add Pictures for Slideshow",
            Filter = "Image Files (*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.tif)|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.tif|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog(this) == true)
        {
            foreach (var file in dlg.FileNames)
            {
                AddPicture(file);
            }
        }
    }

    private void RemovePicturesClick(object sender, RoutedEventArgs e)
    {
        var selected = pictureList.SelectedItems.Cast<SlideshowPicture>().ToList();
        foreach (var pic in selected)
        {
            pictures.Remove(pic);
        }
        UpdateSummary();
    }

    private void RotateLeftClick(object sender, RoutedEventArgs e)
    {
        foreach (var pic in pictureList.SelectedItems.Cast<SlideshowPicture>())
        {
            pic.RotateCounterClockwise();
        }
        pictureList.Items.Refresh();
    }

    private void RotateRightClick(object sender, RoutedEventArgs e)
    {
        foreach (var pic in pictureList.SelectedItems.Cast<SlideshowPicture>())
        {
            pic.RotateClockwise();
        }
        pictureList.Items.Refresh();
    }

    private void MoveUpClick(object sender, RoutedEventArgs e)
    {
        var selected = pictureList.SelectedItems.Cast<SlideshowPicture>().ToList();
        if (selected.Count == 0) return;

        for (int i = 0; i < pictures.Count; i++)
        {
            if (selected.Contains(pictures[i]) && i > 0 && !selected.Contains(pictures[i - 1]))
            {
                var item = pictures[i];
                pictures.RemoveAt(i);
                pictures.Insert(i - 1, item);
            }
        }
        pictureList.SelectedItems.Clear();
        foreach (var s in selected) pictureList.SelectedItems.Add(s);
    }

    private void MoveDownClick(object sender, RoutedEventArgs e)
    {
        var selected = pictureList.SelectedItems.Cast<SlideshowPicture>().ToList();
        if (selected.Count == 0) return;

        for (int i = pictures.Count - 1; i >= 0; i--)
        {
            if (selected.Contains(pictures[i]) && i < pictures.Count - 1 && !selected.Contains(pictures[i + 1]))
            {
                var item = pictures[i];
                pictures.RemoveAt(i);
                pictures.Insert(i + 1, item);
            }
        }
        pictureList.SelectedItems.Clear();
        foreach (var s in selected) pictureList.SelectedItems.Add(s);
    }

    private void SortAlphaClick(object sender, RoutedEventArgs e)
    {
        var sorted = pictures.OrderBy(p => p.Filename, StringComparer.OrdinalIgnoreCase).ToList();
        pictures.Clear();
        foreach (var p in sorted) pictures.Add(p);
    }

    private void SortDateClick(object sender, RoutedEventArgs e)
    {
        var sorted = pictures.OrderBy(p => p.PhotoDate ?? DateTime.MinValue).ToList();
        pictures.Clear();
        foreach (var p in sorted) pictures.Add(p);
    }

    private void UpdateSummary()
    {
        var count = pictures.Count;
        double perSlide = double.TryParse(txtPerSlide.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ps) ? Math.Max(0.5, ps) : 5.0;
        double overlap = double.TryParse(txtOverlap.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ov) ? Math.Max(0.0, ov) : 1.0;
        double totalLength = double.TryParse(txtTotalDuration.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tl) ? Math.Max(1.0, tl) : 60.0;

        if (rbTotalDuration.IsChecked == true && count > 0)
        {
            double net = totalLength - ((count - 1) * overlap);
            perSlide = Math.Max(0.5, net / count);
        }

        double totalSeconds = count == 0 ? 0.0 : (count == 1 ? perSlide : (count * perSlide) - ((count - 1) * overlap));

        summaryText.Text = $"{count} pictures • Slide: {perSlide:F1}s • Overlap: {overlap:F1}s • Total: {totalSeconds:F1}s";
    }

    private void CreateSlideshowClick(object sender, RoutedEventArgs e)
    {
        if (pictures.Count == 0)
        {
            MessageBox.Show(this, "Please add at least one picture to build the slideshow.", "Create Slideshow", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        double perSlide = double.TryParse(txtPerSlide.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ps) ? Math.Max(0.5, ps) : 5.0;
        double overlap = double.TryParse(txtOverlap.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ov) ? Math.Max(0.0, ov) : 1.0;
        double totalLength = double.TryParse(txtTotalDuration.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tl) ? Math.Max(1.0, tl) : 60.0;

        if (overlap >= perSlide)
        {
            overlap = Math.Max(0.0, perSlide - 0.5);
        }

        var effect = effectCombo.SelectedIndex switch
        {
            1 => SlideshowEffectKind.RandomTransitions,
            2 => SlideshowEffectKind.PanAndZoom,
            3 => SlideshowEffectKind.TransitionsAndPanZoom,
            4 => SlideshowEffectKind.None,
            _ => SlideshowEffectKind.Crossfade
        };

        var settings = new SlideshowSettings
        {
            StartMode = rbAtStart.IsChecked == true ? SlideshowStartMode.AtStart : SlideshowStartMode.AtCursor,
            CustomStartFrame = rbAtStart.IsChecked == true ? 0 : (int)Math.Max(0, vm.Position),
            LengthSetting = rbTotalDuration.IsChecked == true ? SlideshowLengthSetting.TotalLength : SlideshowLengthSetting.PerSlide,
            SecondsPerSlide = perSlide,
            SecondsOfOverlap = overlap,
            TotalSlideshowSeconds = totalLength,
            Effect = effect,
            Pictures = pictures.Select(p => p.DeepClone()).ToList()
        };

        try
        {
            vm.Editor.CreateSlideshow(settings);
            onProjectChanged();
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to create slideshow: {ex.Message}", "Create Slideshow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class RotationTagConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int deg && deg > 0)
            {
                return $"⟳ {deg}°";
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}