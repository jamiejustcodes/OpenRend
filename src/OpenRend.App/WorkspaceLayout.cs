using System.Text.Json.Serialization;

namespace OpenRend.App;

/// <summary>
/// Represents a window docking and workspace geometry layout configuration matching Vegas Pro
/// window layout presets (Default, Audio Mixing, Color Correction, Source Window, Timeline Top, Custom).
/// </summary>
public sealed class WorkspaceLayout
{
    public string Name { get; set; } = string.Empty;

    public double WorkspaceHeight { get; set; } = 340;

    public double MediaWidth { get; set; } = 420;

    public double PreviewWidth { get; set; } = 880;

    /// <summary>
    /// Active tab in the tools deck: 0=Explorer, 1=Project Media, 2=Media Generator,
    /// 3=Transitions, 4=Video FX, 5=Color Grading, 6=Clip Properties.
    /// </summary>
    public int ToolsTabIndex { get; set; } = 1;

    /// <summary>
    /// Active tab in the preview deck: 0=Video Preview, 1=Trimmer.
    /// </summary>
    public int PreviewDeckTabIndex { get; set; } = 0;

    /// <summary>
    /// When true, inverts the layout placing the timeline track view at the top
    /// and the workspace dock decks at the bottom, matching Vegas Pro's Timeline Top Layout.
    /// </summary>
    public bool TimelineOnTop { get; set; } = false;

    public bool VideoScopesOpen { get; set; } = false;

    public bool MixingConsoleOpen { get; set; } = false;

    public bool EditDetailsOpen { get; set; } = false;

    /// <summary>
    /// Numbered shortcut slot 0 through 9 (triggerable via Alt+D, 0-9), or null if unassigned.
    /// </summary>
    public int? ShortcutSlot { get; set; }

    [JsonIgnore]
    public bool IsBuiltIn { get; set; }

    public WorkspaceLayout Clone(string? newName = null)
    {
        return new WorkspaceLayout
        {
            Name = newName ?? Name,
            WorkspaceHeight = WorkspaceHeight,
            MediaWidth = MediaWidth,
            PreviewWidth = PreviewWidth,
            ToolsTabIndex = ToolsTabIndex,
            PreviewDeckTabIndex = PreviewDeckTabIndex,
            TimelineOnTop = TimelineOnTop,
            VideoScopesOpen = VideoScopesOpen,
            MixingConsoleOpen = MixingConsoleOpen,
            EditDetailsOpen = EditDetailsOpen,
            ShortcutSlot = ShortcutSlot,
            IsBuiltIn = false
        };
    }
}

