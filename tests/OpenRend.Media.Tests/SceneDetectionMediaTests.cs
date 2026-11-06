using System;
using System.Threading;
using System.Threading.Tasks;
using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class SceneDetectionMediaTests : IClassFixture<MediaFixture>
{
    private readonly MediaFixture fixture;

    public SceneDetectionMediaTests(MediaFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task DetectScenesAsync_DetectsHardCutBetweenRedAndBlue_InTestVideo()
    {
        using var mediaService = new MediaService();
        var service = new SceneDetectionService(mediaService);

        var options = new SceneDetectionOptions
        {
            Sensitivity = 0.65,
            MinSceneLengthMs = 300.0, // 9 frames at 30fps
            DetectHardCuts = true
        };

        var progressReported = false;
        var progress = new Progress<double>(pct =>
        {
            if (pct > 0) progressReported = true;
        });

        // fixture.Source has 1s of Red (frames 0-29) followed by 1s of Blue (frames 30-59) at 30fps
        var cuts = await service.DetectScenesAsync(
            fixture.Source,
            startFrame: 0,
            totalFrames: 60,
            frameRate: new Rational(30, 1),
            options: options,
            clipStartAbsoluteFrame: 0,
            progress: progress,
            cancellationToken: CancellationToken.None
        );

        Assert.True(progressReported, "Expected progress reports during scan");
        Assert.Single(cuts);

        // Frame offset should be right at the transition point (frame 30)
        Assert.Equal(30, cuts[0].FrameOffset);
        Assert.Equal(30, cuts[0].AbsoluteFrame);
        Assert.Equal(1.0, cuts[0].TimestampSeconds, 2);
        Assert.True(cuts[0].Score > 50.0, $"Expected strong cut score, but got {cuts[0].Score}");
        Assert.Equal(SceneCutType.HardCut, cuts[0].CutType);

        // Build continuous spans
        var spans = SceneDetectionEngine.BuildSceneSpans(cuts, totalFrames: 60, clipStartAbsoluteFrame: 0, baseName: "Colors");
        Assert.Equal(2, spans.Count);

        Assert.Equal(0, spans[0].StartOffsetFrames);
        Assert.Equal(30, spans[0].EndOffsetFrames);
        Assert.Equal(30, spans[0].DurationFrames);
        Assert.Equal("Colors [Scene 1]", spans[0].Name);

        Assert.Equal(30, spans[1].StartOffsetFrames);
        Assert.Equal(60, spans[1].EndOffsetFrames);
        Assert.Equal(30, spans[1].DurationFrames);
        Assert.Equal("Colors [Scene 2]", spans[1].Name);
    }

    [Fact]
    public async Task DetectScenesAsync_RespectsCancellationToken()
    {
        using var mediaService = new MediaService();
        var service = new SceneDetectionService(mediaService);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var options = new SceneDetectionOptions();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.DetectScenesAsync(
                fixture.Source,
                startFrame: 0,
                totalFrames: 60,
                frameRate: new Rational(30, 1),
                options: options,
                cancellationToken: cts.Token
            );
        });
    }
}

