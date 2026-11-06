using System;
using System.Collections.Generic;
using System.IO;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class SceneDetectionTests
{
    [Fact]
    public void Options_DefaultValues_AreSensible()
    {
        var options = new SceneDetectionOptions();
        Assert.Equal(0.65, options.Sensitivity, 2);
        Assert.Equal(500.0, options.MinSceneLengthMs);
        Assert.True(options.DetectHardCuts);
        Assert.False(options.DetectFades);
        Assert.False(options.DetectDissolves);
        Assert.True(options.IncludeGroupedTracks);
        Assert.InRange(options.Threshold, 20.0, 35.0);
    }

    [Fact]
    public void Options_Validation_ThrowsOnOutOfRange()
    {
        var opt1 = new SceneDetectionOptions { Sensitivity = -0.1 };
        Assert.Throws<InvalidDataException>(() => opt1.Validate());

        var opt2 = new SceneDetectionOptions { Sensitivity = 1.5 };
        Assert.Throws<InvalidDataException>(() => opt2.Validate());

        var opt3 = new SceneDetectionOptions { MinSceneLengthMs = 10.0 };
        Assert.Throws<InvalidDataException>(() => opt3.Validate());

        var opt4 = new SceneDetectionOptions { MinSceneLengthMs = 120_000.0 };
        Assert.Throws<InvalidDataException>(() => opt4.Validate());
    }

    [Fact]
    public void Options_DeepClone_ProducesExactIndependentCopy()
    {
        var opt = new SceneDetectionOptions
        {
            Sensitivity = 0.82,
            MinSceneLengthMs = 750.0,
            DetectHardCuts = true,
            DetectFades = true,
            IncludeGroupedTracks = false
        };

        var clone = opt.DeepClone();
        Assert.Equal(opt, clone);
        Assert.NotSame(opt, clone);

        clone.Sensitivity = 0.50;
        Assert.NotEqual(opt, clone);
    }

    [Fact]
    public void Options_CalculateMinFrames_CalculatesFramesFromFrameRate()
    {
        var opt = new SceneDetectionOptions { MinSceneLengthMs = 500.0 }; // 0.5 seconds
        var fps30 = new Rational(30, 1);
        var fps60 = new Rational(60, 1);
        var fps24 = new Rational(24, 1);

        Assert.Equal(15, opt.CalculateMinFrames(fps30));
        Assert.Equal(30, opt.CalculateMinFrames(fps60));
        Assert.Equal(12, opt.CalculateMinFrames(fps24));
    }

    [Fact]
    public void ComputeFrameDifference_IdenticalFrames_ReturnsZero()
    {
        const int width = 32;
        const int height = 18;
        var frame = new byte[width * height * 4];
        new Random(42).NextBytes(frame);

        var diff = SceneDetectionEngine.ComputeFrameDifference(frame, frame, width, height);
        Assert.Equal(0.0, diff, 4);
    }

    [Fact]
    public void ComputeFrameDifference_BlackVsWhite_ReturnsHighDifference()
    {
        const int width = 32;
        const int height = 18;
        var black = new byte[width * height * 4];
        var white = new byte[width * height * 4];
        Array.Fill(white, (byte)255);

        var diff = SceneDetectionEngine.ComputeFrameDifference(white, black, width, height);
        Assert.True(diff > 200.0, $"Expected diff > 200, but got {diff}");
    }

    [Fact]
    public void ComputeFrameDifference_SolidRedVsSolidBlue_CapturesColorDifference()
    {
        const int width = 16;
        const int height = 16;
        var red = new byte[width * height * 4];
        var blue = new byte[width * height * 4];

        for (var i = 0; i < width * height; i++)
        {
            red[i * 4 + 2] = 255; // R
            blue[i * 4] = 255;    // B
        }

        var diff = SceneDetectionEngine.ComputeFrameDifference(red, blue, width, height);
        Assert.True(diff > 50.0, $"Expected diff > 50 due to chromatic histogram delta, but got {diff}");
    }

    [Fact]
    public void DetectCutsFromScores_DebouncesRapidCuts_AccordingToMinFrames()
    {
        var opt = new SceneDetectionOptions
        {
            Sensitivity = 0.65, // Threshold ~29.25
            MinSceneLengthMs = 500.0
        };
        var fps = new Rational(30, 1); // 15 frames min

        // Scores with peaks at frame 20, 25 (too close!), 60, and 65 (too close!)
        var scores = new List<(long Offset, double Score)>
        {
            (10, 5.0),
            (20, 75.0), // CUT 1
            (25, 80.0), // Skipped: only 5 frames since frame 20 (< 15)
            (40, 10.0),
            (60, 90.0), // CUT 2 (40 frames since frame 20 >= 15)
            (65, 85.0), // Skipped: only 5 frames since frame 60 (< 15)
            (80, 12.0)
        };

        var cuts = SceneDetectionEngine.DetectCutsFromScores(scores, 100, opt, fps, clipStartAbsoluteFrame: 1000);

        Assert.Equal(2, cuts.Count);
        Assert.Equal(20, cuts[0].FrameOffset);
        Assert.Equal(1020, cuts[0].AbsoluteFrame);
        Assert.Equal(60, cuts[1].FrameOffset);
        Assert.Equal(1060, cuts[1].AbsoluteFrame);
    }

    [Fact]
    public void BuildSceneSpans_PartitionsTotalFramesIntoContiguousSpans()
    {
        var cuts = new List<SceneCut>
        {
            new(30, 1030, 1.0, 80.0, SceneCutType.HardCut),
            new(90, 1090, 3.0, 75.0, SceneCutType.HardCut)
        };

        var spans = SceneDetectionEngine.BuildSceneSpans(cuts, 150, clipStartAbsoluteFrame: 1000, baseName: "Camera 1");

        Assert.Equal(3, spans.Count);

        // Shot 1: [0, 30]
        Assert.Equal(1, spans[0].Index);
        Assert.Equal(0, spans[0].StartOffsetFrames);
        Assert.Equal(30, spans[0].EndOffsetFrames);
        Assert.Equal(30, spans[0].DurationFrames);
        Assert.Equal(1000, spans[0].StartAbsoluteFrame);
        Assert.Equal(1030, spans[0].EndAbsoluteFrame);
        Assert.Equal("Camera 1 [Scene 1]", spans[0].Name);

        // Shot 2: [30, 90]
        Assert.Equal(2, spans[1].Index);
        Assert.Equal(30, spans[1].StartOffsetFrames);
        Assert.Equal(90, spans[1].EndOffsetFrames);
        Assert.Equal(60, spans[1].DurationFrames);
        Assert.Equal(1030, spans[1].StartAbsoluteFrame);
        Assert.Equal(1090, spans[1].EndAbsoluteFrame);

        // Shot 3: [90, 150]
        Assert.Equal(3, spans[2].Index);
        Assert.Equal(90, spans[2].StartOffsetFrames);
        Assert.Equal(150, spans[2].EndOffsetFrames);
        Assert.Equal(60, spans[2].DurationFrames);
        Assert.Equal(1090, spans[2].StartAbsoluteFrame);
        Assert.Equal(1150, spans[2].EndAbsoluteFrame);
    }
}

