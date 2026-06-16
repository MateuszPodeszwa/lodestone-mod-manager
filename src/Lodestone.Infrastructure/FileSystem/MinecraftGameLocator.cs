using Lodestone.Application.Abstractions;
using Lodestone.Domain.Common;

namespace Lodestone.Infrastructure.FileSystem;

/// <summary>
/// Detects and validates the Minecraft (Java Edition) directory. Probes the well-known locations and,
/// failing that, the caller falls back to a folder picker (onboarding / settings).
/// </summary>
public sealed class MinecraftGameLocator : IGameLocator
{
    public Result<string> Detect()
    {
        foreach (string candidate in CandidatePaths())
        {
            if (IsValid(candidate))
            {
                return Result.Success(candidate);
            }
        }

        return Result.Failure<string>("game.not_found", "Couldn't find your Minecraft folder automatically.");
    }

    public bool IsValid(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        // A real install has at least one of these markers.
        return Directory.Exists(Path.Combine(path, "versions"))
               || Directory.Exists(Path.Combine(path, "mods"))
               || File.Exists(Path.Combine(path, "launcher_profiles.json"));
    }

    private static IEnumerable<string> CandidatePaths()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            yield return Path.Combine(appData, ".minecraft");
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            // OneDrive-redirected AppData and a couple of common custom-launcher spots.
            yield return Path.Combine(userProfile, "AppData", "Roaming", ".minecraft");
            yield return Path.Combine(userProfile, "OneDrive", "AppData", "Roaming", ".minecraft");
            yield return Path.Combine(userProfile, "scoop", "persist", "minecraft", ".minecraft");
        }
    }
}
