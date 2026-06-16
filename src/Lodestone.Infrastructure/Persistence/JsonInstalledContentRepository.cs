using Lodestone.Application.Abstractions;
using Lodestone.Domain;

namespace Lodestone.Infrastructure.Persistence;

/// <summary>
/// File-backed library repository. Keeps an in-memory cache loaded lazily and persists the whole set
/// on each mutation (atomic write). A <see cref="SemaphoreSlim"/> serializes access so concurrent
/// installs/toggles can't corrupt the index.
/// </summary>
public sealed class JsonInstalledContentRepository : IInstalledContentRepository, IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, InstalledContent>? _cache;

    public JsonInstalledContentRepository(string? path = null) => _path = path ?? LodestonePaths.LibraryFile;

    public void Dispose() => _gate.Dispose();

    public async Task<IReadOnlyList<InstalledContent>> GetAllAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Dictionary<string, InstalledContent> cache = await EnsureLoadedAsync(ct).ConfigureAwait(false);
            return cache.Values.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<InstalledContent?> FindAsync(string id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Dictionary<string, InstalledContent> cache = await EnsureLoadedAsync(ct).ConfigureAwait(false);
            return cache.GetValueOrDefault(id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(InstalledContent content, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Dictionary<string, InstalledContent> cache = await EnsureLoadedAsync(ct).ConfigureAwait(false);
            cache[content.Id] = content;
            await PersistAsync(cache, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Dictionary<string, InstalledContent> cache = await EnsureLoadedAsync(ct).ConfigureAwait(false);
            if (cache.Remove(id))
            {
                await PersistAsync(cache, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, InstalledContent>> EnsureLoadedAsync(CancellationToken ct)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        List<InstalledContentDto> dtos =
            await JsonStore.ReadAsync<List<InstalledContentDto>>(_path, ct).ConfigureAwait(false) ?? [];

        _cache = new Dictionary<string, InstalledContent>(StringComparer.OrdinalIgnoreCase);
        foreach (InstalledContentDto dto in dtos)
        {
            InstalledContent? domain = InstalledContentMapper.ToDomain(dto);
            if (domain is not null)
            {
                _cache[domain.Id] = domain;
            }
        }

        return _cache;
    }

    private Task PersistAsync(Dictionary<string, InstalledContent> cache, CancellationToken ct)
        => JsonStore.WriteAsync(_path, cache.Values.Select(InstalledContentMapper.ToDto).ToList(), ct);
}
