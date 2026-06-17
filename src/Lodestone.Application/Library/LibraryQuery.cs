using Lodestone.Domain;

namespace Lodestone.Application.Library;

/// <summary>The active filters on the "My Content" screen.</summary>
public sealed record LibraryFilter(ContentType Type, GameVersion? Version = null, string? Search = null, Loader? Loader = null);

/// <summary>Matches content of a given <see cref="ContentType"/>.</summary>
public sealed class OfTypeSpecification(ContentType type) : Specification<InstalledContent>
{
    public override bool IsSatisfiedBy(InstalledContent candidate) => candidate.Type == type;
}

/// <summary>Matches content that declares support for a specific game version.</summary>
public sealed class SupportsVersionSpecification(GameVersion version) : Specification<InstalledContent>
{
    public override bool IsSatisfiedBy(InstalledContent candidate) => candidate.SupportsVersion(version);
}

/// <summary>Matches content built for a specific loader — used to isolate one profile's mods.</summary>
public sealed class OfLoaderSpecification(Loader loader) : Specification<InstalledContent>
{
    public override bool IsSatisfiedBy(InstalledContent candidate) => candidate.Loader == loader;
}

/// <summary>Matches content whose name or author contains the search text (case-insensitive).</summary>
public sealed class TextMatchSpecification(string text) : Specification<InstalledContent>
{
    private readonly string _text = text.Trim();

    public override bool IsSatisfiedBy(InstalledContent candidate)
        => candidate.Name.Contains(_text, StringComparison.OrdinalIgnoreCase)
           || candidate.Author.Contains(_text, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Builds and applies the library specification for a given filter.</summary>
public static class LibraryQuery
{
    public static Specification<InstalledContent> Build(LibraryFilter filter)
    {
        Specification<InstalledContent> spec = new OfTypeSpecification(filter.Type);

        if (filter.Version is not null)
        {
            spec = spec.And(new SupportsVersionSpecification(filter.Version));
        }

        // A loader only constrains loader-based content (mods); packs and shaders are loader-agnostic.
        if (filter.Loader is { } loader && loader != Loader.None && filter.Type.UsesLoader())
        {
            spec = spec.And(new OfLoaderSpecification(loader));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            spec = spec.And(new TextMatchSpecification(filter.Search));
        }

        return spec;
    }

    public static IReadOnlyList<InstalledContent> Apply(
        IEnumerable<InstalledContent> items,
        LibraryFilter filter)
    {
        Specification<InstalledContent> spec = Build(filter);
        return items.Where(spec.IsSatisfiedBy).ToList();
    }
}
