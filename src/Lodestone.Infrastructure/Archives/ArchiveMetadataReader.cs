using System.IO.Compression;
using Lodestone.Application.Abstractions;
using Lodestone.Domain;
using Lodestone.Domain.Common;

namespace Lodestone.Infrastructure.Archives;

/// <summary>
/// Inspects a local archive in memory (never extracting it, so there's no zip-slip risk) and returns
/// its content type plus any loader metadata. Tries each loader parser in turn (Strategy), then falls
/// back to structural detection (shaders folder / pack.mcmeta) and finally the file extension.
/// </summary>
public sealed class ArchiveMetadataReader : IArchiveMetadataReader
{
    private readonly IReadOnlyList<IArchiveFormatParser> _parsers =
    [
        new FabricModParser(),
        new QuiltModParser(),
        new ForgeModParser(),
    ];

    public Task<Result<LocalContentMetadata>> ReadAsync(string filePath, CancellationToken ct = default)
        => Task.Run(() => ReadCore(filePath), ct);

    private Result<LocalContentMetadata> ReadCore(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return Result.Failure<LocalContentMetadata>("archive.not_found", "The file could not be found.");
        }

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(filePath);
        }
        catch (InvalidDataException)
        {
            return Result.Failure<LocalContentMetadata>("archive.invalid", "This file isn't a valid mod/pack archive.");
        }
        catch (IOException ex)
        {
            return Result.Failure<LocalContentMetadata>("archive.io", ex.Message);
        }

        using (archive)
        {
            foreach (IArchiveFormatParser parser in _parsers)
            {
                LocalContentMetadata? metadata = parser.TryParse(archive);
                if (metadata is not null)
                {
                    return Result.Success(metadata);
                }
            }

            if (ArchiveReadHelpers.LooksLikeShaderPack(archive))
            {
                return Result.Success(new LocalContentMetadata(ContentType.Shader));
            }

            if (archive.GetEntry("pack.mcmeta") is not null)
            {
                return Result.Success(new LocalContentMetadata(ContentType.ResourcePack));
            }

            return Result.Success(new LocalContentMetadata(InferTypeByExtension(filePath)));
        }
    }

    private static ContentType InferTypeByExtension(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jar" or ".litemod" => ContentType.Mod,
            _ => ContentType.ResourcePack,
        };
    }
}
