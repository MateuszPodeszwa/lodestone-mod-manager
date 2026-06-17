using Lodestone.Domain;
using Lodestone.Domain.Compatibility;

namespace Lodestone.Application.Compatibility.Rules;

/// <summary>
/// Flags a mod Lodestone adopted from the game folder but couldn't confidently attribute to a Minecraft
/// version — its jar didn't declare one, and there isn't exactly one matching loader profile installed to
/// pin it to. Surfaced as a warning so the user can find it under the "Unknown" filter in My Content and
/// sort it. Stays quiet when nothing is installed to sort it into, or once the item has a version.
/// </summary>
public sealed class UnsortedContentRule : ICompatibilityRule
{
    public IEnumerable<CompatibilityIssue> Evaluate(
        InstalledContent item,
        CompatibilityContext context,
        CompatibilityIndex index)
    {
        if (item.Type.UsesLoader() && item.GameVersions.Count == 0 && context.InstalledGameVersions.Count > 0)
        {
            yield return CompatibilityIssue.Warning(
                CompatibilityKind.Unsorted,
                "Lodestone couldn't tell which Minecraft version this is for — sort it under the “Unknown” filter in My Content.");
        }
    }
}
