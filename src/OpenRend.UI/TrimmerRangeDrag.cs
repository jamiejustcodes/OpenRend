using OpenRend.Core;

namespace OpenRend.UI;

public sealed record TrimmerRangeDrag(Guid MediaId, FrameRange SourceRange, TrackKind Stream);
