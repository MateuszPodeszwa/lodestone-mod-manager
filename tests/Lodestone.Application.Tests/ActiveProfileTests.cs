using Lodestone.Application.Abstractions;
using Lodestone.Application.Settings;
using Lodestone.Domain;
using NSubstitute;

namespace Lodestone.Application.Tests;

public class ActiveProfileTests
{
    private static IGameInventory Inventory(params string[] installed)
    {
        var inv = Substitute.For<IGameInventory>();
        inv.InstalledVersions().Returns(installed.Select(GameVersion.Parse).ToList());
        return inv;
    }

    [Theory]
    [InlineData("all")]
    [InlineData("")]
    public void Selected_is_null_on_the_all_versions_view(string selected)
        => ActiveProfile.Selected(new LodestoneSettings { SelectedVersion = selected }).ShouldBeNull();

    [Fact]
    public void Selected_returns_the_concrete_version()
        => ActiveProfile.Selected(new LodestoneSettings { SelectedVersion = "1.20.1" })!.Value.ShouldBe("1.20.1");

    [Fact]
    public void Target_uses_the_explicit_selection_when_set()
    {
        var settings = new LodestoneSettings { SelectedVersion = "1.20.1" };

        ActiveProfile.Target(settings, Inventory("1.21.4", "1.20.1"))!.Value.ShouldBe("1.20.1");
    }

    [Fact]
    public void Target_falls_back_to_the_newest_installed_version_on_the_all_versions_view()
    {
        var settings = new LodestoneSettings { SelectedVersion = "all" };

        // InstalledVersions is newest-first, so the first entry is the newest.
        ActiveProfile.Target(settings, Inventory("1.21.4", "1.20.1"))!.Value.ShouldBe("1.21.4");
    }

    [Fact]
    public void Target_is_null_when_all_is_selected_and_nothing_is_installed()
    {
        var settings = new LodestoneSettings { SelectedVersion = "all" };

        ActiveProfile.Target(settings, Inventory()).ShouldBeNull();
    }

    [Fact]
    public void IsLoaderReady_is_true_for_mods_when_the_loader_is_installed_for_the_target()
    {
        var settings = new LodestoneSettings { SelectedVersion = "1.20.1", DefaultLoader = Loader.Fabric };
        IGameInventory inv = Inventory("1.20.1");
        inv.IsLoaderInstalled(Loader.Fabric, Arg.Any<GameVersion>()).Returns(true);

        ActiveProfile.IsLoaderReady(settings, inv, usesLoader: true).ShouldBeTrue();
    }

    [Fact]
    public void IsLoaderReady_is_false_for_mods_when_the_loader_is_not_installed_for_the_target()
    {
        var settings = new LodestoneSettings { SelectedVersion = "1.20.1", DefaultLoader = Loader.Forge };
        IGameInventory inv = Inventory("1.20.1");
        inv.IsLoaderInstalled(Arg.Any<Loader>(), Arg.Any<GameVersion>()).Returns(false);

        ActiveProfile.IsLoaderReady(settings, inv, usesLoader: true).ShouldBeFalse();
    }

    [Fact]
    public void IsLoaderReady_is_false_for_mods_when_no_loader_is_selected()
    {
        var settings = new LodestoneSettings { SelectedVersion = "1.20.1", DefaultLoader = Loader.None };
        IGameInventory inv = Inventory("1.20.1");
        inv.IsLoaderInstalled(Arg.Any<Loader>(), Arg.Any<GameVersion>()).Returns(true);

        ActiveProfile.IsLoaderReady(settings, inv, usesLoader: true).ShouldBeFalse();
    }

    [Fact]
    public void IsLoaderReady_is_true_for_loader_independent_content()
    {
        var settings = new LodestoneSettings { SelectedVersion = "1.20.1", DefaultLoader = Loader.None };
        IGameInventory inv = Inventory(); // no loader, nothing installed — still fine for packs/shaders

        ActiveProfile.IsLoaderReady(settings, inv, usesLoader: false).ShouldBeTrue();
    }

    [Fact]
    public void IsLoaderReady_is_true_for_mods_when_there_is_no_concrete_target()
    {
        // "All versions" with nothing installed → no target; gated separately at install, so ready here.
        var settings = new LodestoneSettings { SelectedVersion = "all", DefaultLoader = Loader.Forge };

        ActiveProfile.IsLoaderReady(settings, Inventory(), usesLoader: true).ShouldBeTrue();
    }

    [Fact]
    public void LoaderGateMessage_names_the_loader_and_version()
    {
        var settings = new LodestoneSettings { SelectedVersion = "1.20.1", DefaultLoader = Loader.Forge };

        ActiveProfile.LoaderGateMessage(settings, Inventory("1.20.1"))
            .ShouldBe("Install the Forge loader for 1.20.1 in Settings before adding mods.");
    }

    [Fact]
    public void LoaderGateMessage_asks_for_a_loader_when_none_is_selected()
    {
        var settings = new LodestoneSettings { SelectedVersion = "1.20.1", DefaultLoader = Loader.None };

        ActiveProfile.LoaderGateMessage(settings, Inventory("1.20.1"))
            .ShouldBe("Install a mod loader for 1.20.1 in Settings before adding mods.");
    }

    [Fact]
    public void LoaderGateMessage_drops_the_version_when_there_is_no_target()
    {
        var settings = new LodestoneSettings { SelectedVersion = "all", DefaultLoader = Loader.Forge };

        ActiveProfile.LoaderGateMessage(settings, Inventory())
            .ShouldBe("Install the Forge loader in Settings before adding mods.");
    }
}
