using OpenRend.Core;

namespace OpenRend.Media;

public sealed record ExportOptions
{
    public FrameRange? Range { get; init; }
    public string TemplateId { get; init; } = RenderTemplateCatalog.DefaultId;
}
