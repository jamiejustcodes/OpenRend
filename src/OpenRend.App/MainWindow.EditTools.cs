using System.Windows;
using OpenRend.Core;

namespace OpenRend.App;

public partial class MainWindow
{
    private void InitializeEditToolsEvents()
    {
        Timeline.EditToolChanged += mode => Guard(() =>
        {
            vm.EditTool = mode;
            UpdateEditToolUI(mode);
        });

        vm.EditToolChanged += mode => Guard(() =>
        {
            Timeline.EditTool = mode;
            UpdateEditToolUI(mode);
        });

        Timeline.ZoomChanged += _ => Guard(() =>
        {
            Refresh();
        });

        UpdateEditToolUI(vm.EditTool);
    }

    private void EditToolNormalClick(object sender, RoutedEventArgs e) => SetEditTool(EditToolMode.Normal);
    private void EditToolEnvelopeClick(object sender, RoutedEventArgs e) => SetEditTool(EditToolMode.Envelope);
    private void EditToolSelectionClick(object sender, RoutedEventArgs e) => SetEditTool(EditToolMode.Selection);
    private void EditToolZoomClick(object sender, RoutedEventArgs e) => SetEditTool(EditToolMode.Zoom);
    private void EditToolNextClick(object sender, RoutedEventArgs e) => CycleNextEditTool();
    private void EditToolPreviousClick(object sender, RoutedEventArgs e) => CyclePreviousEditTool();

    public void SetEditTool(EditToolMode mode)
    {
        vm.EditTool = mode;
        Timeline.EditTool = mode;
        UpdateEditToolUI(mode);
    }

    public void CycleNextEditTool()
    {
        var next = vm.EditTool.NextTool();
        SetEditTool(next);
    }

    public void CyclePreviousEditTool()
    {
        var prev = vm.EditTool.PreviousTool();
        SetEditTool(prev);
    }

    private void UpdateEditToolUI(EditToolMode mode)
    {
        if (ToolNormalRadio != null) ToolNormalRadio.IsChecked = mode == EditToolMode.Normal;
        if (ToolEnvelopeRadio != null) ToolEnvelopeRadio.IsChecked = mode == EditToolMode.Envelope;
        if (ToolSelectionRadio != null) ToolSelectionRadio.IsChecked = mode == EditToolMode.Selection;
        if (ToolZoomRadio != null) ToolZoomRadio.IsChecked = mode == EditToolMode.Zoom;

        if (EditToolNormalMenu != null) EditToolNormalMenu.IsChecked = mode == EditToolMode.Normal;
        if (EditToolEnvelopeMenu != null) EditToolEnvelopeMenu.IsChecked = mode == EditToolMode.Envelope;
        if (EditToolSelectionMenu != null) EditToolSelectionMenu.IsChecked = mode == EditToolMode.Selection;
        if (EditToolZoomMenu != null) EditToolZoomMenu.IsChecked = mode == EditToolMode.Zoom;
    }
}
