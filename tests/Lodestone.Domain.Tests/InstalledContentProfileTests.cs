using Lodestone.Domain;

namespace Lodestone.Domain.Tests;

public class InstalledContentProfileTests
{
    [Theory]
    [InlineData(Loader.Fabric, Loader.Fabric, true)]
    [InlineData(Loader.Fabric, Loader.Quilt, false)]
    [InlineData(Loader.Quilt, Loader.Fabric, false)]
    [InlineData(Loader.Forge, Loader.NeoForge, false)]
    public void A_mod_matches_only_its_own_loader_profile(Loader modLoader, Loader activeLoader, bool expected)
    {
        var mod = new InstalledContent("sodium", "Sodium", ContentType.Mod) { Loader = modLoader };

        mod.MatchesLoaderProfile(activeLoader).ShouldBe(expected);
    }

    [Theory]
    [InlineData(Loader.Fabric)]
    [InlineData(Loader.Quilt)]
    [InlineData(Loader.None)]
    public void Loader_independent_content_matches_any_profile(Loader activeLoader)
    {
        var pack = new InstalledContent("faithful", "Faithful", ContentType.ResourcePack) { Loader = Loader.None };
        var shader = new InstalledContent("complementary", "Complementary", ContentType.Shader) { Loader = Loader.None };

        pack.MatchesLoaderProfile(activeLoader).ShouldBeTrue();
        shader.MatchesLoaderProfile(activeLoader).ShouldBeTrue();
    }
}
