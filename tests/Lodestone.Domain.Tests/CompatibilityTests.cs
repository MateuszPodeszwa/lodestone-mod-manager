using Lodestone.Domain.Compatibility;

namespace Lodestone.Domain.Tests;

public class CompatibilityIssueTests
{
    [Fact]
    public void Error_factory_sets_severity_and_glyph()
    {
        var issue = CompatibilityIssue.Error(CompatibilityKind.MissingDependency, "Requires Fabric API", "Fabric API");

        issue.Severity.ShouldBe(CompatibilitySeverity.Error);
        issue.Glyph.ShouldBe("⛔");
        issue.RelatedName.ShouldBe("Fabric API");
    }

    [Fact]
    public void Warning_and_info_factories_set_their_severities()
    {
        CompatibilityIssue.Warning(CompatibilityKind.GameVersionMismatch, "x").Severity
            .ShouldBe(CompatibilitySeverity.Warning);
        CompatibilityIssue.Info(CompatibilityKind.OrphanLibrary, "x").Severity
            .ShouldBe(CompatibilitySeverity.Info);
    }

    [Theory]
    [InlineData(CompatibilityKind.MissingDependency, "Fabric API", "Requires Fabric API")]
    [InlineData(CompatibilityKind.DisabledDependency, "Fabric API", "Fabric API disabled")]
    [InlineData(CompatibilityKind.OutdatedDependency, "Fabric API", "Fabric API outdated")]
    [InlineData(CompatibilityKind.UnknownDependency, "Some Mod", "Unverified: Some Mod")]
    [InlineData(CompatibilityKind.Conflict, "OptiFine", "Incompatible with OptiFine")]
    public void ShortLabel_names_the_related_mod_when_present(CompatibilityKind kind, string related, string expected)
        => new CompatibilityIssue(CompatibilitySeverity.Warning, kind, "full message", related)
            .ShortLabel.ShouldBe(expected);

    [Theory]
    [InlineData(CompatibilityKind.GameVersionMismatch, "Wrong MC version")]
    [InlineData(CompatibilityKind.GameVersionNotInstalled, "Version not installed")]
    [InlineData(CompatibilityKind.LoaderMismatch, "Wrong loader")]
    [InlineData(CompatibilityKind.Duplicate, "Duplicate")]
    [InlineData(CompatibilityKind.OrphanLibrary, "Library unused")]
    [InlineData(CompatibilityKind.Unsorted, "Unsorted")]
    public void ShortLabel_uses_a_kind_only_phrase_when_no_related_mod(CompatibilityKind kind, string expected)
        => new CompatibilityIssue(CompatibilitySeverity.Warning, kind, "full message").ShortLabel.ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShortLabel_falls_back_to_a_kind_phrase_when_related_name_is_blank(string? related)
        => new CompatibilityIssue(CompatibilitySeverity.Error, CompatibilityKind.MissingDependency, "msg", related)
            .ShortLabel.ShouldBe("Missing dependency");

    [Fact]
    public void ShortLabel_trims_whitespace_around_the_related_name()
        => new CompatibilityIssue(CompatibilitySeverity.Error, CompatibilityKind.Conflict, "msg", "  Sodium  ")
            .ShortLabel.ShouldBe("Incompatible with Sodium");
}

public class CompatibilityReportTests
{
    [Fact]
    public void Clean_report_has_no_issues_and_no_highest_severity()
    {
        var report = CompatibilityReport.Clean("sodium");

        report.HasIssues.ShouldBeFalse();
        report.HasErrors.ShouldBeFalse();
        report.HighestSeverity.ShouldBeNull();
    }

    [Fact]
    public void Highest_severity_is_the_worst_present()
    {
        var report = new CompatibilityReportBuilder("iris")
            .Add(CompatibilityIssue.Info(CompatibilityKind.OrphanLibrary, "info"))
            .Add(CompatibilityIssue.Error(CompatibilityKind.Conflict, "conflict"))
            .Add(CompatibilityIssue.Warning(CompatibilityKind.Duplicate, "dupe"))
            .Build();

        report.HasIssues.ShouldBeTrue();
        report.HasErrors.ShouldBeTrue();
        report.HighestSeverity.ShouldBe(CompatibilitySeverity.Error);
        report.Issues.Count.ShouldBe(3);
    }
}
