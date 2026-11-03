using System.IO;
using System.Text.Json;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public class ClipPlaybackTests
{
    [Fact]
    public void CalculateAccumulatedSourceFrameForwardPlayback()
    {
        var env = new ClipEnvelope { Type = EnvelopeType.Velocity };
        // Default linear 100% velocity
        Assert.Equal(100, env.CalculateAccumulatedSourceFrame(0, 100, 1.0, false, 200));
        Assert.Equal(150, env.CalculateAccumulatedSourceFrame(50, 100, 1.0, false, 200));

        // 2.0x double speed playback rate
        Assert.Equal(200, env.CalculateAccumulatedSourceFrame(0, 100, 2.0, false, 200));

        // 0.5x half speed playback rate
        Assert.Equal(50, env.CalculateAccumulatedSourceFrame(0, 100, 0.5, false, 200));
    }

    [Fact]
    public void CalculateAccumulatedSourceFrameReversePlayback()
    {
        var env = new ClipEnvelope { Type = EnvelopeType.Velocity };
        // Reversing 100-frame clip starting at source frame 0:
        // Frame 0 should play the end frame (99)
        Assert.Equal(99, env.CalculateAccumulatedSourceFrame(0, 0, 1.0, true, 100));
        // Frame 10 should play frame 89
        Assert.Equal(89, env.CalculateAccumulatedSourceFrame(0, 10, 1.0, true, 100));
        // Frame 99 should play frame 0
        Assert.Equal(0, env.CalculateAccumulatedSourceFrame(0, 99, 1.0, true, 100));
    }

    [Fact]
    public void CalculateAccumulatedSourceFrameWithVelocityEnvelopePoints()
    {
        var env = new ClipEnvelope
        {
            Type = EnvelopeType.Velocity,
            Points =
            [
                new() { Frame = 0, Value = 1.0 },
                new() { Frame = 50, Value = 0.0 }, // Decelerate to freeze frame at frame 50
                new() { Frame = 100, Value = 1.0 } // Accelerate back to normal speed
            ]
        };

        // Frame 0
        Assert.Equal(0, env.CalculateAccumulatedSourceFrame(0, 0, 1.0, false, 100));

        // Frame 50 should have integrated ~25 frames (average speed 0.5)
        var at50 = env.CalculateAccumulatedSourceFrame(0, 50, 1.0, false, 100);
        Assert.InRange(at50, 23, 27);

        // Frame 100 should have integrated ~50 frames
        var at100 = env.CalculateAccumulatedSourceFrame(0, 100, 1.0, false, 100);
        Assert.InRange(at100, 48, 52);
    }

    [Fact]
    public void CalculateAccumulatedSourceFrameNegativeVelocityReverseRamping()
    {
        var env = new ClipEnvelope
        {
            Type = EnvelopeType.Velocity,
            Points =
            [
                new() { Frame = 0, Value = 1.0 },
                new() { Frame = 30, Value = -1.0 } // Smooth reverse ramp
            ]
        };

        // Beginning moves forward from frame 100
        var f10 = env.CalculateAccumulatedSourceFrame(100, 10, 1.0, false, 100);
        Assert.True(f10 > 100);

        // Point with negative speed reduces accumulated position
        var f50 = env.CalculateAccumulatedSourceFrame(100, 50, 1.0, false, 100);
        // Beyond frame 30, velocity is -1.0 so source frame decreases
        var f60 = env.CalculateAccumulatedSourceFrame(100, 60, 1.0, false, 100);
        Assert.True(f60 < f50);
    }

    [Fact]
    public void TimelineClipPlaybackPropertiesCloneAndSerializeRoundTrip()
    {
        var clip = new TimelineClip
        {
            Name = "VegasClip",
            IsReversed = true,
            PlaybackRate = 2.0,
            UnderSampleRate = 0.5,
            ResampleMode = VideoResampleMode.Disable,
            MaintainAspectRatio = false,
            ReduceInterlace = true,
            Normalize = true,
            InvertPhase = true
        };

        var clone = clip.DeepClone();
        Assert.True(clone.IsReversed);
        Assert.Equal(2.0, clone.PlaybackRate);
        Assert.Equal(0.5, clone.UnderSampleRate);
        Assert.Equal(VideoResampleMode.Disable, clone.ResampleMode);
        Assert.False(clone.MaintainAspectRatio);
        Assert.True(clone.ReduceInterlace);
        Assert.True(clone.Normalize);
        Assert.True(clone.InvertPhase);

        var json = JsonSerializer.Serialize(clip);
        var deserialized = JsonSerializer.Deserialize<TimelineClip>(json)!;
        Assert.True(deserialized.IsReversed);
        Assert.Equal(2.0, deserialized.PlaybackRate);
        Assert.Equal(0.5, deserialized.UnderSampleRate);
        Assert.Equal(VideoResampleMode.Disable, deserialized.ResampleMode);
        Assert.False(deserialized.MaintainAspectRatio);
        Assert.True(deserialized.ReduceInterlace);
        Assert.True(deserialized.Normalize);
        Assert.True(deserialized.InvertPhase);
    }
}
