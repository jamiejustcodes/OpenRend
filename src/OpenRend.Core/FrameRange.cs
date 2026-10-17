namespace OpenRend.Core;

public readonly record struct FrameRange(long StartFrame, long FrameCount)
{
    public long EndFrame => checked(StartFrame + FrameCount);

    public void Validate(long timelineFrames)
    {
        if (StartFrame < 0 || FrameCount <= 0 || StartFrame > timelineFrames || FrameCount > timelineFrames - StartFrame)
            throw new ArgumentOutOfRangeException(nameof(FrameRange), "Choose a nonempty range within the timeline.");
    }
}
