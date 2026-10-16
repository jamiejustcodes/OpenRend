namespace OpenRend.App;

internal readonly record struct PreviewFrameRequest(long Revision, long PlaybackGeneration, long Frame)
{
    public bool CanPresent(long revision, long playbackGeneration, long position, bool playing) =>
        Revision == revision && PlaybackGeneration == playbackGeneration && (playing || Frame == position);
}
