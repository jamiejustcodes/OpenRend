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

    [Fact]
    public void CalculateHomographyMapsQuadPointsAccurately()
    {
        // Source rectangle: (10, 10), (110, 10), (110, 90), (10, 90)
        float[] src = [10f, 10f, 110f, 10f, 110f, 90f, 10f, 90f];
        // Target quad with perspective distortion: (20, 15), (125, 25), (105, 100), (15, 85)
        float[] dst = [20f, 15f, 125f, 25f, 105f, 100f, 15f, 85f];

        var h = new float[9];
        MotionTrackerEngine.CalculateHomography(src, dst, h, 4);

        for (var i = 0; i < 4; i++)
        {
            var p = new Point2D(src[2 * i], src[2 * i + 1]);
            var mapped = MotionTrackerEngine.ApplyHomography(h, p);
            Assert.Equal(dst[2 * i], mapped.X, 2);
            Assert.Equal(dst[2 * i + 1], mapped.Y, 2);
        }
    }

    [Fact]
    public void CalculateTransformationParameterExtractsAccurateAffineParameters()
    {
        const int width = 1920;
        const int height = 1080;

        // Source quad at center: (860, 440), (1060, 440), (1060, 640), (860, 640) -> 200x200, center (960, 540)
        float[] src = [860f, 440f, 1060f, 440f, 1060f, 640f, 860f, 640f];
        float[] srcCenter = [960f, 540f];

        // Target quad shifted by +192px (+0.1) in X, +108px (+0.1) in Y, scaled 2x -> 400x400
        // Target center = (1152, 648)
        // Target TL = (952, 448), TR = (1352, 448), BR = (1352, 848), BL = (952, 848)
        float[] dst = [952f, 448f, 1352f, 448f, 1352f, 848f, 952f, 848f];

        var sixParams = new float[6];
        MotionTrackerEngine.CalculateTransformationParameter(width, height, src, srcCenter, dst, sixParams);

        // sixParams: [dx, dy, angle, scaleX, scaleY, shear]
        Assert.Equal(0.1f, sixParams[0], 2); // dx = +0.1
        Assert.Equal(0.1f, sixParams[1], 2); // dy = +0.1
        Assert.Equal(0.0f, sixParams[2], 1); // angle = 0
        Assert.Equal(2.0f, sixParams[3], 2); // scaleX = 2.0
        Assert.Equal(2.0f, sixParams[4], 2); // scaleY = 2.0
        Assert.Equal(0.0f, sixParams[5], 1); // shear = 0
    }

    [Fact]
    public void ReduceKeyframesSimplifiesCollinearFramesPreservingEndpoints()
    {
        var list = new List<TrackingKeyframe>
        {
            new(0, 0.1, 0.1, 0.2, 0.2),
            new(1, 0.2, 0.2, 0.2, 0.2), // perfectly collinear
            new(2, 0.3, 0.3, 0.2, 0.2), // perfectly collinear
            new(3, 0.4, 0.4, 0.2, 0.2), // perfectly collinear
            new(4, 0.5, 0.5, 0.2, 0.2)
        };

        var reduced = MotionTrackerEngine.ReduceKeyframes(list, epsilon: 0.001);

        // Intermediate linear frames are pruned, retaining start and end
        Assert.Equal(2, reduced.Count);
        Assert.Equal(0, reduced[0].ProjectFrame);
        Assert.Equal(4, reduced[1].ProjectFrame);
    }
}

