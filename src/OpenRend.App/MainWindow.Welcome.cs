using System.IO;
using System.Windows;
using OpenRend.Core;
using OpenRend.Project;

namespace OpenRend.App;

public partial class MainWindow
{
    private string WelcomePreferencesPath => Path.Combine(Path.GetDirectoryName(recoveryPath)!, "welcome.json");

    public async Task ShowStartupAsync()
    {
        await GuardAsync(async () =>
        {
            var recovered = await CheckCrashRecoveryAsync();
            if (recovered) return;

            LoadGeneralPreferences();
            if (generalPreferences.CheckForUpdatesOnStartup)
            {
                await CheckForUpdatesStartupAsync();
            }

            if (WelcomePreferencesStore.Load(WelcomePreferencesPath).ShowAtStartup) await ShowWelcomeAsync();
        });
    }

    private async Task CheckForUpdatesStartupAsync()
    {
        try
        {
            var updateService = new GitHubUpdateService();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var result = await updateService.CheckForUpdatesAsync(cancellationToken: cts.Token).ConfigureAwait(true);
            if (result.IsUpdateAvailable && result.Release is not null)
            {
                var dialog = new UpdateAvailableWindow(result.Release, generalPreferences.CheckForUpdatesOnStartup, updateService)
                {
                    Owner = this
                };
                dialog.ShowDialog();
                if (dialog.DisableStartupUpdatesRequested)
                {
                    generalPreferences = generalPreferences with { CheckForUpdatesOnStartup = false };
                    GeneralPreferencesStore.Save(generalPreferences, GeneralPreferencesPath);
                    MessageBox.Show(
                        this,
                        "Startup update checks have been disabled.\n\nYou can scan for updates anytime in Options → Preferences → Updates.",
                        "Update Notifications Disabled",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    vm.Status = "Startup update checks disabled. Scan for updates anytime in Options → Preferences.";
                }
            }
        }
        catch
        {
            // Network failure or timeout during automatic startup check is handled gracefully
        }
    }

    private async void CheckForUpdatesClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        vm.Status = "Scanning for updates...";
        var updateService = new GitHubUpdateService();
        var result = await updateService.CheckForUpdatesAsync().ConfigureAwait(true);
        if (result.IsUpdateAvailable && result.Release is not null)
        {
            var dialog = new UpdateAvailableWindow(result.Release, generalPreferences.CheckForUpdatesOnStartup, updateService)
            {
                Owner = this
            };
            dialog.ShowDialog();
            if (dialog.DisableStartupUpdatesRequested)
            {
                generalPreferences = generalPreferences with { CheckForUpdatesOnStartup = false };
                GeneralPreferencesStore.Save(generalPreferences, GeneralPreferencesPath);
                MessageBox.Show(
                    this,
                    "Startup update checks have been disabled.\n\nYou can scan for updates anytime in Options → Preferences → Updates.",
                    "Update Notifications Disabled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                vm.Status = "Startup update checks disabled. Scan for updates anytime in Options → Preferences.";
            }
        }
        else if (result.Status == UpdateStatus.UpToDate)
        {
            MessageBox.Show(
                this,
                $"You are running the latest version of OpenRend (v{AppVersion.CurrentVersionString}).",
                "Check for Updates",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            vm.Status = $"OpenRend is up to date (version {AppVersion.CurrentVersionString}).";
        }
        else
        {
            MessageBox.Show(
                this,
                $"Unable to check for updates: {result.ErrorMessage}\n\nPlease check your internet connection or visit the GitHub repository.",
                "Check for Updates",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            vm.Status = $"Update check failed: {result.ErrorMessage}";
        }
    });

    private async Task ShowWelcomeAsync()
    {
        Pause();
        var preferences = WelcomePreferencesStore.Load(WelcomePreferencesPath);
        var dialog = new WelcomeWindow(preferences, show =>
        {
            var current = WelcomePreferencesStore.Load(WelcomePreferencesPath);
            WelcomePreferencesStore.Save(current with { ShowAtStartup = show }, WelcomePreferencesPath);
        }) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (dialog.Action == WelcomeAction.Open)
        {
            await OpenPathAsync(dialog.ProjectPath!);
            return;
        }
        if (!await ConfirmUnsavedAsync()) return;
        if (dialog.Action == WelcomeAction.Create)
        {
            var template = WelcomePreferencesStore.Load(WelcomePreferencesPath).DefaultProjectTemplate;
            var project = dialog.Settings.CreateProject();
            if (template is not null)
            {
                project.FieldOrder = template.FieldOrder;
                project.PixelAspectRatio = template.PixelAspectRatio;
                project.PixelFormat = template.PixelFormat;
                project.RenderQuality = template.RenderQuality;
                project.MotionBlur = template.MotionBlur;
                project.DeinterlaceMethod = template.DeinterlaceMethod;
                project.AudioSampleRate = template.AudioSampleRate;
                project.AudioBitDepth = template.AudioBitDepth;
                project.PrerenderFolder = template.PrerenderFolder;
                project.RecordedFilesFolder = template.RecordedFilesFolder;
                project.RulerFormat = template.RulerFormat;
                project.RulerTimeFormat = template.RulerTimeFormat;
                project.BeatsPerMinute = template.BeatsPerMinute;
                project.BeatsPerMeasure = template.BeatsPerMeasure;
                project.Author = template.Author;
                project.Engineer = template.Engineer;
                project.Copyright = template.Copyright;
                project.Comments = template.Comments;
                project.UniversalAlbumId = template.UniversalAlbumId;
            }
            vm.Replace(project, null);
            var current = WelcomePreferencesStore.Load(WelcomePreferencesPath);
            WelcomePreferencesStore.Save(current with { NewProject = dialog.Settings }, WelcomePreferencesPath);
            vm.Status = $"Created {vm.Project.Width} × {vm.Project.Height}, {vm.Project.FrameRate.Value:0.###} fps project.";
        }
        else if (dialog.Action == WelcomeAction.Advanced)
        {
            // Advanced creation starts from project defaults, independent of the welcome tiles.
            var template = WelcomePreferencesStore.Load(WelcomePreferencesPath).DefaultProjectTemplate;
            var project = template?.DeepClone() ?? new NewProjectSettings().CreateProject();
            project.Tracks.Clear();
            project.Media.Clear();
            project.Markers.Clear();
            var advanced = new ProjectSettingsWindow(project) { Owner = this };
            if (advanced.ShowDialog() != true) return;
            project.Name = advanced.ProjectName;
            project.Width = advanced.ProjectWidth; project.Height = advanced.ProjectHeight; project.FrameRate = advanced.Rate;
            project.FieldOrder = advanced.FieldOrder; project.PixelAspectRatio = advanced.PixelAspectRatio;
            project.RenderQuality = advanced.RenderQuality; project.MotionBlur = advanced.MotionBlur;
            project.DeinterlaceMethod = advanced.DeinterlaceMethod; project.PixelFormat = advanced.PixelFormat;
            project.AudioSampleRate = advanced.AudioSampleRate; project.AudioBitDepth = advanced.AudioBitDepth;
            project.PrerenderFolder = advanced.PrerenderFolder; project.RecordedFilesFolder = advanced.RecordedFilesFolder;
            project.RulerFormat = advanced.RulerFormat; project.RulerTimeFormat = advanced.RulerTimeFormat;
            project.RulerStartTimeFrames = advanced.RulerStartTimeFrames;
            project.BeatsPerMinute = advanced.BeatsPerMinute; project.BeatsPerMeasure = advanced.BeatsPerMeasure;
            project.Author = advanced.Author; project.Engineer = advanced.Engineer;
            project.Copyright = advanced.Copyright; project.Comments = advanced.Comments;
            project.UniversalAlbumId = advanced.UniversalAlbumId;
            if (advanced.StartWithSettingsChecked)
            {
                SaveDefaultProjectSettings(project);
            }
            vm.Replace(project, null);
        }
    }

    internal void SaveDefaultProjectSettings(ProjectDocument project)
    {
        try
        {
            var prefs = WelcomePreferencesStore.Load(WelcomePreferencesPath);
            var template = new ProjectDocument
            {
                Width = project.Width,
                Height = project.Height,
                FrameRate = project.FrameRate,
                FieldOrder = project.FieldOrder,
                PixelAspectRatio = project.PixelAspectRatio,
                PixelFormat = project.PixelFormat,
                RenderQuality = project.RenderQuality,
                MotionBlur = project.MotionBlur,
                DeinterlaceMethod = project.DeinterlaceMethod,
                AudioSampleRate = project.AudioSampleRate,
                AudioBitDepth = project.AudioBitDepth,
                PrerenderFolder = project.PrerenderFolder,
                RecordedFilesFolder = project.RecordedFilesFolder,
                RulerFormat = project.RulerFormat,
                RulerTimeFormat = project.RulerTimeFormat,
                BeatsPerMinute = project.BeatsPerMinute,
                BeatsPerMeasure = project.BeatsPerMeasure,
                Author = project.Author,
                Engineer = project.Engineer,
                Copyright = project.Copyright,
                Comments = project.Comments,
                UniversalAlbumId = project.UniversalAlbumId
            };
            WelcomePreferencesStore.Save(prefs with { DefaultProjectTemplate = template }, WelcomePreferencesPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            vm.Status = $"Default project settings could not be saved: {error.Message}";
        }
    }

    private void RecordRecentProject(string path)
    {
        try
        {
            var settings = WelcomePreferencesStore.Load(WelcomePreferencesPath);
            WelcomePreferencesStore.Save(settings.WithRecentProject(path), WelcomePreferencesPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        { vm.Status = $"Project is available, but its recent-project entry could not be saved: {error.Message}"; }
    }

    private async void WelcomeClick(object sender, RoutedEventArgs e) => await GuardAsync(ShowWelcomeAsync);
    private async void NewEmptyClick(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (!await ConfirmUnsavedAsync()) return;
        Pause();
        var template = WelcomePreferencesStore.Load(WelcomePreferencesPath).DefaultProjectTemplate;
        var project = template?.DeepClone() ?? new ProjectDocument();
        project.Tracks.Clear();
        project.Media.Clear();
        project.Markers.Clear();
        vm.Replace(project, null);
    });
}
