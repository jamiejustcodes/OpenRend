using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using OpenRend.Core;

namespace OpenRend.App;

internal static class TimelineAnnotationColors
{
    private static readonly string[] names = ["Blue", "Coral", "Teal", "Gold", "Sage", "Red", "Slate", "Violet"];

    public static string NameOf(int colorId) => names[Math.Clamp(colorId, 0, names.Length - 1)];

    public static ComboBox CreatePicker(int colorId)
    {
        var picker = new ComboBox();
        AutomationProperties.SetAutomationId(picker, "AnnotationColor");
        foreach (var (name, hex) in names.Zip(TimelineMarker.MarkerColors))
        {
            var swatch = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Width = 12,
                Height = 12,
                Margin = new Thickness(0, 0, 7, 0)
            };
            var caption = new StackPanel { Orientation = Orientation.Horizontal };
            caption.Children.Add(swatch);
            caption.Children.Add(new TextBlock { Text = name });
            var item = new ComboBoxItem { Content = caption };
            AutomationProperties.SetName(item, name);
            picker.Items.Add(item);
        }
        picker.SelectedIndex = Math.Clamp(colorId, 0, picker.Items.Count - 1);
        return picker;
    }
}
