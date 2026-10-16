using OpenRend.App;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class PreviewSchedulingTests
{
    [Fact]
    public void CompletedVideoFramesRemainDisplayableWhileTheAudioClockAdvances()
    {
        var decode = new PreviewFrameRequest(2, 5, 30);
        // A slow decode must still produce a visible frame, even several clock ticks later.
        for (long audioFrame = 31; audioFrame <= 39; audioFrame++)
            Assert.True(decode.CanPresent(2, 5, audioFrame, playing: true));
    }

    [Theory]
    [InlineData(3, 5, 30, true)]
    [InlineData(2, 6, 30, true)]
    [InlineData(2, 6, 30, false)]
    [InlineData(2, 5, 60, false)]
    public void EditsNewPlaybackSessionsAndPausedSeeksRejectObsoleteFrames(long revision, long generation, long position, bool playing)
    {
        var decode = new PreviewFrameRequest(2, 5, 30);
        Assert.False(decode.CanPresent(revision, generation, position, playing));
    }

    [Fact]
    public void PausedPreviewAcceptsOnlyTheExactRequestedFrame()
    {
        var decode = new PreviewFrameRequest(2, 5, 30);
        Assert.True(decode.CanPresent(2, 5, 30, playing: false));
        Assert.False(decode.CanPresent(2, 5, 31, playing: false));
    }
}
