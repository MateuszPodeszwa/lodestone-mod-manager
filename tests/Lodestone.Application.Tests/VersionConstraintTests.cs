using Lodestone.Application.Catalog;

namespace Lodestone.Application.Tests;

public class VersionConstraintTests
{
    [Theory]
    [InlineData(">=0.100.0", "0.100.0", VersionSatisfaction.Satisfied)]
    [InlineData(">=0.100.0", "0.100.5", VersionSatisfaction.Satisfied)]
    [InlineData(">=0.100.0", "0.95.0", VersionSatisfaction.Violated)]
    [InlineData(">0.100.0", "0.100.0", VersionSatisfaction.Violated)]
    [InlineData("<=1.0.0", "1.0.0", VersionSatisfaction.Satisfied)]
    [InlineData("<1.0.0", "1.0.0", VersionSatisfaction.Violated)]
    [InlineData(">=1.0", "1.0.0", VersionSatisfaction.Satisfied)]                 // 1.0 == 1.0.0 numerically
    [InlineData(">=0.100.0", "0.100.0+1.21.4", VersionSatisfaction.Satisfied)]   // build metadata on the installed version
    public void Evaluates_simple_comparators(string constraint, string installed, VersionSatisfaction expected)
        => VersionConstraint.Check(constraint, installed).ShouldBe(expected);

    [Theory]
    [InlineData("*")]
    [InlineData("any")]
    public void Wildcards_impose_no_constraint(string constraint)
        => VersionConstraint.Check(constraint, "0.1.0").ShouldBe(VersionSatisfaction.Satisfied);

    [Theory]
    [InlineData(">=0.90 <0.100", "0.50.0")] // compound range
    [InlineData("~1.2", "1.0.0")]           // tilde
    [InlineData("^1.2", "1.0.0")]           // caret
    [InlineData("1.21.x", "1.0.0")]         // wildcard component
    [InlineData("1.0.0", "0.1.0")]          // bare version, no comparator
    [InlineData(null, "1.0.0")]
    [InlineData("", "1.0.0")]
    public void Ambiguous_or_absent_constraints_are_unknown(string? constraint, string installed)
        => VersionConstraint.Check(constraint, installed).ShouldBe(VersionSatisfaction.Unknown);

    [Fact]
    public void Missing_installed_version_is_unknown()
        => VersionConstraint.Check(">=1.0.0", null).ShouldBe(VersionSatisfaction.Unknown);
}
