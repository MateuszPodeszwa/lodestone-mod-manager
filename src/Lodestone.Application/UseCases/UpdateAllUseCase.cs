using Lodestone.Application.Abstractions;
using Lodestone.Application.Catalog;
using Lodestone.Application.Settings;
using Lodestone.Domain;
using Lodestone.Domain.Common;

namespace Lodestone.Application.UseCases;

/// <summary>
/// Updates every item currently flagged with an available update to its latest compatible build
/// (the "Update all" action). Unlike <see cref="RefreshUpdatesUseCase"/> this always applies, and is
/// invoked explicitly by the user regardless of the auto-update setting. Items are updated in parallel,
/// bounded by the "concurrent downloads" setting so the network isn't flooded.
/// </summary>
public sealed class UpdateAllUseCase
{
    private readonly IInstalledContentRepository _repository;
    private readonly IModSourceRegistry _registry;
    private readonly IVersionResolver _resolver;
    private readonly IUpdateContentUseCase _updateContent;
    private readonly ISettingsStore _settings;

    public UpdateAllUseCase(
        IInstalledContentRepository repository,
        IModSourceRegistry registry,
        IVersionResolver resolver,
        IUpdateContentUseCase updateContent,
        ISettingsStore settings)
    {
        _repository = repository;
        _registry = registry;
        _resolver = resolver;
        _updateContent = updateContent;
        _settings = settings;
    }

    public async Task<Result<int>> ExecuteAsync(GameVersion? activeVersion, CancellationToken ct = default)
    {
        IReadOnlyList<InstalledContent> items = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        var pending = items.Where(i => i.UpdateAvailable).ToList();
        int updated = 0;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(
                _settings.Current.ConcurrentDownloads,
                LodestoneSettings.MinConcurrentDownloads,
                LodestoneSettings.MaxConcurrentDownloads),
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(pending, options, async (item, token) =>
        {
            if (string.IsNullOrWhiteSpace(item.ProjectId) || _registry.Find(item.Source) is not { IsConfigured: true } source)
            {
                return;
            }

            Result<IReadOnlyList<ProjectVersion>> versions = await source.GetVersionsAsync(item.ProjectId!, token).ConfigureAwait(false);
            if (versions.IsFailure)
            {
                return;
            }

            GameVersion? checkVersion = activeVersion ?? item.GameVersions.OrderByDescending(v => v).FirstOrDefault();
            if (checkVersion is null)
            {
                return;
            }

            ProjectVersion? latest = _resolver.Resolve(versions.Value, checkVersion, item.Loader);
            if (latest is not null && (await _updateContent.ApplyAsync(item, latest, null, token).ConfigureAwait(false)).IsSuccess)
            {
                Interlocked.Increment(ref updated);
            }
        }).ConfigureAwait(false);

        return Result.Success(updated);
    }
}
