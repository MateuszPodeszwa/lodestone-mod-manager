using Lodestone.Application.Library;
using Lodestone.Domain;

namespace Lodestone.Application.Tests;

public class LibraryQueryTests
{
    private static readonly InstalledContent[] Library =
    [
        Make.Mod("sodium", name: "Sodium", versions: ["1.21.4", "1.20.1"]),
        Make.Mod("jei", name: "Just Enough Items", versions: ["1.20.1"]),
        Make.Pack("faithful", name: "Faithful 32x", versions: ["1.21.4"]),
    ];

    [Fact]
    public void Filters_by_content_type()
    {
        var mods = LibraryQuery.Apply(Library, new LibraryFilter(ContentType.Mod));

        mods.Count.ShouldBe(2);
        mods.ShouldAllBe(i => i.Type == ContentType.Mod);
    }

    [Fact]
    public void Filters_by_game_version()
    {
        var result = LibraryQuery.Apply(Library, new LibraryFilter(ContentType.Mod, GameVersion.Parse("1.21.4")));

        result.Select(i => i.Id).ShouldBe(["sodium"]);
    }

    [Fact]
    public void Search_matches_name_case_insensitively()
    {
        var result = LibraryQuery.Apply(Library, new LibraryFilter(ContentType.Mod, Search: "enough"));

        result.Single().Id.ShouldBe("jei");
    }

    [Fact]
    public void Filters_by_loader_to_isolate_a_profile()
    {
        InstalledContent[] library =
        [
            Make.Mod("sodium", loader: Loader.Fabric, versions: ["1.20.1"]),
            Make.Mod("create", loader: Loader.Forge, versions: ["1.20.1"]),
        ];

        var result = LibraryQuery.Apply(
            library,
            new LibraryFilter(ContentType.Mod, GameVersion.Parse("1.20.1"), Loader: Loader.Fabric));

        result.Select(i => i.Id).ShouldBe(["sodium"]);
    }

    [Fact]
    public void Combined_filters_apply_together()
    {
        var result = LibraryQuery.Apply(
            Library,
            new LibraryFilter(ContentType.Mod, GameVersion.Parse("1.20.1"), "just"));

        result.Single().Id.ShouldBe("jei");
    }
}
