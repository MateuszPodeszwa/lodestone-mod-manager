using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lodestone.Infrastructure.Persistence;

/// <summary>
/// Shared JSON options and atomic read/write helpers for Lodestone's local stores. Writes go to a
/// temp file and are then atomically swapped in, so a crash or power-loss mid-write can never leave a
/// half-written (corrupt) file in place (see docs/RISK-ANALYSIS.md §11).
/// </summary>
public static class JsonStore
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static async Task WriteAsync<T>(string path, T value, CancellationToken ct = default)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        string temp = path + ".tmp";
        await using (FileStream stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, value, Options, ct).ConfigureAwait(false);
        }

        if (File.Exists(path))
        {
            File.Replace(temp, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temp, path);
        }
    }

    /// <summary>
    /// Reads and deserializes, or returns <c>default</c> if the file is absent. A corrupt file is
    /// quarantined (renamed <c>.corrupt</c>) and <c>default</c> returned, so the app self-heals.
    /// </summary>
    public static async Task<T?> ReadAsync<T>(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            QuarantineCorruptFile(path);
            return default;
        }
    }

    private static void QuarantineCorruptFile(string path)
    {
        try
        {
            string backup = path + ".corrupt";
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }

            File.Move(path, backup);
        }
        catch (IOException)
        {
            // If we can't move it, leave it; the caller falls back to defaults regardless.
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new GameVersionJsonConverter());
        return options;
    }
}
