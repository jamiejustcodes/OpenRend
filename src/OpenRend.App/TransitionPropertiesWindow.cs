using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Runtime.InteropServices;

namespace OpenRend.App;

public sealed class TransitionPropertiesWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly Guid clipId;
    private readonly TextBlock heading = new(), timing = new();
    private readonly Button remove, undo, redo;

    public TransitionPropertiesWindow(EditorViewModel viewModel, Guid eventId)
    {
        vm = viewModel; clipId = eventId;
        Style = (Style)FindResource(typeof(Window));
        Title = "Video event FX"; Width = 580; Height = 240; MinWidth = 470; MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Background = (Brush)FindResource("PanelBrush") };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Background = (Brush)FindResource("HeaderBrush") };
        remove = Button("Remove Selected Plug-In", "RemoveTransitionPlugin", () => vm.Editor.RemoveVideoTransition(clipId));
        undo = Button("Undo", "UndoTransitionEdit", () => vm.Editor.Undo());
        redo = Button("Redo", "RedoTransitionEdit", () => vm.Editor.Redo());
        tools.Children.Add(remove); tools.Children.Add(undo); tools.Children.Add(redo);
        DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        var close = Button("Close", "CloseTransitionProperties", Close);
        close.HorizontalAlignment = HorizontalAlignment.Right; close.MinWidth = 78;
        DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        var body = new StackPanel { Margin = new Thickness(14) };
        heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 12);
        AutomationProperties.SetAutomationId(heading, "TransitionPluginName");
        body.Children.Add(heading); body.Children.Add(timing); root.Children.Add(body);
        Content = root;
        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
        vm.Editor.Changed += EditorChanged;
        Closed += (_, _) => vm.Editor.Changed -= EditorChanged;
        Refresh();
    }

    private Button Button(string label, string id, Action edit)
    {
        var button = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3) };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) =>
        {
            try { edit(); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Transition", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        return button;
    }

    private void EditorChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        // Undo replaces clip objects, so always resolve the current event by identity.
        var item = vm.Project.Tracks.SelectMany(track => track.Clips.Select(clip => (Track: track, Clip: clip)))
            .FirstOrDefault(item => item.Clip.Id == clipId);
        var clip = item.Clip;
        heading.Text = clip?.FadeInTransition is { } effect ? $"{effect.Kind} — {effect.Preset}" : "No transition plug-in";
        timing.Text = clip is null ? "Event is no longer in the project." : $"Fade in: {clip.FadeInFrames} frames";
        remove.IsEnabled = clip?.FadeInTransition is not null && !item.Track.IsLocked;
        undo.IsEnabled = vm.Editor.CanUndo; redo.IsEnabled = vm.Editor.CanRedo;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
