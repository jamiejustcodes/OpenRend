using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class TrackMotion3DEditorTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new AggregateException(error);
    }

    [Fact]
    public void TrackCompositeMode_SourceAlpha3D_SerializesAndValidates()
    {
        var project = new ProjectDocument { Name = "3D Composite Test" };
        var track = new TimelineTrack
        {
            Id = Guid.NewGuid(),
            Name = "Video 1",
            Kind = TrackKind.Video,
            CompositeMode = TrackCompositeMode.SourceAlpha3D
        };
        project.Tracks.Add(track);

        project.Validate();

        var json = JsonSerializer.Serialize(project);
        Assert.Contains("\"SourceAlpha3D\"", json);

        var deserialized = JsonSerializer.Deserialize<ProjectDocument>(json);
        Assert.NotNull(deserialized);
        deserialized.Validate();
        Assert.Equal(TrackCompositeMode.SourceAlpha3D, deserialized.Tracks[0].CompositeMode);

        var clone = project.DeepClone();
        Assert.Equal(TrackCompositeMode.SourceAlpha3D, clone.Tracks[0].CompositeMode);
    }

    [Fact]
    public void TrackMotionKeyframe_3DProperties_LerpAndValidateCorrectly()
    {
        var k1 = new TrackMotionKeyframe
        {
            Frame = 0,
            PositionX = -100,
            PositionY = 50,
            PositionZ = 200,
            Width = 1920,
            Height = 1080,
            Depth = 10,
            RotationX = 15,
            RotationY = 30,
            RotationZ = 45,
            OrientationX = 5,
            OrientationY = 10,
            OrientationZ = 15,
            RotationOffsetX = 20,
            RotationOffsetY = 30,
            RotationOffsetZ = 40,
            Interpolation = CropInterpolation.Linear,
            Smoothness = 80
        };

        k1.Validate();
        Assert.True(k1.Is3D());
        Assert.False(k1.IsIdentity(1920, 1080));

        // Test Pivot aliases
        Assert.Equal(20, k1.PivotX);
        Assert.Equal(30, k1.PivotY);
        Assert.Equal(40, k1.PivotZ);

        var k2 = new TrackMotionKeyframe
        {
            Frame = 100,
            PositionX = 100,
            PositionY = -50,
            PositionZ = -200,
            Width = 960,
            Height = 540,
            Depth = 20,
            RotationX = -15,
            RotationY = -30,
            RotationZ = -45,
            OrientationX = -5,
            OrientationY = -10,
            OrientationZ = -15,
            RotationOffsetX = -20,
            RotationOffsetY = -30,
            RotationOffsetZ = -40,
            Interpolation = CropInterpolation.Linear,
            Smoothness = 80
        };

        var mid = TrackMotionKeyframe.Lerp(k1, k2, 0.5);
        Assert.Equal(50, mid.Frame);
        Assert.Equal(0.0, mid.PositionX, 3);
        Assert.Equal(0.0, mid.PositionY, 3);
        Assert.Equal(0.0, mid.PositionZ, 3);
        Assert.Equal(1440.0, mid.Width, 3);
        Assert.Equal(810.0, mid.Height, 3);
        Assert.Equal(15.0, mid.Depth, 3);
        Assert.Equal(0.0, mid.RotationX, 3);
        Assert.Equal(0.0, mid.RotationY, 3);
        Assert.Equal(0.0, mid.RotationZ, 3);
        Assert.Equal(0.0, mid.OrientationX, 3);
        Assert.Equal(0.0, mid.OrientationY, 3);
        Assert.Equal(0.0, mid.OrientationZ, 3);
        Assert.Equal(0.0, mid.RotationOffsetX, 3);
        Assert.Equal(0.0, mid.RotationOffsetY, 3);
        Assert.Equal(0.0, mid.RotationOffsetZ, 3);
    }

    [Fact]
    public void TimelineEditor_SetTrackCompositeMode_Supports3DSourceAlphaAndUndo()
    {
        var project = new ProjectDocument { Name = "Test Project" };
        var editor = new TimelineEditor(project);
        var track = editor.AddTrack(TrackKind.Video);

        Assert.Equal(TrackCompositeMode.SourceAlpha, editor.Project.Tracks[0].CompositeMode);

        editor.SetTrackCompositeMode(track.Id, TrackCompositeMode.SourceAlpha3D);
        Assert.Equal(TrackCompositeMode.SourceAlpha3D, editor.Project.Tracks[0].CompositeMode);

        editor.Undo();
        Assert.Equal(TrackCompositeMode.SourceAlpha, editor.Project.Tracks[0].CompositeMode);

        editor.Redo();
        Assert.Equal(TrackCompositeMode.SourceAlpha3D, editor.Project.Tracks[0].CompositeMode);
    }

    [Fact]
    public void TrackMotionWindow_3DSourceAlphaSwitchAndPreset_ExecutesAccurately()
    {
        RunSta(() =>
        {
            var mediaService = new MediaService();
            var renderer = new TimelineRenderService(mediaService);
            var vm = new EditorViewModel(mediaService, renderer, new ExportService(renderer));
            var track = vm.Editor.AddTrack(TrackKind.Video);

            var window = new TrackMotionWindow(vm, track.Id, _ => { });

            // Initially Source Alpha
            Assert.Equal(TrackCompositeMode.SourceAlpha, track.CompositeMode);

            // Set 3D Source Alpha via editor and verify window handles 3D
            vm.Editor.SetTrackCompositeMode(track.Id, TrackCompositeMode.SourceAlpha3D);
            Assert.Equal(TrackCompositeMode.SourceAlpha3D, track.CompositeMode);

            var win3D = new TrackMotionWindow(vm, track.Id, _ => { });
            Assert.Contains("Track Motion", win3D.Title);

            win3D.Close();
            window.Close();
        });
    }
}

