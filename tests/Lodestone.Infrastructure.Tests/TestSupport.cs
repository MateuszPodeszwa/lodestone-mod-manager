using System.IO.Compression;
using System.Security.Cryptography;

namespace Lodestone.Infrastructure.Tests;

/// <summary>A unique temp directory that cleans itself up at the end of a test.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lodestone-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // best effort cleanup
        }
    }
}

internal static class ZipFixtures
{
    /// <summary>Writes a zip/jar with the given (entryName, textContent) pairs and returns its path.</summary>
    public static string Create(string path, params (string Entry, string Content)[] entries)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string entry, string content) in entries)
        {
            ZipArchiveEntry e = zip.CreateEntry(entry);
            using var writer = new StreamWriter(e.Open());
            writer.Write(content);
        }

        return path;
    }
}

internal static class Hashing
{
    public static string Sha512Hex(byte[] data)
        => Convert.ToHexString(SHA512.HashData(data)).ToLowerInvariant();
}
