using Lodestone.Domain;
using Lodestone.Domain.Compatibility;

namespace Lodestone.Application.Compatibility.Rules;

/// <summary>
/// Flags installed content that can't run on <em>any</em> Minecraft version the user actually has —
/// e.g. a mod built only for 1.21.4 when only 1.20.1 is installed. This is the "warn, don't block"
/// counterpart to the loader install gate: mod installs are allowed (you may be pre-staging), but the
/// problem is surfaced in My Content rather than failing silently. Stays quiet when the installed set
/// is unknown (empty) or the item declares no versions (see <see cref="GameVersionMismatchRule"/>).
/// </summary>
public sealed class GameVersionNotInstalledRule : ICompatibilityRule
{
    public IEnumerable<CompatibilityIssue> Evaluate(
        InstalledContent item,
        CompatibilityContext context,
        CompatibilityIndex index)
    {
        if (context.InstalledGameVersions.Count == 0 || item.GameVersions.Count == 0)
        {
            yield break;
        }

        bool runnableOnSomething = item.GameVersions.Any(
            declared => context.InstalledGameVersions.Any(installed => installed.Equals(declared)));
        if (!runnableOnSomething)
        {
            yield return CompatibilityIssue.Warning(
                CompatibilityKind.GameVersionNotInstalled,
                $"Built for {string.Join(", ", item.GameVersions)}, but none of those Minecraft versions are installed.");
        }
    }
}
