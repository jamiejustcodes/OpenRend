using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class GeneralPreferencesAndBackupTests
{
    [Fact]
    public void GeneralPreferences_Defaults_MatchVegasProSettings()
    {
        var prefs = new GeneralPreferences();
        Assert.True(prefs.EnableAutoSave);
        Assert.Equal(5, prefs.AutoSaveIntervalMinutes);
        Assert.True(prefs.CreateProjectBackups);
        Assert.True(prefs.UseProjectDirectoryForAutoSave);
        Assert.True(prefs.CheckForUpdatesOnStartup);
    }

    [Fact]
    public void GeneralPreferences_Validation_EnforcesIntervalBounds()
    {
        var valid = new GeneralPreferences { AutoSaveIntervalMinutes = 15 };
        valid.Validate();

        var tooLow = new GeneralPreferences { AutoSaveIntervalMinutes = 0 };
        Assert.Throws<InvalidDataException>(() => tooLow.Validate());

        var tooHigh = new GeneralPreferences { AutoSaveIntervalMinutes = 121 };
        Assert.Throws<InvalidDataException>(() => tooHigh.Validate());
    }

    [Fact]
    public void GeneralPreferences_RoundTrip_PersistsAndRestores()
    {
        var dir = Path.Combine(Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "general.json");
        try
        {
            var original = new GeneralPreferences
            {
                EnableAutoSave = false,
                AutoSaveIntervalMinutes = 10,
                CreateProjectBackups = false,
                UseProjectDirectoryForAutoSave = false,
                CheckForUpdatesOnStartup = false
            };

            GeneralPreferencesStore.Save(original, file);
            var loaded = GeneralPreferencesStore.Load(file);

            Assert.Equal(original, loaded);

            File.WriteAllText(file, "{corrupt_json");
            Assert.Throws<InvalidDataException>(() => GeneralPreferencesStore.Load(file));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectStore_SaveWithBackup_CreatesBakFileWithPreviousState()
    {
        var dir = Path.Combine(Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N"));
        var projectFile = Path.Combine(dir, "my_project.openrend");
        var backupFile = ProjectStore.GetBackupPath(projectFile);

        try
        {
            Directory.CreateDirectory(dir);

            // Version 1
            var doc1 = new ProjectDocument();
            var media = new MediaAsset { Path = "test.mp4", HasVideo = true, DurationSeconds = 10 };
            doc1.Media.Add(media);
            var clip1 = new TimelineClip { MediaId = media.Id, StartFrame = 0, DurationFrames = 100 };
            var track1 = new TimelineTrack { Kind = TrackKind.Video };
            track1.Clips.Add(clip1);
            doc1.Tracks.Add(track1);

            await ProjectStore.SaveAsync(doc1, projectFile, createBackup: true);

            Assert.True(File.Exists(projectFile));
            // First save has no previous file to back up
            Assert.False(File.Exists(backupFile));

            // Version 2 (add another clip)
            var doc2 = doc1.DeepClone();
            doc2.Tracks[0].Clips.Add(new TimelineClip { MediaId = media.Id, StartFrame = 150, DurationFrames = 50 });

            await ProjectStore.SaveAsync(doc2, projectFile, createBackup: true);

            // Now backup must exist!
            Assert.True(File.Exists(backupFile));

            var loadedProject = await ProjectStore.LoadAsync(projectFile);
            var loadedBackup = await ProjectStore.LoadAsync(backupFile);

            Assert.Equal(2, loadedProject.Tracks[0].Clips.Count);
            Assert.Single(loadedBackup.Tracks[0].Clips);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
