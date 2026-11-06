using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using OpenRend.App;
using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class AutoSaveAndRecoveryEditorTests
{
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }

    [Fact]
    public void SessionRecoveryManager_Lifecycle_TracksSessionAndCleanExit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OpenRendSessionTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            SessionRecoveryManager.StartSession(tempDir, @"C:\Projects\my_edit.openrend");

            var sessionFile = SessionRecoveryManager.GetSessionFilePath(tempDir);
            Assert.True(File.Exists(sessionFile));

            // Record dirty heartbeat
            SessionRecoveryManager.RecordHeartbeat(tempDir, isDirty: true, @"C:\Projects\my_edit.openrend");
            var meta = JsonSerializer.Deserialize<SessionMetadata>(File.ReadAllText(sessionFile));
            Assert.NotNull(meta);
            Assert.True(meta.IsDirty);
            Assert.False(meta.CleanExit);
            Assert.Equal(@"C:\Projects\my_edit.openrend", meta.ProjectPath);

            // Record clean exit
            SessionRecoveryManager.RecordCleanExit(tempDir);
            Assert.False(File.Exists(sessionFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SessionRecoveryManager_CheckCrashRecovery_ReturnsNullWhenSessionFileMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OpenRendSessionTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var recoverySnapshot = SessionRecoveryManager.GetDefaultRecoveryPath(tempDir);
            File.WriteAllText(recoverySnapshot, "stale_unmanaged_snapshot");

            // Session file does not exist (e.g. after normal exit)
            var candidate = SessionRecoveryManager.CheckCrashRecovery(tempDir);
            Assert.Null(candidate);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SessionRecoveryManager_CheckCrashRecovery_ReturnsNullWhenCleanExitOrNotDirty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OpenRendSessionTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var recoverySnapshot = SessionRecoveryManager.GetDefaultRecoveryPath(tempDir);
            File.WriteAllText(recoverySnapshot, "mock_snapshot");
            var sessionFile = SessionRecoveryManager.GetSessionFilePath(tempDir);

            // Case A: CleanExit is true
            var metaClean = new SessionMetadata
            {
                ProcessId = 99999999,
                IsDirty = true,
                CleanExit = true,
                RecoverySnapshotPath = recoverySnapshot
            };
            File.WriteAllText(sessionFile, JsonSerializer.Serialize(metaClean));
            Assert.Null(SessionRecoveryManager.CheckCrashRecovery(tempDir));

            // Case B: IsDirty is false
            var metaNotDirty = new SessionMetadata
            {
                ProcessId = 99999999,
                IsDirty = false,
                CleanExit = false,
                RecoverySnapshotPath = recoverySnapshot
            };
            File.WriteAllText(sessionFile, JsonSerializer.Serialize(metaNotDirty));
            Assert.Null(SessionRecoveryManager.CheckCrashRecovery(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SessionRecoveryManager_RecordCleanExit_RemovesRecoverySnapshotAndSession()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OpenRendSessionTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            SessionRecoveryManager.StartSession(tempDir, @"C:\Projects\my_edit.openrend");
            var recoverySnapshot = SessionRecoveryManager.GetDefaultRecoveryPath(tempDir);
            File.WriteAllText(recoverySnapshot, "mock_snapshot");
            var sessionFile = SessionRecoveryManager.GetSessionFilePath(tempDir);

            Assert.True(File.Exists(sessionFile));
            Assert.True(File.Exists(recoverySnapshot));

            // Record clean exit must discard recovery snapshot and clean up session
            SessionRecoveryManager.RecordCleanExit(tempDir);

            Assert.False(File.Exists(sessionFile));
            Assert.False(File.Exists(recoverySnapshot));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectStore_Save_Synchronous_WritesManifestAtomically()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OpenRendSessionTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var tempFile = Path.Combine(tempDir, "project.openrend");
            var media = new MediaAsset { Id = Guid.NewGuid(), Path = Path.Combine(tempDir, "media.mp4"), Width = 1920, Height = 1080, HasVideo = true };
            var project = new ProjectDocument
            {
                Name = "SyncSaveTest",
                Media = [media],
                Tracks = [new() { Name = "V1", Kind = TrackKind.Video, Clips = [new() { MediaId = media.Id, Name = "Clip1", DurationFrames = 100 }] }]
            };

            ProjectStore.Save(project, tempFile);
            Assert.True(File.Exists(tempFile));

            var loaded = await ProjectStore.LoadAsync(tempFile);
            Assert.Equal("SyncSaveTest", loaded.Name);
            Assert.Single(loaded.Media);
            Assert.Single(loaded.Tracks);
            Assert.Single(loaded.Tracks[0].Clips);
            Assert.Equal("Clip1", loaded.Tracks[0].Clips[0].Name);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SessionRecoveryManager_CheckCrashRecovery_DetectsUncleanDeadSession()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OpenRendSessionTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var recoverySnapshot = SessionRecoveryManager.GetDefaultRecoveryPath(tempDir);
            File.WriteAllText(recoverySnapshot, "mock_project_payload");

            var sessionFile = SessionRecoveryManager.GetSessionFilePath(tempDir);
            // Use a PID that does not exist
            var deadMeta = new SessionMetadata
            {
                ProcessId = 99999999,
                IsDirty = true,
                CleanExit = false,
                ProjectPath = @"C:\Edits\feature.openrend",
                RecoverySnapshotPath = recoverySnapshot
            };
            File.WriteAllText(sessionFile, JsonSerializer.Serialize(deadMeta));

            var candidate = SessionRecoveryManager.CheckCrashRecovery(tempDir);
            Assert.NotNull(candidate);
            Assert.Equal(recoverySnapshot, candidate.SnapshotPath);
            Assert.Equal(@"C:\Edits\feature.openrend", candidate.OriginalProjectPath);

            // Discarding recovery cleans up files
            SessionRecoveryManager.DiscardRecovery(tempDir);
            Assert.False(File.Exists(recoverySnapshot));
            Assert.False(File.Exists(sessionFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void CrashRecoveryWindow_MatchesVegasProDialogPrompt() => RunSta(() =>
    {
        var candidate = new CrashRecoveryCandidate(
            @"C:\Temp\recovery.openrend",
            @"C:\Media\commercial.openrend",
            DateTimeOffset.UtcNow);

        var win = new CrashRecoveryWindow(candidate);
        Assert.Equal("OpenRend", win.Title);

        var grid = Assert.IsType<Grid>(win.Content);
        var contentGrid = Assert.IsType<Grid>(grid.Children[0]);

        var textStack = Assert.IsType<StackPanel>(contentGrid.Children[1]);
        var mainPrompt = Assert.IsType<TextBlock>(textStack.Children[0]);

        // Vegas Pro verbatim prompt text
        Assert.Contains("The last session did not complete properly.", mainPrompt.Text);
        Assert.Contains("You can restore the autosaved version of the project that was opened", mainPrompt.Text);
        Assert.Contains("Would you like to restore the autosaved project?", mainPrompt.Text);

        var targetInfo = Assert.IsType<TextBlock>(textStack.Children[1]);
        Assert.Contains("commercial.openrend", targetInfo.Text);

        var btnPanel = Assert.IsType<StackPanel>(grid.Children[1]);
        Assert.Equal(2, btnPanel.Children.Count);

        var yesBtn = Assert.IsType<Button>(btnPanel.Children[0]);
        var noBtn = Assert.IsType<Button>(btnPanel.Children[1]);

        Assert.Equal("_Yes", yesBtn.Content);
        Assert.True(yesBtn.IsDefault);

        Assert.Equal("_No", noBtn.Content);
        Assert.True(noBtn.IsCancel);
    });

    [Fact]
    public void EditingPreferencesWindow_GeneralTab_RendersAndUpdatesAutoSave() => RunSta(() =>
    {
        var editing = new EditingPreferences { CutOverlapSeconds = 0.5 };
        var general = new GeneralPreferences
        {
            EnableAutoSave = true,
            AutoSaveIntervalMinutes = 15,
            CreateProjectBackups = true,
            UseProjectDirectoryForAutoSave = false
        };

        var win = new EditingPreferencesWindow(editing, general);
        var dock = Assert.IsType<DockPanel>(win.Content);

        // TabControl has General, Editing, and Updates
        var tabControl = Assert.IsType<TabControl>(dock.Children[2]);
        Assert.Equal(3, tabControl.Items.Count);

        var tab0 = Assert.IsType<TabItem>(tabControl.Items[0]);
        var tab1 = Assert.IsType<TabItem>(tabControl.Items[1]);
        var tab2 = Assert.IsType<TabItem>(tabControl.Items[2]);

        Assert.Equal("General", tab0.Header);
        Assert.Equal("Editing", tab1.Header);
        Assert.Equal("Updates", tab2.Header);

        // Verify General Tab elements
        var generalGroup = Assert.IsType<GroupBox>(tab0.Content);
        var generalGrid = Assert.IsType<Grid>(generalGroup.Content);

        var autoSaveCheck = Assert.IsType<CheckBox>(generalGrid.Children[0]);
        Assert.True(autoSaveCheck.IsChecked);

        var backupsCheck = Assert.IsType<CheckBox>(generalGrid.Children[3]);
        Assert.True(backupsCheck.IsChecked);

        var useProjectDirCheck = Assert.IsType<CheckBox>(generalGrid.Children[4]);
        Assert.False(useProjectDirCheck.IsChecked);

        // Verify Updates Tab elements
        var updateGroup = Assert.IsType<GroupBox>(tab2.Content);
        var updateStack = Assert.IsType<StackPanel>(updateGroup.Content);
        var startupUpdatesCheck = Assert.IsType<CheckBox>(updateStack.Children[0]);
        Assert.True(startupUpdatesCheck.IsChecked);
    });
}

