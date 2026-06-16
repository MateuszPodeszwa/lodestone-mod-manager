using System.Net.Http.Json;
using System.Text.Json;
using Lodestone.Application.Abstractions;
using Lodestone.Domain;
using Lodestone.Domain.Common;

namespace Lodestone.Infrastructure.Sources.Modrinth;

/// <summary>
/// Modrinth implementation of <see cref="IModSource"/> (keyless). Builds faceted search URLs, maps the
/// JSON to domain types via <see cref="ModrinthMapper"/>, and turns network/parse failures into typed
/// results so the UI degrades gracefully instead of crashing.
/// </summary>
public sealed class ModrinthModSource : IModSource
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    public ModrinthModSource(HttpClient http) => _http = http;

    public string Name => "modrinth";

    public bool IsConfigured => true;

    public async Task<Result<IReadOnlyList<CatalogProject>>> SearchAsync(ModSearchQuery query, CancellationToken ct = default)
    {
        string url = BuildSearchUrl(query);
        return await SendAsync<ModrinthSearchResponse, IReadOnlyList<CatalogProject>>(
            url,
            response => (response?.Hits ?? []).Select(ModrinthMapper.ToCatalog).ToList(),
            ct).ConfigureAwait(false);
    }

    public async Task<Result<CatalogProject>> GetProjectAsync(string idOrSlug, CancellationToken ct = default)
    {
        string url = $"v2/project/{Uri.EscapeDataString(idOrSlug)}";
        return await SendAsync<ModrinthProject, CatalogProject>(
            url,
            response => response is null
                ? throw new JsonException("Empty project response.")
                : ModrinthMapper.ToCatalog(response),
            ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<ProjectVersion>>> GetVersionsAsync(string projectId, CancellationToken ct = default)
    {
        string url = $"v2/project/{Uri.EscapeDataString(projectId)}/version";
        return await SendAsync<List<ModrinthVersion>, IReadOnlyList<ProjectVersion>>(
            url,
            response => (response ?? [])
                .Select(ModrinthMapper.ToProjectVersion)
                .Where(v => v is not null)
                .Select(v => v!)
                .ToList(),
            ct).ConfigureAwait(false);
    }

    private async Task<Result<TOut>> SendAsync<TResponse, TOut>(
        string url,
        Func<TResponse?, TOut> map,
        CancellationToken ct)
    {
        try
        {
            TResponse? response = await _http.GetFromJsonAsync<TResponse>(url, JsonOptions, ct).ConfigureAwait(false);
            return Result.Success(map(response));
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<TOut>("modrinth.http", $"Modrinth request failed: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return Result.Failure<TOut>("modrinth.parse", $"Couldn't read Modrinth's response: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<TOut>("modrinth.timeout", "Modrinth took too long to respond.");
        }
    }

    private static string BuildSearchUrl(ModSearchQuery query)
    {
        var facets = new List<string[]>();
        if (query.Type is { } type)
        {
            facets.Add([$"project_type:{ModrinthMapper.ToProjectType(type)}"]);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            facets.Add([$"categories:{query.Category}"]);
        }

        if (query.Loader is { } loader && loader != Loader.None)
        {
            facets.Add([$"categories:{loader.ToSlug()}"]);
        }

        if (query.GameVersion is { } gameVersion)
        {
            facets.Add([$"versions:{gameVersion.Value}"]);
        }

        string index = query.Sort switch
        {
            ModSortOrder.Downloads => "downloads",
            ModSortOrder.Followers => "follows",
            _ => "relevance",
        };

        var parameters = new List<string>
        {
            $"query={Uri.EscapeDataString(query.Text)}",
            $"index={index}",
            $"offset={query.Offset}",
            $"limit={query.Limit}",
        };

        if (facets.Count > 0)
        {
            parameters.Add($"facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}");
        }

        return "v2/search?" + string.Join('&', parameters);
    }
}
