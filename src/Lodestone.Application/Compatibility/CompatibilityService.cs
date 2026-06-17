using Lodestone.Application.Compatibility.Rules;
using Lodestone.Domain;
using Lodestone.Domain.Compatibility;

namespace Lodestone.Application.Compatibility;

/// <summary>
/// Runs the compatibility rule pipeline over the library. Disabled items are reported as clean (they
/// don't load, so they can't break anything); enabled items are passed through every rule and the
/// resulting issues collected into one report each.
/// </summary>
public sealed class CompatibilityService : ICompatibilityService
{
    private readonly IReadOnlyList<ICompatibilityRule> _rules;

    public CompatibilityService(IEnumerable<ICompatibilityRule> rules)
        => _rules = rules as IReadOnlyList<ICompatibilityRule> ?? rules.ToList();

    public IReadOnlyDictionary<string, CompatibilityReport> Analyze(CompatibilityContext context)
    {
        var index = new CompatibilityIndex(context.Items);
        var reports = new Dictionary<string, CompatibilityReport>(StringComparer.Ordinal);

        foreach (InstalledContent item in context.Items)
        {
            if (!item.Enabled)
            {
                reports[item.Id] = CompatibilityReport.Clean(item.Id);
                continue;
            }

            var builder = new CompatibilityReportBuilder(item.Id);
            foreach (ICompatibilityRule rule in _rules)
            {
                builder.AddRange(rule.Evaluate(item, context, index));
            }

            reports[item.Id] = builder.Build();
        }

        return reports;
    }
}

/// <summary>The default, ordered rule pipeline. Centralised so DI and tests share one source of truth.</summary>
public static class CompatibilityRuleSet
{
    public static IReadOnlyList<ICompatibilityRule> Default { get; } =
    [
        new MissingRequiredDependencyRule(),
        new DisabledDependencyRule(),
        new DependencyVersionRule(),
        new IncompatibleModRule(),
        new GameVersionMismatchRule(),
        new GameVersionNotInstalledRule(),
        new LoaderMismatchRule(),
        new DuplicateRule(),
        new OrphanLibraryRule(),
        new UnsortedContentRule(),
    ];
}
