using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenRend.Core;
using OpenRend.UI;

namespace OpenRend.App;

public partial class MainWindow
{
    private Point videoFxDragOrigin;
    private ListBoxItem? videoFxDragItem;

    private void VideoFxDragStart(object sender, MouseButtonEventArgs e)
    {
        videoFxDragItem = e.OriginalSource is DependencyObject source
            ? ItemsControl.ContainerFromElement(VideoFxPresets, source) as ListBoxItem : null;
        videoFxDragOrigin = e.GetPosition(VideoFxPresets);
    }

    private void VideoFxDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            videoFxDragItem = null;
            return;
        }
        if (videoFxDragItem is not { DataContext: VideoEffectDefinition definition, Tag: VideoFxPreset preset } item) return;
        var movement = e.GetPosition(VideoFxPresets) - videoFxDragOrigin;
        if (Math.Abs(movement.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(movement.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        videoFxDragItem = null;
        var effect = new VideoEffectDrag(definition.Kind, preset.Parameters);
        DragDrop.DoDragDrop(item, new DataObject(TimelineView.VideoEffectDataFormat, effect), DragDropEffects.Copy);
    }

    private void VideoFxPreviewDragHover(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(TimelineView.VideoEffectDataFormat)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void VideoFxPreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(TimelineView.VideoEffectDataFormat) is not VideoEffectDrag preset) return;
        e.Handled = true;
        ApplyVideoFxDrop(EffectScope.Output, Guid.Empty, preset, (e.KeyStates & DragDropKeyStates.ShiftKey) != 0);
    }

    private void ApplyVideoFxDrop(EffectScope scope, Guid targetId, VideoEffectDrag preset, bool skipProperties) => Guard(() =>
    {
        Pause();
        var effect = vm.Editor.AddScopedVideoEffect(scope, targetId, preset.Kind, preset.Parameters);
        vm.Status = $"Added {VideoEffectCatalog.Get(effect.Kind).Name} to the video {scope.ToString().ToLowerInvariant()}.";
        RequestPreview();
        if (!skipProperties)
            _ = Dispatcher.BeginInvoke(() => Guard(() =>
                new VideoEffectsWindow(vm, scope, targetId, effect.Id) { Owner = this }.ShowDialog()));
    });
}
