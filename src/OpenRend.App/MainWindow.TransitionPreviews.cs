using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenRend.Core;

namespace OpenRend.App;

public partial class MainWindow
{
    private const int DemoStartFrame = 12;
    private const int DemoFadeFrames = 24;
    private const int DemoRestFrames = 5;
    private readonly Dictionary<TransitionPresetInfo, BitmapSource> transitionPosterFrames = new();
    private readonly ProjectDocument transitionDemo = CreateTransitionDemo();
    private TransitionPresetInfo? transitionDemoPreset;
    private DispatcherTimer? transitionDemoTimer;
    private (Image Image, TransitionPresetInfo Preset)? animatedTransition;
    private int transitionDemoFrame;

    private void SetTransitionPresetContent(ListBoxItem item, TransitionPresetInfo preset)
    {
        var image = SetPresetPreviewContent(item, preset.Name, "TransitionPresetThumbnail",
            "Drag to a video overlap or apply to the selected events");

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

    private BitmapSource RenderTransitionDemo(TransitionPresetInfo preset, int frame)
    {
        if (!ReferenceEquals(transitionDemoPreset, preset))
        {
            transitionDemo.Tracks[0].Clips[1].FadeInTransition = MakeTransition(preset);
            transitionDemoPreset = preset;
        }
        return RenderPresetFrame(transitionDemo, frame);
    }

    private static ProjectDocument CreateTransitionDemo()
    {
        var first = new MediaAsset { Path = "openrend://generator?kind=Checkerboard&size=22&color1=F4BF72&color2=98613F", HasVideo = true };
        var second = new MediaAsset { Path = "openrend://generator?kind=Checkerboard&size=12&color1=57C7B3&color2=225763", HasVideo = true };
        return new ProjectDocument
        {
            Width = PresetPreviewWidth, Height = PresetPreviewHeight, FrameRate = new(24, 1), Media = [first, second],
            Tracks = [new() { Kind = TrackKind.Video, Clips =
            [
                new() { MediaId = first.Id, DurationFrames = DemoStartFrame + DemoFadeFrames, FadeOutFrames = DemoFadeFrames },
                new() { MediaId = second.Id, StartFrame = DemoStartFrame, DurationFrames = DemoFadeFrames + DemoStartFrame, FadeInFrames = DemoFadeFrames }
            ] }]
        };
    }
}
