using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class MotionTrackerMediaTests
{
    private static byte[] GenerateMovingSquareFrame(int width, int height, int squareX, int squareY, int squareSize = 20)
    {
        var buffer = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var idx = (y * width + x) * 4;
                var inside = x >= squareX && x < squareX + squareSize && y >= squareY && y < squareY + squareSize;
                var val = inside ? (byte)240 : (byte)20;
                buffer[idx] = val;     // B
                buffer[idx + 1] = val; // G
                buffer[idx + 2] = val; // R
                buffer[idx + 3] = 255; // A
            }
        }
        return buffer;
    }

    [Fact]
    public async Task MotionTrackerTracksTranslatingSubjectAccurately()
    {
        const int width = 160;
        const int height = 120;
        var clip = new TimelineClip { StartFrame = 0, DurationFrames = 5 };

        var region = new MotionTrackData
        {
            Name = "Square Tracker",
            Model = TrackingModel.Translation,
            Precision = TrackingPrecision.High
        };

        // Frame 0: square at (40, 40), size 20x20
        // Normalized center = (40 + 10) / 160 = 50 / 160 = 0.3125, (40 + 10) / 120 = 50 / 120 = 0.4167
        // Size = 20/160 = 0.125, 20/120 = 0.1667
        region.Keyframes.Add(new TrackingKeyframe(0, 50.0 / width, 50.0 / height, 24.0 / width, 24.0 / height));

        // Generate 5 frames where square moves by +3 pixels in X and +2 pixels in Y per frame
        byte[]? FrameDecoder(long frame)
        {
            var sx = (int)(40 + frame * 3);
            var sy = (int)(40 + frame * 2);
            return GenerateMovingSquareFrame(width, height, sx, sy, 20);
        }

        var keyframes = await MotionTrackerEngine.TrackAsync(
            clip,
            region,
            startFrame: 0,
            endFrame: 4,
            frameDecoder: FrameDecoder,
            frameWidth: width,
            frameHeight: height
        );

        Assert.Equal(5, keyframes.Count);

        // Verify initial frame
        Assert.Equal(50.0 / width, keyframes[0].CenterX, 3);
        Assert.Equal(50.0 / height, keyframes[0].CenterY, 3);

        // Frame 4: square at (40 + 12, 40 + 8) = (52, 48), center = (62, 58)
        var expectedCenterX = 62.0 / width;
        var expectedCenterY = 58.0 / height;

        Assert.InRange(keyframes[4].CenterX, expectedCenterX - 0.05, expectedCenterX + 0.05);
        Assert.InRange(keyframes[4].CenterY, expectedCenterY - 0.05, expectedCenterY + 0.05);
        Assert.True(keyframes[4].Confidence > 0.3);
    }

    [Fact]
    public async Task MotionTrackerTracksBackwardAccurately()
    {
        const int width = 160;
        const int height = 120;
        var clip = new TimelineClip { StartFrame = 0, DurationFrames = 5 };

        var region = new MotionTrackData
        {
            Name = "Square Tracker Backwards",
            Model = TrackingModel.Translation,
            Precision = TrackingPrecision.High
        };

        // Frame 4: square at (52, 48), center = 62/160, 58/120
        region.Keyframes.Add(new TrackingKeyframe(4, 62.0 / width, 58.0 / height, 24.0 / width, 24.0 / height));

        byte[]? FrameDecoder(long frame)
        {
            var sx = (int)(40 + frame * 3);
            var sy = (int)(40 + frame * 2);
            return GenerateMovingSquareFrame(width, height, sx, sy, 20);
        }

        var keyframes = await MotionTrackerEngine.TrackAsync(
            clip,
            region,
            startFrame: 4,
            endFrame: 0,
            frameDecoder: FrameDecoder,
            frameWidth: width,
            frameHeight: height
        );

        Assert.Equal(5, keyframes.Count);
        // Keyframe at frame 0 should be close to 50/160, 50/120
        var kf0 = keyframes.Single(k => k.ProjectFrame == 0);
        var expectedX = 50.0 / width;
        var expectedY = 50.0 / height;

        Assert.InRange(kf0.CenterX, expectedX - 0.05, expectedX + 0.05);
        Assert.InRange(kf0.CenterY, expectedY - 0.05, expectedY + 0.05);
    }
}
