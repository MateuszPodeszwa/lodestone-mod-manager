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
/// A single problem found for a piece of installed content. Rendered in the list as a symbol next
/// to the item's name, with the full <see cref="Message"/> in the tooltip. <see cref="Glyph"/> is a
/// plain-text marker so the headless CLI can render issues too; the WPF UI maps severity/kind to its
/// own coloured icons.
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

    public static CompatibilityIssue Error(CompatibilityKind kind, string message, string? relatedName = null)
        => new(CompatibilitySeverity.Error, kind, message, relatedName);

    public static CompatibilityIssue Warning(CompatibilityKind kind, string message, string? relatedName = null)
        => new(CompatibilitySeverity.Warning, kind, message, relatedName);

    public static CompatibilityIssue Info(CompatibilityKind kind, string message, string? relatedName = null)
        => new(CompatibilitySeverity.Info, kind, message, relatedName);
}
