using System;
using System.Collections.Generic;
using System.IO;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class PanCropPreferencesTests
{
    [Fact]
    public void CatmullRomTrajectorySmoothingCurvesThroughIntermediateKeyframes()
    {
        // 3 keyframes turning a 90° corner:
        // Keyframe 0 at frame 0: Center = (0, 0)
        // Keyframe 1 at frame 60: Center = (100, 0)
        // Keyframe 2 at frame 120: Center = (100, 100)
        var animSmooth = new CropAnimation
        {
            Keyframes =
            [
                new() { Frame = 0, State = new CropState(320, 180, 0, 0, 0, 160, 90), Interpolation = CropInterpolation.Smooth, Smoothness = 100.0 },
                new() { Frame = 60, State = new CropState(320, 180, 100, 0, 0, 160, 90), Interpolation = CropInterpolation.Smooth, Smoothness = 100.0 },
                new() { Frame = 120, State = new CropState(320, 180, 100, 100, 0, 160, 90), Interpolation = CropInterpolation.Smooth, Smoothness = 100.0 }
            ]
        };
        animSmooth.Validate();

        var animLinear = new CropAnimation
        {
            Keyframes =
            [
                new() { Frame = 0, State = new CropState(320, 180, 0, 0, 0, 160, 90), Interpolation = CropInterpolation.Linear },
                new() { Frame = 60, State = new CropState(320, 180, 100, 0, 0, 160, 90), Interpolation = CropInterpolation.Linear },
                new() { Frame = 120, State = new CropState(320, 180, 100, 100, 0, 160, 90), Interpolation = CropInterpolation.Linear }
            ]
        };
        animLinear.Validate();

        // In segment [60, 120], linear path stays at CenterX = 100.
        // Smooth Catmull-Rom path bulges outward past 100 to curve gracefully through the corner.
        var evalLinear70 = animLinear.Evaluate(70);
        var evalSmooth70 = animSmooth.Evaluate(70);

        Assert.Equal(100.0, evalLinear70.CenterX, 6);
        Assert.True(evalSmooth70.CenterX > 100.0, "Smooth motion path should curve outward through corner keyframe.");

        // At exact keyframe frames, both land precisely on the keyframe coordinates
        var kf0 = animSmooth.Evaluate(0);
        Assert.Equal(0, kf0.CenterX, 6);
        Assert.Equal(0, kf0.CenterY, 6);

        var kf1 = animSmooth.Evaluate(60);
        Assert.Equal(100, kf1.CenterX, 6);
        Assert.Equal(0, kf1.CenterY, 6);

        var kf2 = animSmooth.Evaluate(120);
        Assert.Equal(100, kf2.CenterX, 6);
        Assert.Equal(100, kf2.CenterY, 6);
    }

    [Fact]
    public void CatmullRomWithZeroSmoothnessBehavesStrictlyLinear()
    {
        var animZeroSmooth = new CropAnimation
        {
            Keyframes =
            [
                new() { Frame = 0, State = new CropState(320, 180, 0, 0, 0, 160, 90), Interpolation = CropInterpolation.Smooth, Smoothness = 0.0 },
                new() { Frame = 60, State = new CropState(320, 180, 100, 0, 0, 160, 90), Interpolation = CropInterpolation.Smooth, Smoothness = 0.0 },
                new() { Frame = 120, State = new CropState(320, 180, 100, 100, 0, 160, 90), Interpolation = CropInterpolation.Smooth, Smoothness = 0.0 }
            ]
        };
        animZeroSmooth.Validate();

        var eval = animZeroSmooth.Evaluate(30);
        Assert.Equal(50, eval.CenterX, 6);
        Assert.Equal(0, eval.CenterY, 6);
    }

    [Fact]
    public void PanCropPreferencesStoreSavesAndLoadsCustomPresets()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"pancrop_test_{Guid.NewGuid():N}.json");
        try
        {
            var initial = new PanCropPreferences
            {
                CustomPresets =
                [
                    new PanCropPreset
                    {
                        Name = "YouTube Shorts (Vertical 9:16)",
                        Width = 1080,
                        Height = 1920,
                        CenterX = 540,
                        CenterY = 960,
                        RotationDegrees = 0,
                        PivotX = 540,
                        PivotY = 960
                    },
                    PanCropPreset.FromState("Custom Cinematic 2.40", new CropState(1920, 800, 960, 540, 0, 960, 540))
                ]
            };

            PanCropPreferencesStore.Save(initial, tempFile);
            Assert.True(File.Exists(tempFile));

            var loaded = PanCropPreferencesStore.Load(tempFile);
            Assert.Equal(2, loaded.CustomPresets.Count);
            Assert.Equal("YouTube Shorts (Vertical 9:16)", loaded.CustomPresets[0].Name);
            Assert.Equal(1080, loaded.CustomPresets[0].Width);
            Assert.Equal(1920, loaded.CustomPresets[0].Height);

            Assert.Equal("Custom Cinematic 2.40", loaded.CustomPresets[1].Name);
            var state = loaded.CustomPresets[1].ToState();
            Assert.Equal(1920, state.Width);
            Assert.Equal(800, state.Height);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void PanCropPresetValidationRejectsEmptyNameAndInvalidGeometry()
    {
        var invalidName = new PanCropPreset
        {
            Name = "  ",
            Width = 100,
            Height = 100
        };
        Assert.Throws<InvalidDataException>(() => invalidName.Validate());

        var invalidGeom = new PanCropPreset
        {
            Name = "Valid Name",
            Width = -10,
            Height = 100
        };
        Assert.Throws<InvalidDataException>(() => invalidGeom.Validate());
    }
}
