using System;
using System.IO;

namespace OpenRend.Core;

/// <summary>
/// Represents a timeline region defining a named span [StartFrame, EndFrame] on the project frame grid.
/// Aligned with Vegas Pro ruler regions and shortcuts (Key 'R', ScriptPortal.Vegas.Region).
/// </summary>
public sealed class TimelineRegion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Index { get; set; } = 1;
    public long StartFrame { get; set; }
    public long EndFrame { get; set; }
    public string Label { get; set; } = "";
    public int ColorId { get; set; }

    public long DurationFrames => Math.Max(0, EndFrame - StartFrame);

    public string ColorHex => TimelineMarker.MarkerColors[Math.Clamp(ColorId, 0, TimelineMarker.MarkerColors.Length - 1)];

    public TimelineRegion DeepClone() => new()
    {
        Id = Id,
        Index = Index,
        StartFrame = StartFrame,
        EndFrame = EndFrame,
        Label = Label,
        ColorId = ColorId
    };

    public void Validate()
    {
        if (Id == Guid.Empty) throw new InvalidDataException("Region ID must not be empty.");
        if (StartFrame < 0) throw new InvalidDataException("Region start frame must be non-negative.");
        if (EndFrame <= StartFrame) throw new InvalidDataException("Region end frame must be greater than start frame.");
        if (Index < 1) throw new InvalidDataException("Region index must be greater than zero.");
        if (Label == null) throw new InvalidDataException("Region label must not be null.");
    }
}
