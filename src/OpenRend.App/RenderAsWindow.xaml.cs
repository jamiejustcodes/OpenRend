using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.UI;

namespace OpenRend.App;

public partial class RenderAsWindow : Window
{
    private readonly ProjectDocument project;
    private readonly FrameRange? loopRegion;
    private readonly HashSet<string> favorites;
    private string selectedId;
    private bool updatingFavorite;
    public string OutputPath { get; private set; } = "";
    public ExportOptions Options { get; private set; } = new();
    public RenderPreferences Preferences { get; private set; }

    public RenderAsWindow(ProjectDocument project, FrameRange? loopRegion, RenderPreferences preferences)
    {
        this.project = project;
        this.loopRegion = loopRegion;
        preferences.Validate();
        Preferences = preferences;
        selectedId = preferences.TemplateId;
        favorites = preferences.Favorites.ToHashSet();
        InitializeComponent();
        Style = (Style)FindResource(typeof(Window));
        Folder.Text = preferences.OutputFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        FileName.Text = project.Name + ".mp4";
        UseLoop.IsEnabled = loopRegion.HasValue;
        UseLoop.IsChecked = loopRegion.HasValue && preferences.UseLoopRegion;
        RangeDetails.Text = loopRegion is { } range
            ? $"{TimelineView.FormatTimecode(range.StartFrame, project.FrameRate)} – {TimelineView.FormatTimecode(range.EndFrame, project.FrameRate)} ({range.FrameCount} frames)"
            : "Drag the upper timeline ruler, or use I and O in the timeline to set in and out points.";
        RefreshTemplates();
        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) => RefreshTemplates();
    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshTemplates();
    private void TargetChanged(object sender, TextChangedEventArgs e) { if (Error is not null) Error.Text = ""; }
    private void RefreshTemplates()
    {
        if (Templates is null || project is null) return;
        var visible = RenderTemplateCatalog.Filter(Preferences.Templates, project, Search.Text, FavoritesOnly.IsChecked == true, favorites).ToArray();
        Templates.ItemsSource = visible;
        Templates.SelectedItem = visible.FirstOrDefault(template => template.Id == selectedId) ?? visible.FirstOrDefault();
        UpdateDetails();
    }

    private void TemplateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Templates.SelectedItem is RenderTemplate template) selectedId = template.Id;
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (Favorite is null || Details is null) return;
        var template = Templates.SelectedItem as RenderTemplate;
        Favorite.IsEnabled = template is not null;
        // Refreshing selection must not remove a favorite while its checkbox catches up.
        updatingFavorite = true;
        try { Favorite.IsChecked = template is not null && favorites.Contains(template.Id); }
        finally { updatingFavorite = false; }
        Details.Text = template?.Description(project) ?? "No templates match the current filters.";
        if (Customize is not null) Customize.IsEnabled = template is not null;
        if (DeleteTemplate is not null) DeleteTemplate.IsEnabled = template?.IsCustom == true;
    }

    private void FavoriteChanged(object sender, RoutedEventArgs e)
    {
        if (updatingFavorite || Templates.SelectedItem is not RenderTemplate template) return;
        if (Favorite.IsChecked == true) favorites.Add(template.Id); else favorites.Remove(template.Id);
        if (FavoritesOnly.IsChecked == true) RefreshTemplates();
    }

    private void BrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose render folder" };
        if (Directory.Exists(Folder.Text)) dialog.InitialDirectory = Folder.Text;
        if (dialog.ShowDialog(this) == true) Folder.Text = dialog.FolderName;
    }

    private void CustomizeClick(object sender, RoutedEventArgs e)
    {
        if (Templates.SelectedItem is not RenderTemplate template) return;
        var preferences = Preferences with { TemplateId = template.Id, Favorites = favorites.Order().ToArray() };
        var dialog = new RenderTemplateWindow(project, preferences, template) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Preferences = dialog.Preferences;
        selectedId = Preferences.TemplateId;
        // A new preset should remain visible even if the previous view showed only favorites.
        FavoritesOnly.IsChecked = false;
        Search.Text = "";
        Error.Text = "";
        RefreshTemplates();
    }

    private void DeleteTemplateClick(object sender, RoutedEventArgs e)
    {
        if (Templates.SelectedItem is not RenderTemplate { IsCustom: true } template) return;
        if (MessageBox.Show(this, $"Delete the template “{template.Name}”?", "Render As", MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Preferences = (Preferences with { TemplateId = template.Id, Favorites = favorites.Order().ToArray() }).DeleteCustomTemplate(template.Id);
        favorites.Remove(template.Id);
        selectedId = Preferences.TemplateId;
        Error.Text = "";
        RefreshTemplates();
    }

    private void RenderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Templates.SelectedItem is not RenderTemplate template) throw new InvalidDataException("Choose a render template.");
            var name = FileName.Text.Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name != Path.GetFileName(name))
                throw new InvalidDataException("Enter a valid output file name.");
            if (Path.GetExtension(name).Length == 0) name += ".mp4";
            if (!Path.GetExtension(name).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("This format requires an .mp4 file name.");
            if (!Path.IsPathFullyQualified(Folder.Text.Trim())) throw new InvalidDataException("Choose an absolute output folder.");
            var output = Path.GetFullPath(Path.Combine(Folder.Text.Trim(), name));
            if (project.Media.Any(asset => string.Equals(Path.GetFullPath(asset.Path), output, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Choose a destination different from the original media.");
            if (File.Exists(output) && MessageBox.Show(this, $"Replace {name}?", "Render As", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            OutputPath = output;
            Options = new ExportOptions { TemplateId = template.Id, CustomTemplate = template.IsCustom ? template with { } : null,
                Range = UseLoop.IsChecked == true ? loopRegion : null };
            Preferences = Preferences with { TemplateId = template.Id, Favorites = favorites.Order().ToArray(),
                OutputFolder = Path.GetDirectoryName(output), UseLoopRegion = UseLoop.IsChecked == true };
            Preferences.Validate();
            DialogResult = true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or NotSupportedException)
        { Error.Text = error.Message; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
