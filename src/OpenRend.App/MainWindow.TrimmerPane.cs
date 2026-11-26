using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace OpenRend.App;

public partial class MainWindow
{
    private Window? trimmerWindow;
    private PaneBounds? trimmerBounds;
    private bool trimmerFloats;
    private bool HasSeparateTrimmerDock { get; set; }
    private TabControl TrimmerDockTabs => HasSeparateTrimmerDock ? SourceDeckTabs : PreviewDeckTabs;
    private Popup? trimmerDockOutline;
    private TrimmerDrag? trimmerDrag;

    private sealed record TrimmerDrag(UIElement Capture, NativePoint Pointer, PaneBounds? Bounds)
    {
        public bool Started { get; set; }
    }

    private void InitializeTrimmerPane()
    {
        TrimmerTabLabel.PreviewMouseLeftButtonDown += (_, e) =>
        {
            TrimmerDockTabs.SelectedItem = TrimmerTab;
            TrimmerTab.Focus();
            e.Handled = BeginTrimmerDrag(this, null);
        };
        AttachTrimmerDragHandlers(this);
    }

    private void ShowTrimmerPane()
    {
        if (trimmerFloats)
        {
            if (trimmerWindow is null)
                OpenFloatingTrimmer(trimmerBounds ?? DefaultTrimmerBounds(), activate: true, restore: true);
            trimmerWindow?.Activate();
        }
        else
        {
            TrimmerTab.Visibility = Visibility.Visible;
            UpdateSourceDockColumns();
            TrimmerDockTabs.SelectedItem = TrimmerTab;
        }
        TrimmerDeck.Focus();
    }

    private void CloseTrimmerClick(object sender, RoutedEventArgs e)
    {
        TrimmerDeck.StopPlayback();
        HideTrimmerTab();
        PreviewDeckTabs.Focus();
    }

    private bool HandleTrimmerShortcut(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers != ModifierKeys.Alt || key is not (Key.D2 or Key.NumPad2))
            return false;

        e.Handled = true;
        if (e.IsRepeat || vm.Busy)
            return true;

