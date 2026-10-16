using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class ClipboardRegressionTests
{
    private sealed class Session : IDisposable
    {
        private readonly MediaService media = new();
        private readonly TimelineRenderService renderer;
        public EditorViewModel ViewModel { get; }
        public Guid VideoId { get; }
        public Guid AudioId { get; }
        public Guid TailId { get; }
        public Session()
        {
            renderer = new TimelineRenderService(media);
            ViewModel = new EditorViewModel(media, renderer, new ExportService(renderer));
            var source = new MediaAsset { Path = "clipboard-source.mp4", DurationSeconds = 30, HasVideo = true, HasAudio = true };
            var tail = new MediaAsset { Path = "clipboard-tail.mp4", DurationSeconds = 30, HasVideo = true };
            var group = Guid.NewGuid();
            var video = new TimelineClip { MediaId = source.Id, Name = "Linked picture", StartFrame = 30, SourceInFrame = 60, DurationFrames = 60, GroupId = group, Gain = .7, Opacity = .6, Pan = -.25, FadeInFrames = 12, FadeOutFrames = 10 };
            var audio = new TimelineClip { MediaId = source.Id, Name = "Linked sound", StartFrame = 45, SourceInFrame = 75, DurationFrames = 45, GroupId = group, Gain = .8, Pan = .5, FadeInFrames = 10, FadeOutFrames = 10, FadeOffsetFrames = 15, FadeDurationFrames = 90 };
            var end = new TimelineClip { MediaId = tail.Id, Name = "Tail", StartFrame = 400, DurationFrames = 30 };
            VideoId = video.Id; AudioId = audio.Id; TailId = end.Id;
            ViewModel.Replace(new ProjectDocument { Media = [source, tail], Tracks = [
                new TimelineTrack { Name = "Picture", Kind = TrackKind.Video, Clips = [video] },
                new TimelineTrack { Name = "Second picture", Kind = TrackKind.Video, Clips = [end] },
                new TimelineTrack { Name = "Sound", Kind = TrackKind.Audio, Clips = [audio] }
            ] }, "source.openrend");
            ViewModel.Selection.Add(VideoId);
        }
        public void Dispose() { renderer.Dispose(); media.Dispose(); }
    }

    [Fact]
    public void CopyIsReadOnlyAndPastePreservesLinkedOffsetsLevelsAndEnvelope()
    {
        using var session = new Session();
        var vm = session.ViewModel;
        var oldGroup = vm.Project.Tracks[0].Clips[0].GroupId;
        vm.Copy();
        Assert.True(vm.CanPaste);
        Assert.False(vm.Dirty);
        Assert.False(vm.Editor.CanUndo);
        vm.Position = 180;
        vm.Paste();
        var clips = Selected(vm);
        Assert.Equal(2, clips.Length);
        Assert.Equal(new long[] { 180, 195 }, clips.Select(c => c.StartFrame));
        Assert.Equal(new long[] { 60, 75 }, clips.Select(c => c.SourceInFrame));
        Assert.Equal(new long[] { 60, 45 }, clips.Select(c => c.DurationFrames));
        Assert.Equal(.6, clips[0].Opacity); Assert.Equal(.7, clips[0].Gain); Assert.Equal(-.25, clips[0].Pan);
        Assert.Equal(.8, clips[1].Gain); Assert.Equal(.5, clips[1].Pan);
        Assert.Equal(15, clips[1].FadeOffsetFrames); Assert.Equal(90, clips[1].FadeDurationFrames);
        Assert.Equal(clips[0].GroupId, clips[1].GroupId);
        Assert.NotNull(clips[0].GroupId); Assert.NotEqual(oldGroup, clips[0].GroupId);
        Assert.Equal(240, vm.Position);
        Assert.Equal(2, vm.Project.Media.Count);
        Assert.Equal(3, vm.Project.Tracks.Count);
        Assert.True(vm.Editor.Undo());
        Assert.Equal(3, vm.Project.Tracks.SelectMany(t => t.Clips).Count());
        Assert.False(vm.Editor.CanUndo);
        Assert.True(vm.Editor.Redo());
        Assert.Equal(5, vm.Project.Tracks.SelectMany(t => t.Clips).Count());
    }

    [Fact]
    public void RepeatedPasteMakesIndependentClipAndGroupIdentities()
    {
        using var session = new Session();
        var vm = session.ViewModel;
        vm.Copy(); vm.Position = 180; vm.Paste();
        var first = Selected(vm);
        vm.Paste();
        var second = Selected(vm);
        Assert.Equal(new long[] { 240, 255 }, second.Select(c => c.StartFrame));
        Assert.Empty(first.Select(c => c.Id).Intersect(second.Select(c => c.Id)));
        Assert.NotEqual(first[0].GroupId, second[0].GroupId);
        Assert.Equal(second[0].GroupId, second[1].GroupId);
    }

    [Fact]
    public void CopyCapturesIndependentClipValuesAndIgnoreGroupsCopiesOnlySelection()
    {
        using var session = new Session();
        var vm = session.ViewModel;
        vm.IgnoreGroups = true;
        vm.Copy();
        vm.Editor.Execute(p => p.Tracks[0].Clips[0].Gain = .1);
        vm.Position = 180; vm.Paste();
        var pasted = Assert.Single(Selected(vm));
        Assert.Equal(.7, pasted.Gain);
        Assert.Equal(4, vm.Project.Tracks.SelectMany(t => t.Clips).Count());
        Assert.Single(vm.Project.Tracks[2].Clips);
    }

    [Fact]
    public void CutRemovesTheWholeGroupInOneUndoAndRetainsClipboardAfterUndo()
    {
        using var session = new Session();
        var vm = session.ViewModel;
        vm.Cut();
        Assert.True(vm.CanPaste);
        Assert.Empty(vm.Selection);
        Assert.Equal(session.TailId, Assert.Single(vm.Project.Tracks.SelectMany(t => t.Clips)).Id);
        Assert.True(vm.Editor.Undo());
        Assert.False(vm.Editor.CanUndo);
        Assert.Equal(3, vm.Project.Tracks.SelectMany(t => t.Clips).Count());
        vm.Position = 180; vm.Paste();
        Assert.Equal(2, Selected(vm).Length);
    }

    [Fact]
    public void PasteAcrossProjectsImportsOnlyRequiredAssetsAndReusesMatchingPaths()
    {
        using var session = new Session();
        var vm = session.ViewModel;
        var source = vm.Project.Media[0].DeepClone();
        vm.Copy(); vm.NewProject(); vm.Paste();
        Assert.Single(vm.Project.Media);
        Assert.Equal(source.Path, vm.Project.Media[0].Path);
        Assert.Single(vm.Library);
        Assert.Equal(2, vm.Project.Tracks.SelectMany(t => t.Clips).Count());
        Assert.Equal(new long[] { 0, 15 }, Selected(vm).Select(c => c.StartFrame));
        var existing = source.DeepClone(); existing.Id = Guid.NewGuid(); existing.Path = existing.Path.ToUpperInvariant();
        vm.Replace(new ProjectDocument { Media = [existing], Tracks = [new TimelineTrack { Kind = TrackKind.Video }, new TimelineTrack { Kind = TrackKind.Audio }] }, "destination.openrend");
        vm.Paste();
        Assert.Equal(existing.Id, Assert.Single(vm.Project.Media).Id);
        Assert.All(Selected(vm), c => Assert.Equal(existing.Id, c.MediaId));
        Assert.True(vm.Editor.Undo());
        Assert.Single(vm.Project.Media);
        Assert.Empty(vm.Project.Tracks.SelectMany(t => t.Clips));
    }

    [Fact]
    public void PasteRemapsAConflictingMediaIdWithoutReplacingTheDestinationAsset()
    {
        using var session = new Session();
        var vm = session.ViewModel;
        var collision = vm.Project.Media[0].DeepClone(); collision.Path = "different-destination.mp4";
        vm.Copy();
        vm.Replace(new ProjectDocument { Media = [collision], Tracks = [new TimelineTrack { Kind = TrackKind.Video }, new TimelineTrack { Kind = TrackKind.Audio }] }, "destination.openrend");
        vm.Paste();
        Assert.Equal(2, vm.Project.Media.Count);
        Assert.Equal("different-destination.mp4", vm.Project.Media.Single(m => m.Id == collision.Id).Path);
        Assert.All(Selected(vm), c => Assert.NotEqual(collision.Id, c.MediaId));
        Assert.Equal("clipboard-source.mp4", vm.Project.Media.Single(m => m.Id == Selected(vm)[0].MediaId).Path);
        Assert.True(vm.Editor.Undo());
        Assert.Equal(collision.Id, Assert.Single(vm.Project.Media).Id);
    }

    [Fact]
    public void FailedPasteIntoLockedTrackRollsBackImportedAssetsAndPreservesSelection()
    {
        using var session = new Session();
        var vm = session.ViewModel;
        var trackId = vm.Project.Tracks[0].Id;
        var tail = vm.Project.Media[1].DeepClone();
        var destinationClip = new TimelineClip { MediaId = tail.Id, DurationFrames = 300 };
        vm.Copy();
        vm.Replace(new ProjectDocument { Media = [tail], Tracks = [new TimelineTrack { Id = trackId, IsLocked = true, Kind = TrackKind.Video, Clips = [destinationClip] }] }, "destination.openrend");
        vm.Selection.Add(destinationClip.Id); vm.Position = 120;
        Assert.Throws<InvalidOperationException>(vm.Paste);
        Assert.Equal(tail.Id, Assert.Single(vm.Project.Media).Id);
        Assert.Equal(destinationClip.Id, Assert.Single(vm.Project.Tracks[0].Clips).Id);
        Assert.Equal(destinationClip.Id, Assert.Single(vm.Selection));
        Assert.Equal(120, vm.Position);
        Assert.False(vm.Editor.CanUndo);
        Assert.False(vm.Dirty);
        Assert.True(vm.CanPaste);
    }

    [Fact]
    public void FailedCutOnLockedGroupPreservesThePreviousClipboard()
    {
        using var session = new Session();
        var vm = session.ViewModel;
        vm.Selection.Clear(); vm.Selection.Add(session.TailId); vm.Copy();
        vm.Editor.SetTrackState(vm.Project.Tracks[0].Id, isLocked: true);
        vm.Selection.Clear(); vm.Selection.Add(session.VideoId);
        Assert.Throws<InvalidOperationException>(vm.Cut);
        Assert.Equal(3, vm.Project.Tracks.SelectMany(t => t.Clips).Count());
        Assert.Equal(session.VideoId, Assert.Single(vm.Selection));
        vm.NewProject(); vm.Paste();
        Assert.Equal("Tail", Assert.Single(Selected(vm)).Name);
        Assert.Equal("clipboard-tail.mp4", Assert.Single(vm.Project.Media).Path);
    }

    [Fact]
    public void PasteAcrossFrameRatesPreservesSourceTimesOffsetsAndFadeEnvelope()
    {
        using var session = new Session();
        var vm = session.ViewModel;
        vm.Copy();
        vm.Replace(new ProjectDocument { FrameRate = new Rational(60, 1), Tracks = [new TimelineTrack { Kind = TrackKind.Video }, new TimelineTrack { Kind = TrackKind.Audio }] }, "60fps.openrend");
        vm.Paste();
        var clips = Selected(vm);
        Assert.Equal(new long[] { 0, 30 }, clips.Select(c => c.StartFrame));
        Assert.Equal(new long[] { 120, 150 }, clips.Select(c => c.SourceInFrame));
        Assert.Equal(new long[] { 120, 90 }, clips.Select(c => c.DurationFrames));
        Assert.Equal(24, clips[0].FadeInFrames); Assert.Equal(20, clips[0].FadeOutFrames);
        Assert.Equal(20, clips[1].FadeInFrames); Assert.Equal(20, clips[1].FadeOutFrames);
        Assert.Equal(30, clips[1].FadeOffsetFrames); Assert.Equal(180, clips[1].FadeDurationFrames);
        Assert.Equal(120, vm.Position);
        Assert.Equal(clips[0].GroupId, clips[1].GroupId);
    }

    [Theory]
    [InlineData(60, 30)]
    [InlineData(120, 24)]
    public void DownsamplingTheFinalSourceFrameKeepsAValidOneFrameClip(int sourceRate, int destinationRate)
    {
        using var session = new Session();
        var vm = session.ViewModel;
        var media = new MediaAsset { Path = "single-second.mp4", DurationSeconds = 1, HasVideo = true };
        var clip = new TimelineClip { MediaId = media.Id, SourceInFrame = sourceRate - 1, DurationFrames = 1 };
        vm.Replace(new ProjectDocument { FrameRate = new Rational(sourceRate, 1), Media = [media], Tracks = [new TimelineTrack { Kind = TrackKind.Video, Clips = [clip] }] }, "source.openrend");
        vm.Selection.Add(clip.Id); vm.Copy();
        vm.Replace(new ProjectDocument { FrameRate = new Rational(destinationRate, 1), Tracks = [new TimelineTrack { Kind = TrackKind.Video }] }, "destination.openrend");
        vm.Paste();
        var result = Assert.Single(Selected(vm));
        Assert.Equal(destinationRate - 1, result.SourceInFrame);
        Assert.Equal(1, result.DurationFrames);
        Assert.Equal(0, result.StartFrame);
        vm.Project.Validate();
    }

    [Fact]
    public void ClipboardCropKeysKeepTheirSecondsAndIndependentGeometryAcrossRates()
    {
        using var session = new Session(); var vm = session.ViewModel;
        var start = CropState.Identity(320, 180); var end = start with { Width = 160, Height = 90, RotationDegrees = 90 };
        vm.Editor.SetCropKeyframe(session.VideoId, 0, start);
        vm.Editor.SetCropKeyframe(session.VideoId, 30, end, CropInterpolation.Hold);
        vm.Editor.TrimClip(session.VideoId, 66, 54);
        vm.Copy();
        vm.Editor.SetCropKeyframe(session.VideoId, 0, start with { CenterX = 42 });
        vm.Replace(new ProjectDocument { FrameRate = new Rational(60, 1), Tracks = [new TimelineTrack { Kind = TrackKind.Video }, new TimelineTrack { Kind = TrackKind.Audio }] }, "crop60.openrend");
        vm.Paste();
        var clip = Selected(vm).Single(c => c.Crop is not null);
        Assert.Equal(12, clip.CropOffsetFrames);
        Assert.Equal(new long[] { 0, 60 }, clip.Crop!.Keyframes.Select(key => key.Frame));
        Assert.Equal(start, clip.Crop.Keyframes[0].State); Assert.Equal(end, clip.Crop.Keyframes[1].State);
        Assert.Equal(CropInterpolation.Hold, clip.Crop.Keyframes[1].Interpolation);
        vm.Paste();
        Selected(vm).Single(c => c.Crop is not null).Crop!.Keyframes[0].State = end;
        Assert.Equal(start, clip.Crop.Keyframes[0].State);
        vm.Project.Validate();
    }

    [Fact]
    public void DownsampledCropKeysResolveFrameCollisionsToTheLaterSourceKey()
    {
        using var session = new Session(); var vm = session.ViewModel;
        var first = CropState.Identity(320, 180);
        vm.Editor.SetCropKeyframe(session.VideoId, 0, first);
        vm.Editor.SetCropKeyframe(session.VideoId, 1, first with { CenterX = 120 });
        vm.Editor.SetCropKeyframe(session.VideoId, 2, first with { CenterX = 80 });
        vm.Copy();
        vm.Replace(new ProjectDocument { FrameRate = new Rational(1, 1), Tracks = [new TimelineTrack { Kind = TrackKind.Video }, new TimelineTrack { Kind = TrackKind.Audio }] }, "crop1.openrend");
        vm.Paste();
        var key = Assert.Single(Selected(vm).Single(c => c.Crop is not null).Crop!.Keyframes);
        Assert.Equal(0, key.Frame); Assert.Equal(80, key.State.CenterX);
        vm.Project.Validate();
    }

    private static TimelineClip[] Selected(EditorViewModel vm) => vm.Project.Tracks.SelectMany(t => t.Clips).Where(c => vm.Selection.Contains(c.Id)).OrderBy(c => c.StartFrame).ToArray();
}
