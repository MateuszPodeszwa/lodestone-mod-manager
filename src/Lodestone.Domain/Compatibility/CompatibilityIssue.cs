namespace Lodestone.Domain.Compatibility;

/// <summary>Severity ordering matters: higher wins when choosing the symbol shown next to an item.</summary>
public enum CompatibilitySeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>The specific class of problem detected. Each maps to one compatibility rule.</summary>
public enum CompatibilityKind
{
    MissingDependency,
    UnknownDependency,
    DisabledDependency,
    OutdatedDependency,
    Conflict,
    GameVersionMismatch,
    GameVersionNotInstalled,
    LoaderMismatch,
    Duplicate,
    OrphanLibrary,
    Unsorted,
}

/// <summary>
/// A single problem found for a piece of installed content. Rendered in the list as one labelled badge
/// next to the item's name (<see cref="ShortLabel"/>), with the full <see cref="Message"/> in the badge's
/// tooltip. <see cref="Glyph"/> is a plain-text marker so the headless CLI can render issues too; the WPF
/// UI maps severity/kind to its own coloured icons.
/// </summary>
public sealed record CompatibilityIssue(
    CompatibilitySeverity Severity,
    CompatibilityKind Kind,
    string Message,
    string? RelatedName = null)
{
    public string Glyph => Severity switch
    {
        CompatibilitySeverity.Error => "⛔",
        CompatibilitySeverity.Warning => "⚠",
        _ => "ⓘ",
    };

    /// <summary>
    /// A terse, badge-sized restatement of the problem for the My Content list — e.g. "Requires Fabric API",
    /// "Incompatible with X", "Library unused". Deliberately short so several badges fit on one row without
    /// clipping on narrow widths; the full sentence stays in <see cref="Message"/> (shown in the tooltip).
    /// Names the related mod (<see cref="RelatedName"/>) when the kind is about another piece of content, and
    /// falls back to a kind-only phrase when it isn't. Lives beside <see cref="Glyph"/> so the headless CLI
    /// can label issues too.
    /// </summary>
    public string ShortLabel
    {
        get
        {
            string? related = string.IsNullOrWhiteSpace(RelatedName) ? null : RelatedName!.Trim();
            return Kind switch
            {
                CompatibilityKind.MissingDependency => related is null ? "Missing dependency" : $"Requires {related}",
                CompatibilityKind.DisabledDependency => related is null ? "Dependency disabled" : $"{related} disabled",
                CompatibilityKind.OutdatedDependency => related is null ? "Dependency outdated" : $"{related} outdated",
                CompatibilityKind.UnknownDependency => related is null ? "Unknown dependency" : $"Unverified: {related}",
                CompatibilityKind.Conflict => related is null ? "Conflict" : $"Incompatible with {related}",
                CompatibilityKind.GameVersionMismatch => "Wrong MC version",
                CompatibilityKind.GameVersionNotInstalled => "Version not installed",
                CompatibilityKind.LoaderMismatch => "Wrong loader",
                CompatibilityKind.Duplicate => "Duplicate",
                CompatibilityKind.OrphanLibrary => "Library unused",
                CompatibilityKind.Unsorted => "Unsorted",
                _ => Kind.ToString(),
            };
        }
    }

    public static CompatibilityIssue Error(CompatibilityKind kind, string message, string? relatedName = null)
        => new(CompatibilitySeverity.Error, kind, message, relatedName);

    public static CompatibilityIssue Warning(CompatibilityKind kind, string message, string? relatedName = null)
        => new(CompatibilitySeverity.Warning, kind, message, relatedName);

    public static CompatibilityIssue Info(CompatibilityKind kind, string message, string? relatedName = null)
        => new(CompatibilitySeverity.Info, kind, message, relatedName);
}
