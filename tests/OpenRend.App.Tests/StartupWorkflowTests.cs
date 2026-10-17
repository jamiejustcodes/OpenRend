using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.App.Tests;

public sealed partial class EditorWorkflowTests
{
    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task WelcomeCreatesPortraitProjectRemembersSettingsAndHonorsStartupPreference()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var preferencesPath = Path.Combine(temporary, "welcome.json");
        var projectPath = Path.Combine(temporary, "portrait.openrend");
        ProcessStartInfo Start(string? path = null)
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
            start.Environment["OPENREND_DATA_DIR"] = temporary;
            if (path is not null) start.ArgumentList.Add(path);
            return start;
        }
        try
        {
            using (var app = Application.Launch(Start()))
            using (var automation = new UIA3Automation())
            {
                try
                {
                    var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(12));
                    Assert.NotNull(window);
                    Window? welcome = null;
                    WaitFor(() => (welcome = window.ModalWindows.SingleOrDefault(dialog => dialog.Title == "Welcome")) is not null,
                        "Startup did not open the welcome screen.");
                    Find(welcome!, "Portrait").Patterns.SelectionItem.Pattern.Select();
                    var resolution = Find(welcome!, "Resolution").AsComboBox();
                    resolution.Select(2); resolution.Patterns.ExpandCollapse.Pattern.Collapse();
                    var rate = Find(welcome!, "FrameRate").AsComboBox();
                    rate.Select(5); rate.Patterns.ExpandCollapse.Pattern.Collapse();
                    var type = Find(welcome!, "FrameRateType").AsComboBox();
                    type.Select(0); type.Patterns.ExpandCollapse.Pattern.Collapse();
                    var artifact = Path.Combine(root, ".local", "desktop-validation"); Directory.CreateDirectory(artifact);
                    using (var screenshot = Capture.Element(welcome!)) screenshot.ToFile(Path.Combine(artifact, "welcome-portrait.png"));
                    Find(welcome!, "ShowAtStartup").AsCheckBox().Toggle();
                    Assert.False(WelcomePreferencesStore.Load(preferencesPath).ShowAtStartup);
                    using (var screenshot = Capture.Element(welcome!)) screenshot.ToFile(Path.Combine(artifact, "welcome-portrait.png"));
                    Find(welcome!, "CreateProject").AsButton().Invoke();
                    WaitFor(() => window.ModalWindows.Length == 0 && Find(window, "PreviewInfo").Name.Contains("2160×3840, 60.000p"),
                        "Creating the portrait project did not apply its dimensions and rate.");
                    Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
                    Window? save = null;
                    WaitFor(() => (save = window.ModalWindows.SingleOrDefault()) is not null, "Save did not open its output dialog.");
                    var controls = save!.FindAllDescendants();
                    var filename = Assert.Single(controls, element => element.ControlType == ControlType.Edit &&
                        element.Name.Contains("File name", StringComparison.OrdinalIgnoreCase)).AsTextBox();
                    filename.Text = projectPath;
                    Assert.Single(controls, element => element.ControlType == ControlType.Button && element.Name.TrimStart('&') == "Save").AsButton().Invoke();
                    WaitFor(() => File.Exists(projectPath) && window.ModalWindows.Length == 0, "The new project was not saved.");
                    var saved = await ProjectStore.LoadAsync(projectPath);
                    Assert.Equal(2160, saved.Width); Assert.Equal(3840, saved.Height);
                    Assert.Equal(new Rational(60, 1), saved.FrameRate);
                    Assert.Empty(saved.Tracks); Assert.Empty(saved.Media);
                    window.Close();
                    WaitFor(() => app.HasExited, "The empty saved project did not close.");
                }
                finally { if (!app.HasExited) app.Kill(); }
            }
            using (var app = Application.Launch(Start()))
            using (var automation = new UIA3Automation())
            {
                try
                {
                    var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(12));
                    Assert.NotNull(window);
                    WaitFor(() => Find(window, "PreviewInfo").Name.Contains("Project:"), "The workspace did not initialize.");
                    Assert.Empty(window.ModalWindows);
                    Chord(window, VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_N);
                    Window? welcome = null;
                    WaitFor(() => (welcome = window.ModalWindows.SingleOrDefault(dialog => dialog.Title == "Welcome")) is not null,
                        "New Project did not reopen welcome after startup was disabled.");
                    var remembered = WelcomePreferencesStore.Load(preferencesPath).NewProject;
                    Assert.Equal(ProjectAspect.Portrait, remembered.Aspect);
                    Assert.Equal(ProjectResolution.UHD, remembered.Resolution);
                    Assert.Equal(60, remembered.FramesPerSecond);
                    Assert.False(remembered.FractionalRate);
                    Find(welcome!, "ShowAtStartup").AsCheckBox().Toggle();
                    Find(welcome!, "OpenProjectPageTabButton").AsButton().Invoke();
                    var recents = Find(welcome!, "RecentProjects").AsListBox();
                    Assert.Single(recents.Items);
                    recents.Items[0].Patterns.SelectionItem.Pattern.Select();
                    Find(welcome!, "OpenRecentProject").AsButton().Invoke();
                    WaitFor(() => window.ModalWindows.Length == 0 && Find(window, "PreviewInfo").Name.Contains("2160×3840, 60.000p"),
                        "Opening a recent project did not restore its project settings.");
                    window.Close(); WaitFor(() => app.HasExited, "The recent project did not close.");
                }
                finally { if (!app.HasExited) app.Kill(); }
            }
            Assert.True(WelcomePreferencesStore.Load(preferencesPath).ShowAtStartup);
            using (var app = Application.Launch(Start(projectPath)))
            using (var automation = new UIA3Automation())
            {
                try
                {
                    var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(12)); Assert.NotNull(window);
                    WaitFor(() => Find(window, "PreviewInfo").Name.Contains("2160×3840, 60.000p"), "Command-line project open did not apply the saved settings.");
                    Assert.Empty(window.ModalWindows);
                    window.Close(); WaitFor(() => app.HasExited, "The command-line project did not close.");
                }
                finally { if (!app.HasExited) app.Kill(); }
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
