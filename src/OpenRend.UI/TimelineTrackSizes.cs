using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using OpenRend.Core;

namespace OpenRend.UI;

internal enum TrackRowSize { Normal, Minimized, Expanded }

public sealed partial class TimelineView
{
    private readonly Dictionary<Guid, TrackRowSize> trackSizes = [];
    private const double MinimizedTrackHeight = 29;

    private double UncollapsedTrackHeight(Guid id) => trackSizes.GetValueOrDefault(id) switch
    {
        TrackRowSize.Minimized => MinimizedTrackHeight,
        TrackRowSize.Expanded => TrackHeight * 2,
        _ => TrackHeight
    };

    private double MaximumVerticalOffset => Math.Max(0, TotalTimelineContentHeight - Math.Max(0, ActualHeight - RulerHeight));

    internal void SetTrackRowSize(Guid id, TrackRowSize size)
    {
        if (trackSizes.GetValueOrDefault(id) == size) return;
        if (size == TrackRowSize.Normal) trackSizes.Remove(id);
        else trackSizes[id] = size;
        if (size == TrackRowSize.Minimized && levelTrack == id) CancelTrackLevelInput();
        verticalOffset = Math.Min(verticalOffset, MaximumVerticalOffset);
        (UIElementAutomationPeer.FromElement(this) as TimelineAutomationPeer)?.RefreshTrackControls(id);
        RefreshGeometry();
    }

    private MenuItem TrackSizeMenu(TimelineTrack track)
    {
        var menu = new MenuItem { Header = "Track height" };
        AutomationProperties.SetAutomationId(menu, "TrackHeightMenu");
        foreach (var (size, name) in new[]
        {
            (TrackRowSize.Minimized, "Minimize"), (TrackRowSize.Normal, "Restore"), (TrackRowSize.Expanded, "Expand")
        })
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = trackSizes.GetValueOrDefault(track.Id) == size };
            AutomationProperties.SetAutomationId(item, $"TrackHeight_{size}");
            item.Click += (_, _) => SetTrackRowSize(track.Id, size);
            menu.Items.Add(item);
        }
        return menu;
    }
}
