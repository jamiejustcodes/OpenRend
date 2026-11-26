using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenRend.Core;

namespace OpenRend.App;

public partial class MainWindow
{
    private void InitializeRulerEvents()
    {
        Timeline.RulerFormatChanged += format => Guard(() =>
        {
            SetRulerFormat(format);
        });

        Timeline.SetTimeAtCursorRequested += () => Guard(OpenSetTimeAtCursorDialog);
        Timeline.InsertTimeRequested += () => InsertTimeClick(this, new RoutedEventArgs());
        Timeline.DeleteTimeRequested += () => DeleteTimeClick(this, new RoutedEventArgs());

        SyncRulerFormatMenuChecks();
    }

    private void RulerFormatMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string tag && Enum.TryParse<RulerFormat>(tag, out var format))
        {
            SetRulerFormat(format);
        }
    }

    public void SetRulerFormat(RulerFormat format)
    {
        vm.SetRulerFormat(format);
        Timeline.RulerFormat = format;
        SyncRulerFormatMenuChecks();
        UpdatePosition();
    }

    private void SyncRulerFormatMenuChecks()
    {
        if (RulerFormatMenu == null) return;

        var current = vm.Project.RulerFormat.ToString();
        foreach (var item in RulerFormatMenu.Items)
        {
            if (item is MenuItem mi && mi.Tag is string tag)
            {
                mi.IsChecked = string.Equals(tag, current, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private void Timecode_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        var menu = new ContextMenu();

        var formatSubmenu = new MenuItem { Header = "Time &Format" };
        var activeFormat = vm.Project.RulerFormat;

        foreach (var fmt in RulerFormatExtensions.AllStandardFormats)
        {
            var mi = new MenuItem
            {
                Header = fmt.ToMenuHeader(),
                IsCheckable = true,
                IsChecked = activeFormat == fmt
            };
            var chosen = fmt;
            mi.Click += (_, _) => SetRulerFormat(chosen);
            formatSubmenu.Items.Add(mi);
        }

        menu.Items.Add(formatSubmenu);
        var rootSetTime = new MenuItem { Header = "Set T&ime at Cursor…" };
        rootSetTime.Click += (_, _) => OpenSetTimeAtCursorDialog();
        menu.Items.Add(rootSetTime);

        menu.IsOpen = true;
    }

    private void Timecode_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            e.Handled = true;
            OpenSetTimeAtCursorDialog();
        }
    }

    public void OpenSetTimeAtCursorDialog()
    {
        var dialog = new SetTimeAtCursorWindow(
            vm.Position,
            vm.Project.FrameRate,
            vm.Project.RulerFormat,
            vm.Project.AudioSampleRate,
            vm.Project.BeatsPerMinute,
            vm.Project.BeatsPerMeasure,
            vm.Project.RulerStartTimeFrames,
            vm.Project.DurationFrames)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.ResultFrame.HasValue)
        {
            vm.SetTimeAtCursor(dialog.ResultFrame.Value);
            UpdatePosition();
        }
    }
}
