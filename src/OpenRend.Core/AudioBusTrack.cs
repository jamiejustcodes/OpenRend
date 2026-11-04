using System.Text.Json.Serialization;

namespace OpenRend.Core;

public sealed class AudioBusTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Bus A";
    public double Gain { get; set; } = 1.0;
    public double Pan { get; set; }
    public bool IsMuted { get; set; }
    public bool IsSolo { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<AudioEffect> AudioEffects { get; set; } = [];

    public AudioBusTrack DeepClone() => new()
    {
        Id = Id,
        Name = Name,
        Gain = Gain,
        Pan = Pan,
        IsMuted = IsMuted,
        IsSolo = IsSolo,
        AudioEffects = AudioEffects.Select(e => e.DeepClone()).ToList()
    };

    public void Validate()
    {
        if (Id == Guid.Empty) throw new InvalidDataException("Audio bus ID must not be empty.");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Audio bus name cannot be empty.");
        if (!double.IsFinite(Gain) || Gain < 0 || !double.IsFinite(Pan) || Pan is < -1 or > 1)
            throw new InvalidDataException("Audio bus levels are invalid.");
        if (AudioEffects != null)
        {
            var fxIds = new HashSet<Guid>();
            foreach (var fx in AudioEffects)
            {
                if (fx == null || !fxIds.Add(fx.Id)) throw new InvalidDataException("Audio bus effect IDs must be unique.");
                fx.Validate();
            }
        }
    }
}

