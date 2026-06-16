using Lodestone.Application.Settings;
using Lodestone.Domain;
using Lodestone.Infrastructure.Persistence;

namespace Lodestone.Infrastructure.Tests;

public class JsonSettingsStoreTests
{
    [Fact]
    public async Task Saves_and_reloads_settings()
    {
        using var dir = new TempDir();
        string path = dir.File("settings.json");

        var store = new JsonSettingsStore(path);
        await store.SaveAsync(new LodestoneSettings
        {
            GameDirectory = @"C:\game\.minecraft",
            DefaultLoader = Loader.Quilt,
            ConcurrentDownloads = 5,
            CloseToTray = true,
        });

        var reloaded = await new JsonSettingsStore(path).LoadAsync();

        reloaded.GameDirectory.ShouldBe(@"C:\game\.minecraft");
        reloaded.DefaultLoader.ShouldBe(Loader.Quilt);
        reloaded.ConcurrentDownloads.ShouldBe(5);
        reloaded.CloseToTray.ShouldBeTrue();
    }

    [Fact]
    public async Task Clamps_out_of_range_concurrency_on_load()
    {
        using var dir = new TempDir();
        string path = dir.File("settings.json");
        await System.IO.File.WriteAllTextAsync(path, """{ "concurrentDownloads": 999 }""");

        var loaded = await new JsonSettingsStore(path).LoadAsync();

        loaded.ConcurrentDownloads.ShouldBe(LodestoneSettings.MaxConcurrentDownloads);
    }

    [Fact]
    public async Task Recovers_from_a_corrupt_file_by_resetting_to_defaults()
    {
        using var dir = new TempDir();
        string path = dir.File("settings.json");
        await System.IO.File.WriteAllTextAsync(path, "{ this is not valid json ");

        var loaded = await new JsonSettingsStore(path).LoadAsync();

        loaded.ConcurrentDownloads.ShouldBe(3); // default
        System.IO.File.Exists(path + ".corrupt").ShouldBeTrue(); // quarantined
    }
}

public class JsonInstalledContentRepositoryTests
{
    [Fact]
    public async Task Round_trips_items_through_disk()
    {
        using var dir = new TempDir();
        string path = dir.File("library.json");

        using (var repo = new JsonInstalledContentRepository(path))
        {
            var sodium = new InstalledContent("sodium", "Sodium", ContentType.Mod)
            {
                Author = "CaffeineMC",
                Version = "0.5.8",
                Loader = Loader.Fabric,
                ProjectId = "AANobbMI",
                Source = "modrinth",
                GameVersions = [GameVersion.Parse("1.21.4")],
                Dependencies = [new Dependency("fabric-api", DependencyKind.Required)],
                ProvidedIds = ["sodium"],
            };
            await repo.UpsertAsync(sodium);
        }

        using var reopened = new JsonInstalledContentRepository(path);
        InstalledContent? loaded = await reopened.FindAsync("sodium");

        loaded.ShouldNotBeNull();
        loaded!.Name.ShouldBe("Sodium");
        loaded.Loader.ShouldBe(Loader.Fabric);
        loaded.GameVersions.ShouldContain(v => v.Value == "1.21.4");
        loaded.Dependencies.ShouldContain(d => d.Identifier == "fabric-api");
    }

    [Fact]
    public async Task Remove_deletes_the_item()
    {
        using var dir = new TempDir();
        using var repo = new JsonInstalledContentRepository(dir.File("library.json"));
        await repo.UpsertAsync(new InstalledContent("x", "X", ContentType.Mod));

        await repo.RemoveAsync("x");

        (await repo.GetAllAsync()).ShouldBeEmpty();
    }
}
