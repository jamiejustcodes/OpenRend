using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenRend.Core;

namespace OpenRend.App;

// A compact catalog, category tree and draft chain follow the editor's native tool layout.
public sealed class VideoEffectChooserWindow : Window
{
    private readonly VideoEffectChooserModel draft;
    private readonly ListBox chain = new(), catalog = new();
    private readonly TreeView categories = new();
    private readonly TextBox search = new();
    private readonly TextBlock emptyChain = new(), status = new();
    private readonly Button add, replace, remove;
    private readonly string[] categoryNames;
    private string? category;
    private bool refreshing;

    public IReadOnlyList<VideoEffect>? Result { get; private set; }

    public VideoEffectChooserWindow(IReadOnlyList<VideoEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        draft = new(effects);
        categoryNames = VideoEffectCatalog.Definitions.Select(definition => definition.Category).Distinct().Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        Style = (Style)FindResource(typeof(Window));
        Title = "Plug-In Chooser - Video Event FX";
        Width = 882; Height = 610; MinWidth = 650; MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetAutomationId(this, "FxChooser");

        var root = new Grid { Background = Brush("PanelBrush") };
        root.ColumnDefinitions.Add(new() { Width = new GridLength(152) });
        root.ColumnDefinitions.Add(new());
        root.ColumnDefinitions.Add(new() { Width = new GridLength(82) });
        root.RowDefinitions.Add(new() { Height = new GridLength(34) });
        root.RowDefinitions.Add(new());
        root.RowDefinitions.Add(new() { Height = new GridLength(23) });

        var chainHost = new Grid { Background = Brush("SurfaceBrush"), Margin = new Thickness(2, 1, 0, 1) };
        Grid.SetColumnSpan(chainHost, 2); root.Children.Add(chainHost);
        chain.Background = Brushes.Transparent;
        chain.ItemsPanel = PanelTemplate(Orientation.Horizontal, false);
        chain.Padding = new Thickness(2, 0, 2, 0);
        ScrollViewer.SetHorizontalScrollBarVisibility(chain, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(chain, ScrollBarVisibility.Disabled);
        ScrollViewer.SetCanContentScroll(chain, false);
        AutomationProperties.SetAutomationId(chain, "FxChooserChain");
        AutomationProperties.SetName(chain, "Pending video effect chain");
        chain.SelectionChanged += (_, _) => UpdateActions();
        chainHost.Children.Add(chain);
        emptyChain.Text = "No plug-ins in chain";
        emptyChain.HorizontalAlignment = HorizontalAlignment.Center;
        emptyChain.IsHitTestVisible = false; chainHost.Children.Add(emptyChain);

        var categoryHost = new Border { Background = Brush("SurfaceBrush"), BorderBrush = Brush("DividerBrush"), BorderThickness = new Thickness(0, 0, 2, 0), Margin = new Thickness(2, 0, 0, 0), Child = categories };
        Grid.SetRow(categoryHost, 1); root.Children.Add(categoryHost);
        categories.Background = Brushes.Transparent; categories.BorderThickness = new Thickness(0);
        AutomationProperties.SetAutomationId(categories, "FxChooserCategories");
        AutomationProperties.SetName(categories, "Video effect categories");
        var all = CategoryItem("FX", null); all.IsExpanded = true;
        foreach (var name in categoryNames) all.Items.Add(CategoryItem(name, name));
        categories.Items.Add(all);
        categories.SelectedItemChanged += (_, _) =>
        {
            if (categories.SelectedItem is not TreeViewItem item) return;
            category = item.Tag as string;
            RefreshCatalog();
        };

        var center = new Grid(); center.RowDefinitions.Add(new() { Height = new GridLength(27) }); center.RowDefinitions.Add(new());
        Grid.SetColumn(center, 1); Grid.SetRow(center, 1); root.Children.Add(center);
        var searchRow = new DockPanel { Margin = new Thickness(5, 2, 5, 2) };
        var searchLabel = new TextBlock { Text = "Search:", Margin = new Thickness(0, 0, 6, 0) }; searchRow.Children.Add(searchLabel);
        search.MinHeight = 21; search.Padding = new Thickness(4, 1, 4, 1);
        AutomationProperties.SetAutomationId(search, "FxChooserSearch"); AutomationProperties.SetName(search, "Search video effects");
        search.TextChanged += (_, _) => RefreshCatalog(); searchRow.Children.Add(search); center.Children.Add(searchRow);
        catalog.Background = Brush("HeaderBrush");
        catalog.ItemsPanel = PanelTemplate(Orientation.Vertical, true);
        ScrollViewer.SetCanContentScroll(catalog, false);
        ScrollViewer.SetHorizontalScrollBarVisibility(catalog, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(catalog, ScrollBarVisibility.Disabled);
        AutomationProperties.SetAutomationId(catalog, "FxChooserCatalog"); AutomationProperties.SetName(catalog, "Available video effects");
        catalog.SelectionChanged += (_, _) => UpdateActions();
        catalog.MouseDoubleClick += (_, e) =>
        {
            if (catalog.SelectedItem is not ListBoxItem item || !item.IsMouseOver) return;
            ActivateCatalogSelection(); e.Handled = true;
        };
        Grid.SetRow(catalog, 1); center.Children.Add(catalog);

        var actions = new StackPanel { Margin = new Thickness(3, 1, 2, 0) };
        Grid.SetColumn(actions, 2); Grid.SetRowSpan(actions, 3); root.Children.Add(actions);
        var accept = MakeButton("OK", "Accept video effect chain", "FxChooserAccept", () =>
        {
            Result = draft.Snapshot(); DialogResult = true;
        });
        accept.IsDefault = true; actions.Children.Add(accept);
        var cancel = MakeButton("Cancel", "Cancel video effect chain changes", "FxChooserCancel", () => DialogResult = false);
        cancel.IsCancel = true; actions.Children.Add(cancel);
        add = MakeButton("Add", "Add selected video effect", "FxChooserAdd", AddSelected); add.Margin = new Thickness(0, 9, 0, 3); actions.Children.Add(add);
        replace = MakeButton("Replace", "Replace selected chain effect", "FxChooserReplace", ReplaceSelected); actions.Children.Add(replace);
        remove = MakeButton("Remove", "Remove selected chain effect", "FxChooserRemove", RemoveSelected); actions.Children.Add(remove);

        status.Foreground = Brush("MutedBrush"); status.Margin = new Thickness(6, 0, 4, 0); status.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(status, 2); Grid.SetColumnSpan(status, 2); root.Children.Add(status);
        Content = root;
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += (_, _) =>
        {
            var dark = 1; DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
        Loaded += (_, _) => search.Focus();
        all.IsSelected = true;
        RefreshChain(draft.Effects.Count > 0 ? 0 : -1);
        RefreshCatalog();
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    private static ItemsPanelTemplate PanelTemplate(Orientation orientation, bool wrap)
    {
        var factory = new FrameworkElementFactory(wrap ? typeof(WrapPanel) : typeof(StackPanel));
        factory.SetValue(wrap ? WrapPanel.OrientationProperty : StackPanel.OrientationProperty, orientation);
        return new ItemsPanelTemplate(factory);
    }

    private static Button MakeButton(string text, string name, string id, Action action)
    {
        var button = new Button { Content = text, MinHeight = 23, Padding = new Thickness(4, 2, 4, 2), Margin = new Thickness(0, 0, 0, 3), HorizontalContentAlignment = HorizontalAlignment.Center, ToolTip = name };
        AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action(); return button;
    }

    private static FrameworkElement EntryGraphic(string name, bool folder)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new Grid { Width = 14, Height = 14, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        if (folder)
        {
            icon.Children.Add(new Rectangle { Width = 7, Height = 3, Fill = new SolidColorBrush(Color.FromRgb(245, 194, 80)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) });
            icon.Children.Add(new Rectangle { Height = 10, Fill = new SolidColorBrush(Color.FromRgb(239, 186, 69)), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) });
        }
        else
        {
            icon.Background = new SolidColorBrush(Color.FromRgb(111, 139, 204));
            icon.Children.Add(new TextBlock { Text = "fx", Foreground = new SolidColorBrush(Color.FromRgb(20, 30, 70)), FontFamily = new FontFamily("Segoe UI"), FontStyle = FontStyles.Italic, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }
        row.Children.Add(icon); row.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis }); return row;
    }

    private static TreeViewItem CategoryItem(string name, string? value)
    {
        var item = new TreeViewItem { Header = EntryGraphic(name, true), Tag = value, MinHeight = 19 };
        AutomationProperties.SetName(item, name); AutomationProperties.SetAutomationId(item, "FxChooserCategory" + (value ?? "All").Replace(" ", ""));
        return item;
    }

    private void RefreshCatalog()
    {
        if (catalog is null) return;
        var selected = (catalog.SelectedItem as ListBoxItem)?.Tag as VideoEffectDefinition;
        refreshing = true;
        catalog.Items.Clear();
        var query = search.Text.Trim();
        if (category is null && query.Length == 0)
            foreach (var name in categoryNames) catalog.Items.Add(CatalogItem(name, name, true));
        foreach (var definition in VideoEffectCatalog.Definitions.Where(definition =>
                     (category is null || definition.Category == category) &&
                     (query.Length == 0 || definition.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                 .OrderBy(definition => definition.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = CatalogItem(definition.Name, definition, false); catalog.Items.Add(item);
            if (selected?.Kind == definition.Kind) catalog.SelectedItem = item;
        }
        refreshing = false;
        UpdateActions();
    }

    private static ListBoxItem CatalogItem(string name, object value, bool folder)
    {
        var item = new ListBoxItem { Content = EntryGraphic(name, folder), Tag = value, Width = 178, Height = 19, Padding = new Thickness(1, 0, 2, 0), ToolTip = name };
        AutomationProperties.SetName(item, name);
        return item;
    }

    private VideoEffectDefinition? SelectedDefinition => (catalog.SelectedItem as ListBoxItem)?.Tag as VideoEffectDefinition;

    private void RefreshChain(int selectedIndex)
    {
        refreshing = true;
        chain.Items.Clear();
        foreach (var effect in draft.Effects)
        {
            var name = VideoEffectCatalog.Definitions.Single(definition => definition.Kind == effect.Kind).Name;
            var item = new ListBoxItem { Content = name, Padding = new Thickness(8, 0, 8, 0), Height = 20, VerticalAlignment = VerticalAlignment.Center, ToolTip = name + (effect.IsEnabled ? "" : " (bypassed)") };
            AutomationProperties.SetName(item, name); chain.Items.Add(item);
        }
        chain.SelectedIndex = selectedIndex;
        if (chain.SelectedItem is { } selected) chain.ScrollIntoView(selected);
        emptyChain.Visibility = draft.Effects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        refreshing = false; UpdateActions();
    }

    private void UpdateActions()
    {
        if (refreshing || add is null) return;
        var definition = SelectedDefinition;
        add.IsEnabled = definition is not null;
        replace.IsEnabled = definition is not null && chain.SelectedIndex >= 0;
        remove.IsEnabled = chain.SelectedIndex >= 0;
        status.Text = catalog.Items.Count == 0 ? "No effects match this search." : definition is null ? $"{draft.Effects.Count} effect{(draft.Effects.Count == 1 ? "" : "s")} in chain" : definition.Name + " · " + definition.Category;
    }

    private void ActivateCatalogSelection()
    {
        if ((catalog.SelectedItem as ListBoxItem)?.Tag is string folder)
        {
            var root = (TreeViewItem)categories.Items[0];
            foreach (TreeViewItem item in root.Items)
                if (item.Tag as string == folder) { item.IsSelected = true; item.Focus(); break; }
        }
        else AddSelected();
    }

    private void AddSelected()
    {
        if (SelectedDefinition is not { } definition) return;
        RefreshChain(draft.Add(definition.Kind));
    }

    private void ReplaceSelected()
    {
        if (SelectedDefinition is not { } definition || chain.SelectedIndex < 0) return;
        var index = chain.SelectedIndex; draft.Replace(index, definition.Kind); RefreshChain(index);
    }

    private void RemoveSelected()
    {
        var index = chain.SelectedIndex;
        if (index < 0) return;
        draft.Remove(index); RefreshChain(Math.Min(index, draft.Effects.Count - 1));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && catalog.IsKeyboardFocusWithin)
        { ActivateCatalogSelection(); e.Handled = true; }
        else if (e.Key == Key.Delete && chain.IsKeyboardFocusWithin)
        { RemoveSelected(); e.Handled = true; }
        else if (e.Key == Key.Enter && search.IsKeyboardFocusWithin)
        {
            if (catalog.Items.Count > 0) { catalog.SelectedIndex = 0; catalog.Focus(); ((ListBoxItem)catalog.SelectedItem).Focus(); }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
