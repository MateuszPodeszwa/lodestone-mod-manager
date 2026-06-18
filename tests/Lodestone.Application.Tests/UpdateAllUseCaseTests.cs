using Lodestone.Application.Abstractions;
using Lodestone.Application.Catalog;
using Lodestone.Application.Settings;
using Lodestone.Application.UseCases;
using Lodestone.Domain;
using Lodestone.Domain.Common;
using NSubstitute;

namespace Lodestone.Application.Tests;

public class UpdateAllUseCaseTests
{
    private static InstalledContent Updatable(string id, string version = "1.0.0")
    {
        InstalledContent m = Make.Mod(id, projectId: id, versions: ["1.21.4"], version: version);
        m.Source = "modrinth";
        m.UpdateAvailable = true;
        return m;
    }

    private static ProjectVersion NewerBuild(string id) => new(
        $"{id}-v2", id, "9.9.9", ContentType.Mod,
        [GameVersion.Parse("1.21.4")], [Loader.Fabric], [], $"{id}-9.9.9.jar", $"https://cdn/{id}", "hash", 1.0);

    private static (UpdateAllUseCase UseCase, IUpdateContentUseCase Update) Build(
        IReadOnlyList<InstalledContent> items,
        int concurrentDownloads,
        IUpdateContentUseCase? update = null)
    {
        var repo = Substitute.For<IInstalledContentRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(items);

        var source = Substitute.For<IModSource>();
        source.IsConfigured.Returns(true);
        foreach (InstalledContent item in items)
        {
            source.GetVersionsAsync(item.Id, Arg.Any<CancellationToken>())
                .Returns(Result.Success<IReadOnlyList<ProjectVersion>>([NewerBuild(item.Id)]));
        }

        var registry = Substitute.For<IModSourceRegistry>();
        registry.Find("modrinth").Returns(source);

        update ??= BuildSucceedingUpdate();

        var settings = Substitute.For<ISettingsStore>();
        settings.Current.Returns(new LodestoneSettings { ConcurrentDownloads = concurrentDownloads });

        return (new UpdateAllUseCase(repo, registry, new VersionResolver(), update, settings), update);
    }

    private static IUpdateContentUseCase BuildSucceedingUpdate()
    {
        var update = Substitute.For<IUpdateContentUseCase>();
        update.ApplyAsync(Arg.Any<InstalledContent>(), Arg.Any<ProjectVersion>(), Arg.Any<IProgress<TransferProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        return update;
    }

    [Fact]
    public async Task Only_items_flagged_for_update_are_applied()
    {
        InstalledContent a = Updatable("a");
        InstalledContent b = Updatable("b");
        InstalledContent notFlagged = Make.Mod("c", projectId: "c", versions: ["1.21.4"]);
        notFlagged.Source = "modrinth"; // UpdateAvailable defaults to false

        (UpdateAllUseCase useCase, IUpdateContentUseCase update) = Build([a, b, notFlagged], concurrentDownloads: 3);

        Result<int> result = await useCase.ExecuteAsync(GameVersion.Parse("1.21.4"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(2); // only a and b
        await update.Received(1).ApplyAsync(a, Arg.Any<ProjectVersion>(), Arg.Any<IProgress<TransferProgress>?>(), Arg.Any<CancellationToken>());
        await update.Received(1).ApplyAsync(b, Arg.Any<ProjectVersion>(), Arg.Any<IProgress<TransferProgress>?>(), Arg.Any<CancellationToken>());
        await update.DidNotReceive().ApplyAsync(notFlagged, Arg.Any<ProjectVersion>(), Arg.Any<IProgress<TransferProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Runs_in_parallel_up_to_the_concurrent_downloads_setting()
    {
        var tracker = new ConcurrencyTrackingUpdate();
        List<InstalledContent> items = Enumerable.Range(0, 6).Select(i => Updatable($"m{i}")).ToList<InstalledContent>();

        (UpdateAllUseCase useCase, _) = Build(items, concurrentDownloads: 3, update: tracker);

        Result<int> result = await useCase.ExecuteAsync(GameVersion.Parse("1.21.4"));

        result.Value.ShouldBe(6);
        tracker.MaxConcurrent.ShouldBeGreaterThanOrEqualTo(2); // genuinely parallel
        tracker.MaxConcurrent.ShouldBeLessThanOrEqualTo(3);    // never exceeds the setting
    }

    [Fact]
    public async Task A_setting_of_one_serializes_the_updates()
    {
        var tracker = new ConcurrencyTrackingUpdate();
        List<InstalledContent> items = Enumerable.Range(0, 4).Select(i => Updatable($"m{i}")).ToList<InstalledContent>();

        (UpdateAllUseCase useCase, _) = Build(items, concurrentDownloads: 1, update: tracker);

        Result<int> result = await useCase.ExecuteAsync(GameVersion.Parse("1.21.4"));

        result.Value.ShouldBe(4);
        tracker.MaxConcurrent.ShouldBe(1); // strictly one at a time
    }

    // Records the peak number of overlapping ApplyAsync calls; the Delay keeps each call in flight long
    // enough for genuine overlap to be observable.
    private sealed class ConcurrencyTrackingUpdate : IUpdateContentUseCase
    {
        private readonly object _sync = new();
        private int _current;

        public int MaxConcurrent { get; private set; }

        public async Task<Result> ApplyAsync(
            InstalledContent item,
            ProjectVersion version,
            IProgress<TransferProgress>? progress = null,
            CancellationToken ct = default)
        {
            lock (_sync)
            {
                _current++;
                if (_current > MaxConcurrent)
                {
                    MaxConcurrent = _current;
                }
            }

            try
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            finally
            {
                lock (_sync)
                {
                    _current--;
                }
            }

            return Result.Success();
        }
    }
}
