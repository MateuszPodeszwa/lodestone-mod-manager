using Lodestone.Domain;

namespace Lodestone.Infrastructure.Persistence;

/// <summary>Flat, serialization-friendly shape for an installed item (decouples disk format from the entity).</summary>
internal sealed class InstalledContentDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = nameof(ContentType.Mod);
    public string Author { get; set; } = "Unknown";
    public string Version { get; set; } = "1.0.0";
    public string Loader { get; set; } = nameof(Domain.Loader.None);
    public List<string> GameVersions { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public double SizeMb { get; set; }
    public string? ProjectId { get; set; }
    public string Source { get; set; } = "local";
    public string? FileName { get; set; }
    public string? Sha512 { get; set; }
    public List<DependencyDto> Dependencies { get; set; } = [];
    public List<string> ProvidedIds { get; set; } = [];
    public bool IsLibrary { get; set; }
    public bool UpdateAvailable { get; set; }
}

internal sealed class DependencyDto
{
    public string Identifier { get; set; } = string.Empty;
    public string Kind { get; set; } = nameof(DependencyKind.Required);
    public string? VersionId { get; set; }
    public string? DisplayName { get; set; }
}

/// <summary>Maps between the persistence DTO and the domain entity (Adapter).</summary>
internal static class InstalledContentMapper
{
    public static InstalledContentDto ToDto(InstalledContent c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Type = c.Type.ToString(),
        Author = c.Author,
        Version = c.Version,
        Loader = c.Loader.ToString(),
        GameVersions = c.GameVersions.Select(v => v.Value).ToList(),
        Enabled = c.Enabled,
        SizeMb = c.SizeMb,
        ProjectId = c.ProjectId,
        Source = c.Source,
        FileName = c.FileName,
        Sha512 = c.Sha512,
        Dependencies = c.Dependencies.Select(d => new DependencyDto
        {
            Identifier = d.Identifier,
            Kind = d.Kind.ToString(),
            VersionId = d.VersionId,
            DisplayName = d.DisplayName,
        }).ToList(),
        ProvidedIds = c.ProvidedIds.ToList(),
        IsLibrary = c.IsLibrary,
        UpdateAvailable = c.UpdateAvailable,
    };

    public static InstalledContent? ToDomain(InstalledContentDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.Name))
        {
            return null; // skip corrupt rows rather than throw
        }

        ContentType type = Enum.TryParse(dto.Type, out ContentType t) ? t : ContentType.Mod;

        var versions = new List<GameVersion>();
        foreach (string raw in dto.GameVersions)
        {
            Domain.Common.Result<GameVersion> parsed = GameVersion.Create(raw);
            if (parsed.IsSuccess)
            {
                versions.Add(parsed.Value);
            }
        }

        var dependencies = dto.Dependencies.Select(d => new Dependency(
            d.Identifier,
            Enum.TryParse(d.Kind, out DependencyKind kind) ? kind : DependencyKind.Required,
            d.VersionId,
            d.DisplayName)).ToList();

        return new InstalledContent(dto.Id, dto.Name, type)
        {
            Author = dto.Author,
            Version = dto.Version,
            Loader = Enum.TryParse(dto.Loader, out Loader loader) ? loader : Loader.None,
            GameVersions = versions,
            Enabled = dto.Enabled,
            SizeMb = dto.SizeMb,
            ProjectId = dto.ProjectId,
            Source = dto.Source,
            FileName = dto.FileName,
            Sha512 = dto.Sha512,
            Dependencies = dependencies,
            ProvidedIds = dto.ProvidedIds,
            IsLibrary = dto.IsLibrary,
            UpdateAvailable = dto.UpdateAvailable,
        };
    }
}
