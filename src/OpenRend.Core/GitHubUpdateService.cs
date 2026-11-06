using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OpenRend.Core;

public enum UpdateStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

public sealed record UpdateCheckResult
{
    public UpdateStatus Status { get; init; }
    public SemanticVersion CurrentVersion { get; init; }
    public SemanticVersion? LatestVersion { get; init; }
    public GitHubRelease? Release { get; init; }
    public string? ErrorMessage { get; init; }

    public bool IsUpdateAvailable => Status == UpdateStatus.UpdateAvailable;
}

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdatesAsync(SemanticVersion? currentVersionOverride = null, CancellationToken cancellationToken = default);
    Task<string> DownloadUpdateAsync(GitHubReleaseAsset asset, string targetDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class GitHubUpdateService : IUpdateService, IDisposable
{
    public const string DefaultRepository = "jamiejustcodes/OpenRend";

    private readonly HttpClient client;
    private readonly bool disposeClient;
    private readonly string repository;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GitHubUpdateService(string? repository = null, HttpClient? httpClient = null)
    {
        this.repository = repository
            ?? Environment.GetEnvironmentVariable("OPENREND_UPDATE_REPO")
            ?? DefaultRepository;

        if (httpClient != null)
        {
            client = httpClient;
            disposeClient = false;
        }
        else
        {
            client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            disposeClient = true;
        }

        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("OpenRend-Updater", AppVersion.CurrentVersionString));
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        SemanticVersion? currentVersionOverride = null,
        CancellationToken cancellationToken = default)
    {
        var current = currentVersionOverride ?? AppVersion.Current;

        try
        {
            // First attempt to get the latest release
            var latestUrl = $"https://api.github.com/repos/{repository}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, latestUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            GitHubRelease? release = null;
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Fall back to listing releases in case "latest" tag isn't marked
                var listUrl = $"https://api.github.com/repos/{repository}/releases?per_page=5";
                using var listRequest = new HttpRequestMessage(HttpMethod.Get, listUrl);
                listRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                using var listResponse = await client.SendAsync(listRequest, cancellationToken).ConfigureAwait(false);
                if (listResponse.IsSuccessStatusCode)
                {
                    var listJson = await listResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var list = JsonSerializer.Deserialize<List<GitHubRelease>>(listJson, JsonOptions);
                    release = list?.FirstOrDefault(r => !r.Draft && (!r.Prerelease || current.PreRelease != null));
                }
                else
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateStatus.Failed,
                        CurrentVersion = current,
                        ErrorMessage = $"GitHub API returned {(int)listResponse.StatusCode} ({listResponse.ReasonPhrase})"
                    };
                }
            }
            else
            {
                return new UpdateCheckResult
                {
                    Status = UpdateStatus.Failed,
                    CurrentVersion = current,
                    ErrorMessage = $"GitHub API returned {(int)response.StatusCode} ({response.ReasonPhrase})"
                };
            }

            if (release == null || release.ParsedVersion == null)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateStatus.Failed,
                    CurrentVersion = current,
                    ErrorMessage = "No valid release information found on GitHub."
                };
            }

            var latestVersion = release.ParsedVersion.Value;
            if (latestVersion.IsNewerThan(current))
            {
                return new UpdateCheckResult
                {
                    Status = UpdateStatus.UpdateAvailable,
                    CurrentVersion = current,
                    LatestVersion = latestVersion,
                    Release = release
                };
            }

            return new UpdateCheckResult
            {
                Status = UpdateStatus.UpToDate,
                CurrentVersion = current,
                LatestVersion = latestVersion,
                Release = release
            };
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult
            {
                Status = UpdateStatus.Failed,
                CurrentVersion = current,
                ErrorMessage = "Update check was cancelled or timed out."
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                Status = UpdateStatus.Failed,
                CurrentVersion = current,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<string> DownloadUpdateAsync(
        GitHubReleaseAsset asset,
        string targetDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, asset.Name);

        using var response = await client.GetAsync(
            asset.BrowserDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            totalRead += bytesRead;

            if (totalBytes > 0 && progress != null)
            {
                progress.Report(Math.Clamp((double)totalRead / totalBytes, 0.0, 1.0));
            }
        }

        progress?.Report(1.0);
        return targetPath;
    }

    /// <summary>
    /// Launches the update mechanism (Installer or Updater script) and gracefully exits the current process.
    /// This updates OpenRend in place without deleting user data or settings.
    /// </summary>
    public static void LaunchUpdateInstaller(string updateFilePath, int currentProcessId, string applicationDirectory)
    {
        var isExe = updateFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        if (isExe)
        {
            // Execute the Inno Setup installer. It will prompt/update the existing install directory in-place.
            var startInfo = new ProcessStartInfo
            {
                FileName = updateFilePath,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        else if (updateFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            // Extract the zip to a staging directory, then launch a PowerShell script to replace files after process exit
            var stagingDir = Path.Combine(Path.GetDirectoryName(updateFilePath)!, "staging_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDir);
            ZipFile.ExtractToDirectory(updateFilePath, stagingDir, overwriteFiles: true);

            var scriptPath = Path.Combine(Path.GetDirectoryName(updateFilePath)!, "apply_update.ps1");
            var exeName = "OpenRend.exe";

            var scriptContent = $@"
param($waitPid, $source, $target, $exe)
Wait-Process -Id $waitPid -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600
Copy-Item -Path ""$source\*"" -Destination $target -Recurse -Force
Start-Process (Join-Path $target $exe)
Start-Sleep -Seconds 2
Remove-Item -Path $source -Recurse -Force -ErrorAction SilentlyContinue
";
            File.WriteAllText(scriptPath, scriptContent);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\" -waitPid {currentProcessId} -source \"{stagingDir}\" -target \"{applicationDirectory}\" -exe \"{exeName}\"",
                UseShellExecute = true,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        else
        {
            throw new NotSupportedException($"Unsupported update asset format: {Path.GetExtension(updateFilePath)}");
        }
    }

    public void Dispose()
    {
        if (disposeClient)
        {
            client.Dispose();
        }
    }
}
