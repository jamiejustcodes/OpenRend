namespace OpenRend.Core;

public enum TranscriptWordKind
{
    Word = 0,
    Pause = 1
}

/// <summary>
/// A time-aligned word or pause token within a clip's speech transcript,
/// measured in integer frames relative to the clip's StartFrame.
/// </summary>
public sealed class WordTiming : IEquatable<WordTiming>
{
    public string Word { get; set; } = string.Empty;
    public long StartFrame { get; set; }
    public long DurationFrames { get; set; }
    public double Confidence { get; set; } = 1.0;
    public string? Speaker { get; set; }
    public TranscriptWordKind Kind { get; set; } = TranscriptWordKind.Word;

    public long EndFrame => StartFrame + DurationFrames;

    public WordTiming() { }

    public WordTiming(
        string word,
        long startFrame,
        long durationFrames,
        double confidence = 1.0,
        string? speaker = null,
        TranscriptWordKind kind = TranscriptWordKind.Word)
    {
        Word = word ?? string.Empty;
        StartFrame = startFrame;
        DurationFrames = durationFrames;
        Confidence = confidence;
        Speaker = speaker;
        Kind = kind;
    }

    public WordTiming DeepClone() => new()
    {
        Word = Word,
        StartFrame = StartFrame,
        DurationFrames = DurationFrames,
        Confidence = Confidence,
        Speaker = Speaker,
        Kind = Kind
    };

    public void Validate()
    {
        if (StartFrame < 0) throw new InvalidOperationException("Word start frame cannot be negative.");
        if (DurationFrames < 0) throw new InvalidOperationException("Word duration cannot be negative.");
    }

    public bool Equals(WordTiming? other) =>
        other is not null &&
        Word == other.Word &&
        StartFrame == other.StartFrame &&
        DurationFrames == other.DurationFrames &&
        Math.Abs(Confidence - other.Confidence) < 1e-4 &&
        Speaker == other.Speaker &&
        Kind == other.Kind;

    public override bool Equals(object? obj) => obj is WordTiming other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Word, StartFrame, DurationFrames, Kind);

    public override string ToString() =>
        Kind == TranscriptWordKind.Pause
            ? $"[Pause: {StartFrame}-{EndFrame} ({DurationFrames}f)]"
            : $"\"{Word}\" ({StartFrame}-{EndFrame}f)";
}

/// <summary>
/// Full speech transcript attached to a timeline clip, holding word-level timings
/// for text-based editing, synchronized playback cursor, and ripple editing.
/// </summary>
public sealed class ClipTranscript : IEquatable<ClipTranscript>
{
    public List<WordTiming> Words { get; set; } = [];
    public string Language { get; set; } = "en";

    public ClipTranscript() { }

    public ClipTranscript(IEnumerable<WordTiming> words, string language = "en")
    {
        Words = words?.Select(w => w.DeepClone()).ToList() ?? [];
        Language = language ?? "en";
    }

    public ClipTranscript DeepClone() => new()
    {
        Words = Words.Select(w => w.DeepClone()).ToList(),
        Language = Language
    };

    public void Validate()
    {
        foreach (var word in Words)
            word.Validate();
    }

    /// <summary>
    /// Returns the active word or pause at the given frame offset within the clip.
    /// </summary>
    public WordTiming? GetWordAtFrame(long frameOffset) =>
        Words.FirstOrDefault(w => frameOffset >= w.StartFrame && frameOffset < w.EndFrame);

    /// <summary>
    /// Returns all words or pauses that intersect the specified range of frame offsets.
    /// </summary>
    public List<WordTiming> GetWordsInRange(long startOffset, long endOffset) =>
        Words.Where(w => w.EndFrame > startOffset && w.StartFrame < endOffset).ToList();

    public bool Equals(ClipTranscript? other)
    {
        if (other is null) return false;
        if (Language != other.Language || Words.Count != other.Words.Count) return false;
        for (var i = 0; i < Words.Count; i++)
        {
            if (!Words[i].Equals(other.Words[i])) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is ClipTranscript other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Words.Count, Language);
}
