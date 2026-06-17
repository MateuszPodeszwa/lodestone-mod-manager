using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lodestone.Application.Abstractions;
using Lodestone.Application.Catalog;
using Lodestone.Application.Settings;
using Lodestone.Domain;
using Lodestone.Domain.Common;

namespace Lodestone.Infrastructure.Loaders;

/// <summary>
/// Installs Fabric or Quilt by fetching a launcher profile from their meta APIs and writing it into
/// <c>versions/</c> plus a <c>launcher_profiles.json</c> entry — exactly what their official installers do,
/// but without a Java step (libraries are fetched by the launcher on first run). Forge/NeoForge are
/// reported unsupported because they need their own Java installers.
/// </summary>
public sealed class MetaLoaderInstaller : ILoaderInstaller
{
    private readonly HttpClient _http;
    private readonly ISettingsStore _settings;
    private readonly IGameLocator _locator;
    private readonly IGameInventory _inventory;

    public MetaLoaderInstaller(HttpClient http, ISettingsStore settings, IGameLocator locator, IGameInventory inventory)
    {
        _http = http;
        _settings = settings;
        _locator = locator;
        _inventory = inventory;
    }

    public bool Supports(Loader loader) => loader is Loader.Fabric or Loader.Quilt;

    public bool IsInstalled(Loader loader, GameVersion gameVersion) => InstalledVersion(loader, gameVersion) is not null;

