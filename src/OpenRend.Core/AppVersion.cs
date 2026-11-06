using System.Reflection;

namespace OpenRend.Core;

/// <summary>
/// Provides application version resolution and metadata for OpenRend.
/// </summary>
public static class AppVersion
{
    private static SemanticVersion? currentVersion;

    public static SemanticVersion Current
    {
        get => currentVersion ??= ResolveCurrentVersion();
        internal set => currentVersion = value; // For testing and override scenarios
    }

    public static string CurrentVersionString => Current.ToString();

    private static SemanticVersion ResolveCurrentVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;

        var infoAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (infoAttr != null && SemanticVersion.TryParse(infoAttr.InformationalVersion, out var infoVersion))
        {
            return infoVersion;
        }

        var fileAttr = asm.GetCustomAttribute<AssemblyFileVersionAttribute>();
        if (fileAttr != null && SemanticVersion.TryParse(fileAttr.Version, out var fileVersion))
        {
            return fileVersion;
        }

        var asmVersion = asm.GetName().Version;
        if (asmVersion != null)
        {
            return new SemanticVersion(asmVersion.Major, asmVersion.Minor, Math.Max(0, asmVersion.Build));
        }

        return new SemanticVersion(0, 1, 0);
    }
}

