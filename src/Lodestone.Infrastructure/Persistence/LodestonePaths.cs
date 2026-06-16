namespace Lodestone.Infrastructure.Persistence;

/// <summary>
/// Canonical on-disk locations for Lodestone's own data (see docs/ARCHITECTURE.md). Centralised so
/// every store agrees, and overridable in tests by passing explicit paths to the stores.
/// </summary>
public static class LodestonePaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lodestone");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string LibraryFile => Path.Combine(Root, "library.json");

    public static string EntitlementsFile => Path.Combine(Root, "entitlements.json");

    public static string LogsDirectory => Path.Combine(Root, "logs");

    public static string TrashDirectory => Path.Combine(Root, "trash");

    public static string CacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lodestone", "cache");
}
