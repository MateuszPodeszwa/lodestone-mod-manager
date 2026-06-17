using System.Text.Json;
using System.Text.RegularExpressions;
using Lodestone.Application.Abstractions;
using Lodestone.Application.Settings;
using Lodestone.Domain;

namespace Lodestone.Infrastructure.FileSystem;

/// <summary>
/// Reads the launcher profiles installed under <c>&lt;game&gt;/versions/</c>. Each entry has a
/// <c>&lt;id&gt;.json</c> manifest: a vanilla version's <c>id</c> is the version itself (e.g.
/// <c>1.21.4</c>); a modded profile carries <c>inheritsFrom</c> naming its base version, and its id or
/// folder name identifies the loader (<c>fabric-loader-…</c>, <c>quilt-loader-…</c>,
/// <c>1.20.1-forge-…</c>, <c>neoforge-…</c>). We map every folder to its base game version + loader,
/// deduping and sorting newest-first. When a manifest is missing/unreadable we fall back to the folder
/// name (Fabric/Quilt fold the base version into the name as a suffix).
/// </summary>
public sealed partial class MinecraftGameInventory : IGameInventory
{
    private static readonly Regex LoaderFolder = BuildLoaderFolderPattern();
    private static readonly Regex Snapshot = BuildSnapshotPattern();

    private readonly ISettingsStore _settings;

    public MinecraftGameInventory(ISettingsStore settings) => _settings = settings;

    public bool IsVersionInstalled(GameVersion version) => InstalledVersions().Any(v => v.Equals(version));

    public bool IsLoaderInstalled(Loader loader, GameVersion version)
        => InstalledProfiles().Any(p => p.Loader == loader && p.GameVersion.Equals(version));

    public IReadOnlyList<GameVersion> InstalledVersions()
    {
        var byValue = new Dictionary<string, GameVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (LoaderProfile profile in ScanProfiles())
        {
            byValue.TryAdd(profile.GameVersion.Value, profile.GameVersion);
        }

        return byValue.Values.OrderByDescending(v => v).ToList();
    }

    public IReadOnlyList<LoaderProfile> InstalledProfiles()
    {
        // Dedupe by (version, loader): multiple loader builds for the same version collapse to one
        // profile, keeping the highest version-id so a later switch can activate the newest build.
        var byKey = new Dictionary<(string Version, Loader Loader), LoaderProfile>();
        foreach (LoaderProfile profile in ScanProfiles())
        {
            (string, Loader) key = (profile.GameVersion.Value, profile.Loader);
            if (!byKey.TryGetValue(key, out LoaderProfile? existing) ||
                string.CompareOrdinal(profile.VersionId, existing.VersionId) > 0)
            {
                byKey[key] = profile;
            }
        }

        return byKey.Values
            .OrderByDescending(p => p.GameVersion)
            .ThenBy(p => p.Loader)
            .ToList();
    }

    private IEnumerable<LoaderProfile> ScanProfiles()
    {
        string? game = _settings.Current.GameDirectory;
        if (string.IsNullOrWhiteSpace(game))
        {
            yield break;
        }

        string versions = Path.Combine(game, "versions");
        if (!Directory.Exists(versions))
        {
            yield break;
        }

        foreach (string directory in Directory.EnumerateDirectories(versions))
        {
            string folder = Path.GetFileName(directory);
            LoaderProfile? profile = ResolveProfile(directory, folder);
            if (profile is not null)
            {
                yield return profile;
            }
        }
    }

    // Prefer the manifest (inheritsFrom for modded, id for vanilla) for the base version, and classify
    // the loader from the id/folder name. Fall back to the folder name when there's no usable manifest.
    private static LoaderProfile? ResolveProfile(string directory, string folder)
    {
        string? id = null;
        GameVersion? baseVersion = null;

        string manifest = Path.Combine(directory, folder + ".json");
        if (File.Exists(manifest))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifest));
                JsonElement root = doc.RootElement;
                id = TryString(root, "id");
                baseVersion = PlausibleVersion(TryString(root, "inheritsFrom")) ?? PlausibleVersion(id);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Unreadable/corrupt manifest — fall through to the folder-name heuristic.
            }
        }

        if (baseVersion is null)
        {
            // No usable manifest: Fabric/Quilt fold the base version into the folder name as a suffix;
            // anything else is a vanilla folder named for the version itself.
            Match loader = LoaderFolder.Match(folder);
            baseVersion = loader.Success ? PlausibleVersion(loader.Groups["mc"].Value) : PlausibleVersion(folder);
        }

        return baseVersion is null
            ? null
            : new LoaderProfile(baseVersion, DetectLoader(id ?? string.Empty, folder), folder);
    }

    // Identify the loader from the manifest id / folder name. NeoForge is checked before Forge because
    // the string "neoforge" contains "forge".
    private static Loader DetectLoader(string id, string folder)
    {
        string hay = (id + " " + folder).ToLowerInvariant();
        if (hay.Contains("quilt"))
        {
            return Loader.Quilt;
        }

        if (hay.Contains("fabric"))
        {
            return Loader.Fabric;
        }

        if (hay.Contains("neoforge"))
        {
            return Loader.NeoForge;
        }

        return hay.Contains("forge") ? Loader.Forge : Loader.None;
    }

    private static string? TryString(JsonElement root, string property)
        => root.ValueKind == JsonValueKind.Object &&
           root.TryGetProperty(property, out JsonElement el) &&
           el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    // GameVersion.Create accepts any non-blank string as a snapshot, so guard against treating a loader
    // folder name (e.g. "fabric-loader-0.16.5-1.21.4") as a version: accept only releases and snapshots.
    private static GameVersion? PlausibleVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        GameVersion? version = GameVersion.Create(raw).Match<GameVersion?>(v => v, _ => null);
        return version is not null && (version.IsRelease || Snapshot.IsMatch(version.Value)) ? version : null;
    }

    [GeneratedRegex(@"^(?:fabric|quilt)-loader-.+-(?<mc>\d+(?:\.\d+){1,2}|\d{2}w\d{2}[a-z])$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildLoaderFolderPattern();

    [GeneratedRegex(@"^\d{2}w\d{2}[a-z]$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildSnapshotPattern();
}
