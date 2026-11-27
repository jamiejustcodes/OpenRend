namespace OpenRend.Core;

public readonly struct VideoSourcePhase
{
    private readonly long sourceIn;
    private readonly long duration;
    private readonly double playbackRate;
    private readonly bool reversed;
    private readonly ClipEnvelope? velocity;

    public VideoSourcePhase(TimelineClip clip, long sourceIn, long duration, double playbackRate)
    {
        this.sourceIn = sourceIn;
        this.duration = duration;
        this.playbackRate = double.IsFinite(playbackRate) && playbackRate != 0 ? playbackRate : 1;
        reversed = clip.IsReversed;
        velocity = clip.Envelopes?.Find(envelope => envelope.Type == EnvelopeType.Velocity && envelope.IsEnabled);
        SampleStep = SampleStepFor(clip);
    }

    public int SampleStep { get; }
    public bool HasVelocity => velocity is not null;

    public static int SampleStepFor(TimelineClip clip) => clip.UnderSampleRate is > .001 and < .999
        ? Math.Max(1, (int)Math.Round(1 / clip.UnderSampleRate)) : 1;

    public long SampledEventFrame(long eventFrame) => eventFrame / SampleStep * SampleStep;

    public long SourceFrameAtSample(long sampledEventFrame) => velocity is not null
        ? velocity.CalculateAccumulatedSourceFrame(sourceIn, sampledEventFrame, playbackRate, reversed, duration)
        : reversed
            ? sourceIn + (long)Math.Round(Math.Max(0, duration - 1 - sampledEventFrame) * Math.Abs(playbackRate))
            : sourceIn + (long)Math.Round(sampledEventFrame * playbackRate);
}
