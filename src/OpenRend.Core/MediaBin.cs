using System.Text.Json.Serialization;

namespace OpenRend.Core;

/// <summary>
/// Vegas Pro 1:1 Project Media Bin representation (ScriptPortal.Vegas.MediaBin).
/// </summary>
public sealed class MediaBin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Bin";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ParentBinId { get; set; }

    public List<Guid> MediaIds { get; set; } = [];

    public MediaBin DeepClone() => new()
    {
        Id = Id,
        Name = Name,
        ParentBinId = ParentBinId,
        MediaIds = [..MediaIds]
    };

    public void Validate()
    {
        if (Id == Guid.Empty) throw new InvalidDataException("Media bin ID must not be empty.");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Media bin name cannot be empty.");
    }
}

