using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

/// <summary>
/// Startup and manual notification dialog when a newer version of OpenRend is available on GitHub.
/// Allows the user to install the update in-place, view release notes, or disable startup notifications.
/// </summary>
public sealed class UpdateAvailableWindow : Window
{
    private readonly GitHubRelease release;
    private readonly IUpdateService updateService;
    private readonly CheckBox disablePopup;
    private readonly ProgressBar progressBar;
    private readonly TextBlock downloadStatus;
    private readonly Button installBtn;
    private readonly Button viewBtn;
    private readonly Button remindBtn;

    public bool DisableStartupUpdatesRequested => disablePopup.IsChecked == true;
    public bool UpdateInstalled { get; private set; }

    public UpdateAvailableWindow(
        GitHubRelease release,
        bool startupCheckCurrentlyEnabled = true,
        IUpdateService? updateService = null)
    {
        this.release = release;
        this.updateService = updateService ?? new GitHubUpdateService();

        Title = "OpenRend Update Available";
        Width = 520;
        Height = 450;
        MinWidth = 460;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var defaultTextBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));
        Brush TextBrush() => TryFindResource("TextBrush") as Brush ?? defaultTextBrush;
        Style? FindStyle(object key) => TryFindResource(key) as Style;
        if (FindStyle(typeof(Window)) is { } winStyle) Style = winStyle;

        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };

        var root = new DockPanel { Margin = new Thickness(14) };

        // Bottom action buttons
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };

        installBtn = new Button
        {
            Content = "Install update",
            MinWidth = 110,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        AutomationProperties.SetAutomationId(installBtn, "InstallUpdateButton");

        viewBtn = new Button
        {
            Content = "View on GitHub",
            MinWidth = 110,
            Margin = new Thickness(0, 0, 8, 0)
        };
        AutomationProperties.SetAutomationId(viewBtn, "ViewReleaseButton");

        remindBtn = new Button
        {
            Content = "Remind me later",
            MinWidth = 110,
            IsCancel = true
        };
        AutomationProperties.SetAutomationId(remindBtn, "RemindLaterButton");

        buttons.Children.Add(installBtn);
        buttons.Children.Add(viewBtn);
        buttons.Children.Add(remindBtn);

        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        // Checkbox to disable startup pop-up
        disablePopup = new CheckBox
        {
            Content = "_Do not check for updates on startup",
            IsChecked = !startupCheckCurrentlyEnabled,
            Foreground = TextBrush(),
            Margin = new Thickness(0, 8, 0, 0)
        };
        AutomationProperties.SetAutomationId(disablePopup, "DisableStartupCheck");
        DockPanel.SetDock(disablePopup, Dock.Bottom);
        root.Children.Add(disablePopup);

        // Progress section (hidden until downloading)
        var progressHost = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        downloadStatus = new TextBlock
        {
            Foreground = TextBrush(),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 4)
        };
        AutomationProperties.SetAutomationId(downloadStatus, "DownloadStatusText");
        progressBar = new ProgressBar
        {
            Height = 6,
            Visibility = Visibility.Collapsed,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };
        AutomationProperties.SetAutomationId(progressBar, "DownloadProgressBar");
        progressHost.Children.Add(downloadStatus);
        progressHost.Children.Add(progressBar);

        DockPanel.SetDock(progressHost, Dock.Bottom);
        root.Children.Add(progressHost);

        // Main content area
        var mainStack = new StackPanel();

        var header = new TextBlock
        {
            Text = "A newer version of OpenRend is available!",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = TryFindResource("ActiveColor") as Brush ?? TextBrush(),
            Margin = new Thickness(0, 0, 0, 6)
        };
        mainStack.Children.Add(header);

        var versionInfo = new TextBlock
        {
            Text = $"Available: {release.TagName}   (Current version: v{AppVersion.CurrentVersionString})",
            Foreground = TextBrush(),
            Margin = new Thickness(0, 0, 0, 10)
        };
        mainStack.Children.Add(versionInfo);

        var notesGroup = new GroupBox
        {
            Header = "Release notes",
            Style = FindStyle("PreferencesGroup"),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var notesText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(release.Body) ? "No release notes provided." : release.Body,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TextBrush(),
            Margin = new Thickness(8)
        };

        var notesScroll = new ScrollViewer
        {
            Content = notesText,
            MaxHeight = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        notesGroup.Content = notesScroll;
        mainStack.Children.Add(notesGroup);

        root.Children.Add(mainStack);
        Content = root;

        // Button handlers
        remindBtn.Click += (_, _) =>
        {
            try { DialogResult = false; } catch (InvalidOperationException) { }
            Close();
        };

        viewBtn.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = release.HtmlUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        };

        installBtn.Click += async (_, _) =>
        {
            await RunInstallAsync();
        };
    }

    private async Task RunInstallAsync()
    {
        var asset = release.GetUpdateAsset();
        if (asset == null)
        {
            downloadStatus.Visibility = Visibility.Visible;
            downloadStatus.Text = "No direct Windows update package found in release assets. Opening release page...";
            try
            {
                Process.Start(new ProcessStartInfo { FileName = release.HtmlUrl, UseShellExecute = true });
            }
            catch { }
            return;
        }

        installBtn.IsEnabled = false;
        viewBtn.IsEnabled = false;
        remindBtn.IsEnabled = false;
        disablePopup.IsEnabled = false;

        downloadStatus.Visibility = Visibility.Visible;
        progressBar.Visibility = Visibility.Visible;
        downloadStatus.Text = $"Downloading {asset.Name}...";

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "OpenRend-Update");
            var progress = new Progress<double>(p =>
            {
                progressBar.Value = p * 100;
                downloadStatus.Text = $"Downloading {asset.Name}: {(int)(p * 100)}%";
            });

            var downloadedPath = await updateService.DownloadUpdateAsync(asset, tempDir, progress).ConfigureAwait(true);

            downloadStatus.Text = "Download complete. Starting update...";
            UpdateInstalled = true;

            // Launch update installer
            var appDir = AppContext.BaseDirectory;
            GitHubUpdateService.LaunchUpdateInstaller(downloadedPath, Environment.ProcessId, appDir);

            try { DialogResult = true; } catch (InvalidOperationException) { }
            Close();
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            downloadStatus.Text = $"Update failed: {ex.Message}";
            installBtn.IsEnabled = true;
            viewBtn.IsEnabled = true;
            remindBtn.IsEnabled = true;
            disablePopup.IsEnabled = true;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

