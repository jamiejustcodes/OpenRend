using System;
using System.IO;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class TrackMotionTests
{
    [Fact]
    public void IdentityKeyframe_HasFullProjectDimensionsAndCentered()
    {
        var key = TrackMotionKeyframe.Identity(1920, 1080);
        Assert.Equal(0, key.Frame);
        Assert.Equal(0.0, key.PositionX);
        Assert.Equal(0.0, key.PositionY);
        Assert.Equal(0.0, key.PositionZ);
        Assert.Equal(1920.0, key.Width);
        Assert.Equal(1080.0, key.Height);
        Assert.Equal(0.0, key.RotationZ);
        Assert.True(key.IsIdentity(1920, 1080));
    }

    [Fact]
    public void Validation_RejectsNegativeFramesOrInvalidDimensions()
    {
        var key = new TrackMotionKeyframe { Frame = -1, Width = 1920, Height = 1080 };
        Assert.Throws<InvalidDataException>(() => key.Validate());

        var key2 = new TrackMotionKeyframe { Frame = 0, Width = 0, Height = 1080 };
        Assert.Throws<InvalidDataException>(() => key2.Validate());

        var key3 = new TrackMotionKeyframe { Frame = 0, Width = 1920, Height = 1080, Smoothness = 150 };
        Assert.Throws<InvalidDataException>(() => key3.Validate());
    }

    [Fact]
    public void Evaluate_InterpolatesBetweenKeyframesLinear()
    {
        var motion = new TrackMotion();
        motion.MotionKeyframes.Add(new TrackMotionKeyframe
        {
            Frame = 0,
            PositionX = 0,
            PositionY = 0,
            Width = 1920,
            Height = 1080,
            Interpolation = CropInterpolation.Linear
        });
        motion.MotionKeyframes.Add(new TrackMotionKeyframe
        {
            Frame = 100,
            PositionX = 200,
            PositionY = 100,
            Width = 960,
            Height = 540,
            Interpolation = CropInterpolation.Linear
        });

        // Frame 0
        var eval0 = motion.Evaluate(0, 1920, 1080);
        Assert.Equal(0.0, eval0.PositionX);
        Assert.Equal(1920.0, eval0.Width);

        // Frame 50 (midpoint)
        var eval50 = motion.Evaluate(50, 1920, 1080);
        Assert.Equal(100.0, eval50.PositionX);
        Assert.Equal(50.0, eval50.PositionY);
        Assert.Equal(1440.0, eval50.Width);
        Assert.Equal(810.0, eval50.Height);

        // Frame 100
        var eval100 = motion.Evaluate(100, 1920, 1080);
        Assert.Equal(200.0, eval100.PositionX);
        Assert.Equal(960.0, eval100.Width);
    }

    [Fact]
    public void Evaluate_RespectsHoldInterpolation()
    {
        var motion = new TrackMotion();
        motion.MotionKeyframes.Add(new TrackMotionKeyframe
        {
            Frame = 0,
            PositionX = 100,
            Width = 1920,
            Height = 1080,
            Interpolation = CropInterpolation.Hold
        });
        motion.MotionKeyframes.Add(new TrackMotionKeyframe
        {
            Frame = 50,
            PositionX = 500,
            Width = 1920,
            Height = 1080
        });

        var mid = motion.Evaluate(25, 1920, 1080);
        Assert.Equal(100.0, mid.PositionX); // Does not interpolate, holds until frame 50

        var at50 = motion.Evaluate(50, 1920, 1080);
        Assert.Equal(500.0, at50.PositionX);
    }

    [Fact]
    public void DeepClone_CreatesIndependentCopies()
    {
        var original = new TrackMotion
        {
            ShadowEnabled = true,
            GlowEnabled = true,
            MotionKeyframes = [TrackMotionKeyframe.Identity(1920, 1080)],
            ShadowKeyframes = [new TrackShadowKeyframe { Frame = 0, Blur = 10, Intensity = 0.8 }],
            GlowKeyframes = [new TrackGlowKeyframe { Frame = 0, Blur = 15, Intensity = 0.9 }]
        };

        var clone = original.DeepClone();
        Assert.True(clone.ShadowEnabled);
        Assert.True(clone.GlowEnabled);
        Assert.Single(clone.MotionKeyframes);
        Assert.Single(clone.ShadowKeyframes);
        Assert.Single(clone.GlowKeyframes);

        clone.MotionKeyframes[0].PositionX = 999;
        Assert.Equal(0.0, original.MotionKeyframes[0].PositionX);
    }
}

