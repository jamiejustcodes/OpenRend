using OpenRend.Core;

namespace OpenRend.Media;

public sealed record ExportOptions
{
    public FrameRange? Range { get; init; }
    public string TemplateId { get; init; } = RenderTemplateCatalog.DefaultId;
    public RenderTemplate? CustomTemplate { get; init; }
    public bool StretchToFill { get; init; }
    public ChannelRemapping ChannelMapping { get; init; } = ChannelRemapping.None;
    public bool GenerateLoudnessLog { get; init; }
}
