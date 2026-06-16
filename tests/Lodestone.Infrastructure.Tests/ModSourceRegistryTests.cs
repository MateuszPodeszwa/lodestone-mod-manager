using Lodestone.Application.Abstractions;
using Lodestone.Application.Settings;
using Lodestone.Domain;
using Lodestone.Domain.Common;
using Lodestone.Infrastructure.Persistence;
using Lodestone.Infrastructure.Sources;

namespace Lodestone.Infrastructure.Tests;

public class ModSourceRegistryTests
{
    private sealed class FakeSource(string name, bool configured) : IModSource
    {
        public string Name => name;
        public bool IsConfigured => configured;
        public Task<Result<IReadOnlyList<CatalogProject>>> SearchAsync(ModSearchQuery q, CancellationToken ct = default)
            => Task.FromResult(Result.Success<IReadOnlyList<CatalogProject>>([]));
        public Task<Result<CatalogProject>> GetProjectAsync(string id, CancellationToken ct = default)
            => Task.FromResult(Result.Failure<CatalogProject>("x", "y"));
        public Task<Result<IReadOnlyList<ProjectVersion>>> GetVersionsAsync(string id, CancellationToken ct = default)
            => Task.FromResult(Result.Success<IReadOnlyList<ProjectVersion>>([]));
    }

    private static async Task<JsonSettingsStore> SettingsAsync(TempDir dir, bool curseForgeFallback)
    {
        var store = new JsonSettingsStore(dir.File("settings.json"));
        await store.SaveAsync(new LodestoneSettings { CurseForgeFallback = curseForgeFallback });
        return store;
    }

    [Fact]
    public async Task Primary_is_modrinth()
    {
        using var dir = new TempDir();
        var registry = new ModSourceRegistry(
            [new FakeSource("modrinth", true), new FakeSource("curseforge", false)],
            await SettingsAsync(dir, curseForgeFallback: true));

        registry.Primary.Name.ShouldBe("modrinth");
    }

    [Fact]
    public async Task Unconfigured_curseforge_is_not_active_even_with_fallback_on()
    {
        using var dir = new TempDir();
        var registry = new ModSourceRegistry(
            [new FakeSource("modrinth", true), new FakeSource("curseforge", false)],
            await SettingsAsync(dir, curseForgeFallback: true));

        registry.GetActiveSources().Select(s => s.Name).ShouldBe(["modrinth"]);
    }

    [Fact]
    public async Task Configured_curseforge_is_appended_only_when_fallback_is_on()
    {
        using var dir = new TempDir();

        var withFallback = new ModSourceRegistry(
            [new FakeSource("modrinth", true), new FakeSource("curseforge", true)],
            await SettingsAsync(dir, curseForgeFallback: true));
        withFallback.GetActiveSources().Select(s => s.Name).ShouldBe(["modrinth", "curseforge"]);

        using var dir2 = new TempDir();
        var withoutFallback = new ModSourceRegistry(
            [new FakeSource("modrinth", true), new FakeSource("curseforge", true)],
            await SettingsAsync(dir2, curseForgeFallback: false));
        withoutFallback.GetActiveSources().Select(s => s.Name).ShouldBe(["modrinth"]);
    }
}
