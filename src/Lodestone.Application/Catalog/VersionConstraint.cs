using System.Text.RegularExpressions;

namespace Lodestone.Application.Catalog;

/// <summary>Whether an installed version meets a declared constraint, or whether we can't tell.</summary>
public enum VersionSatisfaction
{
    /// <summary>The constraint is met, or imposes no real requirement (e.g. <c>*</c>).</summary>
    Satisfied,

    /// <summary>The installed version definitively fails a constraint we fully understand.</summary>
    Violated,

    /// <summary>The constraint (or version) is too complex/odd to evaluate safely — never flagged.</summary>
    Unknown,
}

/// <summary>
/// A deliberately conservative checker for mod-loader dependency version constraints. It only
/// evaluates a single, simple comparator (<c>&gt;=</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&lt;</c>) against a
/// clean numeric version; wildcards (<c>*</c>/<c>any</c>) count as satisfied, and anything richer
/// (compound ranges, <c>~</c>/<c>^</c>, <c>x</c> wildcards, build metadata in the bound) returns
/// <see cref="VersionSatisfaction.Unknown"/> so it is never reported as a problem. This keeps false
/// positives out: we warn only when we are certain.
/// </summary>
public static partial class VersionConstraint
{
    public static VersionSatisfaction Check(string? constraint, string? installedVersion)
    {
        if (string.IsNullOrWhiteSpace(constraint) || string.IsNullOrWhiteSpace(installedVersion))
        {
            return VersionSatisfaction.Unknown;
        }

        string text = constraint.Trim();
        if (text is "*" or "any" || text.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            return VersionSatisfaction.Satisfied;
        }

        Match match = ComparatorPattern().Match(text);
        if (!match.Success)
        {
            return VersionSatisfaction.Unknown; // bare version, compound range, wildcard, etc.
        }

        string bound = match.Groups["ver"].Value;
        if (!SimpleVersionPattern().IsMatch(bound))
        {
            return VersionSatisfaction.Unknown; // bound isn't a plain dotted number — don't guess
        }

        int comparison = VersionComparer.CompareNumeric(installedVersion, bound);
        bool ok = match.Groups["op"].Value switch
        {
            ">=" => comparison >= 0,
            ">" => comparison > 0,
            "<=" => comparison <= 0,
            "<" => comparison < 0,
            _ => true,
        };

        return ok ? VersionSatisfaction.Satisfied : VersionSatisfaction.Violated;
    }

    [GeneratedRegex(@"^(?<op>>=|<=|>|<)\s*v?(?<ver>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ComparatorPattern();

    [GeneratedRegex(@"^[0-9]+(\.[0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleVersionPattern();
}
