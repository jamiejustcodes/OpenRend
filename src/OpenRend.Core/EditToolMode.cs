using System.Text.Json.Serialization;

namespace OpenRend.Core;

/// <summary>
/// Vegas Pro 1:1 timeline editing tool modes (Menu 1003 / Edit -> Editing Tool).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditToolMode>))]
public enum EditToolMode
{
    Normal = 0,
    Envelope = 1,
    Selection = 2,
    Zoom = 3
}

public static class EditToolModeExtensions
{
    public static string ToDisplayName(this EditToolMode mode) => mode switch
    {
        EditToolMode.Normal => "Normal Edit Tool",
        EditToolMode.Envelope => "Envelope Edit Tool",
        EditToolMode.Selection => "Selection Edit Tool",
        EditToolMode.Zoom => "Zoom Tool",
        _ => "Normal Edit Tool"
    };

    public static string ToStatusHint(this EditToolMode mode) => mode switch
    {
        EditToolMode.Normal => "Normal Edit Tool (Ctrl+D)",
        EditToolMode.Envelope => "Envelope Edit Tool: Adjust automation curves without moving events.",
        EditToolMode.Selection => "Selection Edit Tool (D): Drag on timeline to select multiple events.",
        EditToolMode.Zoom => "Zoom Tool: Click to zoom in, right-click to zoom out, drag to zoom box.",
        _ => "Normal Edit Tool"
    };

    public static EditToolMode NextTool(this EditToolMode current) => current switch
    {
        EditToolMode.Normal => EditToolMode.Selection,
        EditToolMode.Selection => EditToolMode.Zoom,
        EditToolMode.Zoom => EditToolMode.Envelope,
        EditToolMode.Envelope => EditToolMode.Normal,
        _ => EditToolMode.Normal
    };

    public static EditToolMode PreviousTool(this EditToolMode current) => current switch
    {
        EditToolMode.Normal => EditToolMode.Envelope,
        EditToolMode.Envelope => EditToolMode.Zoom,
        EditToolMode.Zoom => EditToolMode.Selection,
        EditToolMode.Selection => EditToolMode.Normal,
        _ => EditToolMode.Normal
    };
}
