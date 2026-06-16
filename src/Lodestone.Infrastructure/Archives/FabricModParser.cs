using System.IO.Compression;
using System.Text.Json;
using Lodestone.Application.Abstractions;
using Lodestone.Domain;

namespace Lodestone.Infrastructure.Archives;

/// <summary>Reads Fabric's <c>fabric.mod.json</c> (id, version, depends/breaks/recommends, provides).</summary>
internal sealed class FabricModParser : IArchiveFormatParser
{
    public LocalContentMetadata? TryParse(ZipArchive archive)
    {
        string? json = ArchiveReadHelpers.ReadEntryText(archive, "fabric.mod.json");
        if (json is null)
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string? id = root.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;
            string? name = root.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() : null;
            string? version = root.TryGetProperty("version", out JsonElement verEl) ? verEl.GetString() : null;

            var deps = new List<Dependency>();
            ReadDependencyMap(root, "depends", DependencyKind.Required, deps);
            ReadDependencyMap(root, "recommends", DependencyKind.Optional, deps);
            ReadDependencyMap(root, "suggests", DependencyKind.Optional, deps);
            ReadDependencyMap(root, "breaks", DependencyKind.Incompatible, deps);
            ReadDependencyMap(root, "conflicts", DependencyKind.Incompatible, deps);

            var provided = new List<string>();
            if (!string.IsNullOrWhiteSpace(id))
            {
                provided.Add(id!);
            }

            if (root.TryGetProperty("provides", out JsonElement provEl) && provEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement p in provEl.EnumerateArray())
                {
                    if (p.GetString() is { Length: > 0 } pid)
                    {
                        provided.Add(pid);
                    }
                }
            }

            return new LocalContentMetadata(
                ContentType.Mod,
                ModId: id,
                Name: name,
                Version: version,
                Loaders: [Loader.Fabric],
                Dependencies: deps,
                ProvidedIds: provided);
        }
        catch (JsonException)
        {
            return null; // malformed fabric.mod.json — let other parsers / fallback try
        }
    }

    private static void ReadDependencyMap(JsonElement root, string property, DependencyKind kind, List<Dependency> into)
    {
        if (!root.TryGetProperty(property, out JsonElement map) || map.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty entry in map.EnumerateObject())
        {
            if (ArchiveReadHelpers.IsRealMod(entry.Name))
            {
                into.Add(new Dependency(entry.Name, kind));
            }
        }
    }
}
