using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenRend.Core;
using OpenRend.Media;

namespace OpenRend.App;

public partial class MainWindow
{
    private const int TransitionThumbnailWidth = 144;
    private const int TransitionThumbnailHeight = 81;
    private const int DemoStartFrame = 12;
    private const int DemoFadeFrames = 24;
    private const int DemoRestFrames = 5;
    private readonly Dictionary<TransitionPresetInfo, BitmapSource> transitionPosterFrames = new();
    private readonly ProjectDocument transitionDemo = CreateTransitionDemo();
    private TimelineRenderService? transitionDemoRenderer;
    private MediaService? transitionDemoMedia;
    private TransitionPresetInfo? transitionDemoPreset;
    private DispatcherTimer? transitionDemoTimer;
    private (Image Image, TransitionPresetInfo Preset)? animatedTransition;
    private int transitionDemoFrame;

    private void SetTransitionPresetContent(ListBoxItem item, TransitionPresetInfo preset)
    {
        var image = new Image { MaxWidth = 88, MaxHeight = 49.5, IsHitTestVisible = false };
        AutomationProperties.SetName(image, $"{preset.Name} preview");
        AutomationProperties.SetAutomationId(image, "TransitionPresetThumbnail");
        var imageBorder = new Border
        {
            BorderThickness = new Thickness(1), BorderBrush = (Brush)FindResource("ColorM"),
            Child = image, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var label = new TextBlock { Text = preset.Name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid { Background = Brushes.Transparent };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 98 });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetColumn(label, 1);
        content.Children.Add(imageBorder);
        content.Children.Add(label);
        item.Content = content;
        item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        item.Padding = new Thickness(5, 3, 5, 3);
        item.ToolTip = "Drag to a video overlap or apply to the selected events";
        AutomationProperties.SetName(item, preset.Name);

        item.IsVisibleChanged += (_, _) =>
        {
            if (item.IsVisible) image.Source = TransitionPosterFrame(preset);
            else if (ReferenceEquals(animatedTransition?.Image, image)) StopTransitionDemo();
        };
        item.MouseEnter += (_, _) =>
        {
            if (SystemParameters.ClientAreaAnimation && Mouse.LeftButton == MouseButtonState.Released && Mouse.RightButton == MouseButtonState.Released)
                StartTransitionDemo(image, preset);
        };
        item.MouseLeave += (_, _) =>
        {
            if (ReferenceEquals(animatedTransition?.Image, image)) StopTransitionDemo();
        };
    }

    private BitmapSource TransitionPosterFrame(TransitionPresetInfo preset)
    {
        if (!transitionPosterFrames.TryGetValue(preset, out var frame))
        {
            frame = RenderTransitionDemo(preset, DemoStartFrame + DemoFadeFrames / 2);
            transitionPosterFrames.Add(preset, frame);
        }
        return frame;
    }

    private void StartTransitionDemo(Image image, TransitionPresetInfo preset)
    {
        if (ReferenceEquals(animatedTransition?.Image, image)) return;
        StopTransitionDemo();
        animatedTransition = (image, preset);
        transitionDemoFrame = -DemoRestFrames;
        if (transitionDemoTimer is null)
        {
            transitionDemoTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(1000.0 / transitionDemo.FrameRate.Value)
            };
            transitionDemoTimer.Tick += (_, _) => AdvanceTransitionDemo();
        }
        AdvanceTransitionDemo();
        transitionDemoTimer.Start();
    }

    private void AdvanceTransitionDemo()
    {
        if (animatedTransition is not { } active) return;
        var frame = DemoStartFrame + Math.Clamp(transitionDemoFrame, 0, DemoFadeFrames);
        if (transitionDemoFrame == -DemoRestFrames || transitionDemoFrame is > 0 and <= DemoFadeFrames)
            active.Image.Source = RenderTransitionDemo(active.Preset, frame);
        transitionDemoFrame++;
        if (transitionDemoFrame > DemoFadeFrames + DemoRestFrames) transitionDemoFrame = -DemoRestFrames;
    }

    private void StopTransitionDemo()
    {
        transitionDemoTimer?.Stop();
        if (animatedTransition is { } active) active.Image.Source = TransitionPosterFrame(active.Preset);
        animatedTransition = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopTransitionDemo();
        transitionDemoRenderer?.Dispose();
        transitionDemoMedia?.Dispose();
        base.OnClosed(e);
    }

    private BitmapSource RenderTransitionDemo(TransitionPresetInfo preset, int frame)
    {
        transitionDemoMedia ??= new MediaService();
        transitionDemoRenderer ??= new TimelineRenderService(transitionDemoMedia, useGpuCompositing: false);
        if (!ReferenceEquals(transitionDemoPreset, preset))
        {
            transitionDemo.Tracks[0].Clips[1].FadeInTransition = MakeTransition(preset);
            transitionDemoPreset = preset;
        }
        var pixels = transitionDemoRenderer.RenderFrame(transitionDemo, frame, TransitionThumbnailWidth, TransitionThumbnailHeight);
        var bitmap = BitmapSource.Create(TransitionThumbnailWidth, TransitionThumbnailHeight, 96, 96, PixelFormats.Bgra32,
            null, pixels, TransitionThumbnailWidth * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static ProjectDocument CreateTransitionDemo()
    {
        var first = new MediaAsset { Path = "openrend://generator?kind=Checkerboard&size=22&color1=F4BF72&color2=98613F", HasVideo = true };
        var second = new MediaAsset { Path = "openrend://generator?kind=Checkerboard&size=12&color1=57C7B3&color2=225763", HasVideo = true };
        return new ProjectDocument
        {
            Width = TransitionThumbnailWidth, Height = TransitionThumbnailHeight, FrameRate = new(24, 1), Media = [first, second],
            Tracks = [new() { Kind = TrackKind.Video, Clips =
            [
                new() { MediaId = first.Id, DurationFrames = DemoStartFrame + DemoFadeFrames, FadeOutFrames = DemoFadeFrames },
                new() { MediaId = second.Id, StartFrame = DemoStartFrame, DurationFrames = DemoFadeFrames + DemoStartFrame, FadeInFrames = DemoFadeFrames }
            ] }]
        };
    }
}
