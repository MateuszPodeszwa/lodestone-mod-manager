using Lodestone.Application.Abstractions;
using Lodestone.Application.Settings;
using Lodestone.Domain;
using Lodestone.Domain.Common;
using Lodestone.Infrastructure.FileSystem;
using Lodestone.Infrastructure.Persistence;

namespace Lodestone.Infrastructure.Tests;

public class FileSystemContentInstallerTests
{
    private static async Task<(FileSystemContentInstaller Installer, string GameDir, string Trash)> BuildAsync(TempDir dir)
    {
        string gameDir = dir.File("game");
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
        string trash = dir.File("trash");

        var settings = new JsonSettingsStore(dir.File("settings.json"));
        await settings.SaveAsync(new LodestoneSettings { GameDirectory = gameDir });

        return (new FileSystemContentInstaller(settings, trash), gameDir, trash);
    }

    private static string MakeSourceJar(TempDir dir, string name = "sodium-0.5.8.jar")
    {
        string src = dir.File(name);
        File.WriteAllText(src, "fake jar bytes");
        return src;
    }

    [Fact]
    public async Task Place_copies_into_the_mods_folder()
    {
        using var dir = new TempDir();
        (FileSystemContentInstaller installer, string gameDir, _) = await BuildAsync(dir);

        Result<PlaceResult> result = await installer.PlaceAsync(MakeSourceJar(dir), ContentType.Mod);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FileName.ShouldBe("sodium-0.5.8.jar");
        File.Exists(Path.Combine(gameDir, "mods", "sodium-0.5.8.jar")).ShouldBeTrue();
    }

    [Fact]
    public async Task Disable_then_enable_toggles_the_disabled_suffix()
    {
        using var dir = new TempDir();
        (FileSystemContentInstaller installer, string gameDir, _) = await BuildAsync(dir);
        await installer.PlaceAsync(MakeSourceJar(dir), ContentType.Mod);

        Result<string> disabled = await installer.SetEnabledAsync(ContentType.Mod, "sodium-0.5.8.jar", enabled: false);
        disabled.Value.ShouldBe("sodium-0.5.8.jar.disabled");
        File.Exists(Path.Combine(gameDir, "mods", "sodium-0.5.8.jar.disabled")).ShouldBeTrue();
        File.Exists(Path.Combine(gameDir, "mods", "sodium-0.5.8.jar")).ShouldBeFalse();

        Result<string> enabled = await installer.SetEnabledAsync(ContentType.Mod, "sodium-0.5.8.jar.disabled", enabled: true);
        enabled.Value.ShouldBe("sodium-0.5.8.jar");
        File.Exists(Path.Combine(gameDir, "mods", "sodium-0.5.8.jar")).ShouldBeTrue();
    }

    [Fact]
    public async Task Remove_soft_deletes_into_trash()
    {
        using var dir = new TempDir();
        (FileSystemContentInstaller installer, string gameDir, string trash) = await BuildAsync(dir);
        await installer.PlaceAsync(MakeSourceJar(dir), ContentType.Mod);

        Result removed = await installer.RemoveAsync(ContentType.Mod, "sodium-0.5.8.jar");

        removed.IsSuccess.ShouldBeTrue();
        File.Exists(Path.Combine(gameDir, "mods", "sodium-0.5.8.jar")).ShouldBeFalse();
        Directory.GetFiles(trash).ShouldNotBeEmpty(); // recoverable
    }

    [Fact]
    public async Task Duplicate_fail_is_rejected_but_keep_both_creates_a_unique_name()
    {
        using var dir = new TempDir();
        (FileSystemContentInstaller installer, _, _) = await BuildAsync(dir);
        await installer.PlaceAsync(MakeSourceJar(dir), ContentType.Mod);

        Result<PlaceResult> failed = await installer.PlaceAsync(MakeSourceJar(dir), ContentType.Mod, DuplicateResolution.Fail);
        failed.IsFailure.ShouldBeTrue();

        Result<PlaceResult> kept = await installer.PlaceAsync(MakeSourceJar(dir), ContentType.Mod, DuplicateResolution.KeepBoth);
        kept.IsSuccess.ShouldBeTrue();
        kept.Value.FileName.ShouldNotBe("sodium-0.5.8.jar");
    }

    [Fact]
    public async Task Place_fails_clearly_when_no_game_directory_is_set()
    {
        using var dir = new TempDir();
        var settings = new JsonSettingsStore(dir.File("settings.json")); // GameDirectory null
        var installer = new FileSystemContentInstaller(settings, dir.File("trash"));

        Result<PlaceResult> result = await installer.PlaceAsync(MakeSourceJar(dir), ContentType.Mod);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("game.dir_missing");
    }
}
