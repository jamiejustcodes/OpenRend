using System.Globalization;
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
    private readonly TextBlock error = new() { Margin = new Thickness(8, 0, 8, 0), TextWrapping = TextWrapping.Wrap };
    public EditingPreferences Result { get; private set; }

    public EditingPreferencesWindow(EditingPreferences settings)
    {
        Result = settings;
        Style = (Style)FindResource(typeof(Window));
        Title = "Preferences"; Width = 590; Height = 300; MinWidth = 480; MinHeight = 270;
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
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        DockPanel.SetDock(error, Dock.Bottom); root.Children.Add(error);
        var tabs = new TabControl { TabStripPlacement = Dock.Top, Style = (Style)FindResource("PreferencesTabs") };
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
                Style = (Style)FindResource("NumericSpinButton") };
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) =>
            {
                if (!double.TryParse(amount.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || !double.IsFinite(value)) return;
                amount.Text = Math.Clamp(Math.Round(value + step, 3), .001, 3600).ToString("0.###", CultureInfo.CurrentCulture);
            };
            spinner.Children.Add(button);
        }
        Grid.SetColumn(spinner, 1); amountHost.Children.Add(spinner);
        var amountLabel = new Label { Content = "_Amount (seconds):", Target = amount, Foreground = (Brush)FindResource("TextBrush") };
        fields.Children.Add(amountLabel); Grid.SetColumn(amountHost, 1); fields.Children.Add(amountHost);
        alignment = new ComboBox { ItemsSource = new[] { "Before cut", "Centered on cut", "After cut" },
            SelectedIndex = (int)settings.CutOverlapAlignment, Margin = new Thickness(2, 8, 2, 2) };
        AutomationProperties.SetAutomationId(alignment, "CutOverlapAlignment");
        AutomationProperties.SetName(alignment, "Alignment");
        var alignmentLabel = new Label { Content = "_Alignment:", Target = alignment, Margin = new Thickness(0, 6, 0, 0),
            Foreground = (Brush)FindResource("TextBrush") };
        Grid.SetRow(alignmentLabel, 1); fields.Children.Add(alignmentLabel);
        Grid.SetRow(alignment, 1); Grid.SetColumn(alignment, 1); fields.Children.Add(alignment);
        tabs.Items.Add(new TabItem { Header = "Editing", IsSelected = true, Content = new GroupBox { Header = "Cut-to-overlap conversion",
            Style = (Style)FindResource("PreferencesGroup"), Content = fields, Margin = new Thickness(8) } });
        root.Children.Add(tabs);
        ok.Click += (_, _) =>
        {
            if (!double.TryParse(amount.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
                !double.IsFinite(value) || value is <= 0 or > 3600 || alignment.SelectedIndex < 0)
            { error.Text = "Enter an amount greater than zero and no more than 3600 seconds."; amount.Focus(); return; }
            Result = new EditingPreferences { CutOverlapSeconds = value, CutOverlapAlignment = (CutTransitionAlignment)alignment.SelectedIndex };
            DialogResult = true;
        };
        Content = root;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
