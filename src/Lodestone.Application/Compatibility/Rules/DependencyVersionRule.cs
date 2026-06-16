using Lodestone.Application.Catalog;
using Lodestone.Domain;
using Lodestone.Domain.Compatibility;

namespace Lodestone.Application.Compatibility.Rules;

/// <summary>
/// Flags a required dependency that is installed and enabled but whose version doesn't satisfy the
/// declared constraint (e.g. "requires Fabric API &gt;=0.100.0, but 0.95.0 is installed"). It is
/// intentionally cautious: it only acts when the constraint is one we fully understand (see
/// <see cref="VersionConstraint"/>) and warns only when <em>every</em> enabled provider definitively
/// fails it — an absent or disabled dependency is left to the other rules, and an unverifiable
/// constraint produces nothing. The result is a warning, never a hard error.
/// </summary>
public sealed class DependencyVersionRule : ICompatibilityRule
{
    public IEnumerable<CompatibilityIssue> Evaluate(
        InstalledContent item,
        CompatibilityContext context,
        CompatibilityIndex index)
    {
        foreach (Dependency dep in item.Dependencies)
        {
            if (dep.Kind != DependencyKind.Required ||
                string.IsNullOrWhiteSpace(dep.Identifier) ||
                string.IsNullOrWhiteSpace(dep.VersionRange))
            {
                continue;
            }

            List<InstalledContent> providers = index.Resolve(dep.Identifier)
                .Where(p => p.Enabled && !ReferenceEquals(p, item))
                .ToList();
            if (providers.Count == 0)
            {
                continue; // missing or disabled — handled by the dedicated rules
            }

            // Give the benefit of the doubt: only warn when no enabled provider satisfies the
            // constraint and at least one is a definite violation (the rest being merely unverifiable).
            bool anyAcceptable = providers.Any(p => VersionConstraint.Check(dep.VersionRange, p.Version) != VersionSatisfaction.Violated);
            if (anyAcceptable)
            {
                continue;
            }

            InstalledContent provider = providers[0];
            yield return CompatibilityIssue.Warning(
                CompatibilityKind.OutdatedDependency,
                $"Requires {dep.Label} {dep.VersionRange}, but {provider.Version} is installed.",
                provider.Name);
        }
    }
}
