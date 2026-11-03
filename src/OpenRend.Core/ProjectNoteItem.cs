namespace OpenRend.Core;

public enum ProjectNoteStatus
{
    Active = 0,
    Resolved = 1
}

/// <summary>
/// Review note linked to a project frame or timecode marker.
/// Implements 1:1 Vegas Pro ProjectNotesLibrary data model.
/// </summary>
public sealed class ProjectNoteItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Note { get; set; } = "";
    public long Frame { get; set; }
    public string Timestamp { get; set; } = "";
    public string TimecodeMarker { get; set; } = "";
    public bool UserOverrideLabel { get; set; }
    public string OverrideLabel { get; set; } = "";
    public bool UseOverrideColor { get; set; }
    public int OverrideColorId { get; set; }
    public ProjectNoteStatus Status { get; set; } = ProjectNoteStatus.Active;
    public Guid? MarkerId { get; set; }

    public ProjectNoteItem DeepClone() => new()
    {
        Id = Id,
        Note = Note,
        Frame = Frame,
        Timestamp = Timestamp,
        TimecodeMarker = TimecodeMarker,
        UserOverrideLabel = UserOverrideLabel,
        OverrideLabel = OverrideLabel,
        UseOverrideColor = UseOverrideColor,
        OverrideColorId = OverrideColorId,
        Status = Status,
        MarkerId = MarkerId
    };

    public void Validate()
    {
        if (Id == Guid.Empty) throw new InvalidDataException("Project note ID must not be empty.");
        if (Frame < 0) throw new InvalidDataException("Project note frame must be non-negative.");
        if (Note == null) throw new InvalidDataException("Project note text must not be null.");
        if (!Enum.IsDefined(Status)) throw new InvalidDataException("Project note status is invalid.");
    }
}

