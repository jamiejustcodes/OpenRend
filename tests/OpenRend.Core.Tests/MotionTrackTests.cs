using System.Text.Json.Nodes;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class MotionTrackTests
{
    [Fact]
    public void Point2DOperationsAndRotationsAreAccurate()
    {
        var p1 = new Point2D(0.2, 0.3);
        var p2 = new Point2D(0.5, 0.7);

        var sum = p1 + p2;
        Assert.Equal(0.7, sum.X, 4);
        Assert.Equal(1.0, sum.Y, 4);

        var diff = p2 - p1;
        Assert.Equal(0.3, diff.X, 4);
        Assert.Equal(0.4, diff.Y, 4);
        Assert.Equal(0.5, diff.DistanceTo(Point2D.Zero), 4); // 3-4-5 triangle

        // Rotate (1, 0) by 90 degrees around origin (0, 0) -> (0, 1)
        var p3 = new Point2D(1.0, 0.0);
        var rotated = p3.Rotate(90.0, Point2D.Zero);
        Assert.Equal(0.0, rotated.X, 4);
        Assert.Equal(1.0, rotated.Y, 4);
    }

    [Fact]
    public void TrackingKeyframeCalculatesCornersFromCenterAndDimensions()
    {
        var kf = new TrackingKeyframe(0, 0.5, 0.5, 0.2, 0.1, angle: 0, scaleX: 1, scaleY: 1);
        Assert.Equal(0.4, kf.CornerTL.X, 4);
        Assert.Equal(0.45, kf.CornerTL.Y, 4);
        Assert.Equal(0.6, kf.CornerTR.X, 4);
        Assert.Equal(0.45, kf.CornerTR.Y, 4);
        Assert.Equal(0.6, kf.CornerBR.X, 4);
        Assert.Equal(0.55, kf.CornerBR.Y, 4);
        Assert.Equal(0.4, kf.CornerBL.X, 4);
        Assert.Equal(0.55, kf.CornerBL.Y, 4);
    }

    [Fact]
    public void MotionTrackDataInterpolatesKeyframesSmoothly()
    {
        var track = new MotionTrackData { Name = "Test Region" };
        track.Keyframes.Add(new TrackingKeyframe(10, 0.2, 0.3, 0.1, 0.1));
        track.Keyframes.Add(new TrackingKeyframe(20, 0.6, 0.7, 0.3, 0.3));
        track.SortKeyframes();

        // Exact match at frame 10
        var kf10 = track.Evaluate(10);
        Assert.NotNull(kf10);
        Assert.Equal(0.2, kf10.CenterX, 4);
        Assert.Equal(0.3, kf10.CenterY, 4);

        // Interpolated at midpoint frame 15 (cosine ease midpoint is 0.5)
        var kf15 = track.Evaluate(15);
        Assert.NotNull(kf15);
        Assert.Equal(0.4, kf15.CenterX, 4);
        Assert.Equal(0.5, kf15.CenterY, 4);
        Assert.Equal(0.2, kf15.Width, 4);
        Assert.Equal(0.2, kf15.Height, 4);

        // Clamping before first and after last
        var kf0 = track.Evaluate(0);
        Assert.NotNull(kf0);
        Assert.Equal(0.2, kf0.CenterX, 4);

        var kf100 = track.Evaluate(100);
        Assert.NotNull(kf100);
        Assert.Equal(0.6, kf100.CenterX, 4);
    }

    [Fact]
    public void MotionTrackDeepClonesAndValidatesIndependently()
    {
        var track = new MotionTrackData
        {
            Name = "Subject 1",
            Shape = TrackingShape.Oval,
            Model = TrackingModel.Affine,
            Precision = TrackingPrecision.Medium,
            IsInverted = true
        };
        track.Keyframes.Add(new TrackingKeyframe(0, 0.5, 0.5, 0.2, 0.2));
        track.Validate();

        var clone = track.DeepClone();
        Assert.NotSame(track, clone);
        Assert.NotSame(track.Keyframes, clone.Keyframes);
        Assert.Equal("Subject 1", clone.Name);
        Assert.True(clone.IsInverted);
        Assert.Equal(TrackingShape.Oval, clone.Shape);
        Assert.Equal(TrackingModel.Affine, clone.Model);
        Assert.Single(clone.Keyframes);

        clone.Keyframes[0].CenterX = 0.8;
        Assert.Equal(0.5, track.Keyframes[0].CenterX);
        Assert.Equal(0.8, clone.Keyframes[0].CenterX);
    }

    [Fact]
    public async Task MotionTracksRoundTripThroughProjectSerialization()
    {
        var asset = new MediaAsset { Path = "tracking_test.mp4", HasVideo = true, DurationSeconds = 5 };
        var clip = new TimelineClip { MediaId = asset.Id, StartFrame = 0, DurationFrames = 90 };

        var region = new MotionTrackData
        {
            Name = "Face Tracker",
            Shape = TrackingShape.Rectangle,
            Model = TrackingModel.Homography,
            Precision = TrackingPrecision.High,
            IsInverted = false
        };
        region.Keyframes.Add(new TrackingKeyframe(0, 0.3, 0.4, 0.15, 0.2, angle: 5, scaleX: 1.1, scaleY: 1.1));
        region.Keyframes.Add(new TrackingKeyframe(30, 0.5, 0.6, 0.2, 0.25, angle: -10, scaleX: 1.2, scaleY: 1.2));
        clip.MotionTracks.Add(region);

        var project = new ProjectDocument
        {
            Media = [asset],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }]
        };

        var path = Path.Combine(Path.GetTempPath(), "OpenRend-motiontrack-test-" + Guid.NewGuid().ToString("N") + ".openrend");
        try
        {
            await ProjectStore.SaveAsync(project, path);
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            Assert.Equal(ProjectStore.CurrentVersion, (int)manifest["Version"]!);

            var loaded = await ProjectStore.LoadAsync(path);
            var loadedClip = loaded.Tracks[0].Clips[0];
            Assert.Single(loadedClip.MotionTracks);

            var loadedRegion = loadedClip.MotionTracks[0];
            Assert.Equal("Face Tracker", loadedRegion.Name);
            Assert.Equal(TrackingShape.Rectangle, loadedRegion.Shape);
            Assert.Equal(TrackingModel.Homography, loadedRegion.Model);
            Assert.Equal(2, loadedRegion.Keyframes.Count);
            Assert.Equal(0.3, loadedRegion.Keyframes[0].CenterX, 3);
            Assert.Equal(0.4, loadedRegion.Keyframes[0].CenterY, 3);
            Assert.Equal(5.0, loadedRegion.Keyframes[0].Angle, 3);
            Assert.Equal(0.5, loadedRegion.Keyframes[1].CenterX, 3);
            Assert.Equal(-10.0, loadedRegion.Keyframes[1].Angle, 3);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
