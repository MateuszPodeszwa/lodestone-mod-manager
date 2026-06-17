using Lodestone.Domain;

namespace Lodestone.Application.Tests;

/// <summary>Concise builders for installed content used across the application tests.</summary>
internal static class Make
{
    public static InstalledContent Mod(
        string id,
        string? name = null,
        bool enabled = true,
        Loader loader = Loader.Fabric,
        string[]? versions = null,
        Dependency[]? deps = null,
        string[]? provides = null,
        string? projectId = null,
        bool isLibrary = false,
        string? version = null,
        string[]? categories = null)
        => new(id, name ?? id, ContentType.Mod)
        {
            Enabled = enabled,
            Loader = loader,
            Version = version ?? "1.0.0",
            GameVersions = (versions ?? []).Select(GameVersion.Parse).ToList(),
            Dependencies = deps ?? [],
            ProvidedIds = provides ?? [id],
            ProjectId = projectId,
            IsLibrary = isLibrary,
            Categories = categories ?? [],
            Source = projectId is null ? "local" : "modrinth",
        };

    public static InstalledContent Pack(string id, string? name = null, bool enabled = true, string[]? versions = null, string[]? categories = null)
        => new(id, name ?? id, ContentType.ResourcePack)
        {
            Enabled = enabled,
            Loader = Loader.None,
            GameVersions = (versions ?? []).Select(GameVersion.Parse).ToList(),
            Categories = categories ?? [],
        };

    public static Dependency Requires(string id, string? range = null) => new(id, DependencyKind.Required, VersionRange: range);

    public static Dependency Breaks(string id) => new(id, DependencyKind.Incompatible);
}
