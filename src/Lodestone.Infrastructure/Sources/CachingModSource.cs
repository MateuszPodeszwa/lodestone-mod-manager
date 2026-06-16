using System.Collections.Concurrent;
using Lodestone.Application.Abstractions;
using Lodestone.Domain;
using Lodestone.Domain.Common;

namespace Lodestone.Infrastructure.Sources;

/// <summary>
/// Caches successful responses from an inner <see cref="IModSource"/> for a short TTL (Decorator). This
/// keeps the Browse screen snappy and reduces load on the API; failures are never cached.
/// </summary>
public sealed class CachingModSource : IModSource
{
    private readonly IModSource _inner;
    private readonly TimeSpan _ttl;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public CachingModSource(IModSource inner, IClock clock, TimeSpan? ttl = null)
    {
        _inner = inner;
        _clock = clock;
        _ttl = ttl ?? TimeSpan.FromMinutes(5);
    }

    public string Name => _inner.Name;

    public bool IsConfigured => _inner.IsConfigured;

    public Task<Result<IReadOnlyList<CatalogProject>>> SearchAsync(ModSearchQuery query, CancellationToken ct = default)
        => GetOrAddAsync($"search:{Key(query)}", () => _inner.SearchAsync(query, ct));

    public Task<Result<CatalogProject>> GetProjectAsync(string idOrSlug, CancellationToken ct = default)
        => GetOrAddAsync($"project:{idOrSlug}", () => _inner.GetProjectAsync(idOrSlug, ct));

    public Task<Result<IReadOnlyList<ProjectVersion>>> GetVersionsAsync(string projectId, CancellationToken ct = default)
        => GetOrAddAsync($"versions:{projectId}", () => _inner.GetVersionsAsync(projectId, ct));

    private async Task<Result<T>> GetOrAddAsync<T>(string key, Func<Task<Result<T>>> factory)
    {
        if (_cache.TryGetValue(key, out CacheEntry? entry) && entry.Expires > _clock.UtcNow)
        {
            return (Result<T>)entry.Value;
        }

        Result<T> result = await factory().ConfigureAwait(false);
        if (result.IsSuccess)
        {
            _cache[key] = new CacheEntry(result, _clock.UtcNow + _ttl);
        }

        return result;
    }

    private static string Key(ModSearchQuery q)
        => $"{q.Text}|{q.Type}|{q.Category}|{q.Sort}|{q.GameVersion}|{q.Loader}|{q.Offset}|{q.Limit}";

    private sealed record CacheEntry(object Value, DateTimeOffset Expires);
}
