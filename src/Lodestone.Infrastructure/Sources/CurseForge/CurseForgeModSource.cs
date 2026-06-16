using Lodestone.Application.Abstractions;
using Lodestone.Domain;
using Lodestone.Domain.Common;

namespace Lodestone.Infrastructure.Sources.CurseForge;

/// <summary>
/// Pluggable CurseForge source. It is wired into the registry and respected by the "CurseForge
/// fallback" setting, but reports <see cref="IsConfigured"/> = false until an API key is supplied, so
/// it is skipped rather than failing. Implement the calls once a key is available (see SUPPORTERS/docs).
/// </summary>
public sealed class CurseForgeModSource : IModSource
{
    private readonly string? _apiKey;

    public CurseForgeModSource(string? apiKey = null) => _apiKey = apiKey;

    public string Name => "curseforge";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public Task<Result<IReadOnlyList<CatalogProject>>> SearchAsync(ModSearchQuery query, CancellationToken ct = default)
        => Task.FromResult(NotConfigured<IReadOnlyList<CatalogProject>>());

    public Task<Result<CatalogProject>> GetProjectAsync(string idOrSlug, CancellationToken ct = default)
        => Task.FromResult(NotConfigured<CatalogProject>());

    public Task<Result<IReadOnlyList<ProjectVersion>>> GetVersionsAsync(string projectId, CancellationToken ct = default)
        => Task.FromResult(NotConfigured<IReadOnlyList<ProjectVersion>>());

    private static Result<T> NotConfigured<T>()
        => Result.Failure<T>("curseforge.not_configured", "CurseForge isn't configured (no API key).");
}
