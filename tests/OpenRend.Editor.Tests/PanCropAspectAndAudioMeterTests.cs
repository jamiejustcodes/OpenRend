using System;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class PanCropAspectAndAudioMeterTests
{
    [Fact]
    public void AspectCropMath_FitsAspectInsideSourceBounds()
    {
        // 16:9 source (1920x1080) matching 16:9 target aspect
        var (w1, h1) = CalculateAspect(1920, 1080, 16.0 / 9.0);
        Assert.Equal(1920, w1, 2);
        Assert.Equal(1080, h1, 2);

        // 16:9 landscape source cropped to 9:16 vertical (e.g. TikTok / Shorts)
        var (wVertical, hVertical) = CalculateAspect(1920, 1080, 9.0 / 16.0);
        Assert.Equal(1080, hVertical, 2);
        Assert.Equal(607.5, wVertical, 2);

        // 16:9 landscape source cropped to 1:1 square (Instagram)
        var (wSquare, hSquare) = CalculateAspect(1920, 1080, 1.0);
        Assert.Equal(1080, hSquare, 2);
        Assert.Equal(1080, wSquare, 2);

        // 16:9 landscape source cropped to 4:3 standard TV
        var (w43, h43) = CalculateAspect(1920, 1080, 4.0 / 3.0);
        Assert.Equal(1080, h43, 2);
        Assert.Equal(1440, w43, 2);

        // 16:9 landscape source cropped to 2.39:1 CinemaScope
        var (wScope, hScope) = CalculateAspect(1920, 1080, 2.39);
        Assert.Equal(1920, wScope, 2);
        Assert.Equal(1920.0 / 2.39, hScope, 2);
    }

    [Fact]
    public void RotationAngleMath_CalculatesCorrectAngleAnd15DegreeSnapping()
    {
        // Mouse directly above pivot (0, -100) -> 0 degrees
        double degUp = CalculateRotationAngle(0, 0, 0, -100, shiftSnap: false);
        Assert.Equal(0, degUp, 2);

        // Mouse directly to right of pivot (100, 0) -> 90 degrees
        double degRight = CalculateRotationAngle(0, 0, 100, 0, shiftSnap: false);
        Assert.Equal(90, degRight, 2);

        // Mouse directly below pivot (0, 100) -> 180 degrees
        double degDown = CalculateRotationAngle(0, 0, 0, 100, shiftSnap: false);
        Assert.Equal(180, degDown, 2);

        // Mouse to left of pivot (-100, 0) -> -90 degrees
        double degLeft = CalculateRotationAngle(0, 0, -100, 0, shiftSnap: false);
        Assert.Equal(-90, degLeft, 2);

        // Shift snap: 13.5 degrees snaps to 15.0 degrees
        double degSnap1 = SnapAngle(13.5, shiftSnap: true);
        Assert.Equal(15.0, degSnap1, 2);

        // Shift snap: 44.0 degrees snaps to 45.0 degrees
        double degSnap2 = SnapAngle(44.0, shiftSnap: true);
        Assert.Equal(45.0, degSnap2, 2);

        // Without shift: unsnapped angle is preserved exactly
        double degUnsnapped = SnapAngle(44.0, shiftSnap: false);
        Assert.Equal(44.0, degUnsnapped, 2);
    }

    [Fact]
    public void AudioMeterScaling_ConvertsLinearPeakToNormalizedMeterPosition()
    {
        // Silent or near-zero peak
        Assert.Equal(0.0, DbPosition(0.0), 3);
        Assert.Equal(0.0, DbPosition(0.00005), 3);

        // -60 dB baseline (0.001 linear) -> 0.0 position
        Assert.Equal(0.0, DbPosition(0.001), 3);

        // Unity gain (0 dB, 1.0 linear) -> 60 / 66 (~0.909)
        double unityPos = DbPosition(1.0);
        Assert.Equal(60.0 / 66.0, unityPos, 3);

        // +6 dB maximum peak (~1.995 linear) -> 1.0 (full meter height)
        double maxPos = DbPosition(Math.Pow(10.0, 6.0 / 20.0));
        Assert.Equal(1.0, maxPos, 3);
    }

    [Fact]
    public void RecordTimeCalculation_CalculatesAccurateHoursAndMinutes()
    {
        // 288,000 bytes per second = 48,000 samples/sec * 2 channels * 3 bytes (24-bit)
        // 1 hour = 3600 seconds = 3600 * 288,000 = 1,036,800,000 bytes
        long oneHourBytes = 1_036_800_000L;
        double freeSeconds = (double)oneHourBytes / 288000.0;
        var timeSpan = TimeSpan.FromSeconds(freeSeconds);
        var readout = $"Record Time (2 ch): {(int)timeSpan.TotalHours:00}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}:00";
        Assert.Equal("Record Time (2 ch): 01:00:00:00", readout);

        // 24 hours
        long oneDayBytes = oneHourBytes * 24L;
        var timeSpan24 = TimeSpan.FromSeconds((double)oneDayBytes / 288000.0);
        var readout24 = $"Record Time (2 ch): {(int)timeSpan24.TotalHours:00}:{timeSpan24.Minutes:00}:{timeSpan24.Seconds:00}:00";
        Assert.Equal("Record Time (2 ch): 24:00:00:00", readout24);
    }

    [Fact]
    public void LoopPlayback_WrapsPositionWhenAtOrBeyondDuration()
    {
        long durationFrames = 300;
        long currentPosition = 300;
        bool isLooping = true;

        if (currentPosition >= durationFrames && isLooping && durationFrames > 0)
        {
            currentPosition = 0;
        }

        Assert.Equal(0, currentPosition);
    }

    private static (double Width, double Height) CalculateAspect(double sourceW, double sourceH, double targetAspect)
    {
        double sourceAspect = sourceW / sourceH;
        if (sourceAspect > targetAspect)
        {
            double h = sourceH;
            double w = h * targetAspect;
            return (w, h);
        }
        else
        {
            double w = sourceW;
            double h = w / targetAspect;
            return (w, h);
        }
    }

    private static double CalculateRotationAngle(double pivotX, double pivotY, double mouseX, double mouseY, bool shiftSnap)
    {
        double rad = Math.Atan2(mouseY - pivotY, mouseX - pivotX);
        double deg = rad * 180.0 / Math.PI + 90.0;
        while (deg > 180.0) deg -= 360.0;
        while (deg <= -180.0) deg += 360.0;
        return SnapAngle(deg, shiftSnap);
    }

    private static double SnapAngle(double deg, bool shiftSnap)
    {
        return shiftSnap ? Math.Round(deg / 15.0) * 15.0 : deg;
    }

    private static double DbPosition(double peak)
    {
        if (peak <= 0.0001) return 0.0;
        double db = 20.0 * Math.Log10(peak);
        return Math.Clamp((db + 60.0) / 66.0, 0.0, 1.0);
    }
}
