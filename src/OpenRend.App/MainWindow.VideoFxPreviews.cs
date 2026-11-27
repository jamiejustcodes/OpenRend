using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using OpenRend.Core;

namespace OpenRend.App;

public partial class MainWindow
{
    private readonly Dictionary<(VideoEffectKind Kind, string Preset), BitmapSource> videoFxThumbnails = new();
    private readonly ProjectDocument videoFxDemo = CreateVideoFxDemo();
    private BitmapSource? videoFxOriginal;

    private void SetVideoFxPresetContent(ListBoxItem item, VideoEffectKind kind, VideoFxPreset preset)
    {
        var image = SetPresetPreviewContent(item, preset.Name, "VideoFxPresetThumbnail", "Apply to the selected video events");
        item.IsVisibleChanged += (_, _) =>
        {
            if (item.IsVisible) image.Source = VideoFxThumbnail(kind, preset);
            else image.BeginAnimation(Image.SourceProperty, null);
        };
        item.MouseEnter += (_, _) =>
        {
            if (!SystemParameters.ClientAreaAnimation || Mouse.LeftButton != MouseButtonState.Released ||
                Mouse.RightButton != MouseButtonState.Released) return;
            videoFxOriginal ??= RenderVideoFxDemo(null);
            var animation = new ObjectAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(1800),
                RepeatBehavior = RepeatBehavior.Forever
            };
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(videoFxOriginal, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame(VideoFxThumbnail(kind, preset), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800))));
            image.BeginAnimation(Image.SourceProperty, animation);
        };
        item.MouseLeave += (_, _) => image.BeginAnimation(Image.SourceProperty, null);
    }

    private BitmapSource VideoFxThumbnail(VideoEffectKind kind, VideoFxPreset preset)
    {
        var key = (kind, preset.Name);
        if (!videoFxThumbnails.TryGetValue(key, out var thumbnail))
        {
            thumbnail = RenderVideoFxDemo(VideoEffect.Create(kind, preset.Parameters));
            videoFxThumbnails.Add(key, thumbnail);
        }
        return thumbnail;
    }

    private BitmapSource RenderVideoFxDemo(VideoEffect? effect)
    {
        videoFxDemo.Tracks[0].Clips[0].VideoEffects = effect is null ? [] : [effect];
        return RenderPresetFrame(videoFxDemo, 12);
    }

    private static ProjectDocument CreateVideoFxDemo()
    {
        var pattern = new MediaAsset { Path = "openrend://generator?kind=TestPattern&type=smpte_ntsc", HasVideo = true };
        return new ProjectDocument
        {
            Width = PresetPreviewWidth, Height = PresetPreviewHeight, FrameRate = new(24, 1), Media = [pattern],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [new() { MediaId = pattern.Id, DurationFrames = 24 }] }]
        };
    }
}
