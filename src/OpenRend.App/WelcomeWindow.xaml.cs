using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OpenRend.Project;

namespace OpenRend.App;

public enum WelcomeAction { None, Create, Open, Advanced }

public partial class WelcomeWindow : Window
{
    private NewProjectSettings settings;
    private bool refreshing = true;
    private readonly Action<bool> saveStartupPreference;
    public NewProjectSettings Settings => settings;
    public WelcomeAction Action { get; private set; }
    public string? ProjectPath { get; private set; }
    private sealed record RecentProject(string Path) { public string Name => System.IO.Path.GetFileNameWithoutExtension(Path); }

    public WelcomeWindow(WelcomePreferences preferences, Action<bool> saveStartupPreference)
    {
        preferences.Validate();
        settings = preferences.NewProject;
        this.saveStartupPreference = saveStartupPreference;
        InitializeComponent();
        Style = (Style)FindResource(typeof(Window));
        FrameRateType.ItemsSource = new[] { "Full Frame (1/1)", "Drop Frame (1/1.001)" };
        ShowAtStartup.IsChecked = preferences.ShowAtStartup;
        var recents = preferences.RecentProjects.Where(File.Exists).Select(path => new RecentProject(path)).ToArray();
        RecentProjects.ItemsSource = recents;
        NoRecentProjects.Visibility = recents.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshSettings();
        ShowPage(NewPage);
        refreshing = false;
    }

    private void RefreshSettings()
    {
        refreshing = true;
        try
        {
            foreach (RadioButton tile in AspectTiles.Children) tile.IsChecked = tile.Tag.ToString() == settings.Aspect.ToString();
            Resolution.ItemsSource = Enum.GetValues<ProjectResolution>().Select(settings.ResolutionLabel).ToArray();
            Resolution.SelectedIndex = (int)settings.Resolution;
            FrameRate.ItemsSource = NewProjectSettings.FrameRates.Select(settings.FrameRateLabel).ToArray();
            FrameRate.SelectedIndex = NewProjectSettings.FrameRates.ToList().IndexOf(settings.FramesPerSecond);
            FrameRateType.SelectedIndex = settings.FractionalRate ? 1 : 0;
            Artwork.Aspect = settings.Aspect;
        }
        finally { refreshing = false; }
    }

    private void AspectChanged(object sender, RoutedEventArgs e)
    {
        if (refreshing || sender is not RadioButton tile) return;
        settings = settings with { Aspect = Enum.Parse<ProjectAspect>((string)tile.Tag) };
        RefreshSettings();
    }
    private void ResolutionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshing && Resolution.SelectedIndex >= 0) settings = settings with { Resolution = (ProjectResolution)Resolution.SelectedIndex };
    }
    private void FrameRateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshing && FrameRate.SelectedIndex >= 0) settings = settings with { FramesPerSecond = NewProjectSettings.FrameRates[FrameRate.SelectedIndex] };
    }
    private void FrameRateTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || FrameRateType.SelectedIndex < 0) return;
        settings = settings with { FractionalRate = FrameRateType.SelectedIndex == 1 };
        RefreshSettings();
    }
    private void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (refreshing) return;
        try { saveStartupPreference(ShowAtStartup.IsChecked == true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { Error.Text = error.Message; }
    }
    private void ShowPage(UIElement page)
    {
        NewPage.Visibility = page == NewPage ? Visibility.Visible : Visibility.Collapsed;
        OpenPage.Visibility = page == OpenPage ? Visibility.Visible : Visibility.Collapsed;
        HelpPage.Visibility = page == HelpPage ? Visibility.Visible : Visibility.Collapsed;
        var activeBrush = (Brush)FindResource("ActiveColor");
        NewPageButton.Background = page == NewPage ? activeBrush : Brushes.Transparent;
        OpenPageButton.Background = page == OpenPage ? activeBrush : Brushes.Transparent;
        HelpPageButton.Background = page == HelpPage ? activeBrush : Brushes.Transparent;
    }
    private void NewPageClick(object sender, RoutedEventArgs e) => ShowPage(NewPage);
    private void OpenPageClick(object sender, RoutedEventArgs e) => ShowPage(OpenPage);
    private void HelpPageClick(object sender, RoutedEventArgs e) => ShowPage(HelpPage);
    private void CreateClick(object sender, RoutedEventArgs e) { settings.Validate(); Action = WelcomeAction.Create; DialogResult = true; }
    private void AdvancedClick(object sender, RoutedEventArgs e) { Action = WelcomeAction.Advanced; DialogResult = true; }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter && OpenPage.IsVisible) { OpenRecentClick(sender, e); e.Handled = true; }
    }
    private void HeaderMouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
    private void BrowseProjectClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "OpenRend project|*.openrend", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) OpenProject(dialog.FileName);
    }
    private void OpenRecentClick(object sender, RoutedEventArgs e) { if (RecentProjects.SelectedItem is RecentProject recent) OpenProject(recent.Path); }
    private void RecentDoubleClick(object sender, MouseButtonEventArgs e) => OpenRecentClick(sender, e);
    private void OpenProject(string path)
    {
        if (!File.Exists(path)) { Error.Text = "The selected project is no longer available."; return; }
        ProjectPath = path; Action = WelcomeAction.Open; DialogResult = true;
    }
}
