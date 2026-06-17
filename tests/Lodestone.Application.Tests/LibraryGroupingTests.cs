using Lodestone.Application.Library;

namespace Lodestone.Application.Tests;

public class LibraryGroupingTests
{
    [Fact]
    public void Groups_items_under_their_primary_category()
    {
        var items = new[]
        {
            Make.Mod("sodium", categories: ["optimization"]),
            Make.Mod("jei", categories: ["utility"]),
            Make.Mod("lithium", categories: ["optimization"]),
        };

        var groups = LibraryGrouping.ByPrimaryCategory(items);

        groups.Select(g => g.Key).ShouldBe(["optimization", "utility"]);
        groups.Single(g => g.Key == "optimization").Items.Select(i => i.Id).ShouldBe(["sodium", "lithium"]);
        groups.Single(g => g.Key == "utility").Items.Select(i => i.Id).ShouldBe(["jei"]);
    }

    [Fact]
    public void Places_a_multi_tag_item_only_under_its_first_category()
    {
        var items = new[]
        {
            Make.Mod("iris", categories: ["optimization", "shaders"]),
            Make.Mod("complementary", categories: ["shaders"]),
        };

        var groups = LibraryGrouping.ByPrimaryCategory(items);

        // Iris is tagged optimization + shaders but appears once, under its primary (first) tag only.
        groups.Single(g => g.Key == "optimization").Items.Select(i => i.Id).ShouldBe(["iris"]);
        groups.Single(g => g.Key == "shaders").Items.Select(i => i.Id).ShouldBe(["complementary"]);
        groups.SelectMany(g => g.Items).Count(i => i.Id == "iris").ShouldBe(1);
    }

    [Fact]
    public void Sorts_sections_alphabetically_by_slug()
    {
        var items = new[]
        {
            Make.Mod("create", categories: ["tech"]),
            Make.Mod("xaeros", categories: ["adventure"]),
            Make.Mod("sodium", categories: ["optimization"]),
        };

        var groups = LibraryGrouping.ByPrimaryCategory(items);

        groups.Select(g => g.Key).ShouldBe(["adventure", "optimization", "tech"]);
    }

    [Fact]
    public void Puts_uncategorized_items_in_a_trailing_bucket()
    {
        var items = new[]
        {
            Make.Mod("local-jar"),                               // no categories at all
            Make.Mod("sodium", categories: ["optimization"]),
            Make.Mod("blank", categories: ["   "]),              // only blank/whitespace categories
        };

        var groups = LibraryGrouping.ByPrimaryCategory(items);

        // Uncategorized is always last, regardless of slug ordering.
        groups[^1].Key.ShouldBe(LibraryGrouping.UncategorizedKey);
        groups[^1].Items.Select(i => i.Id).ShouldBe(["local-jar", "blank"]);
    }

    [Fact]
    public void Normalises_category_case_and_whitespace_so_variants_group_together()
    {
        var items = new[]
        {
            Make.Mod("a", categories: ["Optimization"]),
            Make.Mod("b", categories: ["  optimization  "]),
        };

        var groups = LibraryGrouping.ByPrimaryCategory(items);

        groups.Count.ShouldBe(1);
        groups[0].Key.ShouldBe("optimization");
        groups[0].Items.Select(i => i.Id).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void Preserves_incoming_order_within_a_section()
    {
        var items = new[]
        {
            Make.Mod("third", categories: ["optimization"]),
            Make.Mod("first", categories: ["optimization"]),
            Make.Mod("second", categories: ["optimization"]),
        };

        var groups = LibraryGrouping.ByPrimaryCategory(items);

        groups.Single().Items.Select(i => i.Id).ShouldBe(["third", "first", "second"]);
    }

    [Fact]
    public void Returns_no_sections_for_an_empty_library()
    {
        LibraryGrouping.ByPrimaryCategory([]).ShouldBeEmpty();
    }
}
