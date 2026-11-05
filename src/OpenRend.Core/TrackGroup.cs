using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace OpenRend.Core;

public sealed class TrackGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Track Group 1";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ColorHex { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsCollapsed { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsMuted { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsSolo { get; set; }

    public List<Guid> TrackIds { get; set; } = [];

    public void Validate()
    {
        if (Id == Guid.Empty)
            throw new InvalidDataException("Track group ID cannot be empty.");
        if (string.IsNullOrWhiteSpace(Name))
            throw new InvalidDataException("Track group name cannot be empty.");
        if (TrackIds == null || TrackIds.Count == 0)
            throw new InvalidDataException("Track group must contain at least one track.");

        var unique = new HashSet<Guid>();
        foreach (var id in TrackIds)
        {
            if (id == Guid.Empty || !unique.Add(id))
                throw new InvalidDataException("Track IDs within a track group must be unique and non-empty.");
        }
    }

    public TrackGroup DeepClone()
    {
        return new TrackGroup
        {
            Id = Id,
            Name = Name,
            ColorHex = ColorHex,
            IsCollapsed = IsCollapsed,
            IsMuted = IsMuted,
            IsSolo = IsSolo,
            TrackIds = [..TrackIds]
        };
    }
}

