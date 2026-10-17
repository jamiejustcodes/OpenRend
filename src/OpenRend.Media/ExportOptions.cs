using OpenRend.Core;

namespace OpenRend.Media;

public readonly record struct RenderRange(long StartFrame, long FrameCount)
{
    public void Validate(long timelineFrames)
    {
        if (StartFrame < 0 || FrameCount <= 0 || StartFrame > timelineFrames || FrameCount > timelineFrames - StartFrame)
            throw new ArgumentOutOfRangeException(nameof(RenderRange), "Choose a nonempty render range within the timeline.");
    }
}

public sealed record ExportOptions
{
    public RenderRange? Range { get; init; }
}
