using System.IO;
using Lodestone.Application.Settings;
using Lodestone.Application.UseCases;
using Lodestone.Domain;
using Lodestone.Domain.Common;
using Lodestone.Infrastructure.Archives;
using Lodestone.Infrastructure.FileSystem;
using Lodestone.Infrastructure.Persistence;

namespace Lodestone.Infrastructure.Tests;

public class ReconcileLibraryUseCaseTests
{
    [Fact]
    public async Task Imports_untracked_mods_found_on_disk_and_skips_already_tracked()
    {
        using var dir = new TempDir();
        string gameDir = dir.File("game");
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
        ZipFixtures.Create(Path.Combine(gameDir, "mods", "sodium.jar"),
            ("fabric.mod.json", """{ "id": "sodium", "name": "Sodium", "version": "0.5.8" }"""));

        var settings = new JsonSettingsStore(dir.File("settings.json"));
        await settings.SaveAsync(new LodestoneSettings { GameDirectory = gameDir });

        using var repo = new JsonInstalledContentRepository(dir.File("library.json"));
        var useCase = new ReconcileLibraryUseCase(
            repo,
            new FileSystemContentInstaller(settings, dir.File("trash")),
            new ArchiveMetadataReader(),
            settings,
            new MinecraftGameLocator(),
            new MinecraftGameInventory(settings));

        Result<int> first = await useCase.ExecuteAsync(GameVersion.Parse("1.21.4"));
        first.Value.ShouldBe(1);

        IReadOnlyList<InstalledContent> items = await repo.GetAllAsync();
        items.ShouldContain(i => i.Name == "Sodium" && i.Loader == Loader.Fabric && i.Source == "local");

        // Running again finds nothing new.
        (await useCase.ExecuteAsync(GameVersion.Parse("1.21.4"))).Value.ShouldBe(0);
    }

    [Fact]
    public async Task Does_nothing_when_the_game_directory_is_invalid()
    {
        using var dir = new TempDir();
        var settings = new JsonSettingsStore(dir.File("settings.json")); // no game dir
        using var repo = new JsonInstalledContentRepository(dir.File("library.json"));
        var useCase = new ReconcileLibraryUseCase(
            repo,
            new FileSystemContentInstaller(settings, dir.File("trash")),
            new ArchiveMetadataReader(),
            settings,
            new MinecraftGameLocator(),
            new MinecraftGameInventory(settings));

        (await useCase.ExecuteAsync(GameVersion.Parse("1.21.4"))).Value.ShouldBe(0);
    }

    [Fact]
    public async Task Attributes_a_mod_to_the_only_installed_profile_for_its_loader()
    {
        using var dir = new TempDir();
        string gameDir = dir.File("game");
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
        WriteProfile(gameDir, "fabric-loader-0.16.5-1.21.4", "1.21.4"); // the only Fabric profile
        ZipFixtures.Create(Path.Combine(gameDir, "mods", "sodium.jar"),
            ("fabric.mod.json", """{ "id": "sodium", "name": "Sodium", "version": "0.5.8" }"""));

        var settings = new JsonSettingsStore(dir.File("settings.json"));
        await settings.SaveAsync(new LodestoneSettings { GameDirectory = gameDir });
        using var repo = new JsonInstalledContentRepository(dir.File("library.json"));
        var useCase = new ReconcileLibraryUseCase(
            repo, new FileSystemContentInstaller(settings, dir.File("trash")), new ArchiveMetadataReader(),
            settings, new MinecraftGameLocator(), new MinecraftGameInventory(settings));

        await useCase.ExecuteAsync(targetVersion: null); // no target — attribution comes from the profile

        InstalledContent item = (await repo.GetAllAsync()).Single(i => i.Name == "Sodium");
        item.Loader.ShouldBe(Loader.Fabric);
        item.GameVersions.Select(v => v.Value).ShouldBe(["1.21.4"]);
    }

    [Fact]
    public async Task Leaves_a_mod_unsorted_when_more_than_one_profile_shares_its_loader()
    {
        using var dir = new TempDir();
        string gameDir = dir.File("game");
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
        WriteProfile(gameDir, "fabric-loader-0.16.5-1.20.1", "1.20.1");
        WriteProfile(gameDir, "fabric-loader-0.16.5-1.21.4", "1.21.4");
        ZipFixtures.Create(Path.Combine(gameDir, "mods", "sodium.jar"),
            ("fabric.mod.json", """{ "id": "sodium", "name": "Sodium", "version": "0.5.8" }"""));

        var settings = new JsonSettingsStore(dir.File("settings.json"));
        await settings.SaveAsync(new LodestoneSettings { GameDirectory = gameDir });
        using var repo = new JsonInstalledContentRepository(dir.File("library.json"));
        var useCase = new ReconcileLibraryUseCase(
            repo, new FileSystemContentInstaller(settings, dir.File("trash")), new ArchiveMetadataReader(),
            settings, new MinecraftGameLocator(), new MinecraftGameInventory(settings));

        // Even with a target version, two Fabric profiles make it ambiguous — leave it Unknown, don't guess.
        await useCase.ExecuteAsync(GameVersion.Parse("1.21.4"));

        InstalledContent item = (await repo.GetAllAsync()).Single(i => i.Name == "Sodium");
        item.Loader.ShouldBe(Loader.Fabric);
        item.GameVersions.ShouldBeEmpty();
    }

    private static void WriteProfile(string gameDir, string folder, string inheritsFrom)
    {
        string versionDir = Path.Combine(gameDir, "versions", folder);
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, folder + ".json"), $$"""{ "id": "{{folder}}", "inheritsFrom": "{{inheritsFrom}}" }""");
    }
}
