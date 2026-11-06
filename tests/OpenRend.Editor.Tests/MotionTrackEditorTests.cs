using OpenRend.Core;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class MotionTrackEditorTests
{
    private static (TimelineEditor Editor, TimelineClip Clip) Create()
    {
        var asset = new MediaAsset { Path = "test.mp4", HasVideo = true, DurationSeconds = 10, Width = 1920, Height = 1080 };
        var clip = new TimelineClip { MediaId = asset.Id, StartFrame = 0, DurationFrames = 100 };
        var track = new TimelineTrack { Kind = TrackKind.Video, Clips = [clip] };
        var project = new ProjectDocument { Media = [asset], Tracks = [track], Width = 1920, Height = 1080 };
        return (new TimelineEditor(project), clip);
    }

    [Fact]
    public void AddRemoveAndUpdateMotionTracksSurvivesUndoRedo()
    {
        var (editor, clip) = Create();
        var region = new MotionTrackData
        {
            Name = "Region A",
            Model = TrackingModel.Similarity,
            Shape = TrackingShape.Rectangle
        };
        region.Keyframes.Add(new TrackingKeyframe(0, 0.4, 0.4, 0.2, 0.2));

        editor.AddMotionTrack(clip.Id, region);
        var tracks = editor.GetMotionTracks(clip.Id);
        Assert.Single(tracks);
        Assert.Equal("Region A", tracks[0].Name);

        // Update region
        tracks[0].Name = "Renamed Region";
        tracks[0].IsInverted = true;
        editor.UpdateMotionTrack(clip.Id, tracks[0]);
        Assert.Equal("Renamed Region", editor.GetMotionTracks(clip.Id)[0].Name);
        Assert.True(editor.GetMotionTracks(clip.Id)[0].IsInverted);

        // Undo update
        editor.Undo();
        Assert.Equal("Region A", editor.GetMotionTracks(clip.Id)[0].Name);
        Assert.False(editor.GetMotionTracks(clip.Id)[0].IsInverted);

        // Redo update
        editor.Redo();
        Assert.Equal("Renamed Region", editor.GetMotionTracks(clip.Id)[0].Name);

        // Remove region
        editor.RemoveMotionTrack(clip.Id, region.Id);
        Assert.Empty(editor.GetMotionTracks(clip.Id));

        // Undo remove
        editor.Undo();
        Assert.Single(editor.GetMotionTracks(clip.Id));
    }

    private static TimelineClip GetClip(TimelineEditor editor, Guid clipId) =>
        editor.Project.Tracks.SelectMany(t => t.Clips).First(c => c.Id == clipId);

    [Fact]
    public void TransferMotionToPictureInPictureCreatesAndUpdatesKeyframedEffect()
    {
        var (editor, clip) = Create();
        var region = new MotionTrackData { Name = "Tracker" };
        region.Keyframes.Add(new TrackingKeyframe(0, 0.25, 0.35, 0.2, 0.2, angle: 15, scaleX: 1.5, scaleY: 1.5));
        editor.AddMotionTrack(clip.Id, region);

        editor.TransferMotionToPictureInPicture(clip.Id, region.Id, clip.Id);

        var currentClip = GetClip(editor, clip.Id);
        var pip = currentClip.VideoEffects.FirstOrDefault(e => e.Kind == VideoEffectKind.PictureInPicture);
        Assert.NotNull(pip);
        Assert.Equal(0.25, pip.GetParameter("LocationX"), 3);
        Assert.Equal(0.35, pip.GetParameter("LocationY"), 3);
        Assert.Equal(1.5, pip.GetParameter("Scale"), 3);
        Assert.Equal(15.0, pip.GetParameter("Angle"), 3);

        // Undo removes PIP
        editor.Undo();
        currentClip = GetClip(editor, clip.Id);
        Assert.DoesNotContain(currentClip.VideoEffects, e => e.Kind == VideoEffectKind.PictureInPicture);

        // Redo restores PIP
        editor.Redo();
        currentClip = GetClip(editor, clip.Id);
        Assert.Contains(currentClip.VideoEffects, e => e.Kind == VideoEffectKind.PictureInPicture);
    }

    [Fact]
    public void TransferMotionToCropAppliesFollowAndStabilizeKeyframes()
    {
        var (editor, clip) = Create();
        var region = new MotionTrackData { Name = "Tracker" };
        region.Keyframes.Add(new TrackingKeyframe(0, 0.5, 0.5, 0.2, 0.2));
        region.Keyframes.Add(new TrackingKeyframe(10, 0.55, 0.52, 0.2, 0.2));
        editor.AddMotionTrack(clip.Id, region);

        // Stabilize = true
        editor.TransferMotionToCrop(clip.Id, region.Id, clip.Id, stabilize: true);
        var currentClip = GetClip(editor, clip.Id);
        Assert.NotNull(currentClip.Crop);
        Assert.Equal(2, currentClip.Crop.Keyframes.Count);
        Assert.Equal(0, currentClip.Crop.Keyframes[0].Frame);
        Assert.Equal(10, currentClip.Crop.Keyframes[1].Frame);

        // Undo restores no crop
        editor.Undo();
        currentClip = GetClip(editor, clip.Id);
        Assert.Null(currentClip.Crop);
    }

    [Fact]
    public void TransferMotionToMask_GeneratesAnimatedBezierMaskKeyframes_AndSurvivesUndoRedo()
    {
        var (editor, clip) = Create();
        var region = new MotionTrackData
        {
            Name = "Face Tracker",
            Shape = TrackingShape.Oval,
            IsInverted = false
        };
        region.Keyframes.Add(new TrackingKeyframe(0, 0.40, 0.40, 0.20, 0.20));
        region.Keyframes.Add(new TrackingKeyframe(20, 0.45, 0.42, 0.22, 0.22, angle: 10));
        region.Keyframes.Add(new TrackingKeyframe(40, 0.50, 0.45, 0.25, 0.25, angle: 20));
        editor.AddMotionTrack(clip.Id, region);

        editor.TransferMotionToMask(clip.Id, region.Id, clip.Id);

        var currentClip = GetClip(editor, clip.Id);
        Assert.NotNull(currentClip.Mask);
        Assert.True(currentClip.Mask.IsEnabled);
        Assert.Equal(3, currentClip.Mask.Keyframes.Count);

        // Check relative frame positioning
        Assert.Equal(0, currentClip.Mask.Keyframes[0].Frame);
        Assert.Equal(20, currentClip.Mask.Keyframes[1].Frame);
        Assert.Equal(40, currentClip.Mask.Keyframes[2].Frame);

        // Verify mask paths exist and are shaped correctly
        var kf0Shape = currentClip.Mask.Keyframes[0].Shape;
        Assert.Single(kf0Shape.Paths);
        Assert.True(kf0Shape.Paths[0].IsClosed);
        Assert.Equal(MaskMode.Positive, kf0Shape.Paths[0].Mode);
        Assert.True(kf0Shape.Paths[0].Points.Count >= 4);

        // Verify Undo removes mask
        editor.Undo();
        currentClip = GetClip(editor, clip.Id);
        Assert.Null(currentClip.Mask);

        // Verify Redo restores mask
        editor.Redo();
        currentClip = GetClip(editor, clip.Id);
        Assert.NotNull(currentClip.Mask);
        Assert.Equal(3, currentClip.Mask.Keyframes.Count);
    }

    [Fact]
    public void GetMotionTrackParametricKeyframes_ReturnsOrderedTrajectoryParameters()
    {
        var (editor, clip) = Create();
        var region = new MotionTrackData { Name = "Tracker" };
        region.Keyframes.Add(new TrackingKeyframe(15, 0.60, 0.40, 0.2, 0.2, angle: 5, scaleX: 1.1, scaleY: 1.1));
        region.Keyframes.Add(new TrackingKeyframe(5, 0.50, 0.30, 0.2, 0.2));
        editor.AddMotionTrack(clip.Id, region);

        var keyframes = editor.GetMotionTrackParametricKeyframes(clip.Id, region.Id);
        Assert.Equal(2, keyframes.Count);
        // Verified ordered by frame ascending
        Assert.Equal(5, keyframes[0].Frame);
        Assert.Equal(0.50, keyframes[0].CenterX, 2);
        Assert.Equal(15, keyframes[1].Frame);
        Assert.Equal(0.60, keyframes[1].CenterX, 2);
        Assert.Equal(5.0, keyframes[1].Angle, 2);
        Assert.Equal(1.1, keyframes[1].ScaleX, 2);
    }
}
