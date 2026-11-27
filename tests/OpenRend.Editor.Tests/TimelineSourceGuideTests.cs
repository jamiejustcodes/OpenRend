using OpenRend.Core;
using OpenRend.Timeline;
using OpenRend.UI;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class TimelineSourceGuideTests
{
    [Theory]
    [InlineData(VideoSourceMode.Loop, false, false)]
    [InlineData(VideoSourceMode.Loop, true, false)]
    [InlineData(VideoSourceMode.Loop, false, true)]
    [InlineData(VideoSourceMode.HoldLastFrame, true, false)]
    public void VideoGuidesUseTheFramesSelectedByTimelineEvaluation(VideoSourceMode mode, bool reverse, bool velocity)
    {
        var rate = new Rational(30000, 1001);
        var asset = new MediaAsset { Path = "clip.mp4", HasVideo = true, DurationSeconds = .95 };
        var clip = new TimelineClip
        {
            MediaId = asset.Id, SourceInFrame = mode == VideoSourceMode.Loop ? -7 : 0, DurationFrames = 150,
            VideoSourceMode = mode, UnderSampleRate = .5, PlaybackRate = 1.5, IsReversed = reverse,
            Envelopes = velocity ? [new ClipEnvelope { Type = EnvelopeType.Velocity, Points =
                [new() { Frame = 0, Value = 1 }, new() { Frame = 70, Value = -1 }, new() { Frame = 140, Value = 2 }] }] : null
        };
        var project = new ProjectDocument { FrameRate = rate, Media = [asset],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [clip] }] };
        double Region(int frame)
        {
            var position = rate.Seconds(Assert.Single(TimelineEvaluator.Evaluate(project, frame).Video).SourceFrame);
            return mode == VideoSourceMode.HoldLastFrame ? (position >= asset.DurationSeconds ? 1 : 0)
                : Math.Floor(position / asset.DurationSeconds);
        }

        var expected = Enumerable.Range(1, 149).Where(frame => Region(frame) != Region(frame - 1))
            .Select(frame => rate.Seconds(frame)).ToArray();
        Assert.NotEmpty(expected);
        var actual = TimelineView.VideoSourceGuides(rate, clip, asset, clip.SourceInFrame, clip.DurationFrames,
            clip.PlaybackRate, 0, rate.Seconds(clip.DurationFrames), 240).ToArray();
        Assert.Equal(expected, actual);
        var scrolled = TimelineView.VideoSourceGuides(rate, clip, asset, clip.SourceInFrame, clip.DurationFrames,
            clip.PlaybackRate, .43, 4.25, 240);
        Assert.Equal(expected.Where(time => time >= .43 && time <= 4.25), scrolled);
    }

    [Fact]
    public void AudioGuidesUseTheMixSamplePeriodAndFollowSourceIn()
    {
        var rate = new Rational(30000, 1001);
        var asset = new MediaAsset { Path = "audio.wav", HasAudio = true, DurationSeconds = 2401 / 48000d };
        var clip = new TimelineClip { AudioSourceMode = AudioSourceMode.Loop };
        var forward = TimelineView.AudioSourceGuides(rate, clip, asset, 1, 6, 0, .1).ToArray();
        Assert.Equal(new[] { 799 / 48000d, 3200 / 48000d }, forward);
        var afterLeftTrim = TimelineView.AudioSourceGuides(rate, clip, asset, 2, 5, 0, .1).ToArray();
        Assert.Equal(new[] { 1599 / 48000d, 4000 / 48000d }, afterLeftTrim);

        clip.AudioSourceMode = AudioSourceMode.PadSilence;
        Assert.Equal(799 / 48000d, Assert.Single(TimelineView.AudioSourceGuides(rate, clip, asset, 1, 6, 0, .1)));
        clip.IsReversed = true;
        Assert.Equal(803 / 48000d, Assert.Single(TimelineView.AudioSourceGuides(rate, clip, asset, 0, 2, 0, .1)));
    }
}
