using System.IO;
using System.Windows;
using OpenRend.Core;

namespace OpenRend.App;

public partial class MainWindow
{
    private string? workspaceDriveRoot;
    private long workspaceDriveReadAt;
    private int workspaceDriveGeneration;

    private void UpdateWorkspaceStatus()
    {
        var project = vm.Project;
        WorkspaceProjectFormat.Text = $"{project.Width}×{project.Height} · {project.FrameRate.Value:0.###} fps";
        UpdateWorkspaceSelectionDuration();
        RefreshWorkspaceDriveSpace();
    }

    private void UpdateWorkspaceSelectionDuration()
    {
        var range = vm.LoopRegion;
        WorkspaceSelectionDuration.Visibility = range.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (range is { } selection)
        {
            var project = vm.Project;
            var length = TimecodeFormatter.Format(selection.FrameCount, project.FrameRate, project.RulerFormat,
                project.AudioSampleRate, project.BeatsPerMinute, project.BeatsPerMeasure);
            WorkspaceSelectionDuration.Text = $"Selection {length}";
        }
    }

    private async void RefreshWorkspaceDriveSpace()
    {
        var root = Path.GetPathRoot(vm.ProjectPath ?? recoveryPath);
        WorkspaceDriveSpace.ToolTip = vm.ProjectPath is null ? "Recovery drive" : "Project drive";
        var now = Environment.TickCount64;
        var sameDrive = string.Equals(root, workspaceDriveRoot, StringComparison.OrdinalIgnoreCase);
        if (sameDrive && now - workspaceDriveReadAt < 10_000)
            return;
        if (!sameDrive)
            WorkspaceDriveSpace.Visibility = Visibility.Collapsed;
        workspaceDriveRoot = root;
        workspaceDriveReadAt = now;
        var generation = ++workspaceDriveGeneration;
        if (string.IsNullOrEmpty(root))
        {
            WorkspaceDriveSpace.Visibility = Visibility.Collapsed;
            return;
        }

        long? bytes = null;
        try { bytes = await Task.Run(() => new DriveInfo(root).AvailableFreeSpace); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        if (generation != workspaceDriveGeneration || !IsLoaded)
            return;

        WorkspaceDriveSpace.Visibility = bytes.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (bytes is long available)
        {
            var volume = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            WorkspaceDriveSpace.Text = $"{volume} · {available / 1_073_741_824.0:0.#} GiB free";
        }
    }
}
