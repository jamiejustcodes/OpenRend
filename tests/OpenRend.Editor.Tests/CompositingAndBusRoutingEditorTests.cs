using System.IO;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class CompositingAndBusRoutingEditorTests
{
    private static (EditorViewModel ViewModel, TimelineEditor Editor, ProjectDocument Project) CreateEnvironment()
    {
        var media = new MediaService();
        var renderer = new TimelineRenderService(media);
        var exporter = new ExportService(renderer);
        var vm = new EditorViewModel(media, renderer, exporter);
        vm.Project.Tracks.Clear();
        return (vm, vm.Editor, vm.Project);
    }

    [Fact]
    public void VideoTrack_MakeCompositingChild_EnforcesHierarchyAndUndoRedo()
    {
        var (vm, editor, project) = CreateEnvironment();

        var track1 = editor.AddTrack(TrackKind.Video);
        var track2 = editor.AddTrack(TrackKind.Video);
        var track3 = editor.AddTrack(TrackKind.Video);

        // Track 1 is the first video track; it cannot become a child
        editor.MakeCompositingChild(track1.Id);
        Assert.Equal(0, track1.CompositeNestingLevel);
        Assert.False(track1.IsCompositingChild);

        // Track 2 can become a child of Track 1 (level 1)
        editor.MakeCompositingChild(track2.Id);
        Assert.Equal(1, track2.CompositeNestingLevel);
        Assert.True(track2.IsCompositingChild);
        Assert.True(editor.IsCompositingParent(track1.Id));

        // Track 2 cannot exceed prev + 1 (max level is 0 + 1 = 1)
        editor.MakeCompositingChild(track2.Id);
        Assert.Equal(1, track2.CompositeNestingLevel);

        // Track 3 can become a child of Track 2 (level 2)
        editor.MakeCompositingChild(track3.Id); // level 1
        editor.MakeCompositingChild(track3.Id); // level 2
        Assert.Equal(2, track3.CompositeNestingLevel);

        // Outdenting Track 2 cascades Track 3 down to preserve continuity
        editor.MakeCompositingParent(track2.Id); // Track 2 becomes 0
        Assert.Equal(0, track2.CompositeNestingLevel);
        Assert.Equal(1, track3.CompositeNestingLevel); // Clamped from 2 to 1 (0 + 1)

        // Undo restores previous hierarchy
        Assert.True(editor.Undo());
        Assert.Equal(1, project.Tracks[1].CompositeNestingLevel);
        Assert.Equal(2, project.Tracks[2].CompositeNestingLevel);

        // Redo restores outdented state
        Assert.True(editor.Redo());
        Assert.Equal(0, project.Tracks[1].CompositeNestingLevel);
        Assert.Equal(1, project.Tracks[2].CompositeNestingLevel);
    }

    [Fact]
    public void AdjustmentTrack_ToggleAndPersistWithUndoRedo()
    {
        var (vm, editor, project) = CreateEnvironment();

        var videoTrack = editor.AddTrack(TrackKind.Video);
        Assert.False(videoTrack.IsAdjustmentTrack);

        vm.ToggleTrackAdjustment(videoTrack.Id);
        Assert.True(videoTrack.IsAdjustmentTrack);

        var clone = videoTrack.DeepClone();
        Assert.True(clone.IsAdjustmentTrack);

        // Audio tracks cannot be adjustment tracks
        var audioTrack = editor.AddTrack(TrackKind.Audio);
        vm.ToggleTrackAdjustment(audioTrack.Id);
        Assert.False(audioTrack.IsAdjustmentTrack);

        // Undo adding the audio track
        Assert.True(editor.Undo());
        Assert.Single(project.Tracks);
        Assert.True(project.Tracks[0].IsAdjustmentTrack);

        // Undo toggling the adjustment track
        Assert.True(editor.Undo());
        Assert.False(project.Tracks[0].IsAdjustmentTrack);

        // Redo toggle
        Assert.True(editor.Redo());
        Assert.True(project.Tracks[0].IsAdjustmentTrack);
    }

    [Fact]
    public void AudioBuses_CreateDeleteAndRoute_TracksMembershipWithUndoRedo()
    {
        var (vm, editor, project) = CreateEnvironment();

        var audioTrack1 = editor.AddTrack(TrackKind.Audio);
        var audioTrack2 = editor.AddTrack(TrackKind.Audio);

        // Initially routed to Master (null)
        Assert.Null(audioTrack1.BusTrackId);

        // Create Sub-Buses A and B
        var busA = vm.CreateAudioBus();
        Assert.Equal("Bus A", busA.Name);
        var busB = vm.CreateAudioBus();
        Assert.Equal("Bus B", busB.Name);
        Assert.Equal(2, project.AudioBuses.Count);

        // Route tracks to buses
        vm.SetTrackAudioBus(audioTrack1.Id, busA.Id);
        vm.SetTrackAudioBus(audioTrack2.Id, busB.Id);
        Assert.Equal(busA.Id, audioTrack1.BusTrackId);
        Assert.Equal(busB.Id, audioTrack2.BusTrackId);

        // Adjust bus volume and pan
        editor.SetAudioBusVolume(busA.Id, 0.8);
        editor.SetAudioBusPan(busA.Id, -0.5);
        Assert.Equal(0.8, busA.Gain);
        Assert.Equal(-0.5, busA.Pan);

        // Deleting bus A re-routes track 1 back to Master (null)
        vm.DeleteAudioBus(busA.Id);
        Assert.Single(project.AudioBuses);
        Assert.Null(project.Tracks[0].BusTrackId);
        Assert.Equal(busB.Id, project.Tracks[1].BusTrackId);

        // Undo restores Bus A and track routing
        Assert.True(editor.Undo());
        Assert.Equal(2, project.AudioBuses.Count);
        Assert.Equal(busA.Id, project.Tracks[0].BusTrackId);

        // Redo restores deletion
        Assert.True(editor.Redo());
        Assert.Single(project.AudioBuses);
        Assert.Null(project.Tracks[0].BusTrackId);
    }

    [Fact]
    public void ProjectDocument_Validation_EnforcesCompositingAndBusInvariants()
    {
        var project = new ProjectDocument();
        var video1 = new TimelineTrack { Kind = TrackKind.Video, CompositeNestingLevel = 0 };
        var video2 = new TimelineTrack { Kind = TrackKind.Video, CompositeNestingLevel = 1 };
        var audio1 = new TimelineTrack { Kind = TrackKind.Audio };
        project.Tracks.Add(video1);
        project.Tracks.Add(video2);
        project.Tracks.Add(audio1);

        // Valid configuration passes
        project.Validate();

        // First video track cannot be a child
        video1.CompositeNestingLevel = 1;
        Assert.Throws<InvalidDataException>(() => project.Validate());
        video1.CompositeNestingLevel = 0;

        // Nesting level cannot skip ahead (0 -> 2)
        video2.CompositeNestingLevel = 2;
        Assert.Throws<InvalidDataException>(() => project.Validate());
        video2.CompositeNestingLevel = 1;

        // Audio track cannot be compositing child
        audio1.CompositeNestingLevel = 1;
        Assert.Throws<InvalidDataException>(() => project.Validate());
        audio1.CompositeNestingLevel = 0;

        // Audio track cannot be adjustment track
        audio1.IsAdjustmentTrack = true;
        Assert.Throws<InvalidDataException>(() => project.Validate());
        audio1.IsAdjustmentTrack = false;

        // Invalid bus routing reference throws
        audio1.BusTrackId = Guid.NewGuid();
        Assert.Throws<InvalidDataException>(() => project.Validate());

        var bus = new AudioBusTrack { Id = audio1.BusTrackId.Value, Name = "Bus A" };
        project.AudioBuses.Add(bus);
        project.Validate(); // now valid with bus added
    }

    [Fact]
    public void TimelineEvaluator_ParentMutingAndSoloing_CascadesToChildren()
    {
        var project = new ProjectDocument();
        var parent = new TimelineTrack { Kind = TrackKind.Video, Name = "Parent", CompositeNestingLevel = 0 };
        var child = new TimelineTrack { Kind = TrackKind.Video, Name = "Child", CompositeNestingLevel = 1 };
        project.Tracks.Add(parent);
        project.Tracks.Add(child);

        var media = new MediaAsset { Path = "test.mp4", DurationSeconds = 10, HasVideo = true };
        project.Media.Add(media);

        var clipParent = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 100 };
        var clipChild = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 100 };
        parent.Clips.Add(clipParent);
        child.Clips.Add(clipChild);

        // When unmuted, both are evaluated
        var frame0 = TimelineEvaluator.Evaluate(project, 10);
        Assert.Equal(2, frame0.Video.Count);

        // When parent is muted, child is also muted
        parent.IsMuted = true;
        var frameMuted = TimelineEvaluator.Evaluate(project, 10);
        Assert.Empty(frameMuted.Video);
        parent.IsMuted = false;

        // When child is soloed, parent is included
        child.IsSolo = true;
        var frameSolo = TimelineEvaluator.Evaluate(project, 10);
        Assert.Equal(2, frameSolo.Video.Count);
    }
}
