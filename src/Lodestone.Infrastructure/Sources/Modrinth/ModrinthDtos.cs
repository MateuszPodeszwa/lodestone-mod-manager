using System.Text.Json.Serialization;
using Lodestone.Domain;

namespace Lodestone.Infrastructure.Sources.Modrinth;

// Tolerant DTOs mirroring the Modrinth v2 API (https://docs.modrinth.com). Unknown fields are ignored.

internal sealed class ModrinthSearchResponse
{
    [JsonPropertyName("hits")] public List<ModrinthHit> Hits { get; set; } = [];
    [JsonPropertyName("total_hits")] public int TotalHits { get; set; }
}

internal sealed class ModrinthHit
{
    [JsonPropertyName("project_id")] public string ProjectId { get; set; } = string.Empty;
    [JsonPropertyName("slug")] public string Slug { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("author")] public string Author { get; set; } = "Unknown";
    [JsonPropertyName("downloads")] public long Downloads { get; set; }
    [JsonPropertyName("follows")] public long Follows { get; set; }
    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = [];
    [JsonPropertyName("display_categories")] public List<string> DisplayCategories { get; set; } = [];
    [JsonPropertyName("versions")] public List<string> Versions { get; set; } = [];
    [JsonPropertyName("project_type")] public string ProjectType { get; set; } = "mod";
    [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
    [JsonPropertyName("latest_version")] public string? LatestVersion { get; set; }
}

internal sealed class ModrinthProject
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("slug")] public string Slug { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("downloads")] public long Downloads { get; set; }
    [JsonPropertyName("followers")] public long Followers { get; set; }
    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = [];
    [JsonPropertyName("loaders")] public List<string> Loaders { get; set; } = [];
    [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = [];
    [JsonPropertyName("project_type")] public string ProjectType { get; set; } = "mod";
    [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
}

internal sealed class ModrinthVersion
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("project_id")] public string ProjectId { get; set; } = string.Empty;
    [JsonPropertyName("version_number")] public string VersionNumber { get; set; } = string.Empty;
    [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = [];
    [JsonPropertyName("loaders")] public List<string> Loaders { get; set; } = [];
    [JsonPropertyName("dependencies")] public List<ModrinthDependency> Dependencies { get; set; } = [];
    [JsonPropertyName("files")] public List<ModrinthFile> Files { get; set; } = [];
    [JsonPropertyName("date_published")] public DateTimeOffset? DatePublished { get; set; }
}

internal sealed class ModrinthDependency
{
    [JsonPropertyName("project_id")] public string? ProjectId { get; set; }
    [JsonPropertyName("version_id")] public string? VersionId { get; set; }
    [JsonPropertyName("dependency_type")] public string DependencyType { get; set; } = "required";
}

internal sealed class ModrinthFile
{
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("filename")] public string Filename { get; set; } = string.Empty;
    [JsonPropertyName("primary")] public bool Primary { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("hashes")] public ModrinthHashes Hashes { get; set; } = new();
}

internal sealed class ModrinthHashes
{
    [JsonPropertyName("sha512")] public string? Sha512 { get; set; }
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
}

/// <summary>Maps Modrinth DTOs to domain types (Adapter), keeping API shapes out of the core.</summary>
internal static class ModrinthMapper
{
    public static ContentType ToContentType(string projectType) => projectType.ToLowerInvariant() switch
    {
        "resourcepack" => ContentType.ResourcePack,
        "shader" => ContentType.Shader,
        _ => ContentType.Mod,
    };

    public static string ToProjectType(ContentType type) => type switch
    {
        ContentType.ResourcePack => "resourcepack",
        ContentType.Shader => "shader",
        _ => "mod",
    };

    public static DependencyKind ToDependencyKind(string type) => type.ToLowerInvariant() switch
    {
        "required" => DependencyKind.Required,
        "incompatible" => DependencyKind.Incompatible,
        "embedded" => DependencyKind.Embedded,
        _ => DependencyKind.Optional,
    };

    public static CatalogProject ToCatalog(ModrinthHit hit)
    {
        List<Loader> loaders = hit.Categories
            .Select(c => c.ParseLoader())
            .Where(l => l != Loader.None)
            .Distinct()
            .ToList();

        List<string> categories = (hit.DisplayCategories.Count > 0 ? hit.DisplayCategories : hit.Categories)
            .Where(c => c.ParseLoader() == Loader.None)
            .ToList();

        return new CatalogProject(
            hit.ProjectId, hit.Slug, hit.Title, hit.Author, ToContentType(hit.ProjectType),
            hit.Description, hit.Downloads, hit.Follows, categories, loaders,
            ParseVersions(hit.Versions), "modrinth", hit.IconUrl, hit.LatestVersion);
    }

    public static CatalogProject ToCatalog(ModrinthProject project)
    {
        List<Loader> loaders = project.Loaders.Select(l => l.ParseLoader()).Where(l => l != Loader.None).ToList();
        List<string> categories = project.Categories.Where(c => c.ParseLoader() == Loader.None).ToList();

        return new CatalogProject(
            project.Id, project.Slug, project.Title, "Unknown", ToContentType(project.ProjectType),
            project.Description, project.Downloads, project.Followers, categories, loaders,
            ParseVersions(project.GameVersions), "modrinth", project.IconUrl);
    }

    public static ProjectVersion? ToProjectVersion(ModrinthVersion version)
    {
        ModrinthFile? file = version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();
        if (file is null)
        {
            return null; // a version with no downloadable file is unusable
        }

        List<Dependency> dependencies = version.Dependencies
            .Where(d => !string.IsNullOrWhiteSpace(d.ProjectId))
            .Select(d => new Dependency(d.ProjectId!, ToDependencyKind(d.DependencyType), d.VersionId))
            .ToList();

        List<Loader> loaders = version.Loaders.Select(l => l.ParseLoader()).Where(l => l != Loader.None).ToList();

        return new ProjectVersion(
            version.Id, version.ProjectId, version.VersionNumber, ContentType.Mod,
            ParseVersions(version.GameVersions), loaders, dependencies,
            file.Filename, file.Url, file.Hashes.Sha512, file.Size / (1024.0 * 1024.0), version.DatePublished);
    }

    private static List<GameVersion> ParseVersions(IEnumerable<string> raw)
    {
        var versions = new List<GameVersion>();
        foreach (string value in raw)
        {
            Domain.Common.Result<GameVersion> parsed = GameVersion.Create(value);
            if (parsed.IsSuccess)
            {
                versions.Add(parsed.Value);
            }
        }

        return versions;
    }
}
