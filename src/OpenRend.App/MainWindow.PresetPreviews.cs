using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

public partial class MainWindow
{
    private const int PresetPreviewWidth = 144;
    private const int PresetPreviewHeight = 81;
    private MediaService? presetPreviewMedia;
    private TimelineRenderService? presetPreviewRenderer;

    private Image SetPresetPreviewContent(ListBoxItem item, string name, string automationId, string tooltip)
    {
        var image = new Image { MaxWidth = 88, MaxHeight = 49.5, IsHitTestVisible = false };
        AutomationProperties.SetName(image, $"{name} preview");
        AutomationProperties.SetAutomationId(image, automationId);
        var imageBorder = new Border
        {
            BorderThickness = new Thickness(1), BorderBrush = (Brush)FindResource("ColorM"),
            Child = image, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var label = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid { Background = Brushes.Transparent };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 98 });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetColumn(label, 1);
        content.Children.Add(imageBorder);
        content.Children.Add(label);
        item.Content = content;
        item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        item.Padding = new Thickness(5, 3, 5, 3);
        item.ToolTip = tooltip;
        AutomationProperties.SetName(item, name);
        return image;
    }

    private BitmapSource RenderPresetFrame(ProjectDocument demo, long frame)
    {
        presetPreviewMedia ??= new MediaService();
        presetPreviewRenderer ??= new TimelineRenderService(presetPreviewMedia, useGpuCompositing: false);
        var pixels = presetPreviewRenderer.RenderFrame(demo, frame, PresetPreviewWidth, PresetPreviewHeight);
        var bitmap = BitmapSource.Create(PresetPreviewWidth, PresetPreviewHeight, 96, 96, PixelFormats.Bgra32,
            null, pixels, PresetPreviewWidth * 4);
        bitmap.Freeze();
        return bitmap;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopTransitionDemo();
        presetPreviewRenderer?.Dispose();
        presetPreviewMedia?.Dispose();
        base.OnClosed(e);
    }
}
