using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class UpdateServiceTests
{
    [Theory]
    [InlineData("0.1.0", 0, 1, 0, null, null)]
    [InlineData("v0.2.1", 0, 2, 1, null, null)]
    [InlineData("V1.0.0", 1, 0, 0, null, null)]
    [InlineData("0.2.0-beta.1", 0, 2, 0, "beta.1", null)]
    [InlineData("0.1.0+commit123", 0, 1, 0, null, "commit123")]
    [InlineData("v1.2.3-preview+git", 1, 2, 3, "preview", "git")]
    public void SemanticVersion_TryParse_ValidVersions(
        string input, int expectedMajor, int expectedMinor, int expectedPatch, string? expectedPre, string? expectedMeta)
    {
        Assert.True(SemanticVersion.TryParse(input, out var ver));
        Assert.Equal(expectedMajor, ver.Major);
        Assert.Equal(expectedMinor, ver.Minor);
        Assert.Equal(expectedPatch, ver.Patch);
        Assert.Equal(expectedPre, ver.PreRelease);
        Assert.Equal(expectedMeta, ver.BuildMetadata);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("invalid")]
    [InlineData("v-1.0.0")]
    [InlineData("1.2.3.4.5")]
    public void SemanticVersion_TryParse_InvalidVersions_ReturnsFalse(string input)
    {
        Assert.False(SemanticVersion.TryParse(input, out _));
    }

    [Fact]
    public void SemanticVersion_Comparison_OrdersCorrectly()
    {
        var v010 = SemanticVersion.Parse("0.1.0");
        var v011 = SemanticVersion.Parse("0.1.1");
        var v020 = SemanticVersion.Parse("0.2.0");
        var v100 = SemanticVersion.Parse("1.0.0");
        var v020Beta = SemanticVersion.Parse("0.2.0-beta");

        Assert.True(v011 > v010);
        Assert.True(v020 > v011);
        Assert.True(v100 > v020);
        Assert.True(v020 > v020Beta); // Final release is newer than pre-release
        Assert.True(v020Beta > v010);
        Assert.True(v020.IsNewerThan(v010));
        Assert.False(v010.IsNewerThan(v020));
        Assert.False(v010.IsNewerThan(v010));
    }

    [Fact]
    public void GitHubRelease_GetUpdateAsset_PrefersExeOverZip()
    {
        var release = new GitHubRelease
        {
            TagName = "v0.2.0",
            Assets =
            [
                new GitHubReleaseAsset { Name = "OpenRend-win-x64.zip", BrowserDownloadUrl = "http://example.com/zip" },
                new GitHubReleaseAsset { Name = "OpenRend-Setup-v0.2.0.exe", BrowserDownloadUrl = "http://example.com/exe" },
                new GitHubReleaseAsset { Name = "SHA256SUMS.txt", BrowserDownloadUrl = "http://example.com/txt" }
            ]
        };

        var asset = release.GetUpdateAsset();
        Assert.NotNull(asset);
        Assert.Equal("OpenRend-Setup-v0.2.0.exe", asset.Name);
    }

    [Fact]
    public void GitHubRelease_GetUpdateAsset_FallsBackToZipWhenNoExe()
    {
        var release = new GitHubRelease
        {
            TagName = "v0.2.0",
            Assets =
            [
                new GitHubReleaseAsset { Name = "SHA256SUMS.txt", BrowserDownloadUrl = "http://example.com/txt" },
                new GitHubReleaseAsset { Name = "OpenRend-win-x64.zip", BrowserDownloadUrl = "http://example.com/zip" }
            ]
        };

        var asset = release.GetUpdateAsset();
        Assert.NotNull(asset);
        Assert.Equal("OpenRend-win-x64.zip", asset.Name);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenNewerReleaseExists_ReturnsUpdateAvailable()
    {
        var releaseJson = JsonSerializer.Serialize(new GitHubRelease
        {
            TagName = "v0.2.0",
            Name = "OpenRend 0.2.0",
            Body = "- Added new feature\n- Performance improvements",
            HtmlUrl = "https://github.com/jamiejustcodes/OpenRend/releases/tag/v0.2.0",
            Assets =
            [
                new GitHubReleaseAsset { Name = "OpenRend-Setup-v0.2.0.exe", BrowserDownloadUrl = "https://example.com/setup.exe", Size = 50000000 }
            ]
        });

        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler);
        var service = new GitHubUpdateService("jamiejustcodes/OpenRend", client);

        var current = SemanticVersion.Parse("0.1.0");
        var result = await service.CheckForUpdatesAsync(current);

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.True(result.IsUpdateAvailable);
        Assert.NotNull(result.Release);
        Assert.Equal("v0.2.0", result.Release.TagName);
        Assert.Equal(SemanticVersion.Parse("0.2.0"), result.LatestVersion);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenOnLatestVersion_ReturnsUpToDate()
    {
        var releaseJson = JsonSerializer.Serialize(new GitHubRelease
        {
            TagName = "v0.1.0",
            Name = "OpenRend 0.1.0",
            HtmlUrl = "https://github.com/jamiejustcodes/OpenRend/releases/tag/v0.1.0"
        });

        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler);
        var service = new GitHubUpdateService("jamiejustcodes/OpenRend", client);

        var current = SemanticVersion.Parse("0.1.0");
        var result = await service.CheckForUpdatesAsync(current);

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.Release);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenHttpFails_ReturnsFailedWithErrorMessage()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            ReasonPhrase = "Internal Server Error"
        });

        var client = new HttpClient(handler);
        var service = new GitHubUpdateService("jamiejustcodes/OpenRend", client);

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("500", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadUpdateAsync_DownloadsContentAndReportsProgress()
    {
        var payload = Encoding.UTF8.GetBytes("Sample binary installer data payload for OpenRend");
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });

        var client = new HttpClient(handler);
        var service = new GitHubUpdateService("jamiejustcodes/OpenRend", client);

        var tempDir = Path.Combine(Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N"));
        try
        {
            var asset = new GitHubReleaseAsset
            {
                Name = "OpenRend-Setup-v0.2.0.exe",
                BrowserDownloadUrl = "https://example.com/setup.exe",
                Size = payload.Length
            };

            var reportedProgress = new List<double>();
            var progress = new Progress<double>(p => reportedProgress.Add(p));

            var path = await service.DownloadUpdateAsync(asset, tempDir, progress);

            Assert.True(File.Exists(path));
            Assert.Equal(payload, await File.ReadAllBytesAsync(path));
            Assert.Contains(reportedProgress, p => p > 0.0);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void GenerateExeUpdateScript_ProducesSilentInnoArgumentsAndProcessWait()
    {
        var script = GitHubUpdateService.GenerateExeUpdateScript(
            waitPid: 12345,
            installerPath: @"C:\Temp\OpenRend-Setup.exe",
            targetDir: @"C:\Users\Tester\AppData\Local\Programs\OpenRend\",
            exeName: "OpenRend.exe");

        Assert.Contains("WAIT_PID=12345", script);
        Assert.Contains(@"INSTALLER=C:\Temp\OpenRend-Setup.exe", script);
        Assert.Contains(@"TARGET_DIR=C:\Users\Tester\AppData\Local\Programs\OpenRend", script);
        Assert.DoesNotContain(@"OpenRend\", script); // Trailing slash stripped
        Assert.Contains("/VERYSILENT", script);
        Assert.Contains("/SUPPRESSMSGBOXES", script);
        Assert.Contains("/NORESTART", script);
        Assert.Contains("/SP-", script);
        Assert.Contains(@"/DIR=""%TARGET_DIR%""", script);
        Assert.Contains(@"start /wait """" ""%INSTALLER%""", script);
        Assert.Contains(@"start """" ""%TARGET_DIR%\%EXE_NAME%""", script);
    }

    [Fact]
    public void GenerateZipUpdateScript_ProducesExtractionAndRelaunchCommands()
    {
        var script = GitHubUpdateService.GenerateZipUpdateScript(
            waitPid: 54321,
            stagingDir: @"C:\Temp\staging",
            targetDir: @"C:\Users\Tester\AppData\Local\Programs\OpenRend\",
            exeName: "OpenRend.exe");

        Assert.Contains("54321", script);
        Assert.Contains("Copy-Item", script);
        Assert.Contains("Start-Process", script);
        Assert.Contains("OpenRend.exe", script);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage response;

        public MockHttpMessageHandler(HttpResponseMessage response)
        {
            this.response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}

