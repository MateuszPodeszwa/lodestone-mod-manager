using System.IO.Compression;
using Lodestone.Application.Abstractions;
using Lodestone.Domain;

namespace Lodestone.Infrastructure.Archives;

/// <summary>
/// Reads Forge's <c>META-INF/mods.toml</c> or NeoForge's <c>META-INF/neoforge.mods.toml</c>
/// (<c>[[mods]]</c> id/version + <c>[[dependencies.&lt;id&gt;]]</c> mandatory flags) via a tiny TOML reader.
/// </summary>
internal sealed class ForgeModParser : IArchiveFormatParser
{
    public LocalContentMetadata? TryParse(ZipArchive archive)
    {
        bool isNeoForge = true;
        string? toml = ArchiveReadHelpers.ReadEntryText(archive, "META-INF/neoforge.mods.toml");
        if (toml is null)
        {
            isNeoForge = false;
            toml = ArchiveReadHelpers.ReadEntryText(archive, "META-INF/mods.toml");
        }

        if (toml is null)
        {
            return null;
        }

        List<TomlBlock> blocks = SimpleToml.ParseBlocks(toml);

        TomlBlock? mod = blocks.FirstOrDefault(b => b.Header.Equals("mods", StringComparison.OrdinalIgnoreCase));
        string? id = mod?.Values.GetValueOrDefault("modId");
        string? version = NormalizeVersion(mod?.Values.GetValueOrDefault("version"));
        string? name = mod?.Values.GetValueOrDefault("displayName") ?? id;

        var deps = new List<Dependency>();
        if (id is not null)
        {
            string header = $"dependencies.{id}";
            foreach (TomlBlock block in blocks.Where(b => b.Header.Equals(header, StringComparison.OrdinalIgnoreCase)))
            {
                string? depId = block.Values.GetValueOrDefault("modId");
                if (depId is null || !ArchiveReadHelpers.IsRealMod(depId))
                {
                    continue;
                }

                bool mandatory = !string.Equals(block.Values.GetValueOrDefault("mandatory"), "false", StringComparison.OrdinalIgnoreCase);
                deps.Add(new Dependency(depId, mandatory ? DependencyKind.Required : DependencyKind.Optional));
            }
        }

        return new LocalContentMetadata(
            ContentType.Mod,
            ModId: id,
            Name: name,
            Version: version,
            Loaders: [isNeoForge ? Loader.NeoForge : Loader.Forge],
            Dependencies: deps,
            ProvidedIds: id is not null ? [id] : []);
    }

    // mods.toml frequently uses "${file.jarVersion}" placeholders that aren't real versions.
    private static string? NormalizeVersion(string? version)
        => string.IsNullOrWhiteSpace(version) || version.Contains("${", StringComparison.Ordinal) ? null : version;
}
