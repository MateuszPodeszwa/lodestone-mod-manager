using Lodestone.Domain;
using Lodestone.Domain.Common;
using Lodestone.Infrastructure.Loaders;
using RichardSzalay.MockHttp;

namespace Lodestone.Infrastructure.Tests;

public class ForgeInstallerLauncherTests
{
    private static ForgeInstallerLauncher Build(MockHttpMessageHandler mock) => new(mock.ToHttpClient(), new FakeLoaderLedger());

    [Fact]
    public async Task Resolves_the_recommended_forge_installer()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json")
            .Respond("application/json", """{ "promos": { "1.20.1-recommended": "47.2.0", "1.20.1-latest": "47.3.0" } }""");

        Result<(string Url, string Version)> result =
            await Build(mock).ResolveInstallerAsync(Loader.Forge, GameVersion.Parse("1.20.1"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe("47.2.0"); // recommended preferred over latest
        result.Value.Url.ShouldBe("https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-installer.jar");
    }

    [Fact]
    public async Task Falls_back_to_latest_when_there_is_no_recommended_forge()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("*promotions_slim.json").Respond("application/json", """{ "promos": { "1.21.1-latest": "52.0.1" } }""");

        Result<(string Url, string Version)> result =
            await Build(mock).ResolveInstallerAsync(Loader.Forge, GameVersion.Parse("1.21.1"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe("52.0.1");
    }

    [Fact]
    public async Task Reports_no_version_when_forge_lacks_a_build()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("*promotions_slim.json").Respond("application/json", """{ "promos": { "1.20.1-recommended": "47.2.0" } }""");

        Result<(string Url, string Version)> result =
            await Build(mock).ResolveInstallerAsync(Loader.Forge, GameVersion.Parse("1.99.9"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("loader.no_version");
    }

    [Fact]
    public async Task Resolves_the_newest_matching_neoforge_installer()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml")
            .Respond("application/xml",
                "<metadata><versioning><versions>" +
                "<version>21.0.167</version><version>21.1.10</version><version>21.1.65</version>" +
                "</versions></versioning></metadata>");

        Result<(string Url, string Version)> result =
            await Build(mock).ResolveInstallerAsync(Loader.NeoForge, GameVersion.Parse("1.21.1"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe("21.1.65"); // newest with the 21.1 prefix; the 21.0.x build is excluded
        result.Value.Url.ShouldBe("https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.65/neoforge-21.1.65-installer.jar");
    }

    [Fact]
    public async Task Resolves_neoforge_for_a_calendar_versioned_minecraft()
    {
        // Minecraft's calendar versions (e.g. "26.2") no longer start with "1.", and NeoForge appends its
        // build to the full version ("26.2.0.1-beta") rather than dropping a leading "1.". The resolver must
        // match on the version verbatim, and "26.20.x" must not be mistaken for a "26.2" build.
        var mock = new MockHttpMessageHandler();
        mock.When("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml")
            .Respond("application/xml",
                "<metadata><versioning><versions>" +
                "<version>21.9.16-beta</version><version>26.2.0.0-beta</version>" +
                "<version>26.2.0.1-beta</version><version>26.20.0.0-beta</version>" +
                "</versions></versioning></metadata>");

        Result<(string Url, string Version)> result =
            await Build(mock).ResolveInstallerAsync(Loader.NeoForge, GameVersion.Parse("26.2"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe("26.2.0.1-beta"); // newest "26.2." build; 21.9.x and 26.20.x excluded
        result.Value.Url.ShouldBe("https://maven.neoforged.net/releases/net/neoforged/neoforge/26.2.0.1-beta/neoforge-26.2.0.1-beta-installer.jar");
    }

    [Fact]
    public async Task Reports_no_version_when_neoforge_lacks_a_build_for_the_version()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("*maven-metadata.xml")
            .Respond("application/xml", "<metadata><versioning><versions><version>21.1.65</version></versions></versioning></metadata>");

        Result<(string Url, string Version)> result =
            await Build(mock).ResolveInstallerAsync(Loader.NeoForge, GameVersion.Parse("26.2"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("loader.no_version");
    }
}
