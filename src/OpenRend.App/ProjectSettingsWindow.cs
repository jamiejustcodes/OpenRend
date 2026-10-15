using System.Windows;
using System.Windows.Controls;
using OpenRend.Core;

namespace OpenRend.App;

public sealed class ProjectSettingsWindow : Window
{
    private readonly TextBox name, width, height, numerator, denominator;
    public int ProjectWidth { get; private set; }
    public int ProjectHeight { get; private set; }
    public Rational Rate { get; private set; }
    public string ProjectName { get; private set; } = "";
    public ProjectSettingsWindow(ProjectDocument project)
    {
        Title = "Project settings"; Width = 470; Height = 350; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new Grid { Margin = new Thickness(20) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 7; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBox Field(int row, string label, string value)
        {
            var text = new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 8) }; Grid.SetRow(text, row); grid.Children.Add(text);
            var field = new TextBox { Text = value }; Grid.SetRow(field, row); Grid.SetColumn(field, 1); grid.Children.Add(field); return field;
        }
        name = Field(0, "Project name", project.Name); width = Field(1, "Width", project.Width.ToString()); height = Field(2, "Height", project.Height.ToString());
        numerator = Field(3, "Frame rate numerator", project.FrameRate.Numerator.ToString()); denominator = Field(4, "Frame rate denominator", project.FrameRate.Denominator.ToString());
        numerator.IsEnabled = denominator.IsEnabled = project.Media.Count == 0;
        var hint = new TextBlock { Text = "Frame rate is fixed after media import.\nUse 30000 / 1001 for 29.97 fps.", Margin = new Thickness(0, 10, 0, 10), Foreground = System.Windows.Media.Brushes.Silver };
        Grid.SetRow(hint, 5); Grid.SetColumnSpan(hint, 2); grid.Children.Add(hint);
        var save = new Button { Content = "Apply settings", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) =>
        {
            try
            {
                ProjectWidth = int.Parse(width.Text); ProjectHeight = int.Parse(height.Text); Rate = new Rational(int.Parse(numerator.Text), int.Parse(denominator.Text)); ProjectName = name.Text.Trim();
                if (ProjectWidth is < 16 or > 16384 || ProjectHeight is < 16 or > 16384 || ProjectWidth % 2 != 0 || ProjectHeight % 2 != 0 || ProjectName.Length == 0)
                    throw new ArgumentException("Use even dimensions between 16 and 16384 and a project name.");
                DialogResult = true;
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException) { MessageBox.Show(this, ex.Message, "Project settings"); }
        };
        Grid.SetRow(save, 6); Grid.SetColumn(save, 1); grid.Children.Add(save); Content = grid;
    }
}
