using System.IO;
using System.Text.Json;

namespace OpenRend.App;

/// <summary>
/// Manages standard Vegas Pro window layout presets and custom user-defined window layouts,
/// file serialization (.OpenRendWindowLayout), and shortcut slot assignments (0–9).
/// </summary>
public static class WorkspaceLayoutManager
{
    public const string LayoutFileExtension = ".OpenRendWindowLayout";

    public static string GetDefaultLayoutsDirectory()
    {
        var baseDir = Environment.GetEnvironmentVariable("OPENREND_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRend");
        var dir = Path.Combine(baseDir, "Layouts");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static IReadOnlyList<WorkspaceLayout> CreateBuiltInLayouts() => new List<WorkspaceLayout>
    {
        new()
        {
            Name = "Default Layout",
            WorkspaceHeight = 450,
            MediaWidth = 380,
            PreviewWidth = 844,
            ToolsTabIndex = 1, // Project Media
            PreviewDeckTabIndex = 0, // Video Preview
            TimelineOnTop = false,
            VideoScopesOpen = false,
            MixingConsoleOpen = false,
            EditDetailsOpen = false,
            IsBuiltIn = true
        },
        new()
        {
            Name = "Audio Mixing",
            WorkspaceHeight = 280,
            MediaWidth = 320,
            PreviewWidth = 600,
            ToolsTabIndex = 1,
            PreviewDeckTabIndex = 0,
            TimelineOnTop = false,
            VideoScopesOpen = false,
            MixingConsoleOpen = true,
            EditDetailsOpen = false,
            IsBuiltIn = true
        },
        new()
        {
            Name = "Color Correction",
            WorkspaceHeight = 520,
            MediaWidth = 380,
            PreviewWidth = 920,
            ToolsTabIndex = 5, // Color Grading tab
            PreviewDeckTabIndex = 0,
            TimelineOnTop = false,
            VideoScopesOpen = true,
            MixingConsoleOpen = false,
            EditDetailsOpen = false,
            IsBuiltIn = true
        },
        new()
        {
            Name = "Source Window Layout",
            WorkspaceHeight = 450,
            MediaWidth = 380,
            PreviewWidth = 844,
            ToolsTabIndex = 0, // Explorer
            PreviewDeckTabIndex = 1, // Trimmer deck
            TimelineOnTop = false,
            VideoScopesOpen = false,
            MixingConsoleOpen = false,
            EditDetailsOpen = false,
            IsBuiltIn = true
        },
        new()
        {
            Name = "Timeline Top Layout",
            WorkspaceHeight = 450,
            MediaWidth = 380,
            PreviewWidth = 844,
            ToolsTabIndex = 1,
            PreviewDeckTabIndex = 0,
            TimelineOnTop = true,
            VideoScopesOpen = false,
            MixingConsoleOpen = false,
            EditDetailsOpen = false,
            IsBuiltIn = true
        }
    };

    public static List<WorkspaceLayout> LoadAllLayouts(string? layoutsDir = null)
    {
        var dir = layoutsDir ?? GetDefaultLayoutsDirectory();
        var results = new List<WorkspaceLayout>(CreateBuiltInLayouts());

        if (Directory.Exists(dir))
        {
            var files = Directory.GetFiles(dir, $"*{LayoutFileExtension}");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var layout = JsonSerializer.Deserialize<WorkspaceLayout>(json);
                    if (layout != null && !string.IsNullOrWhiteSpace(layout.Name))
                    {
                        layout.IsBuiltIn = false;
                        var existingIndex = results.FindIndex(l => string.Equals(l.Name, layout.Name, StringComparison.OrdinalIgnoreCase));
                        if (existingIndex >= 0)
                        {
                            results[existingIndex] = layout;
                        }
                        else
                        {
                            results.Add(layout);
                        }
                    }
                }
                catch
                {
                    // Ignore corrupted or unreadable custom layout files.
                }
            }
        }

        return results;
    }

    public static WorkspaceLayout? GetLayoutBySlot(int slotIndex, string? layoutsDir = null)
    {
        if (slotIndex < 0 || slotIndex > 9) return null;
        var all = LoadAllLayouts(layoutsDir);
        return all.Find(l => l.ShortcutSlot == slotIndex);
    }

    public static void SaveLayout(WorkspaceLayout layout, string? layoutsDir = null)
    {
        if (string.IsNullOrWhiteSpace(layout.Name))
            throw new ArgumentException("Layout name cannot be empty.", nameof(layout));

        var dir = layoutsDir ?? GetDefaultLayoutsDirectory();
        Directory.CreateDirectory(dir);

        // If assigning a shortcut slot, clear that slot from other layouts in this folder.
        if (layout.ShortcutSlot.HasValue)
        {
            var existingLayouts = LoadAllLayouts(dir);
            foreach (var existing in existingLayouts)
            {
                if (!existing.IsBuiltIn &&
                    !string.Equals(existing.Name, layout.Name, StringComparison.OrdinalIgnoreCase) &&
                    existing.ShortcutSlot == layout.ShortcutSlot.Value)
                {
                    existing.ShortcutSlot = null;
                    SaveLayoutFile(existing, dir);
                }
            }
        }

        SaveLayoutFile(layout, dir);
    }

    private static void SaveLayoutFile(WorkspaceLayout layout, string dir)
    {
        var sanitizedName = SanitizeFileName(layout.Name);
        var filePath = Path.Combine(dir, sanitizedName + LayoutFileExtension);
        var json = JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    public static bool DeleteLayout(string layoutName, string? layoutsDir = null)
    {
        var dir = layoutsDir ?? GetDefaultLayoutsDirectory();
        var sanitizedName = SanitizeFileName(layoutName);
        var filePath = Path.Combine(dir, sanitizedName + LayoutFileExtension);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            return true;
        }

        return false;
    }

    public static bool RenameLayout(string oldName, string newName, string? layoutsDir = null)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;
        var dir = layoutsDir ?? GetDefaultLayoutsDirectory();
        var oldPath = Path.Combine(dir, SanitizeFileName(oldName) + LayoutFileExtension);
        var newPath = Path.Combine(dir, SanitizeFileName(newName) + LayoutFileExtension);

        if (!File.Exists(oldPath)) return false;

        try
        {
            var json = File.ReadAllText(oldPath);
            var layout = JsonSerializer.Deserialize<WorkspaceLayout>(json);
            if (layout == null) return false;

            layout.Name = newName.Trim();
            File.Delete(oldPath);
            File.WriteAllText(newPath, JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void AssignShortcutSlot(string layoutName, int? slotIndex, string? layoutsDir = null)
    {
        var dir = layoutsDir ?? GetDefaultLayoutsDirectory();
        var layouts = LoadAllLayouts(dir);
        var target = layouts.Find(l => string.Equals(l.Name, layoutName, StringComparison.OrdinalIgnoreCase));
        if (target == null) return;

        target.ShortcutSlot = slotIndex;
        SaveLayout(target, dir);
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "CustomLayout" : cleaned;
    }
}