    public string? InstalledVersion(Loader loader, GameVersion gameVersion)
    {
        string? game = _settings.Current.GameDirectory;
        if (string.IsNullOrWhiteSpace(game) || !Supports(loader))
        {
            return null;
        }

        string versions = Path.Combine(game, "versions");
        if (!Directory.Exists(versions))
        {
            return null;
        }

        string prefix = loader == Loader.Fabric ? "fabric-loader-" : "quilt-loader-";
        string suffix = "-" + gameVersion.Value;
        string? newest = null;
        foreach (string directory in Directory.EnumerateDirectories(versions))
        {
            string name = Path.GetFileName(directory);
            if (name.Length <= prefix.Length + suffix.Length ||
                !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string version = name[prefix.Length..^suffix.Length];
            if (newest is null || VersionComparer.IsNewer(version, newest))
            {
                newest = version;
            }
        }

        return newest;
    }

    public async Task<Result> EnsureInstalledAsync(Loader loader, GameVersion gameVersion, CancellationToken ct = default)
    {
        if (!Supports(loader))
        {
            return Result.Failure("loader.unsupported",
                $"{loader.ToDisplayName()} must be installed with its official installer; Lodestone installs Fabric and Quilt directly.");
        }

        string? game = _settings.Current.GameDirectory;
        if (string.IsNullOrWhiteSpace(game) || !_locator.IsValid(game))
        {
            return Result.Failure("game.dir_missing", "Set your Minecraft folder before installing a loader.");
        }

        if (IsInstalled(loader, gameVersion))
        {
            return Result.Success();
        }

        Result<LoaderUpdate> installed = await InstallLatestAsync(loader, gameVersion, game, ct).ConfigureAwait(false);
        return installed.IsSuccess ? Result.Success() : Result.Failure(installed.Error);
    }

    public async Task<Result<LoaderUpdate>> UpdateAsync(Loader loader, GameVersion gameVersion, CancellationToken ct = default)
    {
        if (!Supports(loader))
        {
            return Result.Failure<LoaderUpdate>("loader.unsupported",
                $"{loader.ToDisplayName()} must be updated with its official installer; Lodestone manages Fabric and Quilt directly.");
        }

        string? game = _settings.Current.GameDirectory;
        if (string.IsNullOrWhiteSpace(game) || !_locator.IsValid(game))
        {
            return Result.Failure<LoaderUpdate>("game.dir_missing", "Set your Minecraft folder before updating a loader.");
        }

        return await InstallLatestAsync(loader, gameVersion, game, ct).ConfigureAwait(false);
    }

    // Resolves the latest stable build and writes it, unless an equal-or-newer one is already present.
    // A superseded build is left in place (as the official installers do), so launcher profiles pointing
    // at it keep working; InstalledVersion always reports the newest.
    private async Task<Result<LoaderUpdate>> InstallLatestAsync(Loader loader, GameVersion gameVersion, string gameDir, CancellationToken ct)
    {
        // A loader profile inherits from the vanilla version; without that base installed the profile
        // can't launch, so refuse rather than write a dangling entry the launcher will choke on.
        if (!_inventory.IsVersionInstalled(gameVersion))
        {
            return Result.Failure<LoaderUpdate>("loader.base_missing",
                $"Minecraft {gameVersion} isn't installed yet — install it in your launcher first, then add {loader.ToDisplayName()}.");
        }

        try
        {
            (string metaBase, string versionsPath) = MetaEndpoints(loader);

            string? loaderVersion = await ResolveLatestLoaderAsync(metaBase, versionsPath, gameVersion.Value, ct).ConfigureAwait(false);
            if (loaderVersion is null)
            {
                return Result.Failure<LoaderUpdate>("loader.no_version", $"No {loader.ToDisplayName()} build is available for {gameVersion}.");
            }

            string? current = InstalledVersion(loader, gameVersion);
            if (current is not null && !VersionComparer.IsNewer(loaderVersion, current))
            {
                return new LoaderUpdate(Changed: false, current, current);
            }

            string profileJson = await _http
                .GetStringAsync($"{metaBase}/{versionsPath}/{gameVersion.Value}/{loaderVersion}/profile/json", ct)
                .ConfigureAwait(false);

            using JsonDocument doc = JsonDocument.Parse(profileJson);
            if (!doc.RootElement.TryGetProperty("id", out JsonElement idEl) || idEl.GetString() is not { Length: > 0 } versionId)
            {
                return Result.Failure<LoaderUpdate>("loader.bad_profile", "The loader profile was missing an id.");
            }

            WriteVersionProfile(gameDir, versionId, profileJson);
            UpdateLauncherProfiles(gameDir, versionId, $"{loader.ToDisplayName()} {gameVersion.Value}");
            return new LoaderUpdate(Changed: true, current, loaderVersion);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<LoaderUpdate>("loader.network", ex.Message);
        }
        catch (JsonException ex)
        {
            return Result.Failure<LoaderUpdate>("loader.parse", ex.Message);
        }
        catch (IOException ex)
        {
            return Result.Failure<LoaderUpdate>("loader.io", ex.Message);
        }
    }

    public Task<Result<int>> RemoveManagedAsync(CancellationToken ct = default)
    {
        string? game = _settings.Current.GameDirectory;
        if (string.IsNullOrWhiteSpace(game) || !_locator.IsValid(game))
        {
            return Task.FromResult(Result.Failure<int>("game.dir_missing", "Set your Minecraft folder first."));
        }

        string versions = Path.Combine(game, "versions");
        if (!Directory.Exists(versions))
        {
            return Task.FromResult(Result.Success(0));
        }

        var removedIds = new List<string>();
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(versions))
            {
                string name = Path.GetFileName(directory);
                if (name.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("quilt-loader-", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(directory, recursive: true);
                    removedIds.Add(name);
                }
            }

            RemoveLauncherProfiles(game, removedIds);
        }
        catch (IOException ex)
        {
            return Task.FromResult(Result.Failure<int>("loader.io", ex.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(Result.Failure<int>("loader.permission", "Lodestone doesn't have permission to remove those files."));
        }

        return Task.FromResult(Result.Success(removedIds.Count));
    }

    // Drops the launcher entries for the version-ids we just removed (keyed by id, with a lastVersionId
    // fallback), backing up launcher_profiles.json first — the user's own profiles are left alone.
    private static void RemoveLauncherProfiles(string gameDir, List<string> versionIds)
    {
        string path = Path.Combine(gameDir, "launcher_profiles.json");
        if (!File.Exists(path) || versionIds.Count == 0)
        {
            return;
        }

        File.Copy(path, path + ".bak", overwrite: true);
        if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root || root["profiles"] is not JsonObject profiles)
        {
            return;
        }

        foreach (string key in profiles.Select(p => p.Key).ToList())
        {
            string? lastVersionId = profiles[key]?["lastVersionId"]?.GetValue<string>();
            if (versionIds.Contains(key) || (lastVersionId is not null && versionIds.Contains(lastVersionId)))
            {
                profiles.Remove(key);
            }
        }

        string temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Replace(temp, path, null);
    }

    private static (string MetaBase, string VersionsPath) MetaEndpoints(Loader loader) => loader == Loader.Fabric
        ? ("https://meta.fabricmc.net", "v2/versions/loader")
        : ("https://meta.quiltmc.org", "v3/versions/loader");

    private async Task<string?> ResolveLatestLoaderAsync(string metaBase, string versionsPath, string game, CancellationToken ct)
    {
        string json = await _http.GetStringAsync($"{metaBase}/{versionsPath}/{game}", ct).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        // Prefer the first stable entry; the lists are newest-first.
        string? firstVersion = null;
        foreach (JsonElement entry in doc.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("loader", out JsonElement loaderEl) ||
                !loaderEl.TryGetProperty("version", out JsonElement verEl) ||
                verEl.GetString() is not { Length: > 0 } version)
            {
                continue;
            }

            firstVersion ??= version;
            bool stable = !loaderEl.TryGetProperty("stable", out JsonElement stableEl) || stableEl.ValueKind != JsonValueKind.False;
            if (stable)
            {
                return version;
            }
        }

        return firstVersion;
    }

    private static void WriteVersionProfile(string gameDir, string versionId, string profileJson)
    {
        string dir = Path.Combine(gameDir, "versions", versionId);
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, versionId + ".json");
        string temp = target + ".tmp";
        File.WriteAllText(temp, profileJson);
        if (File.Exists(target))
        {
            File.Replace(temp, target, null);
        }
        else
        {
            File.Move(temp, target);
        }
    }

    private static void UpdateLauncherProfiles(string gameDir, string versionId, string name)
    {
        string path = Path.Combine(gameDir, "launcher_profiles.json");

        JsonObject root;
        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", overwrite: true); // safety net before editing the launcher's file
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [];
        }
        else
        {
            root = [];
        }

        if (root["profiles"] is not JsonObject profiles)
        {
            profiles = [];
            root["profiles"] = profiles;
        }

        string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        profiles[versionId] = new JsonObject
        {
            ["name"] = name,
            ["type"] = "custom",
            ["created"] = now,
            ["lastUsed"] = now,
            ["lastVersionId"] = versionId,
            ["icon"] = "Furnace",
        };

        root["version"] ??= 3;

        string temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(path))
        {
            File.Replace(temp, path, null);
        }
        else
        {
            File.Move(temp, path);
        }
    }
}
