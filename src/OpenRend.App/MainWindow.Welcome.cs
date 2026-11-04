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
