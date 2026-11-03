using System;
using System.IO;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class SlideshowDataTests
{
    [Fact]
    public void SlideshowPictureRotationCyclesThrough90DegreeSteps()
    {
        var pic = new SlideshowPicture("C:/photos/vacation.jpg");
        Assert.Equal("vacation.jpg", pic.Filename);
        Assert.Equal(0, pic.OrientationDegrees);

        pic.RotateClockwise();
        Assert.Equal(90, pic.OrientationDegrees);

        pic.RotateClockwise();
        Assert.Equal(180, pic.OrientationDegrees);

        pic.RotateClockwise();
        Assert.Equal(270, pic.OrientationDegrees);

        pic.RotateClockwise();
        Assert.Equal(0, pic.OrientationDegrees);

        pic.RotateCounterClockwise();
        Assert.Equal(270, pic.OrientationDegrees);
    }

    [Fact]
    public void SlideshowPictureDeepCloneAndEqualityPreserveValues()
    {
        var pic1 = new SlideshowPicture("C:/photos/sunset.png")
        {
            OrientationDegrees = 180,
            PhotoDate = new DateTime(2026, 7, 15, 18, 30, 0),
            PanZoom = SlideshowPanZoom.ZoomIn
        };

        var pic2 = pic1.DeepClone();
        Assert.Equal(pic1, pic2);
        Assert.NotSame(pic1, pic2);

        pic2.OrientationDegrees = 270;
        Assert.NotEqual(pic1, pic2);
    }

    [Fact]
    public void SlideshowSettingsComputeEffectiveSlideAndTotalSeconds()
    {
        var settings = new SlideshowSettings
        {
            LengthSetting = SlideshowLengthSetting.PerSlide,
            SecondsPerSlide = 5.0,
            SecondsOfOverlap = 1.0,
            Pictures =
            [
                new SlideshowPicture("C:/p1.jpg"),
                new SlideshowPicture("C:/p2.jpg"),
                new SlideshowPicture("C:/p3.jpg"),
                new SlideshowPicture("C:/p4.jpg")
            ]
        };

        // 4 slides * 5s - 3 overlaps * 1s = 20 - 3 = 17 seconds
        Assert.Equal(5.0, settings.ComputeEffectiveSecondsPerSlide());
        Assert.Equal(17.0, settings.ComputeTotalTimelineSeconds());

        // TotalLength mode: target 33s total for 4 slides with 1s overlap
        // totalOverlap = 3s. Gross duration = 36s. Per slide = 9.0s.
        settings.LengthSetting = SlideshowLengthSetting.TotalLength;
        settings.TotalSlideshowSeconds = 33.0;

        Assert.Equal(9.0, settings.ComputeEffectiveSecondsPerSlide());
        Assert.Equal(33.0, settings.ComputeTotalTimelineSeconds());
    }

    [Fact]
    public void SlideshowSettingsValidationEnforcesStrictBoundaries()
    {
        var settings = new SlideshowSettings();
        // Empty pictures rejected
        Assert.Throws<InvalidOperationException>(() => settings.Validate());

        settings.Pictures.Add(new SlideshowPicture("C:/photo.jpg"));
        settings.SecondsPerSlide = 0;
        Assert.Throws<InvalidOperationException>(() => settings.Validate());

        settings.SecondsPerSlide = 5.0;
        settings.SecondsOfOverlap = 5.0; // Overlap cannot be >= duration
        Assert.Throws<InvalidOperationException>(() => settings.Validate());

        settings.SecondsOfOverlap = 1.0;
        settings.CustomStartFrame = -10;
        Assert.Throws<InvalidOperationException>(() => settings.Validate());

        settings.CustomStartFrame = 0;
        settings.Validate(); // Passes cleanly
    }
}

