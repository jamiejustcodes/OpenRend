using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenRend.Core;

namespace OpenRend.Project;

public enum ProjectAspect { Widescreen, Fullscreen, Portrait, Square, Scope }
public enum ProjectResolution { HD, FullHD, UHD }

public sealed record NewProjectSettings
{
    public ProjectAspect Aspect { get; init; } = ProjectAspect.Widescreen;
    public ProjectResolution Resolution { get; init; } = ProjectResolution.FullHD;
    public int FramesPerSecond { get; init; } = 30;
    public bool FractionalRate { get; init; } = true;
    public static IReadOnlyList<int> FrameRates { get; } = Array.AsReadOnly(new[] { 24, 25, 30, 48, 50, 60, 120 });

    public void Validate()
    {
        if (!Enum.IsDefined(Aspect) || !Enum.IsDefined(Resolution) || !FrameRates.Contains(FramesPerSecond))
            throw new InvalidDataException("New project settings are invalid.");
    }

    public Rational Rate
    {
        get { Validate(); return new Rational(FramesPerSecond * 1000, FractionalRate ? 1001 : 1000); }
    }

    public (int Width, int Height) Dimensions
    {
        get
        {
            Validate();
            return (Aspect, Resolution) switch
            {
                (ProjectAspect.Widescreen, ProjectResolution.HD) => (1280, 720),
                (ProjectAspect.Widescreen, ProjectResolution.FullHD) => (1920, 1080),
                (ProjectAspect.Widescreen, ProjectResolution.UHD) => (3840, 2160),
                (ProjectAspect.Fullscreen, ProjectResolution.HD) => (1280, 960),
                (ProjectAspect.Fullscreen, ProjectResolution.FullHD) => (1920, 1440),
                (ProjectAspect.Fullscreen, ProjectResolution.UHD) => (4096, 3072),
                (ProjectAspect.Portrait, ProjectResolution.HD) => (720, 1280),
                (ProjectAspect.Portrait, ProjectResolution.FullHD) => (1080, 1920),
                (ProjectAspect.Portrait, ProjectResolution.UHD) => (2160, 3840),
                (ProjectAspect.Square, ProjectResolution.HD) => (720, 720),
                (ProjectAspect.Square, ProjectResolution.FullHD) => (1080, 1080),
                (ProjectAspect.Square, ProjectResolution.UHD) => (2160, 2160),
                (ProjectAspect.Scope, ProjectResolution.HD) => (1280, 536),
                (ProjectAspect.Scope, ProjectResolution.FullHD) => (2048, 858),
                (ProjectAspect.Scope, ProjectResolution.UHD) => (4096, 1716),
                _ => throw new InvalidDataException("New project dimensions are unavailable.")
            };
        }
    }

    public string ResolutionLabel(ProjectResolution resolution)
    {
        var (width, height) = (this with { Resolution = resolution }).Dimensions;
        return $"{resolution} ({width} x {height})";
    }

    public string FrameRateLabel(int fps) => $"{fps}p ({(this with { FramesPerSecond = fps }).Rate.Value.ToString("0.000", CultureInfo.InvariantCulture)})";

    public ProjectDocument CreateProject()
    {
        var (width, height) = Dimensions;
        var project = new ProjectDocument { Width = width, Height = height, FrameRate = Rate };
        project.Validate();
        return project;
    }
}

public sealed record WelcomePreferences
{
    public bool ShowAtStartup { get; init; } = true;
    public NewProjectSettings NewProject { get; init; } = new();
    public string[] RecentProjects { get; init; } = [];

    public void Validate()
    {
        if (NewProject is null || RecentProjects is null || RecentProjects.Length > 20 ||
            RecentProjects.Any(path => string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)))
            throw new InvalidDataException("Welcome preferences are invalid.");
        NewProject.Validate();
    }

    public WelcomePreferences WithRecentProject(string path) => this with
    {
        RecentProjects = new[] { Path.GetFullPath(path) }.Concat(RecentProjects)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray()
    };
}

public static class WelcomePreferencesStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static WelcomePreferences Load(string path)
    {
        if (!File.Exists(path)) return new();
        WelcomePreferences settings;
        try { settings = JsonSerializer.Deserialize<WelcomePreferences>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Welcome preferences are empty."); }
        catch (JsonException error) { throw new InvalidDataException("Welcome preferences JSON is invalid.", error); }
        settings.Validate();
        return settings;
    }

    public static void Save(WelcomePreferences settings, string path)
    {
        settings.Validate();
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
