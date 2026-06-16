using Lodestone.Application.Abstractions;
using Lodestone.Application.Settings;
using Lodestone.Domain;
using Lodestone.Domain.Common;
using Lodestone.Infrastructure.Persistence;

namespace Lodestone.Infrastructure.FileSystem;

/// <summary>
/// Places content into the right <c>.minecraft</c> subfolder and manages on-disk state. Enable/disable
/// toggles a <c>.disabled</c> suffix (ignored by loaders and the game); removal is a soft-delete into a
/// trash folder so an accidental uninstall is recoverable.
/// </summary>
public sealed class FileSystemContentInstaller : IContentInstaller
{
    private const string DisabledSuffix = ".disabled";

    private readonly ISettingsStore _settings;
    private readonly string _trashDirectory;

    public FileSystemContentInstaller(ISettingsStore settings, string? trashDirectory = null)
    {
        _settings = settings;
        _trashDirectory = trashDirectory ?? LodestonePaths.TrashDirectory;
    }

    public async Task<Result<PlaceResult>> PlaceAsync(
        string sourceFilePath,
        ContentType type,
        DuplicateResolution onDuplicate = DuplicateResolution.Fail,
        CancellationToken ct = default)
    {
        Result<string> folderResult = ResolveFolder(type);
        if (folderResult.IsFailure)
        {
            return Result.Failure<PlaceResult>(folderResult.Error);
        }

        if (!File.Exists(sourceFilePath))
        {
            return Result.Failure<PlaceResult>("install.source_missing", "The file to install no longer exists.");
        }

        string folder = folderResult.Value;
        string fileName = Path.GetFileName(sourceFilePath);
        bool replaced = false;

        if (Exists(type, fileName))
        {
            switch (onDuplicate)
            {
                case DuplicateResolution.Fail:
                    return Result.Failure<PlaceResult>("install.duplicate_file", $"{fileName} is already installed.");
                case DuplicateResolution.Replace:
                    MoveExistingToTrash(folder, fileName);
                    replaced = true;
                    break;
                case DuplicateResolution.KeepBoth:
                    fileName = UniqueName(folder, fileName);
                    break;
            }
        }

        string destination = Path.Combine(folder, fileName);
        try
        {
            await CopyAsync(sourceFilePath, destination, ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return Result.Failure<PlaceResult>("install.io", ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure<PlaceResult>("install.permission", "Lodestone doesn't have permission to write there.");
        }

        long size = new FileInfo(destination).Length;
        return Result.Success(new PlaceResult(fileName, size, replaced));
    }

    public Task<Result<string>> SetEnabledAsync(
        ContentType type,
        string fileName,
        bool enabled,
        CancellationToken ct = default)
    {
        Result<string> folderResult = ResolveFolder(type);
        if (folderResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<string>(folderResult.Error));
        }

        string folder = folderResult.Value;
        bool currentlyDisabled = fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
        string targetName = enabled
            ? currentlyDisabled ? fileName[..^DisabledSuffix.Length] : fileName
            : currentlyDisabled ? fileName : fileName + DisabledSuffix;

        if (string.Equals(targetName, fileName, StringComparison.Ordinal))
        {
            return Task.FromResult(Result.Success(fileName)); // already in the desired state
        }

        string current = Path.Combine(folder, fileName);
        string target = Path.Combine(folder, targetName);

        try
        {
            if (!File.Exists(current))
            {
                return Task.FromResult(File.Exists(target)
                    ? Result.Success(targetName)
                    : Result.Failure<string>("install.file_missing", "That file is no longer on disk."));
            }

            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(current, target);
            return Task.FromResult(Result.Success(targetName));
        }
        catch (IOException)
        {
            return Task.FromResult(Result.Failure<string>("install.locked", "Couldn't change the file — is Minecraft running?"));
        }
    }

    public Task<Result> RemoveAsync(ContentType type, string fileName, CancellationToken ct = default)
    {
        Result<string> folderResult = ResolveFolder(type);
        if (folderResult.IsFailure)
        {
            return Task.FromResult(Result.Failure(folderResult.Error));
        }

        string folder = folderResult.Value;
        string path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
        {
            string disabled = path + DisabledSuffix;
            if (File.Exists(disabled))
            {
                path = disabled;
            }
            else
            {
                return Task.FromResult(Result.Success()); // already gone — idempotent
            }
        }

        try
        {
            Directory.CreateDirectory(_trashDirectory);
            string stamped = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Path.GetFileName(path)}";
            File.Move(path, Path.Combine(_trashDirectory, stamped));
            return Task.FromResult(Result.Success());
        }
        catch (IOException)
        {
            return Task.FromResult(Result.Failure("install.locked", "Couldn't remove the file — is Minecraft running?"));
        }
    }

    public bool Exists(ContentType type, string fileName)
    {
        string? game = _settings.Current.GameDirectory;
        if (string.IsNullOrWhiteSpace(game))
        {
            return false;
        }

        string folder = Path.Combine(game, type.ToFolderName());
        if (!Directory.Exists(folder))
        {
            return false;
        }

        string baseName = fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^DisabledSuffix.Length]
            : fileName;

        return File.Exists(Path.Combine(folder, baseName))
               || File.Exists(Path.Combine(folder, baseName + DisabledSuffix));
    }

    private Result<string> ResolveFolder(ContentType type)
    {
        string? game = _settings.Current.GameDirectory;
        if (string.IsNullOrWhiteSpace(game) || !Directory.Exists(game))
        {
            return Result.Failure<string>("game.dir_missing", "Set your Minecraft folder in Settings first.");
        }

        string folder = Path.Combine(game, type.ToFolderName());
        Directory.CreateDirectory(folder);
        return Result.Success(folder);
    }

    private void MoveExistingToTrash(string folder, string fileName)
    {
        string baseName = fileName.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^DisabledSuffix.Length]
            : fileName;

        foreach (string candidate in new[] { baseName, baseName + DisabledSuffix })
        {
            string path = Path.Combine(folder, candidate);
            if (!File.Exists(path))
            {
                continue;
            }

            Directory.CreateDirectory(_trashDirectory);
            string stamped = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{candidate}";
            File.Move(path, Path.Combine(_trashDirectory, stamped), overwrite: true);
        }
    }

    private static string UniqueName(string folder, string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        int counter = 2;
        string candidate;
        do
        {
            candidate = $"{stem} ({counter}){ext}";
            counter++;
        }
        while (File.Exists(Path.Combine(folder, candidate)) ||
               File.Exists(Path.Combine(folder, candidate + DisabledSuffix)));

        return candidate;
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken ct)
    {
        await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, ct).ConfigureAwait(false);
    }
}
