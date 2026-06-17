using Lodestone.Application.Compatibility;
using Lodestone.Domain;
using Lodestone.Domain.Compatibility;

namespace Lodestone.Application.Tests;

public class CompatibilityServiceTests
{
    private static readonly ICompatibilityService Service = new CompatibilityService(CompatibilityRuleSet.Default);

    private static CompatibilityReport Analyze(string id, params InstalledContent[] items)
        => Analyze(id, GameVersion.Parse("1.21.4"), Loader.Fabric, items);

    private static CompatibilityReport Analyze(string id, GameVersion? version, Loader loader, params InstalledContent[] items)
        => Service.Analyze(new CompatibilityContext(items, version, loader))[id];

    [Fact]
    public void Missing_required_dependency_is_an_error()
    {
        var iris = Make.Mod("iris", deps: [Make.Requires("fabric-api")], versions: ["1.21.4"]);

        var report = Analyze("iris", iris);

        report.HasErrors.ShouldBeTrue();
        report.Issues.ShouldContain(i => i.Kind == CompatibilityKind.MissingDependency);
    }

    [Fact]
    public void Satisfied_required_dependency_produces_no_issue()
    {
        var iris = Make.Mod("iris", deps: [Make.Requires("fabric-api")], versions: ["1.21.4"]);
        var fabric = Make.Mod("fabric-api", provides: ["fabric-api"], versions: ["1.21.4"]);

        Analyze("iris", iris, fabric).HasIssues.ShouldBeFalse();
    }

    [Fact]
    public void Outdated_required_dependency_version_is_a_warning()
    {
        var iris = Make.Mod("iris", deps: [Make.Requires("fabric-api", ">=0.100.0")], versions: ["1.21.4"]);
        var fabric = Make.Mod("fabric-api", provides: ["fabric-api"], versions: ["1.21.4"], version: "0.95.0");

        var report = Analyze("iris", iris, fabric);

        report.Issues.ShouldContain(i => i.Kind == CompatibilityKind.OutdatedDependency);
        report.HighestSeverity.ShouldBe(CompatibilitySeverity.Warning);
    }

    [Fact]
    public void Satisfied_dependency_version_range_is_clean()
    {
        var iris = Make.Mod("iris", deps: [Make.Requires("fabric-api", ">=0.100.0")], versions: ["1.21.4"]);
        var fabric = Make.Mod("fabric-api", provides: ["fabric-api"], versions: ["1.21.4"], version: "0.100.3");

        Analyze("iris", iris, fabric).HasIssues.ShouldBeFalse();
    }

    [Fact]
    public void Unparseable_dependency_range_is_never_flagged()
    {
        // A compound range we don't evaluate must not produce a false positive, even when the
        // installed version would clearly fail a naive reading of it.
        var iris = Make.Mod("iris", deps: [Make.Requires("fabric-api", ">=0.90 <0.100")], versions: ["1.21.4"]);
        var fabric = Make.Mod("fabric-api", provides: ["fabric-api"], versions: ["1.21.4"], version: "0.50.0");

        Analyze("iris", iris, fabric).Issues.ShouldNotContain(i => i.Kind == CompatibilityKind.OutdatedDependency);
    }

    [Fact]
    public void Required_dependency_present_but_disabled_is_a_warning_not_missing()
    {
        var iris = Make.Mod("iris", deps: [Make.Requires("fabric-api")], versions: ["1.21.4"]);
        var fabric = Make.Mod("fabric-api", enabled: false, provides: ["fabric-api"], versions: ["1.21.4"]);

        var report = Analyze("iris", iris, fabric);

        report.Issues.ShouldContain(i => i.Kind == CompatibilityKind.DisabledDependency);
        report.Issues.ShouldNotContain(i => i.Kind == CompatibilityKind.MissingDependency);
        report.HasErrors.ShouldBeFalse();
    }

    [Fact]
    public void Enabled_incompatible_mod_is_a_conflict_error()
    {
        var optifine = Make.Mod("optifine", deps: [Make.Breaks("sodium")], versions: ["1.21.4"]);
        var sodium = Make.Mod("sodium", provides: ["sodium"], versions: ["1.21.4"]);

        var report = Analyze("optifine", optifine, sodium);

        report.HasErrors.ShouldBeTrue();
        report.Issues.ShouldContain(i => i.Kind == CompatibilityKind.Conflict && i.RelatedName == "sodium");
    }

    [Fact]
    public void Disabled_incompatible_mod_does_not_conflict()
    {
        var optifine = Make.Mod("optifine", deps: [Make.Breaks("sodium")], versions: ["1.21.4"]);
        var sodium = Make.Mod("sodium", enabled: false, provides: ["sodium"], versions: ["1.21.4"]);

        Analyze("optifine", optifine, sodium).Issues
            .ShouldNotContain(i => i.Kind == CompatibilityKind.Conflict);
    }

    [Fact]
    public void Game_version_mismatch_is_a_warning()
    {
        var jei = Make.Mod("jei", versions: ["1.20.1"]);

        Analyze("jei", jei).Issues.ShouldContain(i => i.Kind == CompatibilityKind.GameVersionMismatch);
    }

    [Fact]
    public void Matching_game_version_is_fine_and_all_versions_view_skips_the_check()
    {
        var jei = Make.Mod("jei", versions: ["1.20.1"]);

        Analyze("jei", GameVersion.Parse("1.20.1"), Loader.Fabric, jei).HasIssues.ShouldBeFalse();
        // ActiveVersion null = "All versions" view → no version warning.
        Analyze("jei", null, Loader.Fabric, jei).Issues
            .ShouldNotContain(i => i.Kind == CompatibilityKind.GameVersionMismatch);
    }

