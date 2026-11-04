namespace OpenRend.Core;

/// <summary>
/// Specifies the target stream routing when placing media from the Trimmer to the timeline.
/// Matches Vegas Pro 1:1 stream placement controls:
/// - Video and Audio (both streams placed and grouped).
/// - Video Only (only video placed on video track; audio ignored).
/// - Audio Only (only audio placed on audio track; video ignored).
/// </summary>
public enum TrimmerPlacementStream
{
    VideoAndAudio,
    VideoOnly,
    AudioOnly
}

