using System;
using System.IO;
using System.Text.Json;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class EnvelopeTests
{
    [Fact]
    public void EnvelopePoint_ValidatesProperly()
    {
        var valid = new EnvelopePoint(10, 1.5, EnvelopeCurveType.Linear);
        valid.Validate();

        var negativeFrame = new EnvelopePoint(-1, 1.0);
        Assert.Throws<InvalidDataException>(() => negativeFrame.Validate());

        var nanValue = new EnvelopePoint(0, double.NaN);
        Assert.Throws<InvalidDataException>(() => nanValue.Validate());

        var invalidCurve = new EnvelopePoint(0, 1.0, (EnvelopeCurveType)999);
        Assert.Throws<InvalidDataException>(() => invalidCurve.Validate());
    }

    [Fact]
    public void ClipEnvelope_EvaluatesAllCurveInterpolationModes()
    {
        var env = new ClipEnvelope
        {
            Type = EnvelopeType.Velocity,
            Points =
            [
                new EnvelopePoint(0, 0.0, EnvelopeCurveType.Linear),
                new EnvelopePoint(100, 1.0, EnvelopeCurveType.Linear)
            ]
        };

        // Linear midpoint at frame 50
        env.Points[0].Curve = EnvelopeCurveType.Linear;
        Assert.Equal(0.5, env.ValueAt(50), 3);

        // Fast curve: accelerated rise, 1 - (1 - 0.5)^2 = 0.75
        env.Points[0].Curve = EnvelopeCurveType.Fast;
        Assert.Equal(0.75, env.ValueAt(50), 3);

        // Slow curve: decelerated rise, 0.5^2 = 0.25
        env.Points[0].Curve = EnvelopeCurveType.Slow;
        Assert.Equal(0.25, env.ValueAt(50), 3);

        // Smooth curve: S-curve cubic hermite, 3*(0.25) - 2*(0.125) = 0.5
        env.Points[0].Curve = EnvelopeCurveType.Smooth;
        Assert.Equal(0.5, env.ValueAt(50), 3);

        // Sharp curve: cubic, 0.5^3 = 0.125
        env.Points[0].Curve = EnvelopeCurveType.Sharp;
        Assert.Equal(0.125, env.ValueAt(50), 3);

        // Hold curve: stays at 0.0 until next point
        env.Points[0].Curve = EnvelopeCurveType.Hold;
        Assert.Equal(0.0, env.ValueAt(50), 3);
        Assert.Equal(1.0, env.ValueAt(100), 3);
    }

    [Fact]
    public void VelocityEnvelope_CalculatesAccumulatedSourceFramesAccurately()
    {
        var env = new ClipEnvelope
        {
            Type = EnvelopeType.Velocity,
            Points =
            [
                new EnvelopePoint(0, 1.0, EnvelopeCurveType.Linear),
                new EnvelopePoint(100, 1.0, EnvelopeCurveType.Linear)
            ]
        };

        // Normal 100% velocity: 50 project frames advance 50 source frames
        long sourceStart = 1000;
        Assert.Equal(1050, env.CalculateAccumulatedSourceFrame(sourceStart, 50));
        Assert.Equal(1100, env.CalculateAccumulatedSourceFrame(sourceStart, 100));

        // 200% double speed: 50 project frames advance 100 source frames
        env.Points[0].Value = 2.0;
        env.Points[1].Value = 2.0;
        Assert.Equal(1100, env.CalculateAccumulatedSourceFrame(sourceStart, 50));

        // 50% half speed: 50 project frames advance 25 source frames
        env.Points[0].Value = 0.5;
        env.Points[1].Value = 0.5;
        Assert.Equal(1025, env.CalculateAccumulatedSourceFrame(sourceStart, 50));

        // 0% freeze frame: source frame remains constant
        env.Points[0].Value = 0.0;
        env.Points[1].Value = 0.0;
        Assert.Equal(sourceStart, env.CalculateAccumulatedSourceFrame(sourceStart, 50));

        // -100% reverse speed: advances backward
        env.Points[0].Value = -1.0;
        env.Points[1].Value = -1.0;
        Assert.Equal(950, env.CalculateAccumulatedSourceFrame(sourceStart, 50));
    }

    [Fact]
    public void ClipEnvelope_CloningAndSerialization_PreservesFullFidelity()
    {
        var original = new ClipEnvelope
        {
            Type = EnvelopeType.Composite,
            IsEnabled = true,
            Points =
            [
                new EnvelopePoint(0, 1.0, EnvelopeCurveType.Smooth),
                new EnvelopePoint(30, 0.2, EnvelopeCurveType.Fast),
                new EnvelopePoint(60, 0.8, EnvelopeCurveType.Linear)
            ]
        };

        var clone = original.DeepClone();
        Assert.Equal(original.Id, clone.Id);
        Assert.Equal(original.Type, clone.Type);
        Assert.Equal(original.IsEnabled, clone.IsEnabled);
        Assert.Equal(original.Points.Count, clone.Points.Count);
        Assert.Equal(original.Points[1].Value, clone.Points[1].Value);
        Assert.Equal(original.Points[1].Curve, clone.Points[1].Curve);

        // JSON serialization round-trip
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ClipEnvelope>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.Points.Count, deserialized.Points.Count);
        Assert.Equal(original.Points[1].Curve, deserialized.Points[1].Curve);
    }

    [Fact]
    public void TrackEnvelope_ValidatesTrackAllowedEnvelopeTypes()
    {
        var doc = new ProjectDocument { Width = 1920, Height = 1080, FrameRate = new Rational(30, 1) };
        var media = new MediaAsset { Id = Guid.NewGuid(), Path = "test.mp4", HasVideo = true, HasAudio = true, DurationSeconds = 10 };
        doc.Media.Add(media);

        var videoTrack = new TimelineTrack { Kind = TrackKind.Video, Name = "Video 1" };
        doc.Tracks.Add(videoTrack);

        // Valid video track envelope: Composite
        videoTrack.Envelopes = [new ClipEnvelope { Type = EnvelopeType.Composite, Points = [new EnvelopePoint(0, 1.0)] }];
        doc.Validate();

        // Invalid video track envelope: Volume (only for audio)
        videoTrack.Envelopes.Add(new ClipEnvelope { Type = EnvelopeType.Volume, Points = [new EnvelopePoint(0, 1.0)] });
        Assert.Throws<InvalidDataException>(() => doc.Validate());

        // Audio track validation
        videoTrack.Envelopes.Clear();
        var audioTrack = new TimelineTrack { Kind = TrackKind.Audio, Name = "Audio 1" };
        doc.Tracks.Add(audioTrack);

        audioTrack.Envelopes = [new ClipEnvelope { Type = EnvelopeType.Volume, Points = [new EnvelopePoint(0, 1.0)] }];
        doc.Validate();

        // Invalid audio track envelope: Velocity (only for clips)
        audioTrack.Envelopes.Add(new ClipEnvelope { Type = EnvelopeType.Velocity, Points = [new EnvelopePoint(0, 1.0)] });
        Assert.Throws<InvalidDataException>(() => doc.Validate());
    }
}