using System.Diagnostics;
using System.IO;
using System.Text.Json;
using OpenRend.Core;
using OpenRend.Project;

namespace OpenRend.App;

public sealed class SessionMetadata
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public int ProcessId { get; set; } = Environment.ProcessId;
    public DateTimeOffset StartTimeUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastHeartbeatUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? ProjectPath { get; set; }
    public string RecoverySnapshotPath { get; set; } = string.Empty;
    public bool IsDirty { get; set; }
    public bool CleanExit { get; set; }
}

public sealed record CrashRecoveryCandidate(
    string SnapshotPath,
    string? OriginalProjectPath,
    DateTimeOffset SnapshotTimeUtc);

/// <summary>
/// Manages session persistence, active process sentinels, and abnormal termination recovery
/// matching Vegas Pro's crash recovery architecture.
/// </summary>
public static class SessionRecoveryManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string GetSessionFilePath(string dataDir) => Path.Combine(dataDir, "session.json");

    public static string GetDefaultRecoveryPath(string dataDir) => Path.Combine(dataDir, "recovery.openrend");

    public static CrashRecoveryCandidate? CheckCrashRecovery(string dataDir)
    {
        var sessionFile = GetSessionFilePath(dataDir);
        if (!File.Exists(sessionFile))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(sessionFile);
            var meta = JsonSerializer.Deserialize<SessionMetadata>(json);
            if (meta == null || meta.CleanExit) return null;

            // Check if previous process is still running
            if (meta.ProcessId != Environment.ProcessId && IsProcessRunning(meta.ProcessId))
            {
                // Another instance is currently alive
                return null;
            }

            var snapshot = string.IsNullOrEmpty(meta.RecoverySnapshotPath) ? GetDefaultRecoveryPath(dataDir) : meta.RecoverySnapshotPath;
            if (File.Exists(snapshot) && new FileInfo(snapshot).Length > 0 && meta.IsDirty)
            {
                return new CrashRecoveryCandidate(snapshot, meta.ProjectPath, meta.LastHeartbeatUtc);
            }
        }
        catch
        {
            // Corrupt session file; ignore
        }

        return null;
    }

    public static void StartSession(string dataDir, string? projectPath)
    {
        Directory.CreateDirectory(dataDir);
        var meta = new SessionMetadata
        {
            ProcessId = Environment.ProcessId,
            StartTimeUtc = DateTimeOffset.UtcNow,
            LastHeartbeatUtc = DateTimeOffset.UtcNow,
            ProjectPath = projectPath,
            RecoverySnapshotPath = GetDefaultRecoveryPath(dataDir),
            IsDirty = false,
            CleanExit = false
        };
        SaveSessionMetadata(dataDir, meta);
    }

    public static void RecordHeartbeat(string dataDir, bool isDirty, string? projectPath)
    {
        var meta = LoadSessionMetadata(dataDir) ?? new SessionMetadata
        {
            ProcessId = Environment.ProcessId,
            RecoverySnapshotPath = GetDefaultRecoveryPath(dataDir)
        };

        meta.LastHeartbeatUtc = DateTimeOffset.UtcNow;
        meta.IsDirty = isDirty;
        meta.ProjectPath = projectPath;
        meta.CleanExit = false;
        SaveSessionMetadata(dataDir, meta);
    }

    public static void RecordCleanExit(string dataDir)
    {
        var meta = LoadSessionMetadata(dataDir);
        if (meta != null)
        {
            meta.CleanExit = true;
            meta.IsDirty = false;
            meta.LastHeartbeatUtc = DateTimeOffset.UtcNow;
            SaveSessionMetadata(dataDir, meta);
        }

        // Clean up recovery snapshot and session file on normal exit
        DiscardRecovery(dataDir);
    }

    public static void DiscardRecovery(string dataDir)
    {
        try
        {
            var snapshot = GetDefaultRecoveryPath(dataDir);
            if (File.Exists(snapshot)) File.Delete(snapshot);
            var sessionFile = GetSessionFilePath(dataDir);
            if (File.Exists(sessionFile)) File.Delete(sessionFile);
        }
        catch { }
    }

    private static SessionMetadata? LoadSessionMetadata(string dataDir)
    {
        var sessionFile = GetSessionFilePath(dataDir);
        if (!File.Exists(sessionFile)) return null;
        try
        {
            var json = File.ReadAllText(sessionFile);
            return JsonSerializer.Deserialize<SessionMetadata>(json);
        }
        catch { return null; }
    }

    private static void SaveSessionMetadata(string dataDir, SessionMetadata meta)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            var sessionFile = GetSessionFilePath(dataDir);
            var temp = sessionFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(meta, JsonOptions));
            File.Move(temp, sessionFile, overwrite: true);
        }
        catch { }
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }
}

