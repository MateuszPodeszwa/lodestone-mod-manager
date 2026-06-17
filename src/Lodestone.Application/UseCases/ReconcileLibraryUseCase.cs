using System.IO;
using Lodestone.Application.Abstractions;
using Lodestone.Application.Common;
using Lodestone.Application.Settings;
using Lodestone.Domain;
using Lodestone.Domain.Common;

namespace Lodestone.Application.UseCases;

/// <summary>
/// Auto-discovery: scans the game's mods/resourcepacks/shaderpacks folders and imports any files that
/// aren't already in the library (e.g. mods the user dropped in manually, or an existing install Lodestone
/// is seeing for the first time). Purely additive — it never deletes records, so it's safe to run on every
/// start/refresh.
/// </summary>
public sealed class ReconcileLibraryUseCase
{
    private static readonly ContentType[] AllTypes = [ContentType.Mod, ContentType.ResourcePack, ContentType.Shader];
    private const string DisabledSuffix = ".disabled";

    private readonly IInstalledContentRepository _repository;
    private readonly IContentInstaller _installer;
    private readonly IArchiveMetadataReader _reader;
    private readonly ISettingsStore _settings;
    private readonly IGameLocator _locator;
    private readonly IGameInventory _inventory;

    public ReconcileLibraryUseCase(
        IInstalledContentRepository repository,
        IContentInstaller installer,
        IArchiveMetadataReader reader,
        ISettingsStore settings,
        IGameLocator locator,
        IGameInventory inventory)
    {
        _repository = repository;
        _installer = installer;
        _reader = reader;
        _settings = settings;
        _locator = locator;
        _inventory = inventory;
    }

    public async Task<Result<int>> ExecuteAsync(GameVersion? targetVersion, CancellationToken ct = default)
    {
        if (!_locator.IsValid(_settings.Current.GameDirectory))
        {
            return Result.Success(0);
        }

        IReadOnlyList<InstalledContent> all = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        var trackedBaseNames = all
            .Where(i => !string.IsNullOrWhiteSpace(i.FileName))
            .Select(i => BaseName(i.FileName!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The installed (version + loader) profiles, read once: used to attribute a mod to a version only
        // when there's exactly one profile for its loader (otherwise it's left "Unknown" for the user to sort).
        IReadOnlyList<LoaderProfile> profiles = _inventory.InstalledProfiles();

        int imported = 0;

        foreach (ContentType type in AllTypes)
        {
            foreach (string path in _installer.EnumerateInstalledFiles(type))
            {
                ct.ThrowIfCancellationRequested();

                string fileName = Path.GetFileName(path);
                string baseName = BaseName(fileName);
                if (!trackedBaseNames.Add(baseName))
                {
                    continue; // already tracked (or seen this pass)
                }

                Result<LocalContentMetadata> metaResult = await _reader.ReadAsync(path, ct).ConfigureAwait(false);
                LocalContentMetadata? meta = metaResult.IsSuccess ? metaResult.Value : null;

                bool enabled = !fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
                string name = !string.IsNullOrWhiteSpace(meta?.Name)
                    ? meta!.Name!
                    : Slug.PrettifyFileName(Path.GetFileNameWithoutExtension(baseName));

                Loader loader = Loader.None;
                List<GameVersion> versions;
                if (type.UsesLoader())
                {
                    // The loader is reliable from the jar; without it we can't even narrow the version.
                    bool loaderKnown = meta is { LoadersOrEmpty.Count: > 0 };
                    loader = loaderKnown ? meta!.LoadersOrEmpty[0] : Loader.None;

                    versions =
                        meta is { GameVersionsOrEmpty.Count: > 0 } ? meta.GameVersionsOrEmpty.ToList()
                        : loaderKnown && OnlyVersionFor(profiles, loader) is { } only ? [only]
                        : []; // ambiguous (0 or >1 profiles for this loader) — leave "Unknown" rather than guess wrong
                }
                else
                {
                    // Packs and shaders are loader-agnostic and version-tolerant — keep the active version as a soft tag.
                    versions = meta is { GameVersionsOrEmpty.Count: > 0 }
                        ? meta.GameVersionsOrEmpty.ToList()
                        : targetVersion is not null ? [targetVersion] : [];
                }

                string id = !string.IsNullOrWhiteSpace(meta?.ModId) ? meta!.ModId! : Slug.From(name);
                if (await _repository.FindAsync(id, ct).ConfigureAwait(false) is not null)
                {
                    id = $"{id}-{Slug.From(baseName)}";
                    if (await _repository.FindAsync(id, ct).ConfigureAwait(false) is not null)
                    {
                        continue; // can't form a unique id; leave it
                    }
                }

                IReadOnlyList<string> provided =
                    meta is { ProvidedIdsOrEmpty.Count: > 0 } ? meta.ProvidedIdsOrEmpty
                    : !string.IsNullOrWhiteSpace(meta?.ModId) ? [meta!.ModId!]
                    : [];

                var content = new InstalledContent(id, name, type)
                {
                    Author = "Local file",
                    Version = string.IsNullOrWhiteSpace(meta?.Version) ? "unknown" : meta!.Version!,
                    Loader = loader,
                    GameVersions = versions,
                    Enabled = enabled,
                    Source = "local",
                    FileName = fileName,
                    SizeMb = SafeLength(path) / (1024.0 * 1024.0),
                    Dependencies = meta?.DependenciesOrEmpty ?? [],
                    ProvidedIds = provided,
                };

                await _repository.UpsertAsync(content, ct).ConfigureAwait(false);
                imported++;
            }
        }

        return Result.Success(imported);
    }

    // The single installed game version for this loader, or null when there are none or several (ambiguous).
    private static GameVersion? OnlyVersionFor(IReadOnlyList<LoaderProfile> profiles, Loader loader)
    {
        List<GameVersion> versions = profiles.Where(p => p.Loader == loader).Select(p => p.GameVersion).Distinct().ToList();
        return versions.Count == 1 ? versions[0] : null;
    }

    private static string BaseName(string fileName)
        => fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^DisabledSuffix.Length]
            : fileName;

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
