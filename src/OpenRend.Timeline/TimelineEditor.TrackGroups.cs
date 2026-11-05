using System;
using System.Collections.Generic;
using System.Linq;
using OpenRend.Core;

namespace OpenRend.Timeline;

public sealed partial class TimelineEditor
{
    public TrackGroup GroupSelectedTracks(IReadOnlyList<Guid> trackIds, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(trackIds);
        if (trackIds.Count == 0) throw new ArgumentException("No tracks specified for grouping.", nameof(trackIds));

        // Validate all exist in project
        var trackIndices = new List<(int Index, Guid Id)>();
        foreach (var id in trackIds)
        {
            var idx = Project.Tracks.FindIndex(t => t.Id == id);
            if (idx < 0) throw new KeyNotFoundException($"Track {id} was not found.");
            trackIndices.Add((idx, id));
        }

        // Check contiguity: sort by index
        trackIndices.Sort((a, b) => a.Index.CompareTo(b.Index));
        for (var i = 1; i < trackIndices.Count; i++)
        {
            if (trackIndices[i].Index != trackIndices[i - 1].Index + 1)
                throw new InvalidOperationException("Track grouping requires contiguous tracks to be selected.");
        }

        var sortedIds = trackIndices.Select(x => x.Id).ToList();

        // Calculate next default group number
        var existingNumbers = new HashSet<int>();
        if (Project.TrackGroups != null)
        {
            foreach (var g in Project.TrackGroups)
            {
                if (g.Name.StartsWith("Track Group ", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(g.Name["Track Group ".Length..], out var num))
                {
                    existingNumbers.Add(num);
                }
            }
        }
        var nextNum = 1;
        while (existingNumbers.Contains(nextNum)) nextNum++;

        var groupName = !string.IsNullOrWhiteSpace(name) ? name : $"Track Group {nextNum}";
        var newGroup = new TrackGroup
        {
            Id = Guid.NewGuid(),
            Name = groupName,
            TrackIds = sortedIds
        };

        Execute(p =>
        {
            p.TrackGroups ??= [];
            // Remove tracks from any existing groups
            for (var g = p.TrackGroups.Count - 1; g >= 0; g--)
            {
                var existing = p.TrackGroups[g];
                existing.TrackIds.RemoveAll(id => sortedIds.Contains(id));
                if (existing.TrackIds.Count == 0)
                {
                    p.TrackGroups.RemoveAt(g);
                }
            }
            p.TrackGroups.Add(newGroup.DeepClone());
        });

        return newGroup;
    }

    public void UngroupTracks(IReadOnlyList<Guid> trackIds)
    {
        ArgumentNullException.ThrowIfNull(trackIds);
        if (trackIds.Count == 0) return;

        Execute(p =>
        {
            if (p.TrackGroups == null || p.TrackGroups.Count == 0) return;
            p.TrackGroups.RemoveAll(g => g.TrackIds.Any(trackIds.Contains));
        });
    }

    public void RemoveTrackFromGroup(Guid trackId)
    {
        Execute(p =>
        {
            if (p.TrackGroups == null) return;
            for (var g = p.TrackGroups.Count - 1; g >= 0; g--)
            {
                var group = p.TrackGroups[g];
                if (group.TrackIds.Remove(trackId))
                {
                    if (group.TrackIds.Count == 0)
                    {
                        p.TrackGroups.RemoveAt(g);
                    }
                }
            }
        });
    }

    public void SetTrackGroupCollapsed(Guid groupId, bool isCollapsed)
    {
        Execute(p =>
        {
            var group = p.TrackGroups?.FirstOrDefault(g => g.Id == groupId);
            if (group != null)
            {
                group.IsCollapsed = isCollapsed;
            }
        });
    }

    public void SetTrackGroupMute(Guid groupId, bool isMuted)
    {
        Execute(p =>
        {
            var group = p.TrackGroups?.FirstOrDefault(g => g.Id == groupId);
            if (group != null)
            {
                group.IsMuted = isMuted;
            }
        });
    }

    public void SetTrackGroupSolo(Guid groupId, bool isSolo)
    {
        Execute(p =>
        {
            var group = p.TrackGroups?.FirstOrDefault(g => g.Id == groupId);
            if (group != null)
            {
                group.IsSolo = isSolo;
            }
        });
    }

    public void SetTrackGroupName(Guid groupId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Track group name cannot be empty.", nameof(name));

        Execute(p =>
        {
            var group = p.TrackGroups?.FirstOrDefault(g => g.Id == groupId);
            if (group != null)
            {
                group.Name = name;
            }
        });
    }

    public void SetTrackGroupColor(Guid groupId, string? colorHex)
    {
        Execute(p =>
        {
            var group = p.TrackGroups?.FirstOrDefault(g => g.Id == groupId);
            if (group != null)
            {
                group.ColorHex = colorHex;
            }
        });
    }
}

