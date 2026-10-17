using OpenRend.Project;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class TransitionBrowserPreferencesTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(directory, "transitions.json");

    [Fact]
    public void MissingFileDefaultsToNotFavoriteWithoutCreatingPreferences()
    {
        Assert.False(TransitionBrowserPreferencesStore.Load(SettingsPath).FavoriteDissolve);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void FavoriteRoundTripsAndAnAtomicOverwriteClearsIt()
    {
        TransitionBrowserPreferencesStore.Save(new() { FavoriteDissolve = true }, SettingsPath);
        Assert.True(TransitionBrowserPreferencesStore.Load(SettingsPath).FavoriteDissolve);
        TransitionBrowserPreferencesStore.Save(new(), SettingsPath);
        Assert.False(TransitionBrowserPreferencesStore.Load(SettingsPath).FavoriteDissolve);
        Assert.Equal(new[] { SettingsPath }, Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"FavoriteDissolve\":\"true\"}")]
    [InlineData("{\"FavoriteDissolve\":1}")]
    [InlineData("{\"FavoriteDissolve\":null}")]
    public void CorruptOrWrongTypeJsonIsReportedWithoutOverwritingIt(string json)
    {
        Directory.CreateDirectory(directory); File.WriteAllText(SettingsPath, json);
        Assert.Throws<InvalidDataException>(() => TransitionBrowserPreferencesStore.Load(SettingsPath));
        Assert.Equal(json, File.ReadAllText(SettingsPath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void NullSavePreservesTheExistingDestinationAndCreatesNoTemporaryFile()
    {
        TransitionBrowserPreferencesStore.Save(new() { FavoriteDissolve = true }, SettingsPath);
        var original = File.ReadAllText(SettingsPath);
        Assert.Throws<ArgumentNullException>(() => TransitionBrowserPreferencesStore.Save(null!, SettingsPath));
        Assert.Equal(original, File.ReadAllText(SettingsPath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void FailedReplacementCleansItsTemporaryFileAndPreservesDestination()
    {
        Directory.CreateDirectory(SettingsPath);
        var sentinel = Path.Combine(SettingsPath, "keep.txt"); File.WriteAllText(sentinel, "preserved");
        var error = Record.Exception(() => TransitionBrowserPreferencesStore.Save(new(), SettingsPath));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal("preserved", File.ReadAllText(sentinel));
        Assert.Empty(Directory.GetFiles(directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

