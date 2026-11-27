using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace OpenRend.App;

public sealed class CreateSubclipDialog : TimelineEditDialog
{
    private readonly TextBox nameBox = new();

    public string SubclipName => nameBox.Text.Trim();

    public CreateSubclipDialog(string initialName, string mediaName, string inTc, string outTc, string lengthTc)
        : base("Create Subclip", 400)
    {
        nameBox.Text = initialName;
        nameBox.TextChanged += (_, _) => ClearValidation();
        AutomationProperties.SetAutomationId(nameBox, "SubclipName");
        Form.Children.Add(Field("_Name", nameBox));
        var summary = new StackPanel();
        summary.Children.Add(new TextBlock { Text = mediaName, TextTrimming = TextTrimming.CharacterEllipsis });
        summary.Children.Add(new TextBlock { Text = $"In {inTc}   Out {outTc}   Length {lengthTc}", TextWrapping = TextWrapping.Wrap });
        Form.Children.Add(Options("Source range", summary));
        FocusOnOpen(nameBox);
    }

    protected override bool TryAccept() => SubclipName.Length > 0 || Reject(nameBox, "Enter a subclip name.");
}
