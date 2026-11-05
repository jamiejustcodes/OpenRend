using System;
using System.IO;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public TimelineRegion AddRegion(long startFrame, long endFrame, string label = "", int colorId = 0)
    {
        var clampedStart = Math.Max(0, startFrame);
        var clampedEnd = Math.Max(clampedStart + 1, endFrame);
        var nextIndex = Project.Regions.Count == 0 ? 1 : Project.Regions.Max(r => r.Index) + 1;
        var region = new TimelineRegion
        {
            Index = nextIndex,
            StartFrame = clampedStart,
            EndFrame = clampedEnd,
            Label = label ?? "",
            ColorId = Math.Clamp(colorId, 0, TimelineMarker.MarkerColors.Length - 1)
        };
        Execute(p =>
        {
            p.Regions.Add(region);
            p.Regions.Sort((a, b) => a.StartFrame.CompareTo(b.StartFrame));
        });
        return region;
    }

    public bool RemoveRegion(Guid regionId)
    {
        if (Project.Regions.All(r => r.Id != regionId)) return false;
        Execute(p => p.Regions.RemoveAll(r => r.Id == regionId));
        return true;
    }

    public bool SetRegion(Guid regionId, long startFrame, long endFrame, string label, int colorId = 0)
    {
        var target = Project.Regions.FirstOrDefault(r => r.Id == regionId);
        if (target == null) return false;
        var clampedStart = Math.Max(0, startFrame);
        var clampedEnd = Math.Max(clampedStart + 1, endFrame);
        Execute(p =>
        {
            var r = p.Regions.First(x => x.Id == regionId);
            r.StartFrame = clampedStart;
            r.EndFrame = clampedEnd;
            r.Label = label ?? "";
            r.ColorId = Math.Clamp(colorId, 0, TimelineMarker.MarkerColors.Length - 1);
            p.Regions.Sort((a, b) => a.StartFrame.CompareTo(b.StartFrame));
        });
        return true;
    }

    public bool SetRegionLabel(Guid regionId, string label)
    {
        var target = Project.Regions.FirstOrDefault(r => r.Id == regionId);
        if (target == null) return false;
        Execute(p =>
        {
            var r = p.Regions.First(x => x.Id == regionId);
            r.Label = label ?? "";
        });
        return true;
    }

    public bool SetRegionRange(Guid regionId, long startFrame, long endFrame)
    {
        var target = Project.Regions.FirstOrDefault(r => r.Id == regionId);
        if (target == null) return false;
        var clampedStart = Math.Max(0, startFrame);
        var clampedEnd = Math.Max(clampedStart + 1, endFrame);
        Execute(p =>
        {
            var r = p.Regions.First(x => x.Id == regionId);
            r.StartFrame = clampedStart;
            r.EndFrame = clampedEnd;
            p.Regions.Sort((a, b) => a.StartFrame.CompareTo(b.StartFrame));
        });
        return true;
    }

    public void ClearRegions()
    {
        if (Project.Regions.Count == 0) return;
        Execute(p => p.Regions.Clear());
    }
}
