using System.IO.Compression;
using Lodestone.Application.Abstractions;

namespace Lodestone.Infrastructure.Archives;

/// <summary>
/// Parses one loader's metadata format out of an open archive (Strategy). Returns <c>null</c> when
/// the archive isn't in this parser's format, letting the reader try the next strategy.
/// </summary>
internal interface IArchiveFormatParser
{
    LocalContentMetadata? TryParse(ZipArchive archive);
}

/// <summary>Shared helpers for reading text entries and skipping non-mod dependency identifiers.</summary>
internal static class ArchiveReadHelpers
{
    /// <summary>Loader/runtime pseudo-dependencies that are never installable mods, so we ignore them.</summary>
    public static readonly HashSet<string> NonModDependencyIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft", "java", "fabricloader", "fabric-loader", "quilt_loader", "quilt_base",
        "forge", "neoforge", "mcp", "fml", "javafml", "lowcodefml",
    };

    public static bool IsRealMod(string id) => !string.IsNullOrWhiteSpace(id) && !NonModDependencyIds.Contains(id);

    public static string? ReadEntryText(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry? entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return null;
        }

        using StreamReader reader = new(entry.Open());
        return reader.ReadToEnd();
    }

    /// <summary>True if the archive contains any entry under a top-level <c>shaders/</c> folder.</summary>
    public static bool LooksLikeShaderPack(ZipArchive archive)
        => archive.Entries.Any(e =>
            e.FullName.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.Length > "shaders/".Length);
}
