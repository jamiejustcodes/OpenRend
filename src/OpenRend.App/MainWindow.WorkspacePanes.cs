using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace OpenRend.App;

public partial class MainWindow
{
    private DockablePane trimmerPane = null!;
    private readonly Dictionary<string, DockablePane> workspacePanes = new(StringComparer.Ordinal);
    private readonly Dictionary<TabItem, int> workspaceTabOrder = [];
    private bool HasSeparateTrimmerDock { get; set; }
    private Popup? paneDockOutline;
    private PaneDrag? paneDrag;
    private bool paneBoundsCheckQueued;

    private sealed class DockablePane(string name, TabControl dock, TabItem tab, Action? onHidden = null)
    {
        public string Name { get; } = name;
        public TabControl HomeDock { get; } = dock;
        public TabControl Dock { get; set; } = dock;
        public TabItem Tab { get; } = tab;
        public Action? OnHidden { get; } = onHidden;
        public Window? Window { get; set; }
        public PaneBounds? Bounds { get; set; }
        public bool Floats { get; set; }
        public bool IsDocked => Tab.Visibility == Visibility.Visible;
    }

    private sealed record PaneDrag(DockablePane Pane, UIElement Capture, NativePoint Pointer, PaneBounds? Bounds)
    {
        public bool Started { get; set; }
    }

    private sealed record PaneDockTarget(TabControl Dock, FrameworkElement OutlineOwner, Rect Outline);

    private void InitializeWorkspacePanes()
    {
        foreach (var group in new[] { ToolsTabs, PreviewDeckTabs, MasterBusTabs })
        foreach (var tab in group.Items.OfType<TabItem>())
            workspaceTabOrder.Add(tab, workspaceTabOrder.Count);

        trimmerPane = new DockablePane("Trimmer", PreviewDeckTabs, TrimmerTab, TrimmerDeck.StopPlayback);
        BindPaneDrag(trimmerPane, TrimmerTabLabel);
        AttachPaneDragHandlers(this);

        RegisterWorkspacePane("VideoPreview", PreviewDeckTabs, VideoPreviewTab);
        RegisterWorkspacePane("Explorer", ToolsTabs, ExplorerTab);
        RegisterWorkspacePane("ProjectMedia", ToolsTabs, ProjectMediaTab);
        RegisterWorkspacePane("VideoEffects", ToolsTabs, VideoEffectsTab);
        RegisterWorkspacePane("Transitions", ToolsTabs, TransitionsTab);
        RegisterWorkspacePane("ColorGrading", ToolsTabs, ColorGradingTab, "Alt+G");
        RegisterWorkspacePane("MasterBus", MasterBusTabs, MasterBusTab);
    }

    private void RegisterWorkspacePane(string id, TabControl dock, TabItem tab, string? shortcut = null)
    {
        var name = (string)tab.Header;
        var pane = new DockablePane(name, dock, tab);
        workspacePanes.Add(id, pane);
        AutomationProperties.SetName(tab, name);

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(label);
        var close = CreatePaneCloseButton(pane);
        close.Margin = new Thickness(4, 0, 0, 0);
        close.SetBinding(VisibilityProperty, new Binding(nameof(TabItem.IsSelected))
        {
            Source = tab,
            Converter = new BooleanToVisibilityConverter()
        });
        close.Click += (_, _) => HidePane(pane);
        header.Children.Add(close);

        var menu = new ContextMenu();
        var floatItem = new MenuItem { Header = "Float pane" };
        floatItem.Click += (_, _) =>
        {
            pane.Floats = true;
            OpenFloatingPane(pane, pane.Bounds ?? DefaultPaneBounds(pane), activate: true, restore: true);
        };
        menu.Items.Add(floatItem);
        tab.Header = header;
        tab.ContextMenu = menu;
        BindPaneDrag(pane, tab, close);

        var show = new MenuItem { Header = name };
        if (shortcut is not null) show.InputGestureText = shortcut;
        AutomationProperties.SetAutomationId(show, $"ShowPane_{id}");
        show.Click += (_, _) => ShowPane(pane);
        WorkspaceWindowMenu.Items.Insert(WorkspaceWindowMenu.Items.Count - 1, show);
    }

