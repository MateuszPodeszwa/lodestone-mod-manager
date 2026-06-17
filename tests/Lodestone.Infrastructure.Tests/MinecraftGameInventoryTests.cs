using System.IO;
using Lodestone.Application.Settings;
using Lodestone.Domain;
using Lodestone.Infrastructure.FileSystem;
using Lodestone.Infrastructure.Persistence;

namespace Lodestone.Infrastructure.Tests;

public class MinecraftGameInventoryTests
{
    private static async Task<MinecraftGameInventory> BuildAsync(TempDir dir, string gameDir)
    {
        var settings = new JsonSettingsStore(dir.File("settings.json"));
        await settings.SaveAsync(new LodestoneSettings { GameDirectory = gameDir });
        return new MinecraftGameInventory(settings);
    }

    private static void WriteManifest(string gameDir, string folder, string json)
    {
        string versionDir = Path.Combine(gameDir, "versions", folder);
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, folder + ".json"), json);
    }

    [Fact]
    public async Task Detects_vanilla_and_modded_base_versions_deduped_newest_first()
    {
        using var dir = new TempDir();
        string gameDir = dir.File("game");
        WriteManifest(gameDir, "1.20.1", """{ "id": "1.20.1", "type": "release" }""");
        WriteManifest(gameDir, "1.21.4", """{ "id": "1.21.4", "type": "release" }""");
        WriteManifest(gameDir, "fabric-loader-0.16.5-1.21.4", """{ "id": "fabric-loader-0.16.5-1.21.4", "inheritsFrom": "1.21.4" }""");

        var inventory = await BuildAsync(dir, gameDir);

        // 1.21.4 appears as both vanilla and the Fabric profile's base — collapsed to one, newest first.
        inventory.InstalledVersions().Select(v => v.Value).ShouldBe(["1.21.4", "1.20.1"]);
    }

    [Fact]
    public async Task Returns_empty_when_the_versions_folder_is_absent()
    {
        using var dir = new TempDir();
        string gameDir = dir.File("game");
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));

        (await BuildAsync(dir, gameDir)).InstalledVersions().ShouldBeEmpty();
    }

    [Fact]
    public async Task Falls_back_to_the_folder_name_when_the_manifest_is_missing()
    {
        using var dir = new TempDir();
        string gameDir = dir.File("game");
        Directory.CreateDirectory(Path.Combine(gameDir, "versions", "fabric-loader-0.16.0-1.20.1")); // no .json

        var inventory = await BuildAsync(dir, gameDir);

        inventory.InstalledVersions().Select(v => v.Value).ShouldBe(["1.20.1"]);
    }

    [Fact]
    public async Task IsVersionInstalled_reflects_the_detected_set()
    {
        using var dir = new TempDir();
        string gameDir = dir.File("game");
        WriteManifest(gameDir, "1.21.4", """{ "id": "1.21.4" }""");

        var inventory = await BuildAsync(dir, gameDir);

        inventory.IsVersionInstalled(GameVersion.Parse("1.21.4")).ShouldBeTrue();
        inventory.IsVersionInstalled(GameVersion.Parse("1.20.1")).ShouldBeFalse();
    }

    [Fact]
    public async Task InstalledProfiles_classifies_vanilla_fabric_forge_and_neoforge()
    {
        using var dir = new TempDir();
        string gameDir = dir.File("game");
        WriteManifest(gameDir, "1.20.1", """{ "id": "1.20.1", "type": "release" }""");
        WriteManifest(gameDir, "fabric-loader-0.16.5-1.21.4", """{ "id": "fabric-loader-0.16.5-1.21.4", "inheritsFrom": "1.21.4" }""");
        WriteManifest(gameDir, "1.20.1-forge-47.2.0", """{ "id": "1.20.1-forge-47.2.0", "inheritsFrom": "1.20.1" }""");
        WriteManifest(gameDir, "neoforge-21.1.65", """{ "id": "neoforge-21.1.65", "inheritsFrom": "1.21.1" }""");

        var inventory = await BuildAsync(dir, gameDir);

        IReadOnlyList<LoaderProfile> profiles = inventory.InstalledProfiles();

        profiles.ShouldContain(p => p.GameVersion.Value == "1.20.1" && p.Loader == Loader.None);
        profiles.ShouldContain(p => p.GameVersion.Value == "1.21.4" && p.Loader == Loader.Fabric);
        profiles.ShouldContain(p => p.GameVersion.Value == "1.20.1" && p.Loader == Loader.Forge);
        profiles.ShouldContain(p => p.GameVersion.Value == "1.21.1" && p.Loader == Loader.NeoForge);
    }

    [Fact]
    public async Task IsLoaderInstalled_is_true_only_for_a_present_loader_and_version_pair()
    {
        using var dir = new TempDir();
        string gameDir = dir.File("game");
        WriteManifest(gameDir, "1.21.4", """{ "id": "1.21.4", "type": "release" }""");
        WriteManifest(gameDir, "fabric-loader-0.16.5-1.21.4", """{ "id": "fabric-loader-0.16.5-1.21.4", "inheritsFrom": "1.21.4" }""");

        var inventory = await BuildAsync(dir, gameDir);

        inventory.IsLoaderInstalled(Loader.Fabric, GameVersion.Parse("1.21.4")).ShouldBeTrue();
        inventory.IsLoaderInstalled(Loader.Forge, GameVersion.Parse("1.21.4")).ShouldBeFalse();   // loader not present
        inventory.IsLoaderInstalled(Loader.Fabric, GameVersion.Parse("1.20.1")).ShouldBeFalse();  // version not present
    }

    [Fact]
    public async Task InstalledProfiles_collapses_multiple_loader_builds_for_a_version_to_the_newest()
    {
        using var dir = new TempDir();
        string gameDir = dir.File("game");
        WriteManifest(gameDir, "fabric-loader-0.16.4-1.21.4", """{ "id": "fabric-loader-0.16.4-1.21.4", "inheritsFrom": "1.21.4" }""");
        WriteManifest(gameDir, "fabric-loader-0.16.5-1.21.4", """{ "id": "fabric-loader-0.16.5-1.21.4", "inheritsFrom": "1.21.4" }""");

        var inventory = await BuildAsync(dir, gameDir);

        List<LoaderProfile> fabric = inventory.InstalledProfiles().Where(p => p.Loader == Loader.Fabric).ToList();
        fabric.Count.ShouldBe(1);
        fabric[0].VersionId.ShouldBe("fabric-loader-0.16.5-1.21.4"); // newest build kept
    }
}
