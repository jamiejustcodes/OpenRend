using System.Text.Json;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class TranscriptTests
{
    [Fact]
    public void WordTimingPropertiesAndCloningAreExact()
    {
        var word = new WordTiming("OpenRend", 120, 24, confidence: 0.98, speaker: "Speaker 1");
        Assert.Equal("OpenRend", word.Word);
        Assert.Equal(120, word.StartFrame);
        Assert.Equal(24, word.DurationFrames);
        Assert.Equal(144, word.EndFrame);
        Assert.Equal(0.98, word.Confidence, 3);
        Assert.Equal("Speaker 1", word.Speaker);
        Assert.Equal(TranscriptWordKind.Word, word.Kind);

        var clone = word.DeepClone();
        Assert.Equal(word, clone);
        Assert.NotSame(word, clone);

        clone.Word = "Modified";
        Assert.NotEqual(word.Word, clone.Word);
    }

    [Fact]
    public void WordTimingValidationRejectsNegativeFrames()
    {
        var invalidStart = new WordTiming("Hello", -5, 10);
        Assert.Throws<InvalidOperationException>(() => invalidStart.Validate());

        var invalidDuration = new WordTiming("World", 0, -1);
        Assert.Throws<InvalidOperationException>(() => invalidDuration.Validate());
    }

    [Fact]
    public void ClipTranscriptFindsActiveWordAndRanges()
    {
        var words = new List<WordTiming>
        {
            new("First", 0, 30),
            new("Second", 40, 30),
            new("Pause", 70, 20, kind: TranscriptWordKind.Pause),
            new("Third", 90, 30)
        };
        var transcript = new ClipTranscript(words, "en");

        // Exact hits
        var w0 = transcript.GetWordAtFrame(15);
        Assert.NotNull(w0);
        Assert.Equal("First", w0.Word);

        var w1 = transcript.GetWordAtFrame(50);
        Assert.NotNull(w1);
        Assert.Equal("Second", w1.Word);

        var p = transcript.GetWordAtFrame(75);
        Assert.NotNull(p);
        Assert.Equal(TranscriptWordKind.Pause, p.Kind);

        // Gap hit (between 30 and 40)
        var gap = transcript.GetWordAtFrame(35);
        Assert.Null(gap);

        // Range query
        var range = transcript.GetWordsInRange(25, 80);
        Assert.Equal(3, range.Count);
        Assert.Equal("First", range[0].Word);
        Assert.Equal("Second", range[1].Word);
        Assert.Equal(TranscriptWordKind.Pause, range[2].Kind);
    }

    [Fact]
    public void ClipTranscriptSerializationPreservesData()
    {
        var clip = new TimelineClip
        {
            StartFrame = 0,
            DurationFrames = 200,
            Transcript = new ClipTranscript
            {
                Language = "en-US",
                Words =
                [
                    new("Welcome", 0, 24, 0.99, "Host"),
                    new("to", 25, 15, 0.95, "Host"),
                    new("OpenRend", 45, 30, 0.99, "Host")
                ]
            }
        };

        var json = JsonSerializer.Serialize(clip);
        var restored = JsonSerializer.Deserialize<TimelineClip>(json);

        Assert.NotNull(restored);
        Assert.NotNull(restored.Transcript);
        Assert.Equal(clip.Transcript, restored.Transcript);
        Assert.Equal(3, restored.Transcript.Words.Count);
        Assert.Equal("OpenRend", restored.Transcript.Words[2].Word);
    }
}
