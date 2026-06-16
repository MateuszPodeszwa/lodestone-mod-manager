using System.IO.Compression;
using System.Text.Json;
using Lodestone.Application.Abstractions;
using Lodestone.Domain;

namespace Lodestone.Infrastructure.Archives;

/// <summary>Reads Quilt's <c>quilt.mod.json</c> (quilt_loader.id/version, depends/breaks/provides).</summary>
internal sealed class QuiltModParser : IArchiveFormatParser
{
    public LocalContentMetadata? TryParse(ZipArchive archive)
    {
        string? json = ArchiveReadHelpers.ReadEntryText(archive, "quilt.mod.json");
        if (json is null)
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("quilt_loader", out JsonElement loader))
            {
                return null;
            }

            string? id = loader.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;
            string? version = loader.TryGetProperty("version", out JsonElement verEl) ? verEl.GetString() : null;
            string? name = id;
            if (loader.TryGetProperty("metadata", out JsonElement meta) &&
                meta.TryGetProperty("name", out JsonElement nameEl))
            {
                name = nameEl.GetString() ?? id;
            }

            var deps = new List<Dependency>();
            ReadDependencyArray(loader, "depends", DependencyKind.Required, deps);
            ReadDependencyArray(loader, "breaks", DependencyKind.Incompatible, deps);

            var provided = new List<string>();
            if (!string.IsNullOrWhiteSpace(id))
            {
                provided.Add(id!);
            }

            ReadProvides(loader, provided);

            return new LocalContentMetadata(
                ContentType.Mod,
                ModId: id,
                Name: name,
                Version: version,
                Loaders: [Loader.Quilt],
                Dependencies: deps,
                ProvidedIds: provided);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ReadDependencyArray(JsonElement loader, string property, DependencyKind kind, List<Dependency> into)
    {
        if (!loader.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement element in array.EnumerateArray())
        {
            string? id = element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;

            if (id is not null && ArchiveReadHelpers.IsRealMod(id))
            {
                into.Add(new Dependency(id, kind));
            }
        }
    }

    private static void ReadProvides(JsonElement loader, List<string> into)
    {
        if (!loader.TryGetProperty("provides", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement element in array.EnumerateArray())
        {
            string? id = element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;

            if (id is { Length: > 0 })
            {
                into.Add(id);
            }
        }
    }
}
