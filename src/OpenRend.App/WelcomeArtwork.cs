using System.Windows;
using System.Windows.Media;
using OpenRend.Project;

namespace OpenRend.App;

public sealed class WelcomeArtwork : FrameworkElement
{
    public static readonly DependencyProperty AspectProperty = DependencyProperty.Register(nameof(Aspect), typeof(ProjectAspect),
        typeof(WelcomeArtwork), new FrameworkPropertyMetadata(ProjectAspect.Widescreen, FrameworkPropertyMetadataOptions.AffectsRender));
    public ProjectAspect Aspect { get => (ProjectAspect)GetValue(AspectProperty); set => SetValue(AspectProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.PushTransform(new ScaleTransform(ActualWidth / 720, ActualHeight / 312));
        var (left, right) = Aspect switch
        {
            ProjectAspect.Portrait => ("#752577", "#D878CE"),
            ProjectAspect.Square => ("#CE6B17", "#F9B24D"),
            ProjectAspect.Fullscreen => ("#08776C", "#37C9A5"),
            ProjectAspect.Scope => ("#29263F", "#625B97"),
            _ => ("#1E46A6", "#3869F2")
        };
        dc.DrawRectangle(Gradient(left, right), null, new Rect(0, 0, 720, 312));
        if (Aspect == ProjectAspect.Portrait) DrawPhone(dc); else DrawCamera(dc);
        dc.Pop(); dc.Pop();
    }

    private static void DrawCamera(DrawingContext dc)
    {
        dc.DrawEllipse(Brush("#45000000"), null, new Point(235, 295), 250, 28);
        dc.DrawRoundedRectangle(Gradient("#4C5260", "#14161F"), new Pen(Brush("#5E6470"), 2), new Rect(24, 87, 273, 176), 20, 20);
        dc.DrawRoundedRectangle(Gradient("#555C69", "#282D39"), null, new Rect(55, 62, 199, 41), 9, 9);
        dc.DrawRoundedRectangle(Brush("#343A46"), new Pen(Brush("#69717D"), 1), new Rect(15, 108, 59, 124), 10, 10);
        dc.DrawRoundedRectangle(Brush("#B32436"), null, new Rect(131, 53, 36, 16), 4, 4);
        foreach (var y in new[] { 126, 151, 176, 201 })
            dc.DrawEllipse(Brush("#B6BBC5"), new Pen(Brush("#10131B"), 2), new Point(45, y), 7, 7);
        for (var i = 0; i < 9; i++) dc.DrawLine(new Pen(Brush("#1D222D"), 3), new Point(86, 113 + i * 8), new Point(143, 113 + i * 8));
        dc.DrawRectangle(Brush("#191E28"), new Pen(Brush("#626875"), 1), new Rect(176, 107, 90, 130));
        dc.DrawEllipse(Gradient("#777D89", "#11151E"), new Pen(Brush("#959BA5"), 2), new Point(292, 174), 92, 111);
        dc.DrawEllipse(Gradient("#3B4352", "#10131C"), new Pen(Brush("#151A23"), 10), new Point(326, 174), 83, 105);
        dc.DrawEllipse(Brush("#10141F"), new Pen(Brush("#626A79"), 3), new Point(342, 174), 66, 91);
        dc.DrawEllipse(Gradient("#53689A", "#10101B"), new Pen(Brush("#697995"), 2), new Point(345, 174), 48, 72);
        dc.DrawEllipse(Gradient("#8796CB", "#202B59"), null, new Point(349, 174), 32, 52);
        dc.DrawEllipse(Brush("#9AFFFFFF"), null, new Point(331, 149), 10, 23);
        dc.DrawEllipse(Brush("#704D7BB2"), null, new Point(360, 191), 12, 20);
        dc.DrawLine(new Pen(Brush("#8C929E"), 1), new Point(77, 93), new Point(258, 93));
    }

    private static void DrawPhone(DrawingContext dc)
    {
        dc.PushTransform(new RotateTransform(-9, 165, 200));
        dc.DrawRoundedRectangle(Gradient("#C3B0CF", "#47364D"), new Pen(Brush("#DED3E5"), 3), new Rect(50, 30, 204, 365), 30, 30);
        dc.DrawRoundedRectangle(Gradient("#58445F", "#2F2335"), new Pen(Brush("#D0B9D7"), 2), new Rect(64, 42, 134, 142), 27, 27);
        foreach (var point in new[] { new Point(101, 78), new Point(160, 126), new Point(99, 147) })
        {
            dc.DrawEllipse(Brush("#10101B"), new Pen(Brush("#BDADC9"), 4), point, 26, 26);
            dc.DrawEllipse(Gradient("#6C5B8C", "#181427"), new Pen(Brush("#5F526F"), 2), point, 19, 19);
            dc.DrawEllipse(Brush("#7AFFFFFF"), null, new Point(point.X - 6, point.Y - 8), 5, 9);
        }
        dc.DrawEllipse(Brush("#DED9DE"), null, new Point(160, 68), 11, 11);
        dc.Pop();
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static LinearGradientBrush Gradient(string left, string right) => new((Color)ColorConverter.ConvertFromString(left),
        (Color)ColorConverter.ConvertFromString(right), new Point(0, 0), new Point(1, 1));
}
