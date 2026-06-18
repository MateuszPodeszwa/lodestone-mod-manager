using Lodestone.Domain.Common;

namespace Lodestone.Domain;

/// <summary>
/// A piece of content present in the user's library. This is the aggregate the app reasons about:
/// it knows which game versions it targets, which loader it needs, what it depends on, and which
/// mod-ids it itself provides (used to resolve other items' dependencies).
/// </summary>
public sealed class InstalledContent
{
    public InstalledContent(string id, string name, ContentType type)
    {
        Id = Guard.NotNullOrWhiteSpace(id);
        Name = Guard.NotNullOrWhiteSpace(name);
        Type = type;
    }

    /// <summary>Stable identity — a source project id, or a generated slug for local files.</summary>
    public string Id { get; }

    public string Name { get; set; }

    public ContentType Type { get; }

    public string Author { get; set; } = "Unknown";

    /// <summary>Catalog icon URL (Modrinth/CurseForge) shown in My Content; null for local files.</summary>
    public string? IconUrl { get; set; }

    public string Version { get; set; } = "1.0.0";

    /// <summary><see cref="Loader.None"/> for packs/shaders.</summary>
    public Loader Loader { get; set; } = Loader.None;

    /// <summary>Game versions this item declares support for.</summary>
    public IReadOnlyList<GameVersion> GameVersions { get; set; } = [];

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The user deliberately turned this item off inside its own profile (via the My Content toggle), as
    /// opposed to it being disabled because a profile switch set it aside. A profile switch honors this:
    /// a user-disabled mod stays off even when it belongs to the activated profile, so the choice survives
    /// switching away and back. Cleared the moment the user enables it again.
    /// </summary>
    public bool UserDisabled { get; set; }

    public double SizeMb { get; set; }

    /// <summary>Source project id (e.g. Modrinth) when the item came from a catalog; otherwise null.</summary>
    public string? ProjectId { get; set; }

    /// <summary>Origin: <c>modrinth</c>, <c>curseforge</c> or <c>local</c>.</summary>
    public string Source { get; set; } = "local";

    /// <summary>The on-disk file name (e.g. <c>sodium-fabric-0.5.8.jar</c>).</summary>
    public string? FileName { get; set; }

    /// <summary>SHA-512 of the installed file, used for update/duplicate detection and verification.</summary>
    public string? Sha512 { get; set; }

    /// <summary>Relationships this item declares (required libs, conflicts, etc.).</summary>
    public IReadOnlyList<Dependency> Dependencies { get; set; } = [];

    /// <summary>Mod-ids/slugs this item itself provides, so other items' dependencies can resolve to it.</summary>
    public IReadOnlyList<string> ProvidedIds { get; set; } = [];

    /// <summary>True when this is a support library (e.g. Fabric API) — used to flag unused libraries.</summary>
    public bool IsLibrary { get; set; }

    /// <summary>Catalog categories this item belongs to (e.g. <c>optimization</c>, <c>library</c>), lower-cased
    /// source slugs. Empty for local/adopted files whose metadata doesn't declare any — those read as
    /// "uncategorized" and drive the My Content category filter.</summary>
    public IReadOnlyList<string> Categories { get; set; } = [];

    public bool UpdateAvailable { get; set; }

    /// <summary>True if this item declares support for <paramref name="version"/>.</summary>
    public bool SupportsVersion(GameVersion version) => GameVersions.Any(v => v.Equals(version));

    /// <summary>
    /// True when this content belongs to the given loader's profile. Loader-independent content (resource
    /// packs, shaders) always matches; a mod matches only when its <see cref="Loader"/> is the active one,
    /// so a mod installed for a different loader isn't treated as installed for the current profile.
    /// </summary>
    public bool MatchesLoaderProfile(Loader activeLoader) => !Type.UsesLoader() || Loader == activeLoader;

    /// <summary>Whether any identifier (id, project id or provided id) matches <paramref name="identifier"/>.</summary>
    public bool Provides(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        if (string.Equals(Id, identifier, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ProjectId, identifier, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ProvidedIds.Any(p => string.Equals(p, identifier, StringComparison.OrdinalIgnoreCase));
    }
}