    [Fact]
    public void Loader_mismatch_is_a_warning_but_packs_are_exempt()
    {
        var forgeMod = Make.Mod("jei", loader: Loader.Forge, versions: ["1.21.4"]);
        Analyze("jei", GameVersion.Parse("1.21.4"), Loader.Fabric, forgeMod).Issues
            .ShouldContain(i => i.Kind == CompatibilityKind.LoaderMismatch);

        var pack = Make.Pack("faithful", versions: ["1.21.4"]);
        Analyze("faithful", GameVersion.Parse("1.21.4"), Loader.Fabric, pack).HasIssues.ShouldBeFalse();
    }

    // Isolates the GameVersionNotInstalled rule: "All versions" view + Loader.None silences the
    // version-mismatch and loader-mismatch rules, so only the installed-versions check can fire.
    private static CompatibilityReport AnalyzeInstalled(string id, string[] installed, params InstalledContent[] items)
        => Service.Analyze(new CompatibilityContext(items, null, Loader.None)
        {
            InstalledGameVersions = installed.Select(GameVersion.Parse).ToList(),
        })[id];

    [Fact]
    public void Content_built_only_for_an_uninstalled_version_is_a_warning()
    {
        var sodium = Make.Mod("sodium", versions: ["1.21.4"]);

        var report = AnalyzeInstalled("sodium", ["1.20.1"], sodium);

        report.Issues.ShouldContain(i => i.Kind == CompatibilityKind.GameVersionNotInstalled);
        report.HighestSeverity.ShouldBe(CompatibilitySeverity.Warning);
    }

    [Fact]
    public void Content_runnable_on_an_installed_version_is_clean()
    {
        var sodium = Make.Mod("sodium", versions: ["1.21.4", "1.20.1"]);

        AnalyzeInstalled("sodium", ["1.20.1"], sodium).Issues
            .ShouldNotContain(i => i.Kind == CompatibilityKind.GameVersionNotInstalled);
    }

    [Fact]
    public void Not_installed_check_stays_silent_when_the_installed_set_is_unknown()
    {
        var sodium = Make.Mod("sodium", versions: ["1.21.4"]);

        // Empty installed set = "we don't know what's installed" → never flagged.
        AnalyzeInstalled("sodium", [], sodium).Issues
            .ShouldNotContain(i => i.Kind == CompatibilityKind.GameVersionNotInstalled);
    }

    [Fact]
    public void Not_installed_check_stays_silent_when_the_item_declares_no_versions()
    {
        var mystery = Make.Mod("mystery", versions: []);

        AnalyzeInstalled("mystery", ["1.20.1"], mystery).Issues
            .ShouldNotContain(i => i.Kind == CompatibilityKind.GameVersionNotInstalled);
    }

    [Fact]
    public void Unattributed_mod_is_flagged_for_sorting_when_versions_are_installed()
    {
        var mystery = Make.Mod("mystery", versions: []); // no declared version → couldn't be attributed

        var report = AnalyzeInstalled("mystery", ["1.20.1"], mystery);

        report.Issues.ShouldContain(i => i.Kind == CompatibilityKind.Unsorted);
        report.HighestSeverity.ShouldBe(CompatibilitySeverity.Warning);
    }

    [Fact]
    public void Unattributed_mod_is_not_flagged_when_nothing_is_installed_to_sort_into()
    {
        var mystery = Make.Mod("mystery", versions: []);

        AnalyzeInstalled("mystery", [], mystery).Issues
            .ShouldNotContain(i => i.Kind == CompatibilityKind.Unsorted);
    }

    [Fact]
    public void Duplicate_enabled_copy_is_a_warning()
    {
        var a = Make.Mod("sodium-1", name: "Sodium", projectId: "AANobbMI", versions: ["1.21.4"]);
        var b = Make.Mod("sodium-2", name: "Sodium", projectId: "AANobbMI", versions: ["1.21.4"]);

        Analyze("sodium-1", a, b).Issues.ShouldContain(i => i.Kind == CompatibilityKind.Duplicate);
    }

    [Fact]
    public void Unused_library_is_informational_and_a_used_one_is_clean()
    {
        var fabricUnused = Make.Mod("fabric-api", provides: ["fabric-api"], versions: ["1.21.4"], isLibrary: true);
        Analyze("fabric-api", fabricUnused).Issues
            .ShouldContain(i => i.Kind == CompatibilityKind.OrphanLibrary);

        var fabricUsed = Make.Mod("fabric-api", provides: ["fabric-api"], versions: ["1.21.4"], isLibrary: true);
        var iris = Make.Mod("iris", deps: [Make.Requires("fabric-api")], versions: ["1.21.4"]);
        Analyze("fabric-api", fabricUsed, iris).Issues
            .ShouldNotContain(i => i.Kind == CompatibilityKind.OrphanLibrary);
    }

    [Fact]
    public void Disabled_subject_item_is_always_reported_clean()
    {
        // It has a missing dependency, but because it's disabled it can't break anything.
        var iris = Make.Mod("iris", enabled: false, deps: [Make.Requires("fabric-api")], versions: ["1.20.1"]);

        Analyze("iris", iris).HasIssues.ShouldBeFalse();
    }

    [Fact]
    public void Highest_severity_reflects_the_worst_issue()
    {
        // Missing dep (error) + version mismatch (warning) on the same item.
        var iris = Make.Mod("iris", deps: [Make.Requires("fabric-api")], loader: Loader.Forge, versions: ["1.20.1"]);

        var report = Analyze("iris", iris);

        report.HighestSeverity.ShouldBe(CompatibilitySeverity.Error);
        report.Issues.Count.ShouldBeGreaterThanOrEqualTo(2);
    }
}
