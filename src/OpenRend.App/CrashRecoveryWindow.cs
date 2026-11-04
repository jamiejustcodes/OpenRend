using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenRend.App;

/// <summary>
/// 1:1 Vegas Pro Crash Recovery dialog prompted when an abnormal session termination
/// is detected upon startup.
/// </summary>
public sealed class CrashRecoveryWindow : Window
{
    public bool ShouldRestore { get; private set; }

    public CrashRecoveryWindow(CrashRecoveryCandidate candidate, Window? owner = null)
    {
        Owner = owner;
        Title = "OpenRend";
        Width = 490;
        Height = 230;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC));
        ResizeMode = ResizeMode.NoResize;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Warning Icon
        var iconText = new TextBlock
        {
            Text = "⚠",
            FontSize = 32,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xC0, 0x7B)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(iconText, 0);
        contentGrid.Children.Add(iconText);

        // Prompt Text (verbatim from Vegas Pro)
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        var mainPrompt = new TextBlock
        {
            Text = "The last session did not complete properly.\n\nYou can restore the autosaved version of the project that was opened, but you should use the Save As command to rename it immediately.\n\nWould you like to restore the autosaved project?",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 18,
            Foreground = Foreground
        };
        textStack.Children.Add(mainPrompt);

        if (!string.IsNullOrEmpty(candidate.OriginalProjectPath))
        {
            var projectInfo = new TextBlock
            {
                Text = $"\nTarget: {System.IO.Path.GetFileName(candidate.OriginalProjectPath)} ({candidate.SnapshotTimeUtc.ToLocalTime():g})",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E))
            };
            textStack.Children.Add(projectInfo);
        }

        Grid.SetColumn(textStack, 1);
        contentGrid.Children.Add(textStack);

        Grid.SetRow(contentGrid, 0);
        root.Children.Add(contentGrid);

        // Buttons
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var yesBtn = new Button
        {
            Content = "_Yes",
            Width = 75,
            Height = 24,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Foreground = Foreground
        };
        AutomationProperties.SetAutomationId(yesBtn, "RestoreAutosaveYes");
        yesBtn.Click += (_, _) =>
        {
            ShouldRestore = true;
            DialogResult = true;
        };

        var noBtn = new Button
        {
            Content = "_No",
            Width = 75,
            Height = 24,
            IsCancel = true,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Foreground = Foreground
        };
        AutomationProperties.SetAutomationId(noBtn, "RestoreAutosaveNo");
        noBtn.Click += (_, _) =>
        {
            ShouldRestore = false;
            DialogResult = false;
        };

        btnPanel.Children.Add(yesBtn);
        btnPanel.Children.Add(noBtn);
        Grid.SetRow(btnPanel, 1);
        root.Children.Add(btnPanel);

        Content = root;
    }
}