        if (trimmerWindow is { } floating)
            floating.Close();
        else if (TrimmerTab.Visibility == Visibility.Visible && TrimmerDockTabs.SelectedItem == TrimmerTab)
            CloseTrimmerClick(this, e);
        else
            ShowTrimmerPane();
        return true;
    }

    private void HideTrimmerTab()
    {
        if (!HasSeparateTrimmerDock && PreviewDeckTabs.SelectedItem == TrimmerTab)
            PreviewDeckTabs.SelectedIndex = 0;
        TrimmerTab.Visibility = Visibility.Collapsed;
        UpdateSourceDockColumns();
    }

    private TrimmerPaneLayout CaptureTrimmerPane() => new(trimmerFloats,
        trimmerWindow is not null || TrimmerTab.Visibility == Visibility.Visible,
        ReadTrimmerBounds() ?? trimmerBounds)
    {
        SeparateDock = HasSeparateTrimmerDock,
        DockWidth = sourceDockWidth
    };

    private void RestoreTrimmerPane(TrimmerPaneLayout? layout)
    {
        CancelTrimmerDrag();
        CloseFloatingTrimmer();
        var separateDock = layout?.SeparateDock == true;
        if (separateDock != HasSeparateTrimmerDock)
        {
            TrimmerDockTabs.Items.Remove(TrimmerTab);
            HasSeparateTrimmerDock = separateDock;
            TrimmerDockTabs.Items.Add(TrimmerTab);
        }
        sourceDockWidth = layout is { DockWidth: >= 240 } && double.IsFinite(layout.DockWidth) ? layout.DockWidth : 568;
        trimmerFloats = layout?.Floating == true;
        trimmerBounds = layout?.Bounds;
        if (trimmerFloats || layout?.Visible == false)
            HideTrimmerTab();
        else
        {
            TrimmerTab.Visibility = Visibility.Visible;
            UpdateSourceDockColumns();
            if (HasSeparateTrimmerDock)
                SourceDeckTabs.SelectedItem = TrimmerTab;
        }

        if (layout is { Floating: true, Visible: true })
            OpenFloatingTrimmer(trimmerBounds ?? DefaultTrimmerBounds(), activate: false, restore: true);
    }

    private PaneBounds DefaultTrimmerBounds()
    {
        var position = PointToScreen(new Point(48, 80));
        return new PaneBounds((int)position.X, (int)position.Y, 600, Workspace.ActualHeight);
    }

    private void OpenFloatingTrimmer(PaneBounds bounds, bool activate, bool restore)
    {
        HideTrimmerTab();
        TrimmerTab.Content = null;

        var window = new Window
        {
            Title = "Trimmer",
            Owner = this,
            ShowInTaskbar = false,
            ShowActivated = activate,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResize,
            Width = ClampPaneSize(bounds.Width, 600, 280),
            Height = ClampPaneSize(bounds.Height, 400, 180),
            MinWidth = 280,
            MinHeight = 180,
            Opacity = 0
        };
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(5),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });

        var title = new DockPanel { Height = 30, Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)) };
        var close = new Button
        {
            Width = 19,
            Height = 19,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 5, 0),
            Content = new Image { Source = (ImageSource)FindResource("Icon.Close"), Width = 8, Height = 8 },
            Background = new SolidColorBrush(Color.FromRgb(178, 77, 77)),
            Style = (Style)FindResource("CaptionButton")
        };
        AutomationProperties.SetName(close, "Close Trimmer");
        close.Click += (_, _) => window.Close();
        DockPanel.SetDock(close, Dock.Right);
        title.Children.Add(close);
        var caption = new Border
        {
            Background = Brushes.Transparent,
            Child = new TextBlock
            {
                Text = "Trimmer", FontSize = 11, Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        caption.MouseLeftButtonDown += (_, e) => e.Handled = BeginTrimmerDrag(caption, ReadTrimmerBounds());
        title.Children.Add(caption);

        var panel = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);
        panel.Children.Add(TrimmerDeck);
        window.Content = panel;
        window.Closing += (_, _) => trimmerBounds = ReadTrimmerBounds() ?? trimmerBounds;
        window.Closed += (_, _) =>
        {
            panel.Children.Remove(TrimmerDeck);
            TrimmerTab.Content = TrimmerDeck;
            trimmerWindow = null;
            ShowTrimmerDockOutline(false);
            if (trimmerFloats)
                TrimmerDeck.StopPlayback();
        };
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && CancelTrimmerDrag())
                e.Handled = true;
            else
                HandleTrimmerShortcut(e);
        };
        AttachTrimmerDragHandlers(caption);
        trimmerWindow = window;
        window.Show();
        PlaceTrimmer(bounds.Left, bounds.Top, restore);
        window.Opacity = 1;
    }

    private static double ClampPaneSize(double size, double fallback, double minimum) =>
        double.IsFinite(size) ? Math.Clamp(size, minimum, 4096) : fallback;

    private void CloseFloatingTrimmer()
    {
        trimmerFloats = false;
        trimmerWindow?.Close();
    }

    private PaneBounds? ReadTrimmerBounds()
    {
        if (trimmerWindow is not { } window ||
            !GetWindowRect(new WindowInteropHelper(window).Handle, out var rect))
            return null;
        return new PaneBounds(rect.Left, rect.Top, window.ActualWidth, window.ActualHeight);
    }

    private void PlaceTrimmer(int x, int y, bool restore)
    {
        if (trimmerWindow is null)
            return;
        var hwnd = new WindowInteropHelper(trimmerWindow).Handle;
        const uint noSize = 0x1, noZOrder = 0x4, noActivate = 0x10;
        SetWindowPos(hwnd, 0, x, y, 0, 0, noSize | noZOrder | noActivate);
        if (!restore || !GetWindowRect(hwnd, out var rect))
            return;

        // The saved position uses screen pixels so monitors can have different DPI.
        var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;
        var width = Math.Min(rect.Right - rect.Left, info.Work.Right - info.Work.Left);
        var height = Math.Min(rect.Bottom - rect.Top, info.Work.Bottom - info.Work.Top);
        x = Math.Clamp(rect.Left, info.Work.Left, info.Work.Right - width);
        y = Math.Clamp(rect.Top, info.Work.Top, info.Work.Bottom - height);
        SetWindowPos(hwnd, 0, x, y, width, height, noZOrder | noActivate);
    }

    private bool BeginTrimmerDrag(UIElement capture, PaneBounds? bounds)
    {
        if (!GetCursorPos(out var pointer) || !Mouse.Capture(capture))
            return false;
        trimmerDrag = new TrimmerDrag(capture, pointer, bounds);
        return true;
    }

    private void AttachTrimmerDragHandlers(UIElement surface)
    {
        surface.PreviewMouseMove += MoveTrimmerDrag;
        surface.PreviewMouseLeftButtonUp += (_, _) => FinishTrimmerDrag(cancel: false);
        surface.LostMouseCapture += (_, e) =>
        {
            if (e.OriginalSource == trimmerDrag?.Capture)
                CancelTrimmerDrag();
        };
    }

    private void MoveTrimmerDrag(object sender, MouseEventArgs e)
    {
        if (trimmerDrag is not { } drag || sender != drag.Capture ||
            e.LeftButton != MouseButtonState.Pressed || !GetCursorPos(out var pointer))
            return;

        var dpi = VisualTreeHelper.GetDpi(drag.Capture);
        if (drag.Bounds is { } original)
        {
            if (!drag.Started &&
                Math.Abs(pointer.X - drag.Pointer.X) < SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX &&
                Math.Abs(pointer.Y - drag.Pointer.Y) < SystemParameters.MinimumVerticalDragDistance * dpi.DpiScaleY)
                return;
            drag.Started = true;
            PlaceTrimmer(original.Left + pointer.X - drag.Pointer.X, original.Top + pointer.Y - drag.Pointer.Y, restore: false);
        }
        else
        {
            // Leave the bottom tab strip before detaching the pane.
            if (!drag.Started && drag.Pointer.Y - pointer.Y < 30 * dpi.DpiScaleY)
                return;
            drag.Started = true;
            var x = pointer.X - (int)(20 * dpi.DpiScaleX);
            var y = pointer.Y - (int)(5 * dpi.DpiScaleY);
            if (trimmerWindow is null)
            {
                trimmerFloats = true;
                OpenFloatingTrimmer(new PaneBounds(x, y, trimmerBounds?.Width ?? 600,
                    trimmerBounds?.Height ?? Workspace.ActualHeight), activate: false, restore: false);
            }
            else
                PlaceTrimmer(x, y, restore: false);
        }
        ShowTrimmerDockOutline(IsTrimmerDockTarget(pointer));
    }

    private bool IsTrimmerDockTarget(NativePoint pointer)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || !IsVisible || WindowState == WindowState.Minimized)
            return false;
        var local = PreviewDeckTabs.PointFromScreen(new Point(pointer.X, pointer.Y));
        return new Rect(0, 0, PreviewDeckTabs.ActualWidth, PreviewDeckTabs.ActualHeight).Contains(local);
    }

    private void ShowTrimmerDockOutline(bool visible)
    {
        if (!visible)
        {
            if (trimmerDockOutline is not null)
                trimmerDockOutline.IsOpen = false;
            return;
        }
        trimmerDockOutline ??= new Popup
        {
            PlacementTarget = PreviewDeckTabs,
            Placement = PlacementMode.Relative,
            AllowsTransparency = true,
            IsHitTestVisible = false,
            Child = new Border
            {
                BorderThickness = new Thickness(3),
                BorderBrush = (Brush)FindResource("AccentBrush"),
                IsHitTestVisible = false
            }
        };
        trimmerDockOutline.Width = PreviewDeckTabs.ActualWidth;
        trimmerDockOutline.Height = PreviewDeckTabs.ActualHeight;
        trimmerDockOutline.IsOpen = true;
    }

    private bool CancelTrimmerDrag() => FinishTrimmerDrag(cancel: true);

    private bool FinishTrimmerDrag(bool cancel)
    {
        if (trimmerDrag is not { } drag)
            return false;
        trimmerDrag = null;
        if (Mouse.Captured == drag.Capture)
            Mouse.Capture(null);
        ShowTrimmerDockOutline(false);
        if (!drag.Started || trimmerWindow is null)
            return true;

        if (cancel && drag.Bounds is { } original)
            PlaceTrimmer(original.Left, original.Top, restore: false);
        else if (cancel || (GetCursorPos(out var pointer) && IsTrimmerDockTarget(pointer)))
        {
            CloseFloatingTrimmer();
            Activate();
            ShowTrimmerPane();
        }
        else
        {
            if (ReadTrimmerBounds() is { } dropped)
                PlaceTrimmer(dropped.Left, dropped.Top, restore: true);
            trimmerBounds = ReadTrimmerBounds();
            trimmerWindow.Activate();
            TrimmerDeck.Focus();
        }
        return true;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
}
