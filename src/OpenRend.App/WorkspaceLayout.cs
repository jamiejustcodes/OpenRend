using System.Text.Json.Serialization;

namespace OpenRend.App;

/// <summary>
/// Stores workspace geometry, tab selection and tool window state for a layout preset.
/// </summary>
public sealed class WorkspaceLayout
{
    public string Name { get; set; } = string.Empty;

    public double WorkspaceHeight { get; set; } = 520;

    public double MediaWidth { get; set; } = 360;

    public double PreviewWidth { get; set; } = 980;

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

    public TrimmerPaneLayout? Trimmer { get; set; }

    public Dictionary<string, WorkspacePaneLayout>? Panes { get; set; }

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
            Trimmer = Trimmer,
            Panes = Panes is null ? null : new(Panes),
            ShortcutSlot = ShortcutSlot,
            IsBuiltIn = false
        };
    }
}

public sealed record TrimmerPaneLayout(bool Floating, bool Visible, PaneBounds? Bounds)
{
    public bool SeparateDock { get; init; }
    public double DockWidth { get; init; } = 568;
}

public sealed record WorkspacePaneLayout(bool Floating, bool Visible, PaneBounds? Bounds);

// Position uses physical screen coordinates. Size uses device-independent units.
public sealed record PaneBounds(int Left, int Top, double Width, double Height);

