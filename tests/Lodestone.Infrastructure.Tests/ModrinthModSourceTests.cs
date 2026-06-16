using System.Net;
using Lodestone.Application.Abstractions;
using Lodestone.Domain;
using Lodestone.Domain.Common;
using Lodestone.Infrastructure.Sources.Modrinth;
using RichardSzalay.MockHttp;

namespace Lodestone.Infrastructure.Tests;

public class ModrinthModSourceTests
{
    private static (ModrinthModSource Source, MockHttpMessageHandler Mock) Build()
    {
        var mock = new MockHttpMessageHandler();
        HttpClient http = mock.ToHttpClient();
        http.BaseAddress = new Uri("https://api.modrinth.com/");
        return (new ModrinthModSource(http), mock);
    }

    [Fact]
    public async Task Search_maps_hits_to_catalog_projects()
    {
        (ModrinthModSource source, MockHttpMessageHandler mock) = Build();
        mock.When("https://api.modrinth.com/v2/search*").Respond("application/json", """
        {
          "hits": [{
            "project_id": "AANobbMI", "slug": "sodium", "title": "Sodium",
            "description": "Fast renderer", "author": "CaffeineMC",
            "downloads": 12400000, "follows": 41000,
            "categories": ["optimization", "fabric"], "display_categories": ["optimization"],
            "versions": ["1.21.4", "1.20.1"], "project_type": "mod"
          }],
          "total_hits": 1
        }
        """);

        Result<IReadOnlyList<CatalogProject>> result =
            await source.SearchAsync(new ModSearchQuery("sodium", ContentType.Mod));

        result.IsSuccess.ShouldBeTrue();
        CatalogProject project = result.Value.Single();
        project.Id.ShouldBe("AANobbMI");
        project.Name.ShouldBe("Sodium");
        project.Loaders.ShouldContain(Loader.Fabric);
        project.Categories.ShouldContain("optimization");
        project.Categories.ShouldNotContain("fabric"); // loaders are split out of the chips
    }

    [Fact]
    public async Task GetVersions_maps_files_and_dependencies()
    {
        (ModrinthModSource source, MockHttpMessageHandler mock) = Build();
        mock.When("https://api.modrinth.com/v2/project/AANobbMI/version").Respond("application/json", """
        [{
          "id": "v1", "project_id": "AANobbMI", "version_number": "0.5.8",
          "game_versions": ["1.21.4"], "loaders": ["fabric"],
          "dependencies": [{ "project_id": "P7dR8mSH", "dependency_type": "required" }],
          "files": [{ "url": "https://cdn/sodium.jar", "filename": "sodium-0.5.8.jar",
                      "primary": true, "size": 1200000, "hashes": { "sha512": "abc123" } }],
          "date_published": "2024-06-01T00:00:00Z"
        }]
        """);

        ProjectVersion version = (await source.GetVersionsAsync("AANobbMI")).Value.Single();

        version.VersionNumber.ShouldBe("0.5.8");
        version.FileName.ShouldBe("sodium-0.5.8.jar");
        version.Sha512.ShouldBe("abc123");
        version.SupportsGameVersion(GameVersion.Parse("1.21.4")).ShouldBeTrue();
        version.Dependencies.ShouldContain(d => d.Identifier == "P7dR8mSH" && d.Kind == DependencyKind.Required);
    }

    [Fact]
    public async Task A_server_error_becomes_a_typed_failure_not_an_exception()
    {
        (ModrinthModSource source, MockHttpMessageHandler mock) = Build();
        mock.When("https://api.modrinth.com/v2/search*").Respond(HttpStatusCode.InternalServerError);

        Result<IReadOnlyList<CatalogProject>> result = await source.SearchAsync(new ModSearchQuery("x"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("modrinth.http");
    }
}
