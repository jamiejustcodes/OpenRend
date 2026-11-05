using System.Text.Json.Serialization;

namespace OpenRend.Core;

public sealed class AudioBusTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Bus A";
    public double Gain { get; set; } = 1.0;
    public double Pan { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AudioPanMode PanMode { get; set; } = AudioPanMode.Balance;
    public bool IsMuted { get; set; }
    public bool IsSolo { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<AudioEffect> AudioEffects { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ClipEnvelope>? Envelopes { get; set; }

    public AudioBusTrack DeepClone() => new()
    {
        Id = Id,
        Name = Name,
        Gain = Gain,
        Pan = Pan,
        PanMode = PanMode,
        IsMuted = IsMuted,
        IsSolo = IsSolo,
        AudioEffects = AudioEffects.Select(e => e.DeepClone()).ToList(),
        Envelopes = Envelopes?.Select(e => e.DeepClone()).ToList()
    };

    public void Validate()
    {
        if (Id == Guid.Empty) throw new InvalidDataException("Audio bus ID must not be empty.");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Audio bus name cannot be empty.");
        if (!double.IsFinite(Gain) || Gain < 0 || !double.IsFinite(Pan) || Pan is < -1 or > 1)
            throw new InvalidDataException("Audio bus levels are invalid.");
        if (!Enum.IsDefined(PanMode))
            throw new InvalidDataException("Audio bus pan mode is invalid.");
        if (AudioEffects != null)
        {
            var fxIds = new HashSet<Guid>();
            foreach (var fx in AudioEffects)
            {
                if (fx == null || !fxIds.Add(fx.Id)) throw new InvalidDataException("Audio bus effect IDs must be unique.");
                fx.Validate();
            }
        }
        if (Envelopes != null)
        {
            var envTypes = new HashSet<EnvelopeType>();
            foreach (var env in Envelopes)
            {
                if (env == null || !envTypes.Add(env.Type)) throw new InvalidDataException("Envelope types must be unique within an audio bus.");
                if (env.Type is not (EnvelopeType.Volume or EnvelopeType.Pan or EnvelopeType.Mute))
                    throw new InvalidDataException($"Envelope type '{env.Type}' is not supported on an audio bus.");
                env.Validate();
            }
        }
    }
}
