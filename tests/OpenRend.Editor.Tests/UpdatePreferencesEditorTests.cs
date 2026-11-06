using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class UpdatePreferencesEditorTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }

    [Fact]
    public void EditingPreferencesWindow_UpdatesTab_RendersAndSavesPreferences() => RunSta(() =>
    {
        var editing = new EditingPreferences();
        var general = new GeneralPreferences { CheckForUpdatesOnStartup = true };

        var mockService = new FakeUpdateService(new UpdateCheckResult
        {
            Status = UpdateStatus.UpdateAvailable,
            CurrentVersion = SemanticVersion.Parse("0.1.0"),
            LatestVersion = SemanticVersion.Parse("0.2.0"),
            Release = new GitHubRelease
            {
                TagName = "v0.2.0",
                Name = "OpenRend 0.2.0",
                Body = "Major new features",
                HtmlUrl = "https://github.com/jamiejustcodes/OpenRend/releases/tag/v0.2.0",
                Assets = [new GitHubReleaseAsset { Name = "OpenRend-Setup-v0.2.0.exe", BrowserDownloadUrl = "http://test" }]
            }
        });

        var window = new EditingPreferencesWindow(editing, general, mockService);
        var dock = Assert.IsType<DockPanel>(window.Content);
        var tabs = Assert.IsType<TabControl>(dock.Children[2]);

        Assert.Equal(3, tabs.Items.Count);
        var updatesTab = Assert.IsType<TabItem>(tabs.Items[2]);
        Assert.Equal("Updates", updatesTab.Header);

        var updateGroup = Assert.IsType<GroupBox>(updatesTab.Content);
        var updateStack = Assert.IsType<StackPanel>(updateGroup.Content);

        var startupCheck = Assert.IsType<CheckBox>(updateStack.Children[0]);
        Assert.True(startupCheck.IsChecked);

        // Uncheck to disable startup updates
        startupCheck.IsChecked = false;

        // Find Scan button
        var btnBar = Assert.IsType<StackPanel>(updateStack.Children[4]);
        var scanBtn = Assert.IsType<Button>(btnBar.Children[0]);
        Assert.Equal("Scan for updates", scanBtn.Content);

        // Click scan button
        scanBtn.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

        // Let async click handler complete
        var status = Assert.IsType<TextBlock>(updateStack.Children[2]);
        Assert.Contains("0.2.0", status.Text);

        // Test saving with OK button
        var buttonBar = Assert.IsType<StackPanel>(dock.Children[0]);
        var okBtn = Assert.IsType<Button>(buttonBar.Children[0]);
        okBtn.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

        Assert.False(window.GeneralResult.CheckForUpdatesOnStartup);
    });

    [Fact]
    public void UpdateAvailableWindow_RendersAndCapturesDisableRequest() => RunSta(() =>
    {
        var release = new GitHubRelease
        {
            TagName = "v0.2.0",
            Name = "OpenRend 0.2.0",
            Body = "Important bug fixes and improvements",
            HtmlUrl = "https://github.com/jamiejustcodes/OpenRend/releases/tag/v0.2.0",
            Assets = [new GitHubReleaseAsset { Name = "OpenRend-Setup-v0.2.0.exe", BrowserDownloadUrl = "http://test" }]
        };

        var window = new UpdateAvailableWindow(release, startupCheckCurrentlyEnabled: true);
        var dock = Assert.IsType<DockPanel>(window.Content);

        // Find disable startup check box
        var disableCheck = Assert.IsType<CheckBox>(dock.Children[1]);
        Assert.False(disableCheck.IsChecked);
        Assert.False(window.DisableStartupUpdatesRequested);

        // Check the box
        disableCheck.IsChecked = true;
        Assert.True(window.DisableStartupUpdatesRequested);

        // Verify action buttons
        var buttons = Assert.IsType<StackPanel>(dock.Children[0]);
        var installBtn = Assert.IsType<Button>(buttons.Children[0]);
        var viewBtn = Assert.IsType<Button>(buttons.Children[1]);
        var remindBtn = Assert.IsType<Button>(buttons.Children[2]);

        Assert.Equal("Install update", installBtn.Content);
        Assert.Equal("View on GitHub", viewBtn.Content);
        Assert.Equal("Remind me later", remindBtn.Content);
    });

    private sealed class FakeUpdateService : IUpdateService
    {
        private readonly UpdateCheckResult checkResult;

        public FakeUpdateService(UpdateCheckResult checkResult)
        {
            this.checkResult = checkResult;
        }

        public Task<UpdateCheckResult> CheckForUpdatesAsync(SemanticVersion? currentVersionOverride = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(checkResult);
        }

        public Task<string> DownloadUpdateAsync(GitHubReleaseAsset asset, string targetDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            var fakeFile = Path.Combine(targetDirectory, asset.Name);
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(fakeFile, "dummy");
            progress?.Report(1.0);
            return Task.FromResult(fakeFile);
        }
    }
}
