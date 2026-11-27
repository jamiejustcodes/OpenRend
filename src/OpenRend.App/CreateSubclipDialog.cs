using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using OpenRend.Core;
using OpenRend.UI;

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

    internal static void Create(EditorViewModel viewModel, Guid sourceId, FrameRange range, Window? owner, string? customName = null)
    {
        var editor = viewModel.Editor;
        var source = editor.Project.Media.FirstOrDefault(asset => asset.Id == sourceId);
        if (source is null || source.IsGenerated || range.FrameCount <= 0)
        {
            viewModel.Status = "Choose a nonempty range in a file from Project Media.";
            return;
        }

        var count = editor.Project.Media.Count(asset => asset.SourceRange is not null &&
            string.Equals(asset.Path, source.Path, StringComparison.OrdinalIgnoreCase));
        var name = customName ?? $"{source.Name} - Subclip {count + 1}";
        if (owner is not null)
        {
            var rate = editor.Project.FrameRate;
            var dialog = new CreateSubclipDialog(name, source.Name,
                TimelineView.FormatTimecode(range.StartFrame, rate), TimelineView.FormatTimecode(range.EndFrame, rate),
                TimelineView.FormatTimecode(range.FrameCount, rate)) { Owner = owner };
            if (dialog.ShowDialog() != true) return;
            name = dialog.SubclipName;
        }

        if (!ReferenceEquals(editor, viewModel.Editor)) return;
        try
        {
            var subclip = editor.CreateSubclip(sourceId, range, name);
            viewModel.Status = $"Created subclip '{subclip.Name}' in Project Media.";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            viewModel.Status = error.Message;
        }
    }
}
