namespace OpenRend.Core;

/// <summary>
/// Represents a timeline marker placed on the project frame grid.
/// Aligned with Vegas Pro ruler markers and shortcuts (Key 'M').
/// </summary>
public sealed class TimelineMarker
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Index { get; set; } = 1;
    public long Frame { get; set; }
    public string Label { get; set; } = "";
    public int ColorId { get; set; }

    /// <summary>
    /// Standard Vegas Pro marker palette colors (8 track/marker color slots).
    /// </summary>
    public static readonly string[] MarkerColors =
    [
        "#3A6EA5", // Track 0 - Blue
        "#E06D53", // Track 1 - Orange/Coral
        "#43AA8B", // Track 2 - Teal/Green
        "#F9C74F", // Track 3 - Gold/Yellow
        "#90BE6D", // Track 4 - Sage
        "#F94144", // Track 5 - Crimson
        "#577590", // Track 6 - Steel Slate
        "#9B5DE5"  // Track 7 - Violet
    ];

    public string ColorHex => MarkerColors[Math.Clamp(ColorId, 0, MarkerColors.Length - 1)];

    public TimelineMarker DeepClone() => new()
    {
        Id = Id,
        Index = Index,
        Frame = Frame,
        Label = Label,
        ColorId = ColorId
    };

    public void Validate()
    {
        if (Id == Guid.Empty) throw new InvalidDataException("Marker ID must not be empty.");
        if (Frame < 0) throw new InvalidDataException("Marker frame must be non-negative.");
        if (Index < 1) throw new InvalidDataException("Marker index must be greater than zero.");
        if (Label == null) throw new InvalidDataException("Marker label must not be null.");
    }
}

