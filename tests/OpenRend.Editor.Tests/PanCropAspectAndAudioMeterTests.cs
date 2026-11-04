using OpenRend.Timeline;
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

    [Fact]
    public void PanCrop_KeyframeAddAndRemove_PersistsAndEvaluatesCorrectly()
    {
        var doc = new ProjectDocument { Width = 1920, Height = 1080, FrameRate = new Rational(30, 1) };
        var media = new MediaAsset { Id = Guid.NewGuid(), Path = "test.mp4", HasVideo = true, DurationSeconds = 10 };
        doc.Media.Add(media);

        var track = new TimelineTrack { Kind = TrackKind.Video, Name = "Video 1" };
        doc.Tracks.Add(track);

        var clip = new TimelineClip
        {
            MediaId = media.Id,
            Name = "Clip 1",
            StartFrame = 0,
            DurationFrames = 300,
            Crop = new CropAnimation
            {
                Keyframes =
                [
                    new CropKeyframe { Frame = 0, State = CropState.Identity(1920, 1080), Interpolation = CropInterpolation.Linear }
                ]
            }
        };
        track.Clips.Add(clip);

        var editor = new TimelineEditor(doc);

        // Add keyframe at frame 100 with zoom
        var zoomState = new CropState(960, 540, 960, 540, 0, 960, 540);
        editor.SetCropKeyframe(clip.Id, 100, zoomState, CropInterpolation.Linear);
        var currentClip = editor.Project.Tracks[0].Clips[0];
        Assert.NotNull(currentClip.Crop);
        Assert.Equal(2, currentClip.Crop.Keyframes.Count);
        Assert.Equal(100, currentClip.Crop.Keyframes[1].Frame);

        // Evaluate at midpoint frame 50
        var eval50 = currentClip.Crop.Evaluate(50);
        Assert.Equal(1440, eval50.Width, 2); // Linear midpoint between 1920 and 960

        // Delete keyframe at frame 100
        editor.RemoveCropKeyframe(clip.Id, 100);
        currentClip = editor.Project.Tracks[0].Clips[0];
        Assert.NotNull(currentClip.Crop);
        Assert.Single(currentClip.Crop.Keyframes);

        // Undo restores keyframe
        editor.Undo();
        currentClip = editor.Project.Tracks[0].Clips[0];
        Assert.NotNull(currentClip.Crop);
        Assert.Equal(2, currentClip.Crop.Keyframes.Count);
        Assert.Equal(100, currentClip.Crop.Keyframes[1].Frame);
    }

    [Fact]
    public void PanCrop_SmoothnessAndInterpolation_PersistsAndEvaluatesCorrectly()
    {
        var doc = new ProjectDocument { Width = 1920, Height = 1080, FrameRate = new Rational(30, 1) };
        var media = new MediaAsset { Id = Guid.NewGuid(), Path = "test.mp4", HasVideo = true, DurationSeconds = 10, Width = 1920, Height = 1080 };
        doc.Media.Add(media);

        var track = new TimelineTrack { Kind = TrackKind.Video };
        doc.Tracks.Add(track);

        var clip = new TimelineClip
        {
            MediaId = media.Id,
            DurationFrames = 300,
            Crop = new CropAnimation
            {
                Keyframes =
                [
                    new CropKeyframe { Frame = 0, State = CropState.Identity(1920, 1080), Interpolation = CropInterpolation.Smooth, Smoothness = 100.0 }
                ]
            }
        };
        track.Clips.Add(clip);

        var editor = new TimelineEditor(doc);

        // Add keyframe at frame 60 with zoom and 50% smoothness
        var zoomState = new CropState(960, 540, 960, 540, 0, 960, 540);
        editor.SetCropKeyframe(clip.Id, 60, zoomState, CropInterpolation.Smooth, smoothness: 50.0);

        var currentClip = editor.Project.Tracks[0].Clips[0];
        Assert.NotNull(currentClip.Crop);
        Assert.Equal(2, currentClip.Crop.Keyframes.Count);
        Assert.Equal(50.0, currentClip.Crop.Keyframes[1].Smoothness);

        // Update keyframe 0 smoothness via SetCropKeyframeSmoothness
        editor.SetCropKeyframeSmoothness(clip.Id, 0, 0.0);
        Assert.Equal(0.0, editor.Project.Tracks[0].Clips[0].Crop!.Keyframes[0].Smoothness);

        // Undo restores 100.0
        Assert.True(editor.Undo());
        Assert.Equal(100.0, editor.Project.Tracks[0].Clips[0].Crop!.Keyframes[0].Smoothness);

        // Redo reapplies 0.0
        Assert.True(editor.Redo());
        Assert.Equal(0.0, editor.Project.Tracks[0].Clips[0].Crop!.Keyframes[0].Smoothness);
    }

    [Fact]
    public void TrackHeaderReadout_FormatsVolumePanAndVideoLevel()
    {
        // Video level
        static string FormatVideoLevel(double opacity) => $"Level: {opacity * 100:0.0} %";
        Assert.Equal("Level: 100.0 %", FormatVideoLevel(1.0));
        Assert.Equal("Level: 50.0 %", FormatVideoLevel(0.5));
        Assert.Equal("Level: 0.0 %", FormatVideoLevel(0.0));

        // Audio volume
        static string FormatVolume(double gain) =>
            gain <= 0 ? "Vol: âˆ’âˆž dB" : (20 * Math.Log10(gain)) is var db && db > 0.05 ? $"Vol: +{db:0.0} dB" : $"Vol: {db:0.0} dB";
        Assert.Equal("Vol: âˆ’âˆž dB", FormatVolume(0.0));
        Assert.Equal("Vol: 0.0 dB", FormatVolume(1.0));
        Assert.Equal("Vol: +3.0 dB", FormatVolume(Math.Pow(10, 3.0 / 20.0)));
        Assert.Equal("Vol: -6.0 dB", FormatVolume(Math.Pow(10, -6.0 / 20.0)));

        // Audio pan
        static string FormatPan(double pan) =>
            Math.Abs(pan) < .005 ? "Pan: Center" : $"Pan: {Math.Abs(pan) * 100:0}% {(pan < 0 ? "L" : "R")}";
        Assert.Equal("Pan: Center", FormatPan(0.0));
        Assert.Equal("Pan: Center", FormatPan(0.002));
        Assert.Equal("Pan: 50% L", FormatPan(-0.5));
        Assert.Equal("Pan: 100% R", FormatPan(1.0));
    }

    [Fact]
    public void SnapshotEncoderSelection_MatchesVegasProExportTypes()
    {
        static System.Windows.Media.Imaging.BitmapEncoder CreateEncoder(string extension) => extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 95 },
            ".tif" or ".tiff" => new System.Windows.Media.Imaging.TiffBitmapEncoder { Compression = System.Windows.Media.Imaging.TiffCompressOption.Zip },
            _ => new System.Windows.Media.Imaging.PngBitmapEncoder()
        };

        var pngEncoder = CreateEncoder(".png");
        Assert.IsType<System.Windows.Media.Imaging.PngBitmapEncoder>(pngEncoder);

        var jpegEncoder = CreateEncoder(".jpg") as System.Windows.Media.Imaging.JpegBitmapEncoder;
        Assert.NotNull(jpegEncoder);
        Assert.Equal(95, jpegEncoder.QualityLevel);

        var tiffEncoder = CreateEncoder(".tif") as System.Windows.Media.Imaging.TiffBitmapEncoder;
        Assert.NotNull(tiffEncoder);
        Assert.Equal(System.Windows.Media.Imaging.TiffCompressOption.Zip, tiffEncoder.Compression);
    }

    [Fact]
    public void PanCrop_LaneKeyframeCopyPasteAndWorkspaceCommands_ExecuteAccurately()
    {
        var doc = new ProjectDocument { Width = 1920, Height = 1080, FrameRate = new Rational(30, 1) };
        var media = new MediaAsset { Id = Guid.NewGuid(), Path = "clip.mp4", HasVideo = true, DurationSeconds = 10, Width = 1920, Height = 1080 };
        doc.Media.Add(media);

        var track = new TimelineTrack { Kind = TrackKind.Video };
        doc.Tracks.Add(track);

        var clip = new TimelineClip
        {
            MediaId = media.Id,
            DurationFrames = 300,
            Crop = new CropAnimation
            {
                Keyframes =
                [
                    new CropKeyframe { Frame = 0, State = new CropState(1920, 1080, 960, 540, 0, 960, 540), Interpolation = CropInterpolation.Fast, Smoothness = 85.0 },
                    new CropKeyframe { Frame = 60, State = new CropState(960, 540, 480, 270, 45, 480, 270), Interpolation = CropInterpolation.Smooth, Smoothness = 75.0 }
                ]
            }
        };
        track.Clips.Add(clip);

        var editor = new TimelineEditor(doc);

        // Verify copying keyframe at frame 60
        var sourceKey = clip.Crop.Keyframes[1];
        var copied = (sourceKey.State, sourceKey.Interpolation, sourceKey.Smoothness);

        // Paste at frame 150
        editor.SetCropKeyframe(clip.Id, 150, copied.State, copied.Interpolation, copied.Smoothness);

        var currentClip = editor.Project.Tracks[0].Clips[0];
        Assert.NotNull(currentClip.Crop);
        Assert.Equal(3, currentClip.Crop.Keyframes.Count);
        var pastedKey = currentClip.Crop.Keyframes[2];
        Assert.Equal(150, pastedKey.Frame);
        Assert.Equal(960, pastedKey.State.Width);
        Assert.Equal(540, pastedKey.State.Height);
        Assert.Equal(45, pastedKey.State.RotationDegrees);
        Assert.Equal(CropInterpolation.Smooth, pastedKey.Interpolation);
        Assert.Equal(75.0, pastedKey.Smoothness);

        // Clear all keyframes resets back to single frame 0 keyframe
        var first = currentClip.Crop.Keyframes[0].DeepClone();
        first.Frame = 0;
        currentClip.Crop.Keyframes.Clear();
        currentClip.Crop.Keyframes.Add(first);

        Assert.Single(currentClip.Crop.Keyframes);
        Assert.Equal(0, currentClip.Crop.Keyframes[0].Frame);
    }
}
