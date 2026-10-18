using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class TransitionRenderTests
{
    private static ProjectDocument CreateTransitionProject(VideoTransition transition)
    {
        var project = new ProjectDocument { Width = 32, Height = 24 };
        var red = new MediaAsset { Path = "openrend://color?hex=#FF0000", DurationSeconds = 3, HasVideo = true };
        var blue = new MediaAsset { Path = "openrend://color?hex=#0000FF", DurationSeconds = 3, HasVideo = true };
        var editor = new TimelineEditor(project);
        editor.AddMedia(red);
        editor.AddMedia(blue);
        var track = editor.AddTrack(TrackKind.Video);
        var outgoing = editor.PlaceClip(red.Id, track.Id, 0, 30);
        var incoming = editor.PlaceClip(blue.Id, track.Id, 15, 30);
        editor.ApplyDissolve(outgoing.Id, incoming.Id, transition);
        return project;
    }

    [Fact]
    public void LinearWipeRendersLeftToRightHardEdgeCorrectly()
    {
        var transition = new VideoTransition
        {
            Kind = VideoTransitionKind.LinearWipe,
            Preset = "Left-Right, Hard Edge",
            Parameters = new Dictionary<string, string>
            {
                ["Angle"] = "0.0",
                ["Feather"] = "0.0"
            }
        };

        var project = CreateTransitionProject(transition);
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        // At frame 22 (approx midpoint, progress ~ 0.5):
        // Left side (x = 2) should be incoming blue
        // Right side (x = 30) should be outgoing red
        var frame = renderer.RenderFrame(project, 22, 32, 24);

        // Check left pixel (x = 2, y = 12)
        var leftOffset = (12 * 32 + 2) * 4;
        Assert.InRange((int)frame[leftOffset + 0], 250, 255); // Blue
        Assert.InRange((int)frame[leftOffset + 2], 0, 5);     // Red

        // Check right pixel (x = 30, y = 12)
        var rightOffset = (12 * 32 + 30) * 4;
        Assert.InRange((int)frame[rightOffset + 0], 0, 5);     // Blue
        Assert.InRange((int)frame[rightOffset + 2], 250, 255); // Red
    }

    [Fact]
    public void BarnDoorRendersCenterOutwardCorrectly()
    {
        var transition = new VideoTransition
        {
            Kind = VideoTransitionKind.BarnDoor,
            Preset = "Vertical, Out, No Border",
            Parameters = new Dictionary<string, string>
            {
                ["Orientation"] = "1",
                ["Direction"] = "1",
                ["BorderSize"] = "0.0"
            }
        };

        var project = CreateTransitionProject(transition);
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        // At frame 22 (midpoint):
        // Center pixel (x = 16, y = 12) should be incoming blue
        // Left edge (x = 1, y = 12) and right edge (x = 31, y = 12) should be outgoing red
        var frame = renderer.RenderFrame(project, 22, 32, 24);

        var centerOffset = (12 * 32 + 16) * 4;
        Assert.InRange((int)frame[centerOffset + 0], 250, 255); // Blue
        Assert.InRange((int)frame[centerOffset + 2], 0, 5);     // Red

        var edgeOffset = (12 * 32 + 1) * 4;
        Assert.InRange((int)frame[edgeOffset + 0], 0, 5);       // Blue
        Assert.InRange((int)frame[edgeOffset + 2], 250, 255);   // Red
    }

    [Fact]
    public void IrisRendersCenterCircleOutwardCorrectly()
    {
        var transition = new VideoTransition
        {
            Kind = VideoTransitionKind.Iris,
            Preset = "Circle, Out, Center",
            Parameters = new Dictionary<string, string>
            {
                ["Shape"] = "0",
                ["Direction"] = "1",
                ["Center"] = "0.5 0.5",
                ["Border"] = "0.0"
            }
        };

        var project = CreateTransitionProject(transition);
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        // At frame 22: center should be blue, corners should be red
        var frame = renderer.RenderFrame(project, 22, 32, 24);

        var centerOffset = (12 * 32 + 16) * 4;
        Assert.InRange((int)frame[centerOffset + 0], 250, 255); // Blue
        Assert.InRange((int)frame[centerOffset + 2], 0, 5);     // Red

        var cornerOffset = (0 * 32 + 0) * 4;
        Assert.InRange((int)frame[cornerOffset + 0], 0, 5);     // Blue
        Assert.InRange((int)frame[cornerOffset + 2], 250, 255); // Red
    }

    [Fact]
    public void FlashTransitionCreatesLuminanceSpikeAtMidpoint()
    {
        var transition = new VideoTransition
        {
            Kind = VideoTransitionKind.Flash,
            Preset = "Hard Flash",
            Parameters = new Dictionary<string, string> { ["Tint"] = "1 1 1" }
        };

        var project = CreateTransitionProject(transition);
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        // At frame 22 (progress ~ 0.5), flash adds white to the blend
        var frame = renderer.RenderFrame(project, 22, 32, 24);
        var offset = (12 * 32 + 16) * 4;
        // With white flash, green channel is spiked
        Assert.True(frame[offset + 1] > 100, $"Expected spiked green channel during flash, got {frame[offset + 1]}");
    }

    [Fact]
    public void DissolveAdditiveAndFadeThroughColorRenderAccurately()
    {
        var additive = new VideoTransition
        {
            Kind = VideoTransitionKind.Dissolve,
            Preset = "Additive Dissolve",
            Parameters = new Dictionary<string, string> { ["Type"] = "1" }
        };

        var project = CreateTransitionProject(additive);
        using var media = new MediaService();
        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        var addFrame = renderer.RenderFrame(project, 22, 32, 24);
        var offset = (12 * 32 + 16) * 4;
        // In additive dissolve of red + blue, both red and blue are intensely bright
        Assert.True(addFrame[offset + 0] > 120);
        Assert.True(addFrame[offset + 2] > 120);

        // Fade Through Black
        var fadeBlack = new VideoTransition
        {
            Kind = VideoTransitionKind.Dissolve,
            Preset = "Fade Through Black",
            Parameters = new Dictionary<string, string>
            {
                ["Type"] = "8",
                ["FadeThroughColor"] = "0 0 0 1"
            }
        };

        var projectBlack = CreateTransitionProject(fadeBlack);
        var blackFrame = renderer.RenderFrame(projectBlack, 22, 32, 24);
        var blackOffset = (12 * 32 + 16) * 4;
        // At midpoint, luminance dips toward black (0)
        Assert.InRange((int)blackFrame[blackOffset + 0], 0, 30);
        Assert.InRange((int)blackFrame[blackOffset + 2], 0, 30);
    }
}