    private void BindPaneDrag(DockablePane pane, FrameworkElement tabHeader, Button? close = null)
    {
        tabHeader.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (close?.IsMouseOver == true || !new Rect(tabHeader.RenderSize).Contains(e.GetPosition(tabHeader)))
                return;
            pane.Dock.SelectedItem = pane.Tab;
            pane.Tab.Focus();
            e.Handled = BeginPaneDrag(pane, this, null);
        };
    }

    private Button CreatePaneCloseButton(DockablePane pane)
    {
        var close = new Button
        {
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Content = new Image { Source = (ImageSource)FindResource("Icon.Close"), Width = 8, Height = 8 },
            Style = (Style)FindResource("CaptionButton")
        };
        AutomationProperties.SetName(close, $"Close {pane.Name}");
        return close;
    }

    private void ShowTrimmerPane() => ShowPane(trimmerPane);

    private void ShowPane(DockablePane pane)
    {
        if (pane.Floats)
        {
            if (pane.Window is null)
                OpenFloatingPane(pane, pane.Bounds ?? DefaultPaneBounds(pane), activate: true, restore: true);
            pane.Window?.Activate();
        }
        else
        {
            pane.Tab.Visibility = Visibility.Visible;
            UpdatePaneDockLayout(pane);
            pane.Dock.SelectedItem = pane.Tab;
            pane.Tab.Focus();
        }

        if (pane == trimmerPane)
            TrimmerDeck.Focus();
    }

    private void CloseTrimmerClick(object sender, RoutedEventArgs e) => HidePane(trimmerPane);

    private void HidePane(DockablePane pane)
    {
        if (pane.Window is { } window)
        {
            window.Close();
            return;
        }

        pane.OnHidden?.Invoke();
        HidePaneTab(pane);
        (pane.Dock.Visibility == Visibility.Visible ? pane.Dock : PreviewDeckTabs).Focus();
    }

    private bool HandleTrimmerShortcut(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers != ModifierKeys.Alt || key is not (Key.D2 or Key.NumPad2))
            return false;

        e.Handled = true;
        if (e.IsRepeat || vm.Busy)
            return true;

        TogglePane(trimmerPane);
        return true;
    }

    private void TogglePane(DockablePane pane)
    {
        if (pane.Window is not null || (pane.IsDocked && pane.Dock.SelectedItem == pane.Tab))
            HidePane(pane);
        else
            ShowPane(pane);
    }

    private void HidePaneTab(DockablePane pane)
    {
        pane.Tab.Visibility = Visibility.Collapsed;
        if (pane.Dock.SelectedItem == pane.Tab)
            pane.Dock.SelectedItem = pane.Dock.Items.OfType<TabItem>().FirstOrDefault(tab => tab.Visibility == Visibility.Visible);
        UpdatePaneDockLayout(pane);
    }

    private void UpdatePaneDockLayout(DockablePane pane)
    {
        if (pane == trimmerPane)
            UpdateSourceDockColumns();
        else if (pane.Dock == UpperDockTabs)
            UpdateUpperDockRows();
    }

    private TrimmerPaneLayout CaptureTrimmerPane() => new(trimmerPane.Floats,
        trimmerPane.Window is not null || trimmerPane.IsDocked, ReadPaneBounds(trimmerPane) ?? trimmerPane.Bounds)
    {
        SeparateDock = HasSeparateTrimmerDock,
        DockWidth = sourceDockWidth
    };

    private Dictionary<string, WorkspacePaneLayout>? CaptureWorkspacePanes()
    {
        var layouts = new Dictionary<string, WorkspacePaneLayout>(StringComparer.Ordinal);
        foreach (var (key, pane) in workspacePanes)
        {
            var bounds = ReadPaneBounds(pane) ?? pane.Bounds;
            var dockGroup = pane.Dock == pane.HomeDock ? null : pane.Dock.Name;
            if (pane.Floats || !pane.IsDocked || bounds is not null || dockGroup is not null)
                layouts.Add(key, new(pane.Floats, pane.Window is not null || pane.IsDocked, bounds) { DockGroup = dockGroup });
        }
        return layouts.Count > 0 ? layouts : null;
    }

    private void RestoreTrimmerPane(TrimmerPaneLayout? layout)
    {
        CancelWorkspacePaneDrag();
        CloseFloatingPane(trimmerPane);
        var separateDock = layout?.SeparateDock == true;
        if (separateDock != HasSeparateTrimmerDock)
        {
            HasSeparateTrimmerDock = separateDock;
            MovePaneToDock(trimmerPane, separateDock ? SourceDeckTabs : PreviewDeckTabs);
        }

        sourceDockWidth = layout is { DockWidth: >= 240 } && double.IsFinite(layout.DockWidth) ? layout.DockWidth : 568;
        RestorePane(trimmerPane, layout?.Floating == true, layout?.Visible != false, layout?.Bounds);
        if (separateDock && trimmerPane.IsDocked)
            SourceDeckTabs.SelectedItem = TrimmerTab;
    }

    private void RestoreWorkspacePanes(Dictionary<string, WorkspacePaneLayout>? layouts)
    {
        foreach (var (key, pane) in workspacePanes)
        {
            CloseFloatingPane(pane);
            var layout = layouts?.GetValueOrDefault(key);
            var dock = layout?.DockGroup switch
            {
                nameof(ToolsTabs) => ToolsTabs,
                nameof(PreviewDeckTabs) => PreviewDeckTabs,
                nameof(UpperDockTabs) => UpperDockTabs,
                _ => pane.HomeDock
            };
            MovePaneToDock(pane, dock);
            RestorePane(pane, layout?.Floating == true, layout?.Visible != false, layout?.Bounds);
        }
    }

    private void RestorePane(DockablePane pane, bool floating, bool visible, PaneBounds? bounds)
    {
        pane.Floats = floating;
        pane.Bounds = bounds;
        if (floating || !visible)
            HidePaneTab(pane);
        else
        {
            pane.Tab.Visibility = Visibility.Visible;
            if (pane.Dock.SelectedItem is null)
                pane.Dock.SelectedItem = pane.Tab;
            UpdatePaneDockLayout(pane);
        }

        if (floating && visible)
            OpenFloatingPane(pane, bounds ?? DefaultPaneBounds(pane), activate: false, restore: true);
    }

    private PaneBounds DefaultPaneBounds(DockablePane pane)
    {
        var position = PointToScreen(new Point(48, 80));
        var width = pane == trimmerPane ? 600 : Math.Clamp(pane.Dock.ActualWidth, 340, 800);
        return new PaneBounds((int)position.X, (int)position.Y, width, Workspace.ActualHeight);
    }

    private void OpenFloatingPane(DockablePane pane, PaneBounds bounds, bool activate, bool restore)
    {
        HidePaneTab(pane);
        var content = (UIElement)pane.Tab.Content;
        pane.Tab.Content = null;
        if (content is ColorGradingDockControl grading) grading.SetWorkspaceFloating(true);

        var window = new Window
        {
            Title = pane.Name,
            Owner = this,
            DataContext = DataContext,
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

        var title = new DockPanel { Height = 30, Background = (Brush)FindResource("ColorD") };
        var close = CreatePaneCloseButton(pane);
        close.Margin = new Thickness(0, 0, 5, 0);
        close.Click += (_, _) => window.Close();
        DockPanel.SetDock(close, Dock.Right);
        title.Children.Add(close);
        var caption = new Border
        {
            Background = Brushes.Transparent,
            Child = new TextBlock
            {
                Text = pane.Name, FontSize = 11, Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        caption.MouseLeftButtonDown += (_, e) => e.Handled = BeginPaneDrag(pane, caption, ReadPaneBounds(pane));
        var menu = new ContextMenu();
        var dockItem = new MenuItem { Header = "Dock pane" };
        dockItem.Click += (_, _) => DockFloatingPane(pane);
        menu.Items.Add(dockItem);
        caption.ContextMenu = menu;
        title.Children.Add(caption);

        var panel = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);
        panel.Children.Add(content);
        window.Content = panel;
        window.DpiChanged += (_, _) => QueueFloatingPaneBoundsCheck();
        window.Closing += (_, _) => pane.Bounds = ReadPaneBounds(pane) ?? pane.Bounds;
        window.Closed += (_, _) =>
        {
            panel.Children.Remove(content);
            pane.Tab.Content = content;
            if (content is ColorGradingDockControl grading) grading.SetWorkspaceFloating(false);
            pane.Window = null;
            ShowPaneDockOutline(null);
            if (pane.Floats)
                pane.OnHidden?.Invoke();
        };
        if (pane == trimmerPane)
        {
            window.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && CancelWorkspacePaneDrag())
                    e.Handled = true;
                else
                    HandleTrimmerShortcut(e);
            };
        }
        else
        {
            window.PreviewKeyDown += WindowKeyDown;
            window.PreviewKeyUp += ShuttleKeyUp;
            window.Deactivated += EndHeldShuttle;
        }
        AttachPaneDragHandlers(caption);
        pane.Window = window;
        window.Show();
        PlacePane(pane, bounds.Left, bounds.Top, restore);
        window.Opacity = 1;
    }

    private static double ClampPaneSize(double size, double fallback, double minimum) =>
        double.IsFinite(size) ? Math.Clamp(size, minimum, 4096) : fallback;

    private static void CloseFloatingPane(DockablePane pane)
    {
        pane.Floats = false;
        pane.Window?.Close();
    }

    private void DockFloatingPane(DockablePane pane, TabControl? target = null)
    {
        CloseFloatingPane(pane);
        if (target is not null && pane != trimmerPane)
            MovePaneToDock(pane, target);
        Activate();
        ShowPane(pane);
    }

    private void MovePaneToDock(DockablePane pane, TabControl target)
    {
        var source = pane.Dock;
        if (source == target) return;

        source.Items.Remove(pane.Tab);
        if (source.SelectedItem is not TabItem { Visibility: Visibility.Visible })
            source.SelectedItem = source.Items.OfType<TabItem>().FirstOrDefault(tab => tab.Visibility == Visibility.Visible);
        pane.Dock = target;
        // Fixed order keeps saved active-tab indexes stable after restoring pane groups.
        var order = workspaceTabOrder[pane.Tab];
        var index = 0;
        while (index < target.Items.Count && workspaceTabOrder[(TabItem)target.Items[index]] < order)
            index++;
        target.Items.Insert(index, pane.Tab);
        if (source == UpperDockTabs || target == UpperDockTabs)
            UpdateUpperDockRows();
    }

    private static PaneBounds? ReadPaneBounds(DockablePane pane)
    {
        if (pane.Window is not { } window ||
            !GetWindowRect(new WindowInteropHelper(window).Handle, out var rect))
            return null;
        return new PaneBounds(rect.Left, rect.Top, window.ActualWidth, window.ActualHeight);
    }

    private static void PlacePane(DockablePane pane, int x, int y, bool clampToMonitor)
    {
        if (pane.Window is null)
            return;
        var hwnd = new WindowInteropHelper(pane.Window).Handle;
        const uint noSize = 0x1, noZOrder = 0x4, noActivate = 0x10;
        SetWindowPos(hwnd, 0, x, y, 0, 0, noSize | noZOrder | noActivate);
        if (clampToMonitor) ClampPaneToMonitor(hwnd);
    }

    private void QueueFloatingPaneBoundsCheck()
    {
        if (paneBoundsCheckQueued || !IsLoaded) return;
        paneBoundsCheckQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            paneBoundsCheckQueued = false;
            if (!IsLoaded) return;
            foreach (var pane in workspacePanes.Values.Prepend(trimmerPane))
            {
                if (pane == paneDrag?.Pane || pane.Window is not { IsVisible: true, WindowState: WindowState.Normal } window) continue;
                ClampPaneToMonitor(new WindowInteropHelper(window).Handle);
                pane.Bounds = ReadPaneBounds(pane) ?? pane.Bounds;
            }
        });
    }

    private static void ClampPaneToMonitor(nint hwnd)
    {
        // Screen coordinates stay in physical pixels across monitors.
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetWindowRect(hwnd, out var rect) || !GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info))
            return;
        var current = new Int32Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        var work = new Int32Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        var constrained = WorkspacePaneGeometry.ConstrainToWorkArea(current, work);
        if (constrained != current)
            SetWindowPos(hwnd, 0, constrained.X, constrained.Y, constrained.Width, constrained.Height, 0x4 | 0x10);
    }

    private bool BeginPaneDrag(DockablePane pane, UIElement capture, PaneBounds? bounds)
    {
        if (paneDrag is not null || !GetCursorPos(out var pointer) || !Mouse.Capture(capture))
            return false;
        paneDrag = new PaneDrag(pane, capture, pointer, bounds);
        return true;
    }

    private void AttachPaneDragHandlers(UIElement surface)
    {
        surface.PreviewMouseMove += MovePaneDrag;
        surface.PreviewMouseLeftButtonUp += (_, _) => FinishPaneDrag(cancel: false);
        surface.LostMouseCapture += (_, e) =>
        {
            if (e.OriginalSource == paneDrag?.Capture)
                CancelWorkspacePaneDrag();
        };
    }

    private void MovePaneDrag(object sender, MouseEventArgs e)
    {
        if (paneDrag is not { } drag || sender != drag.Capture ||
            e.LeftButton != MouseButtonState.Pressed || !GetCursorPos(out var pointer))
            return;

        var pane = drag.Pane;
        var dpi = VisualTreeHelper.GetDpi(drag.Capture);
        if (drag.Bounds is { } original)
        {
            if (!drag.Started &&
                Math.Abs(pointer.X - drag.Pointer.X) < SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX &&
                Math.Abs(pointer.Y - drag.Pointer.Y) < SystemParameters.MinimumVerticalDragDistance * dpi.DpiScaleY)
                return;
            drag.Started = true;
            PlacePane(pane, original.Left + pointer.X - drag.Pointer.X, original.Top + pointer.Y - drag.Pointer.Y, clampToMonitor: false);
        }
        else
        {
            // Leave the bottom tab strip before detaching the pane.
            if (!drag.Started && drag.Pointer.Y - pointer.Y < 30 * dpi.DpiScaleY)
                return;
            drag.Started = true;
            var x = pointer.X - (int)(20 * dpi.DpiScaleX);
            var y = pointer.Y - (int)(5 * dpi.DpiScaleY);
            if (pane.Window is null)
            {
                pane.Floats = true;
                var bounds = pane.Bounds ?? DefaultPaneBounds(pane);
                OpenFloatingPane(pane, bounds with { Left = x, Top = y }, activate: false, restore: false);
            }
            else
                PlacePane(pane, x, y, clampToMonitor: false);
        }

        ShowPaneDockOutline(GetPaneDockTarget(pane, pointer));
    }

    private PaneDockTarget? GetPaneDockTarget(DockablePane pane, NativePoint pointer)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || !Workspace.IsVisible || WindowState == WindowState.Minimized)
            return null;
        var screen = new Point(pointer.X, pointer.Y);
        if (pane == trimmerPane)
            return Hit(PreviewDeckTabs);
        if (UpperDockTabs.IsVisible)
        {
            if (Hit(UpperDockTabs) is { } upper) return upper;
        }
        else
        {
            var workspaceTop = new Rect(0, 0, Workspace.ActualWidth, Math.Min(32, Workspace.ActualHeight * .15));
            if (workspaceTop.Contains(Workspace.PointFromScreen(screen)))
                return new(UpperDockTabs, Workspace,
                    new Rect(0, 0, Workspace.ActualWidth, Math.Max(0, Workspace.ActualHeight - UpperDockSplitter.Height) * upperDockShare));
        }

        return Hit(ToolsTabs) ?? Hit(PreviewDeckTabs) ?? (pane.HomeDock == MasterBusTabs ? Hit(MasterBusTabs) : null);

        PaneDockTarget? Hit(TabControl target)
        {
            var bounds = new Rect(target.RenderSize);
            return target.IsVisible && bounds.Contains(target.PointFromScreen(screen)) ? new(target, target, bounds) : null;
        }
    }

    private void ShowPaneDockOutline(PaneDockTarget? target)
    {
        if (target is null)
        {
            if (paneDockOutline is not null)
                paneDockOutline.IsOpen = false;
            return;
        }

        paneDockOutline ??= new Popup
        {
            Placement = PlacementMode.Relative,
            AllowsTransparency = true,
            IsHitTestVisible = false,
            Child = new Border
            {
                BorderThickness = new Thickness(3),
                BorderBrush = (Brush)FindResource("AccentBrush"),
                Background = new SolidColorBrush(((SolidColorBrush)FindResource("AccentBrush")).Color) { Opacity = 0.14 },
                IsHitTestVisible = false
            }
        };
        paneDockOutline.PlacementTarget = target.OutlineOwner;
        paneDockOutline.HorizontalOffset = target.Outline.X;
        paneDockOutline.VerticalOffset = target.Outline.Y;
        paneDockOutline.Width = target.Outline.Width;
        paneDockOutline.Height = target.Outline.Height;
        paneDockOutline.IsOpen = true;
    }

    private bool CancelWorkspacePaneDrag() => FinishPaneDrag(cancel: true);

    private bool FinishPaneDrag(bool cancel)
    {
        if (paneDrag is not { } drag)
            return false;
        paneDrag = null;
        if (Mouse.Captured == drag.Capture)
            Mouse.Capture(null);
        ShowPaneDockOutline(null);

        var pane = drag.Pane;
        if (!drag.Started || pane.Window is null)
            return true;
        if (cancel && drag.Bounds is { } original)
            PlacePane(pane, original.Left, original.Top, clampToMonitor: true);
        else if (cancel)
            DockFloatingPane(pane);
        else if (GetCursorPos(out var pointer) && GetPaneDockTarget(pane, pointer) is { } target)
            DockFloatingPane(pane, target.Dock);
        else
        {
            if (ReadPaneBounds(pane) is { } dropped)
                PlacePane(pane, dropped.Left, dropped.Top, clampToMonitor: true);
            pane.Bounds = ReadPaneBounds(pane);
            pane.Window.Activate();
            if (pane == trimmerPane)
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
