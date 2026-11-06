using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OpenRend.Core;
using OpenRend.Project;

namespace OpenRend.App;

public sealed class EditingPreferencesWindow : Window
{
    private readonly TextBox amount;
    private readonly ComboBox alignment;
    private readonly CheckBox enableAutoSave;
    private readonly TextBox autoSaveInterval;
    private readonly CheckBox createProjectBackups;
    private readonly CheckBox useProjectDirForAutoSave;
    private readonly CheckBox checkUpdatesOnStartup;
    private readonly TextBlock error = new() { Margin = new Thickness(8, 0, 8, 0), TextWrapping = TextWrapping.Wrap };

    public EditingPreferences Result { get; private set; }
    public GeneralPreferences GeneralResult { get; private set; }

    public EditingPreferencesWindow(
        EditingPreferences settings,
        GeneralPreferences? generalSettings = null,
        IUpdateService? updateService = null)
    {
        Result = settings;
        var gen = generalSettings ?? new GeneralPreferences();
        GeneralResult = gen;

        var defaultTextBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));
        Brush TextBrush() => TryFindResource("TextBrush") as Brush ?? defaultTextBrush;
        Style? FindStyle(object key) => TryFindResource(key) as Style;

        if (FindStyle(typeof(Window)) is { } winStyle) Style = winStyle;
        Title = "Preferences"; Width = 600; Height = 390; MinWidth = 500; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };

        var root = new DockPanel { Margin = new Thickness(10) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 78 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 78 };
        AutomationProperties.SetAutomationId(ok, "EditingPreferencesOk");
        AutomationProperties.SetAutomationId(cancel, "EditingPreferencesCancel");
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        DockPanel.SetDock(error, Dock.Bottom); root.Children.Add(error);

        var tabs = new TabControl { TabStripPlacement = Dock.Top, Style = FindStyle("PreferencesTabs") };

        // 1. General Tab (AutoSave & Backups)
        var genFields = new Grid { Margin = new Thickness(12) };
        genFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        genFields.ColumnDefinitions.Add(new ColumnDefinition());
        genFields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        genFields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        genFields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        genFields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        enableAutoSave = new CheckBox
        {
            Content = "_Enable AutoSave",
            IsChecked = gen.EnableAutoSave,
            Foreground = TextBrush(),
            Margin = new Thickness(0, 4, 0, 8)
        };
        AutomationProperties.SetAutomationId(enableAutoSave, "EnableAutoSave");
        Grid.SetRow(enableAutoSave, 0); Grid.SetColumnSpan(enableAutoSave, 2);
        genFields.Children.Add(enableAutoSave);

        var intervalHost = new Grid();
        intervalHost.ColumnDefinitions.Add(new ColumnDefinition());
        intervalHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        autoSaveInterval = new TextBox { Text = gen.AutoSaveIntervalMinutes.ToString(CultureInfo.CurrentCulture) };
        AutomationProperties.SetAutomationId(autoSaveInterval, "AutoSaveInterval");
        AutomationProperties.SetName(autoSaveInterval, "AutoSave interval (minutes)");
        intervalHost.Children.Add(autoSaveInterval);

        var intervalSpinner = new UniformGrid { Rows = 2, Margin = new Thickness(0, 2, 2, 2) };
        foreach (var (lbl, step) in new[] { ("Increase interval", 1), ("Decrease interval", -1) })
        {
            var btn = new RepeatButton { Content = step > 0 ? "▴" : "▾", Padding = new Thickness(0), Focusable = false, Style = FindStyle("NumericSpinButton") };
            AutomationProperties.SetName(btn, lbl);
            btn.Click += (_, _) =>
            {
                if (!int.TryParse(autoSaveInterval.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var v)) return;
                autoSaveInterval.Text = Math.Clamp(v + step, 1, 120).ToString(CultureInfo.CurrentCulture);
            };
            intervalSpinner.Children.Add(btn);
        }
        Grid.SetColumn(intervalSpinner, 1); intervalHost.Children.Add(intervalSpinner);

        var intervalLabel = new Label { Content = "AutoSave _interval (minutes):", Target = autoSaveInterval, Foreground = TextBrush() };
        Grid.SetRow(intervalLabel, 1); genFields.Children.Add(intervalLabel);
        Grid.SetRow(intervalHost, 1); Grid.SetColumn(intervalHost, 1); genFields.Children.Add(intervalHost);

        createProjectBackups = new CheckBox
        {
            Content = "_Create project file backups (.bak)",
            IsChecked = gen.CreateProjectBackups,
            Foreground = TextBrush(),
            Margin = new Thickness(0, 8, 0, 4)
        };
        AutomationProperties.SetAutomationId(createProjectBackups, "CreateProjectBackups");
        Grid.SetRow(createProjectBackups, 2); Grid.SetColumnSpan(createProjectBackups, 2);
        genFields.Children.Add(createProjectBackups);

        useProjectDirForAutoSave = new CheckBox
        {
            Content = "Use project _directory for AutoSave",
            IsChecked = gen.UseProjectDirectoryForAutoSave,
            Foreground = TextBrush(),
            Margin = new Thickness(0, 4, 0, 4)
        };
        AutomationProperties.SetAutomationId(useProjectDirForAutoSave, "UseProjectDirForAutoSave");
        Grid.SetRow(useProjectDirForAutoSave, 3); Grid.SetColumnSpan(useProjectDirForAutoSave, 2);
        genFields.Children.Add(useProjectDirForAutoSave);

        var generalTab = new TabItem
        {
            Header = "General",
            Content = new GroupBox
            {
                Header = "AutoSave and Project Backup",
                Style = FindStyle("PreferencesGroup"),
                Content = genFields,
                Margin = new Thickness(8)
            }
        };
        tabs.Items.Add(generalTab);

        // 2. Editing Tab (Cut-to-overlap)
        var fields = new Grid { Margin = new Thickness(12) };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        fields.ColumnDefinitions.Add(new ColumnDefinition());
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var amountHost = new Grid();
        amountHost.ColumnDefinitions.Add(new ColumnDefinition());
        amountHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        amount = new TextBox { Text = settings.CutOverlapSeconds.ToString("0.###", CultureInfo.CurrentCulture) };
        AutomationProperties.SetAutomationId(amount, "CutOverlapSeconds");
        AutomationProperties.SetName(amount, "Amount (seconds)");
        amountHost.Children.Add(amount);
        var spinner = new UniformGrid { Rows = 2, Margin = new Thickness(0, 2, 2, 2) };
        foreach (var (label, step) in new[] { ("Increase amount", .01), ("Decrease amount", -.01) })
        {
            var button = new RepeatButton { Content = step > 0 ? "▴" : "▾", Padding = new Thickness(0), Focusable = false,
                Style = FindStyle("NumericSpinButton") };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) =>
            {
                if (!double.TryParse(amount.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value)) return;
                amount.Text = Math.Clamp(Math.Round(value + step, 3), .001, 3600).ToString("0.###", CultureInfo.CurrentCulture);
            };
            spinner.Children.Add(button);
        }
        Grid.SetColumn(spinner, 1); amountHost.Children.Add(spinner);
        var amountLabel = new Label { Content = "_Amount (seconds):", Target = amount, Foreground = TextBrush() };
        fields.Children.Add(amountLabel); Grid.SetColumn(amountHost, 1); fields.Children.Add(amountHost);
        alignment = new ComboBox { ItemsSource = new[] { "Before cut", "Centered on cut", "After cut" },
            SelectedIndex = (int)settings.CutOverlapAlignment, Margin = new Thickness(2, 8, 2, 2) };
        AutomationProperties.SetAutomationId(alignment, "CutOverlapAlignment");
        AutomationProperties.SetName(alignment, "Alignment");
        var alignmentLabel = new Label { Content = "_Alignment:", Target = alignment, Margin = new Thickness(0, 6, 0, 0),
            Foreground = TextBrush() };
        Grid.SetRow(alignmentLabel, 1); fields.Children.Add(alignmentLabel);
        Grid.SetRow(alignment, 1); Grid.SetColumn(alignment, 1); fields.Children.Add(alignment);

        var editingTab = new TabItem
        {
            Header = "Editing",
            IsSelected = true,
            Content = new GroupBox
            {
                Header = "Cut-to-overlap conversion",
                Style = FindStyle("PreferencesGroup"),
                Content = fields,
                Margin = new Thickness(8)
            }
        };
        tabs.Items.Add(editingTab);

        // 3. Updates Tab (Automatic Startup Check & Manual Scan)
        var updateStack = new StackPanel { Margin = new Thickness(12) };

        checkUpdatesOnStartup = new CheckBox
        {
            Content = "_Check for updates automatically on startup",
            IsChecked = gen.CheckForUpdatesOnStartup,
            Foreground = TextBrush(),
            Margin = new Thickness(0, 4, 0, 10)
        };
        AutomationProperties.SetAutomationId(checkUpdatesOnStartup, "CheckForUpdatesOnStartup");
        updateStack.Children.Add(checkUpdatesOnStartup);

        var verText = new TextBlock
        {
            Text = $"Current version: OpenRend v{AppVersion.CurrentVersionString}",
            Foreground = TextBrush(),
            Margin = new Thickness(0, 0, 0, 6)
        };
        updateStack.Children.Add(verText);

        var statusText = new TextBlock
        {
            Text = "Click 'Scan for updates' to check GitHub for the latest release.",
            Foreground = TextBrush(),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        AutomationProperties.SetAutomationId(statusText, "UpdateStatusText");
        updateStack.Children.Add(statusText);

        var progBar = new ProgressBar
        {
            Height = 4,
            Visibility = Visibility.Collapsed,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Margin = new Thickness(0, 0, 0, 6)
        };
        AutomationProperties.SetAutomationId(progBar, "UpdateProgressBar");
        updateStack.Children.Add(progBar);

        var btnHost = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };

        var scanBtn = new Button
        {
            Content = "Scan for updates",
            MinWidth = 120,
            Margin = new Thickness(0, 0, 8, 0)
        };
        AutomationProperties.SetAutomationId(scanBtn, "ScanForUpdatesButton");
        btnHost.Children.Add(scanBtn);

        var installBtn = new Button
        {
            Content = "Install update",
            MinWidth = 110,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 8, 0)
        };
        AutomationProperties.SetAutomationId(installBtn, "InstallUpdateButton");
        btnHost.Children.Add(installBtn);

        var viewBtn = new Button
        {
            Content = "View on GitHub",
            MinWidth = 120,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetAutomationId(viewBtn, "ViewReleaseButton");
        btnHost.Children.Add(viewBtn);

        updateStack.Children.Add(btnHost);

        var notesText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = TextBrush(),
            Margin = new Thickness(4)
        };
        var notesScroll = new ScrollViewer
        {
            Content = notesText,
            MaxHeight = 80,
            Visibility = Visibility.Collapsed,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 4, 0, 0)
        };
        updateStack.Children.Add(notesScroll);

        var service = updateService ?? new GitHubUpdateService();

        scanBtn.Click += async (_, _) =>
        {
            scanBtn.IsEnabled = false;
            statusText.Text = "Scanning GitHub for newer releases...";
            try
            {
                var check = await service.CheckForUpdatesAsync();
                if (check.IsUpdateAvailable && check.Release is not null)
                {
                    var rel = check.Release;
                    statusText.Text = $"Update available: {rel.TagName} (current: v{AppVersion.CurrentVersionString})";
                    notesText.Text = string.IsNullOrWhiteSpace(rel.Body) ? "No release notes provided." : rel.Body;
                    notesScroll.Visibility = Visibility.Visible;
                    installBtn.Visibility = Visibility.Visible;
                    viewBtn.Visibility = Visibility.Visible;

                    viewBtn.Click += (_, _) =>
                    {
                        try { Process.Start(new ProcessStartInfo { FileName = rel.HtmlUrl, UseShellExecute = true }); } catch { }
                    };

                    installBtn.Click += async (_, _) =>
                    {
                        var asset = rel.GetUpdateAsset();
                        if (asset == null)
                        {
                            statusText.Text = "No direct Windows update package found. Opening release page...";
                            try { Process.Start(new ProcessStartInfo { FileName = rel.HtmlUrl, UseShellExecute = true }); } catch { }
                            return;
                        }
                        installBtn.IsEnabled = false;
                        scanBtn.IsEnabled = false;
                        viewBtn.IsEnabled = false;
                        progBar.Visibility = Visibility.Visible;
                        statusText.Text = $"Downloading {asset.Name}...";
                        try
                        {
                            var tempDir = Path.Combine(Path.GetTempPath(), "OpenRend-Update");
                            var progress = new Progress<double>(p =>
                            {
                                progBar.Value = p * 100;
                                statusText.Text = $"Downloading {asset.Name}: {(int)(p * 100)}%";
                            });
                            var downloaded = await service.DownloadUpdateAsync(asset, tempDir, progress);
                            statusText.Text = "Starting installer...";
                            GitHubUpdateService.LaunchUpdateInstaller(downloaded, Environment.ProcessId, AppContext.BaseDirectory);
                            Application.Current.Shutdown(0);
                        }
                        catch (Exception ex)
                        {
                            statusText.Text = $"Download failed: {ex.Message}";
                            installBtn.IsEnabled = true;
                            scanBtn.IsEnabled = true;
                            viewBtn.IsEnabled = true;
                        }
                    };
                }
                else if (check.Status == UpdateStatus.UpToDate)
                {
                    statusText.Text = $"OpenRend is up to date (version {AppVersion.CurrentVersionString}).";
                    notesScroll.Visibility = Visibility.Collapsed;
                    installBtn.Visibility = Visibility.Collapsed;
                    viewBtn.Visibility = Visibility.Collapsed;
                }
                else
                {
                    statusText.Text = $"Scan failed: {check.ErrorMessage}";
                }
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                scanBtn.IsEnabled = true;
            }
        };

        var updatesTab = new TabItem
        {
            Header = "Updates",
            Content = new GroupBox
            {
                Header = "Software Updates & GitHub Releases",
                Style = FindStyle("PreferencesGroup"),
                Content = updateStack,
                Margin = new Thickness(8)
            }
        };
        AutomationProperties.SetAutomationId(updatesTab, "UpdatesTab");
        tabs.Items.Add(updatesTab);

        root.Children.Add(tabs);
        ok.Click += (_, _) =>
        {
            if (!double.TryParse(amount.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
                !double.IsFinite(value) || value is <= 0 or > 3600 || alignment.SelectedIndex < 0)
            { error.Text = "Enter an amount greater than zero and no more than 3600 seconds."; amount.Focus(); return; }

            if (!int.TryParse(autoSaveInterval.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var intervalVal) ||
                intervalVal is < 1 or > 120)
            { error.Text = "AutoSave interval must be between 1 and 120 minutes."; autoSaveInterval.Focus(); return; }

            Result = new EditingPreferences { CutOverlapSeconds = value, CutOverlapAlignment = (CutTransitionAlignment)alignment.SelectedIndex };
            GeneralResult = new GeneralPreferences
            {
                EnableAutoSave = enableAutoSave.IsChecked == true,
                AutoSaveIntervalMinutes = intervalVal,
                CreateProjectBackups = createProjectBackups.IsChecked == true,
                UseProjectDirectoryForAutoSave = useProjectDirForAutoSave.IsChecked == true,
                CheckForUpdatesOnStartup = checkUpdatesOnStartup.IsChecked == true
            };
            try { DialogResult = true; } catch (InvalidOperationException) { }
        };
        Content = root;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
