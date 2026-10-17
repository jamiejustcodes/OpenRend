using OpenRend.Core;
using OpenRend.Project;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class EditingPreferencesTests
{
    [Fact]
    public void PreferencesRoundTripAndConvertSecondsAtTheProjectRate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenRendTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "editing.json");
        var settings = new EditingPreferences { CutOverlapSeconds = .5, CutOverlapAlignment = CutTransitionAlignment.BeforeCut };
        try
        {
            EditingPreferencesStore.Save(settings, path);
            var loaded = EditingPreferencesStore.Load(path);
            Assert.Equal(settings, loaded);
            Assert.Equal(15, loaded.OverlapFrames(new Rational(30000, 1001)));
            Assert.Equal(12, loaded.OverlapFrames(new Rational(24, 1)));
            Assert.Equal(1, (loaded with { CutOverlapSeconds = .001 }).OverlapFrames(new Rational(24, 1)));
            File.WriteAllText(path, "{broken");
            Assert.Throws<InvalidDataException>(() => EditingPreferencesStore.Load(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
