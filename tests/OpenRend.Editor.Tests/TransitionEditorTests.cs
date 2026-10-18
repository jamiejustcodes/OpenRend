using System.Windows;
using OpenRend.Core;
using OpenRend.Project;
using OpenRend.Timeline;
using OpenRend.UI;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class TransitionEditorTests
{
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }

    [Fact]
    public void TimelineViewTransitionAtAcceptsNonDefaultTransitionsOnOverlapsAndCuts() => Sta(() =>
    {
        var project = new ProjectDocument();
        var track = new TimelineTrack { Kind = TrackKind.Video };
        var outgoing = new TimelineClip { StartFrame = 0, DurationFrames = 90 };
        var incoming = new TimelineClip { StartFrame = 75, DurationFrames = 60 };
        track.Clips.Add(outgoing);
        track.Clips.Add(incoming);
        project.Tracks.Add(track);

        var view = new TimelineView { Project = project };
        var linearWipe = new VideoTransition
        {
            Kind = VideoTransitionKind.LinearWipe,
            Preset = "Left-Right, Soft Edge",
            Parameters = new Dictionary<string, string> { ["Angle"] = "0.0", ["Feather"] = "0.5" }
        };

        var overlapPoint = new Point(TimelineView.HeaderWidth + 80.0 / 30 * view.PixelsPerSecond, 80);
        var target = view.TransitionAt(overlapPoint, linearWipe);
        Assert.NotNull(target);
        Assert.Equal(outgoing.Id, target.OutgoingClipId);
        Assert.Equal(incoming.Id, target.IncomingClipId);
        Assert.Equal(VideoTransitionKind.LinearWipe, target.Transition.Kind);
        Assert.Equal("Left-Right, Soft Edge", target.Transition.Preset);
        Assert.Equal("0.5", target.Transition.Parameters!["Feather"]);

        // Cut detection with non-default transition
        track.Clips.Clear();
        var cutOutgoing = new TimelineClip { StartFrame = 0, DurationFrames = 60 };
        var cutIncoming = new TimelineClip { StartFrame = 60, DurationFrames = 60 };
        track.Clips.Add(cutOutgoing);
        track.Clips.Add(cutIncoming);

        var push = new VideoTransition
        {
            Kind = VideoTransitionKind.Push,
            Preset = "Push In, Left",
            Parameters = new Dictionary<string, string> { ["Direction"] = "2" }
        };

        var cutX = TimelineView.HeaderWidth + view.Project.FrameRate.Seconds(60) * view.PixelsPerSecond;
        var cutTarget = view.TransitionAt(new Point(cutX + 2, 80), push);
        Assert.NotNull(cutTarget);
        Assert.True(cutTarget.IsCut);
        Assert.Equal(VideoTransitionKind.Push, cutTarget.Transition.Kind);
        Assert.Equal("Push In, Left", cutTarget.Transition.Preset);
    });

    [Fact]
    public void TimelineEditorApplyDissolveAppliesSpecifiedTransitionAndSupportsUndoRedo()
    {
        var media = new MediaAsset { Path = "test.mp4", DurationSeconds = 10, Width = 1920, Height = 1080, HasVideo = true };
        var project = new ProjectDocument { Media = [media] };
        var track = new TimelineTrack { Kind = TrackKind.Video };
        var outgoing = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 90 };
        var incoming = new TimelineClip { MediaId = media.Id, StartFrame = 75, DurationFrames = 60 };
        track.Clips.Add(outgoing);
        track.Clips.Add(incoming);
        project.Tracks.Add(track);

        var editor = new TimelineEditor(project);
        var barnDoor = new VideoTransition
        {
            Kind = VideoTransitionKind.BarnDoor,
            Preset = "Vertical, In, White Border",
            Parameters = new Dictionary<string, string>
            {
                ["Orientation"] = "1",
                ["Direction"] = "0",
                ["BorderSize"] = "0.05",
                ["BorderColor"] = "1 1 1"
            }
        };

        editor.ApplyDissolve(outgoing.Id, incoming.Id, barnDoor);
        Assert.Equal(15, outgoing.FadeOutFrames);
        Assert.Equal(15, incoming.FadeInFrames);
        Assert.NotNull(incoming.FadeInTransition);
        Assert.Equal(VideoTransitionKind.BarnDoor, incoming.FadeInTransition.Kind);
        Assert.Equal("Vertical, In, White Border", incoming.FadeInTransition.Preset);
        Assert.Equal("0.05", incoming.FadeInTransition.Parameters!["BorderSize"]);

        Assert.True(editor.Undo());
        Assert.Null(project.Tracks[0].Clips[1].FadeInTransition);
        Assert.Equal(0, project.Tracks[0].Clips[1].FadeInFrames);

        Assert.True(editor.Redo());
        Assert.NotNull(project.Tracks[0].Clips[1].FadeInTransition);
        Assert.Equal(VideoTransitionKind.BarnDoor, project.Tracks[0].Clips[1].FadeInTransition!.Kind);
    }

    [Fact]
    public void SetVideoTransitionUpdatesTransitionParametersAtomically()
    {
        var media = new MediaAsset { Path = "test.mp4", DurationSeconds = 10, Width = 1920, Height = 1080, HasVideo = true };
        var project = new ProjectDocument { Media = [media] };
        var track = new TimelineTrack { Kind = TrackKind.Video };
        var outgoing = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 90, FadeOutFrames = 15 };
        var incoming = new TimelineClip
        {
            MediaId = media.Id,
            StartFrame = 75,
            DurationFrames = 60,
            FadeInFrames = 15,
            FadeInTransition = new VideoTransition
            {
                Kind = VideoTransitionKind.Iris,
                Preset = "Circle, Out, Center"
            }
        };
        track.Clips.Add(outgoing);
        track.Clips.Add(incoming);
        project.Tracks.Add(track);

        var editor = new TimelineEditor(project);
        var updated = new VideoTransition
        {
            Kind = VideoTransitionKind.Iris,
            Preset = "Square, In, Center",
            Parameters = new Dictionary<string, string>
            {
                ["Shape"] = "1",
                ["Direction"] = "0",
                ["Border"] = "0.1"
            }
        };

        editor.SetVideoTransition(incoming.Id, updated);
        var current = project.Tracks[0].Clips[1].FadeInTransition;
        Assert.NotNull(current);
        Assert.Equal("Square, In, Center", current.Preset);
        Assert.Equal("0.1", current.Parameters!["Border"]);

        Assert.True(editor.Undo());
        Assert.Equal("Circle, Out, Center", project.Tracks[0].Clips[1].FadeInTransition!.Preset);

        Assert.True(editor.Redo());
        Assert.Equal("Square, In, Center", project.Tracks[0].Clips[1].FadeInTransition!.Preset);
    }
}
