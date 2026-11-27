using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenRend.App;

public abstract class TimelineEditDialog : Window
{
    private readonly TextBlock error = new()
    {
        Margin = new Thickness(0, 0, 0, 10),
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed
    };

    protected StackPanel Form { get; } = new();
    protected Button AcceptButton { get; } = new() { Content = "OK", IsDefault = true, MinWidth = 76 };
    protected Button CancelButton { get; } = new() { Content = "Cancel", IsCancel = true, MinWidth = 76 };

    protected TimelineEditDialog(string title, double width)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        error.SetResourceReference(TextBlock.ForegroundProperty, "ValidationTextBrush");
        AutomationProperties.SetAutomationId(error, "DialogValidationError");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);

        AcceptButton.Click += (_, _) =>
        {
            if (TryAccept()) DialogResult = true;
        };
        AcceptButton.Margin = new Thickness(0, 0, 8, 0);
        CancelButton.Margin = new Thickness(0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(AcceptButton);
        buttons.Children.Add(CancelButton);
        var footer = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 10, 0, 0),
            Child = buttons
        };
        footer.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        body.Children.Add(Form);
        body.Children.Add(error);
        body.Children.Add(footer);
        Content = body;
    }

    protected static StackPanel Field(string name, Control input, UIElement? surface = null)
    {
        var label = new Label { Content = name, Target = input, Padding = new Thickness(0, 0, 0, 4) };
        label.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        AutomationProperties.SetLabeledBy(input, label);
        input.Margin = new Thickness(0);
        var field = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        field.Children.Add(label);
        field.Children.Add(surface ?? input);
        return field;
    }

    protected static GroupBox Options(string name, UIElement content)
    {
        var group = new GroupBox
        {
            Header = name,
            Content = content,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10, 6, 10, 0)
        };
        group.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        group.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        return group;
    }

    protected static TextBox TimeField(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Consolas"),
        HorizontalContentAlignment = HorizontalAlignment.Right
    };

    protected void FocusOnOpen(TextBox input) => Loaded += (_, _) => FocusField(input);

    protected bool Reject(TextBox input, string message)
    {
        error.Text = message;
        error.Visibility = Visibility.Visible;
        FocusField(input);
        UIElementAutomationPeer.CreatePeerForElement(error)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        return false;
    }

    private static void FocusField(TextBox input)
    {
        input.Focus();
        input.SelectAll();
    }

    protected abstract bool TryAccept();
}
