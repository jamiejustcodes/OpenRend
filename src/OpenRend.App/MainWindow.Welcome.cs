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
            if (WelcomePreferencesStore.Load(WelcomePreferencesPath).ShowAtStartup) await ShowWelcomeAsync();
        });
    }

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
            vm.Replace(dialog.Settings.CreateProject(), null);
            var current = WelcomePreferencesStore.Load(WelcomePreferencesPath);
            WelcomePreferencesStore.Save(current with { NewProject = dialog.Settings }, WelcomePreferencesPath);
            vm.Status = $"Created {vm.Project.Width} × {vm.Project.Height}, {vm.Project.FrameRate.Value:0.###} fps project.";
        }
        else if (dialog.Action == WelcomeAction.Advanced)
        {
            // Advanced creation starts from project defaults, independent of the welcome tiles.
            var project = new NewProjectSettings().CreateProject();
            var advanced = new ProjectSettingsWindow(project) { Owner = this };
            if (advanced.ShowDialog() != true) return;
            project.Name = advanced.ProjectName;
            project.Width = advanced.ProjectWidth; project.Height = advanced.ProjectHeight; project.FrameRate = advanced.Rate;
            vm.Replace(project, null);
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
        Pause(); vm.Replace(new ProjectDocument(), null);
    });
}
