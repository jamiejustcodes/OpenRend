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

        MatchProject.IsChecked = preferences.MatchProjectSettings;
        StretchToFill.IsChecked = preferences.StretchToFill;
        GenerateLoudnessLog.IsChecked = preferences.GenerateLoudnessLog;

        ChannelMappingCombo.ItemsSource = new[]
        {
            new { Name = "None (Stereo)", Value = ChannelRemapping.None },
            new { Name = "Downmix to Mono", Value = ChannelRemapping.Mono },
            new { Name = "Swap Left and Right", Value = ChannelRemapping.Swap },
            new { Name = "Mute Left Channel", Value = ChannelRemapping.MuteLeft },
            new { Name = "Mute Right Channel", Value = ChannelRemapping.MuteRight }
        };
        ChannelMappingCombo.DisplayMemberPath = "Name";
        ChannelMappingCombo.SelectedValuePath = "Value";
        ChannelMappingCombo.SelectedValue = preferences.ChannelMapping;

        Formats.ItemsSource = RenderTemplateCatalog.Formats;
        var initialFormat = RenderTemplateCatalog.Formats.FirstOrDefault(f => f.Id == preferences.SelectedFormatId)
            ?? RenderTemplateCatalog.Formats[0];
        Formats.SelectedItem = initialFormat;

        RefreshTemplates();
        UpdateEstimates();

        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) => RefreshTemplates();
    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshTemplates();
    private void FormatChanged(object sender, SelectionChangedEventArgs e) => RefreshTemplates();
    private void OptionStateChanged(object sender, RoutedEventArgs e) => UpdateEstimates();
    private void TargetChanged(object sender, TextChangedEventArgs e)
    {
        if (Error is not null) Error.Text = "";
        UpdateEstimates();
    }

    private void RefreshTemplates()
    {
        if (Templates is null || project is null) return;
        var selectedFormat = (Formats?.SelectedItem as RenderFormatInfo)?.Id ?? "all";
        var matchProject = MatchProject?.IsChecked == true;
        var visible = RenderTemplateCatalog.Filter(
            Preferences.Templates,
            project,
            Search?.Text ?? "",
            FavoritesOnly?.IsChecked == true,
            favorites,
            selectedFormat,
            matchProject
        ).ToArray();

        Templates.ItemsSource = visible;
        Templates.SelectedItem = visible.FirstOrDefault(template => template.Id == selectedId) ?? visible.FirstOrDefault();
        UpdateDetails();
        UpdateEstimates();
    }

    private void TemplateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Templates.SelectedItem is RenderTemplate template)
        {
            selectedId = template.Id;
            UpdateExtensionForTemplate(template);
        }
        UpdateDetails();
        UpdateEstimates();
    }

    private void UpdateExtensionForTemplate(RenderTemplate template)
    {
        if (FileName is null || string.IsNullOrWhiteSpace(FileName.Text)) return;
        var currentName = FileName.Text.Trim();
        var currentExt = Path.GetExtension(currentName);
        var targetExt = template.Extension;
        if (string.IsNullOrEmpty(targetExt)) return;

        if (string.IsNullOrEmpty(currentExt))
        {
            FileName.Text = currentName + targetExt;
        }
        else if (!currentExt.Equals(targetExt, StringComparison.OrdinalIgnoreCase))
        {
            var baseName = Path.GetFileNameWithoutExtension(currentName);
            FileName.Text = baseName + targetExt;
        }
    }

    private void UpdateDetails()
    {
        if (Favorite is null || Details is null) return;
        var template = Templates.SelectedItem as RenderTemplate;
        Favorite.IsEnabled = template is not null;
        updatingFavorite = true;
        try { Favorite.IsChecked = template is not null && favorites.Contains(template.Id); }
        finally { updatingFavorite = false; }
        Details.Text = template?.Description(project) ?? "No templates match the current filters.";
        if (Customize is not null) Customize.IsEnabled = template is not null;
        if (DeleteTemplate is not null) DeleteTemplate.IsEnabled = template?.IsCustom == true;
    }

    private void UpdateEstimates()
    {
        if (EstimatedFileSize is null || DiskSpace is null || project is null) return;
        var template = Templates?.SelectedItem as RenderTemplate;
        if (template is null)
        {
            EstimatedFileSize.Text = "Estimated file size: —";
            return;
        }

        var frames = (UseLoop?.IsChecked == true && loopRegion.HasValue)
            ? loopRegion.Value.FrameCount
            : project.DurationFrames;

        var bytes = template.EstimatedBytes(project, frames);
        var mb = bytes / (1024.0 * 1024.0);
        EstimatedFileSize.Text = mb >= 1000
            ? $"Estimated file size: ~{mb / 1024.0:0.##} GB"
            : $"Estimated file size: ~{mb:0.#} MB";

        try
        {
            var targetFolder = Folder?.Text.Trim();
            if (!string.IsNullOrEmpty(targetFolder) && Path.IsPathFullyQualified(targetFolder))
            {
                var root = Path.GetPathRoot(targetFolder);
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    var drive = new DriveInfo(root);
                    var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    DiskSpace.Text = $"Disk space available: {freeGb:0.1} GB free on {drive.Name.TrimEnd('\\')}";
                    return;
                }
            }
        }
        catch
        {
            // Ignore drive inspection failures
        }
        DiskSpace.Text = "Disk space available: —";
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
            var targetExt = template.Extension;
            if (Path.GetExtension(name).Length == 0) name += targetExt;
            if (!Path.GetExtension(name).Equals(targetExt, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"This format requires a {targetExt} file name.");
            if (!Path.IsPathFullyQualified(Folder.Text.Trim())) throw new InvalidDataException("Choose an absolute output folder.");
            var output = Path.GetFullPath(Path.Combine(Folder.Text.Trim(), name));
            if (project.Media.Any(asset => string.Equals(Path.GetFullPath(asset.Path), output, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Choose a destination different from the original media.");
            if (File.Exists(output) && MessageBox.Show(this, $"Replace {name}?", "Render As", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            OutputPath = output;
            var channelMapping = ChannelMappingCombo.SelectedValue is ChannelRemapping mapping ? mapping : ChannelRemapping.None;
            Options = new ExportOptions
            {
                TemplateId = template.Id,
                CustomTemplate = template.IsCustom ? template with { } : null,
                Range = UseLoop.IsChecked == true ? loopRegion : null,
                StretchToFill = StretchToFill.IsChecked == true,
                ChannelMapping = channelMapping,
                GenerateLoudnessLog = GenerateLoudnessLog.IsChecked == true
            };
            Preferences = Preferences with
            {
                TemplateId = template.Id,
                Favorites = favorites.Order().ToArray(),
                OutputFolder = Path.GetDirectoryName(output),
                UseLoopRegion = UseLoop.IsChecked == true,
                StretchToFill = StretchToFill.IsChecked == true,
                ChannelMapping = channelMapping,
                GenerateLoudnessLog = GenerateLoudnessLog.IsChecked == true,
                SelectedFormatId = (Formats.SelectedItem as RenderFormatInfo)?.Id ?? "all",
                MatchProjectSettings = MatchProject.IsChecked == true
            };
            Preferences.Validate();
            DialogResult = true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or NotSupportedException)
        { Error.Text = error.Message; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
