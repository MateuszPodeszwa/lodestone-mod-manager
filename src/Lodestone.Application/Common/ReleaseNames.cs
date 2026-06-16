namespace Lodestone.Application.Common;

/// <summary>
/// Friendly per-release codenames, in the spirit of Minecraft's update names (e.g. "The Flattening").
/// Looked up by version with any pre-release/build suffix ignored, so <c>0.1.0</c>, <c>0.1.0-beta</c>
/// and <c>0.1.0+abc123</c> all resolve to the same name.
///
/// To name a release, add an entry keyed by its <c>MAJOR.MINOR.PATCH</c> string. Keep them short and fun.
/// </summary>
public static class ReleaseNames
{
    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["0.1.0"] = "Spawn Point",
        };

    /// <summary>The codename for a version, or null when the release hasn't been named.</summary>
    public static string? For(string? version)
        => Normalize(version) is { } key && Names.TryGetValue(key, out string? name) ? name : null;

    // Drop the "-prerelease" and "+build" suffixes so a beta resolves to its release name.
    private static string? Normalize(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        string trimmed = version.Trim();
        int cut = trimmed.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            trimmed = trimmed[..cut];
        }

        return trimmed.Length == 0 ? null : trimmed;
    }
}
