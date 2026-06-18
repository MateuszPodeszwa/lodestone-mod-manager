using Lodestone.Application.Abstractions;
using Lodestone.Application.Catalog;
using Lodestone.Application.Settings;
using Lodestone.Application.UseCases;
using Lodestone.Domain;
using Lodestone.Domain.Common;
using NSubstitute;

namespace Lodestone.Application.Tests;

public class InstallLocalFileUseCaseTests
{
    [Fact]
    public async Task Installs_local_jar_tagging_metadata_and_target_version()
    {
        var reader = Substitute.For<IArchiveMetadataReader>();
        reader.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            Result.Success(new LocalContentMetadata(
                ContentType.Mod,
                ModId: "sodium",
                Name: "Sodium",
                Version: "0.5.8",
                Loaders: [Loader.Fabric],
                Dependencies: [new Dependency("fabric-api", DependencyKind.Required)],
                ProvidedIds: ["sodium"],
                GameVersions: [GameVersion.Parse("1.21.4")])));

        var installer = Substitute.For<IContentInstaller>();
        installer.PlaceAsync(Arg.Any<string>(), ContentType.Mod, Arg.Any<DuplicateResolution>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PlaceResult("sodium-0.5.8.jar", 1_200_000, false)));

        var repo = Substitute.For<IInstalledContentRepository>();
        var settings = Substitute.For<ISettingsStore>();
        settings.Current.Returns(new LodestoneSettings { DefaultLoader = Loader.Fabric });

        var inventory = Substitute.For<IGameInventory>();
        inventory.IsLoaderInstalled(Arg.Any<Loader>(), Arg.Any<GameVersion>()).Returns(true);

        var useCase = new InstallLocalFileUseCase(reader, installer, repo, settings, inventory);

        Result<InstalledContent> result = await useCase.ExecuteAsync(@"C:\drop\sodium.jar", GameVersion.Parse("1.20.1"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Sodium");
        result.Value.Loader.ShouldBe(Loader.Fabric);
        result.Value.Source.ShouldBe("local");
        result.Value.GameVersions.ShouldContain(v => v.Value == "1.21.4");
        result.Value.GameVersions.ShouldContain(v => v.Value == "1.20.1"); // the dropped-onto profile
        result.Value.Dependencies.ShouldContain(d => d.Identifier == "fabric-api");
        await repo.Received(1).UpsertAsync(Arg.Any<InstalledContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Propagates_a_reader_failure_without_touching_the_library()
    {
        var reader = Substitute.For<IArchiveMetadataReader>();
        reader.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<LocalContentMetadata>("archive.corrupt", "Not a valid archive."));
        var installer = Substitute.For<IContentInstaller>();
        var repo = Substitute.For<IInstalledContentRepository>();
        var settings = Substitute.For<ISettingsStore>();
        settings.Current.Returns(new LodestoneSettings());

        var useCase = new InstallLocalFileUseCase(reader, installer, repo, settings, Substitute.For<IGameInventory>());

        Result<InstalledContent> result = await useCase.ExecuteAsync(@"C:\drop\bad.jar", GameVersion.Parse("1.21.4"));

        result.IsFailure.ShouldBeTrue();
        await repo.DidNotReceive().UpsertAsync(Arg.Any<InstalledContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_a_dropped_mod_when_the_loader_is_not_installed()
    {
        var reader = Substitute.For<IArchiveMetadataReader>();
        reader.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            Result.Success(new LocalContentMetadata(
                ContentType.Mod, ModId: "sodium", Name: "Sodium", Version: "0.5.8",
                Loaders: [Loader.Fabric], Dependencies: [], ProvidedIds: ["sodium"],
                GameVersions: [GameVersion.Parse("1.21.4")])));

        var installer = Substitute.For<IContentInstaller>();
        var repo = Substitute.For<IInstalledContentRepository>();
        var settings = Substitute.For<ISettingsStore>();
        settings.Current.Returns(new LodestoneSettings { DefaultLoader = Loader.Fabric });

        var inventory = Substitute.For<IGameInventory>();
        inventory.IsLoaderInstalled(Arg.Any<Loader>(), Arg.Any<GameVersion>()).Returns(false);

        var useCase = new InstallLocalFileUseCase(reader, installer, repo, settings, inventory);

        Result<InstalledContent> result = await useCase.ExecuteAsync(@"C:\drop\sodium.jar", GameVersion.Parse("1.21.4"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("install.loader_missing");
        await installer.DidNotReceive().PlaceAsync(Arg.Any<string>(), Arg.Any<ContentType>(), Arg.Any<DuplicateResolution>(), Arg.Any<CancellationToken>());
        await repo.DidNotReceive().UpsertAsync(Arg.Any<InstalledContent>(), Arg.Any<CancellationToken>());
    }
}

public class InstallFromCatalogUseCaseTests
{
    private static CatalogProject Sodium() => new(
        "sodium", "sodium", "Sodium", "CaffeineMC", ContentType.Mod, "Fast renderer",
        12_400_000, 41_000, ["optimization"], [Loader.Fabric], [GameVersion.Parse("1.21.4")], "modrinth");

    private static ProjectVersion SodiumBuild(Loader loader = Loader.Fabric) => new(
        "v1", "sodium", "0.5.8", ContentType.Mod,
        [GameVersion.Parse("1.21.4")], [loader], [], "sodium-0.5.8.jar", "https://cdn/sodium", "deadbeef", 1.2);

    private static (InstallFromCatalogUseCase UseCase, IInstalledContentRepository Repo) Build(
        IReadOnlyList<ProjectVersion> versions,
        InstalledContent? existing = null,
        bool loaderInstalled = true)
    {
        var source = Substitute.For<IModSource>();
        source.IsConfigured.Returns(true);
        source.GetVersionsAsync("sodium", Arg.Any<CancellationToken>())
            .Returns(Result.Success(versions));

        var registry = Substitute.For<IModSourceRegistry>();
        registry.Find("modrinth").Returns(source);
        registry.Primary.Returns(source);

        var downloader = Substitute.For<IDownloader>();
        downloader.DownloadAsync(Arg.Any<DownloadRequest>(), Arg.Any<IProgress<TransferProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DownloadedFile(@"C:\tmp\sodium.jar", 1_200_000, "deadbeef")));

        var installer = Substitute.For<IContentInstaller>();
        installer.PlaceAsync(Arg.Any<string>(), ContentType.Mod, Arg.Any<DuplicateResolution>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PlaceResult("sodium-0.5.8.jar", 1_200_000, false)));

        var repo = Substitute.For<IInstalledContentRepository>();
        repo.FindAsync("sodium", Arg.Any<CancellationToken>()).Returns(existing);

        var settings = Substitute.For<ISettingsStore>();
        settings.Current.Returns(new LodestoneSettings());

        var inventory = Substitute.For<IGameInventory>();
        inventory.IsLoaderInstalled(Arg.Any<Loader>(), Arg.Any<GameVersion>()).Returns(loaderInstalled);

        return (new InstallFromCatalogUseCase(registry, new VersionResolver(), downloader, installer, repo, settings, inventory), repo);
    }

    [Fact]
    public async Task Resolves_downloads_and_records_the_install()
    {
        (InstallFromCatalogUseCase useCase, IInstalledContentRepository repo) = Build([SodiumBuild()]);

        Result<CatalogInstall> result =
            await useCase.ExecuteAsync(Sodium(), GameVersion.Parse("1.21.4"), Loader.Fabric);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Item.Version.ShouldBe("0.5.8");
        result.Value.Item.ProjectId.ShouldBe("sodium");
        result.Value.Item.Sha512.ShouldBe("deadbeef");
        result.Value.Item.Categories.ShouldBe(["optimization"]); // carried from the catalog for the My Content filter
        result.Value.InstalledDependencies.ShouldBeEmpty();
        await repo.Received(1).UpsertAsync(Arg.Any<InstalledContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refuses_to_install_what_is_already_installed()
    {
        (InstallFromCatalogUseCase useCase, _) = Build([SodiumBuild()], existing: Make.Mod("sodium"));

        Result<CatalogInstall> result =
            await useCase.ExecuteAsync(Sodium(), GameVersion.Parse("1.21.4"), Loader.Fabric);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("install.duplicate");
    }

    [Fact]
    public async Task Fails_when_no_build_matches_the_active_loader()
    {
        (InstallFromCatalogUseCase useCase, _) = Build([SodiumBuild(Loader.Forge)]);

        Result<CatalogInstall> result =
            await useCase.ExecuteAsync(Sodium(), GameVersion.Parse("1.21.4"), Loader.Fabric);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("install.no_compatible_version");
    }

    [Fact]
    public async Task Refuses_a_mod_when_its_loader_is_not_installed_for_the_version()
    {
        (InstallFromCatalogUseCase useCase, IInstalledContentRepository repo) = Build([SodiumBuild()], loaderInstalled: false);

        Result<CatalogInstall> result =
            await useCase.ExecuteAsync(Sodium(), GameVersion.Parse("1.21.4"), Loader.Fabric);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("install.loader_missing");
        await repo.DidNotReceive().UpsertAsync(Arg.Any<InstalledContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reinstalls_for_a_different_loader_removing_the_stale_build()
    {
        // Sodium is already installed for Quilt; installing for Fabric is NOT a duplicate (it's a different
        // profile) — it should re-target and drop the old Quilt build so it doesn't orphan a file on disk.
        var source = Substitute.For<IModSource>();
        source.IsConfigured.Returns(true);
        source.GetVersionsAsync("sodium", Arg.Any<CancellationToken>()).Returns(Result.Success<IReadOnlyList<ProjectVersion>>([SodiumBuild(Loader.Fabric)]));

        var registry = Substitute.For<IModSourceRegistry>();
        registry.Find("modrinth").Returns(source);
        registry.Primary.Returns(source);

        var downloader = Substitute.For<IDownloader>();
        downloader.DownloadAsync(Arg.Any<DownloadRequest>(), Arg.Any<IProgress<TransferProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DownloadedFile(@"C:\tmp\sodium.jar", 1_200_000, "deadbeef")));

        var installer = Substitute.For<IContentInstaller>();
        installer.PlaceAsync(Arg.Any<string>(), ContentType.Mod, Arg.Any<DuplicateResolution>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PlaceResult("sodium-fabric-0.5.8.jar", 1_200_000, false)));
        installer.RemoveAsync(Arg.Any<ContentType>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        InstalledContent existing = Make.Mod("sodium", loader: Loader.Quilt);
        existing.FileName = "sodium-quilt-0.5.8.jar";
        var repo = Substitute.For<IInstalledContentRepository>();
        repo.FindAsync("sodium", Arg.Any<CancellationToken>()).Returns(existing);

        var settings = Substitute.For<ISettingsStore>();
        settings.Current.Returns(new LodestoneSettings());

        var inventory = Substitute.For<IGameInventory>();
        inventory.IsLoaderInstalled(Arg.Any<Loader>(), Arg.Any<GameVersion>()).Returns(true);

        var useCase = new InstallFromCatalogUseCase(registry, new VersionResolver(), downloader, installer, repo, settings, inventory);

        Result<CatalogInstall> result = await useCase.ExecuteAsync(Sodium(), GameVersion.Parse("1.21.4"), Loader.Fabric);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Item.Loader.ShouldBe(Loader.Fabric); // re-targeted to the active loader
        await installer.Received(1).RemoveAsync(ContentType.Mod, "sodium-quilt-0.5.8.jar", Arg.Any<CancellationToken>());
        await repo.Received().UpsertAsync(Arg.Any<InstalledContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Auto_installs_required_dependencies_from_the_source()
    {
        var source = Substitute.For<IModSource>();
        source.IsConfigured.Returns(true);

        // Sodium declares a required dependency on Fabric API (a Modrinth project id).
        IReadOnlyList<ProjectVersion> sodiumVersions =
        [
            new ProjectVersion(
                "v1", "sodium", "0.5.8", ContentType.Mod,
                [GameVersion.Parse("1.21.4")], [Loader.Fabric],
                [new Dependency("fabric-api", DependencyKind.Required)],
                "sodium-0.5.8.jar", "https://cdn/sodium", "deadbeef", 1.2),
        ];
        source.GetVersionsAsync("sodium", Arg.Any<CancellationToken>()).Returns(Result.Success(sodiumVersions));

        var fabricApi = new CatalogProject(
            "fabric-api", "fabric-api", "Fabric API", "FabricMC", ContentType.Mod, "Hooks",
            5_000_000, 9_000, ["library"], [Loader.Fabric], [GameVersion.Parse("1.21.4")], "modrinth");
        source.GetProjectAsync("fabric-api", Arg.Any<CancellationToken>()).Returns(Result.Success(fabricApi));

        IReadOnlyList<ProjectVersion> fabricApiVersions =
        [
            new ProjectVersion(
                "fv1", "fabric-api", "0.100.0", ContentType.Mod,
                [GameVersion.Parse("1.21.4")], [Loader.Fabric], [],
                "fabric-api-0.100.0.jar", "https://cdn/fapi", "cafebabe", 2.0),
        ];
        source.GetVersionsAsync("fabric-api", Arg.Any<CancellationToken>()).Returns(Result.Success(fabricApiVersions));

        var registry = Substitute.For<IModSourceRegistry>();
        registry.Find("modrinth").Returns(source);
        registry.Primary.Returns(source);

        var downloader = Substitute.For<IDownloader>();
        downloader.DownloadAsync(Arg.Any<DownloadRequest>(), Arg.Any<IProgress<TransferProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DownloadedFile(@"C:\tmp\dep.jar", 1_000_000, "filehash")));

        var installer = Substitute.For<IContentInstaller>();
        installer.PlaceAsync(Arg.Any<string>(), ContentType.Mod, Arg.Any<DuplicateResolution>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PlaceResult("placed.jar", 1_000_000, false)));

        var repo = Substitute.For<IInstalledContentRepository>();
        repo.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((InstalledContent?)null);

        var settings = Substitute.For<ISettingsStore>();
        settings.Current.Returns(new LodestoneSettings());

        var inventory = Substitute.For<IGameInventory>();
        inventory.IsLoaderInstalled(Arg.Any<Loader>(), Arg.Any<GameVersion>()).Returns(true);

        var useCase = new InstallFromCatalogUseCase(registry, new VersionResolver(), downloader, installer, repo, settings, inventory);

        Result<CatalogInstall> result =
            await useCase.ExecuteAsync(Sodium(), GameVersion.Parse("1.21.4"), Loader.Fabric);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Item.ProjectId.ShouldBe("sodium");
        result.Value.InstalledDependencies.ShouldContain("Fabric API");
        await repo.Received().UpsertAsync(Arg.Is<InstalledContent>(c => c.Id == "sodium"), Arg.Any<CancellationToken>());
        await repo.Received(1).UpsertAsync(Arg.Is<InstalledContent>(c => c.Id == "fabric-api"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Backfills_dependency_display_names_from_the_resolved_project()
    {
        var source = Substitute.For<IModSource>();
        source.IsConfigured.Returns(true);

        // Sodium declares a required dependency known only by its Modrinth project id.
        IReadOnlyList<ProjectVersion> sodiumVersions =
        [
            new ProjectVersion(
                "v1", "sodium", "0.5.8", ContentType.Mod,
                [GameVersion.Parse("1.21.4")], [Loader.Fabric],
                [new Dependency("9s6osm5g", DependencyKind.Required)],
                "sodium-0.5.8.jar", "https://cdn/sodium", "deadbeef", 1.2),
        ];
        source.GetVersionsAsync("sodium", Arg.Any<CancellationToken>()).Returns(Result.Success(sodiumVersions));

        var clothConfig = new CatalogProject(
            "9s6osm5g", "cloth-config", "Cloth Config", "shedaniel", ContentType.Mod, "Config lib",
            8_000_000, 12_000, ["library"], [Loader.Fabric], [GameVersion.Parse("1.21.4")], "modrinth");
        source.GetProjectAsync("9s6osm5g", Arg.Any<CancellationToken>()).Returns(Result.Success(clothConfig));

        IReadOnlyList<ProjectVersion> clothVersions =
        [
            new ProjectVersion(
                "cv1", "9s6osm5g", "11.0.0", ContentType.Mod,
                [GameVersion.Parse("1.21.4")], [Loader.Fabric], [],
                "cloth-config-11.0.0.jar", "https://cdn/cloth", "cafebabe", 2.0),
        ];
        source.GetVersionsAsync("9s6osm5g", Arg.Any<CancellationToken>()).Returns(Result.Success(clothVersions));

        var registry = Substitute.For<IModSourceRegistry>();
        registry.Find("modrinth").Returns(source);
        registry.Primary.Returns(source);

        var downloader = Substitute.For<IDownloader>();
        downloader.DownloadAsync(Arg.Any<DownloadRequest>(), Arg.Any<IProgress<TransferProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DownloadedFile(@"C:\tmp\dep.jar", 1_000_000, "filehash")));

        var installer = Substitute.For<IContentInstaller>();
        installer.PlaceAsync(Arg.Any<string>(), ContentType.Mod, Arg.Any<DuplicateResolution>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PlaceResult("placed.jar", 1_000_000, false)));

        var repo = Substitute.For<IInstalledContentRepository>();
        repo.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((InstalledContent?)null);

        var settings = Substitute.For<ISettingsStore>();
        settings.Current.Returns(new LodestoneSettings());

        var inventory = Substitute.For<IGameInventory>();
        inventory.IsLoaderInstalled(Arg.Any<Loader>(), Arg.Any<GameVersion>()).Returns(true);

        var useCase = new InstallFromCatalogUseCase(registry, new VersionResolver(), downloader, installer, repo, settings, inventory);

        Result<CatalogInstall> result =
            await useCase.ExecuteAsync(Sodium(), GameVersion.Parse("1.21.4"), Loader.Fabric);

        result.IsSuccess.ShouldBeTrue();

        // The parent mod's stored dependency carries the human name, not the raw project id, so the
        // compatibility badge reads "Requires Cloth Config".
        Dependency stored = result.Value.Item.Dependencies.Single(d => d.Identifier == "9s6osm5g");
        stored.DisplayName.ShouldBe("Cloth Config");
        stored.Label.ShouldBe("Cloth Config");

        var issue = Lodestone.Domain.Compatibility.CompatibilityIssue.Error(
            Lodestone.Domain.Compatibility.CompatibilityKind.MissingDependency,
            $"Requires {stored.Label}, which isn't installed.", stored.Label);
        issue.ShortLabel.ShouldBe("Requires Cloth Config");

        // The parent was re-persisted with the backfilled name.
        await repo.Received().UpsertAsync(
            Arg.Is<InstalledContent>(c => c.Id == "sodium" &&
                c.Dependencies.Any(d => d.Identifier == "9s6osm5g" && d.DisplayName == "Cloth Config")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Falls_back_to_the_identifier_when_the_dependency_name_is_unknown()
    {
        // The dependency's project can't be resolved (deleted/unknown id), so no name is captured and
        // the badge must keep showing the raw identifier rather than crash or blank out.
        var source = Substitute.For<IModSource>();
        source.IsConfigured.Returns(true);

        IReadOnlyList<ProjectVersion> sodiumVersions =
        [
            new ProjectVersion(
                "v1", "sodium", "0.5.8", ContentType.Mod,
                [GameVersion.Parse("1.21.4")], [Loader.Fabric],
                [new Dependency("9s6osm5g", DependencyKind.Required)],
                "sodium-0.5.8.jar", "https://cdn/sodium", "deadbeef", 1.2),
        ];
        source.GetVersionsAsync("sodium", Arg.Any<CancellationToken>()).Returns(Result.Success(sodiumVersions));
        source.GetProjectAsync("9s6osm5g", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CatalogProject>("source.not_found", "No such project."));

        var registry = Substitute.For<IModSourceRegistry>();
        registry.Find("modrinth").Returns(source);
        registry.Primary.Returns(source);

        var downloader = Substitute.For<IDownloader>();
        downloader.DownloadAsync(Arg.Any<DownloadRequest>(), Arg.Any<IProgress<TransferProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DownloadedFile(@"C:\tmp\sodium.jar", 1_200_000, "deadbeef")));

        var installer = Substitute.For<IContentInstaller>();
        installer.PlaceAsync(Arg.Any<string>(), ContentType.Mod, Arg.Any<DuplicateResolution>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PlaceResult("sodium-0.5.8.jar", 1_200_000, false)));

        var repo = Substitute.For<IInstalledContentRepository>();
        repo.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((InstalledContent?)null);

        var settings = Substitute.For<ISettingsStore>();
        settings.Current.Returns(new LodestoneSettings());

        var inventory = Substitute.For<IGameInventory>();
        inventory.IsLoaderInstalled(Arg.Any<Loader>(), Arg.Any<GameVersion>()).Returns(true);

        var useCase = new InstallFromCatalogUseCase(registry, new VersionResolver(), downloader, installer, repo, settings, inventory);

        Result<CatalogInstall> result =
            await useCase.ExecuteAsync(Sodium(), GameVersion.Parse("1.21.4"), Loader.Fabric);

        result.IsSuccess.ShouldBeTrue();
        Dependency stored = result.Value.Item.Dependencies.Single(d => d.Identifier == "9s6osm5g");
        stored.DisplayName.ShouldBeNull();
        stored.Label.ShouldBe("9s6osm5g"); // graceful fallback to the raw id
    }
}

public class ResetGameUseCaseTests
{
    [Fact]
    public async Task Removes_all_content_and_managed_loaders_then_clears_the_selection()
    {
        InstalledContent a = Make.Mod("a", loader: Loader.Fabric, versions: ["1.20.1"]);
        a.FileName = "a.jar";
        InstalledContent b = Make.Mod("b", loader: Loader.Forge, versions: ["1.20.1"]);
        b.FileName = "b.jar";
        IReadOnlyList<InstalledContent> items = [a, b];

        var repo = Substitute.For<IInstalledContentRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(items);

        var installer = Substitute.For<IContentInstaller>();
        installer.RemoveAsync(Arg.Any<ContentType>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var loaders = Substitute.For<ILoaderInstaller>();
        loaders.RemoveManagedAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(2));

        var settings = Substitute.For<ISettingsStore>();
        settings.Current.Returns(new LodestoneSettings { SelectedVersion = "1.20.1", SelectedLoader = Loader.Fabric });
        var launcher = Substitute.For<ILauncherVisibility>();

        var useCase = new ResetGameUseCase(repo, installer, loaders, settings, launcher);
        Result<ResetSummary> result = await useCase.ExecuteAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ContentRemoved.ShouldBe(2);
        result.Value.LoadersRemoved.ShouldBe(2);
        await installer.Received(1).RemoveAsync(ContentType.Mod, "a.jar", Arg.Any<CancellationToken>());
        await repo.Received(1).RemoveAsync("a", Arg.Any<CancellationToken>());
        await repo.Received(1).RemoveAsync("b", Arg.Any<CancellationToken>());
        await settings.Received(1).SaveAsync(
            Arg.Is<LodestoneSettings>(s => s.SelectedVersion == "all" && s.SelectedLoader == Loader.None),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stops_and_reports_when_a_file_cannot_be_removed()
    {
        InstalledContent a = Make.Mod("a", loader: Loader.Fabric, versions: ["1.20.1"]);
        a.FileName = "a.jar";
        IReadOnlyList<InstalledContent> items = [a];

        var repo = Substitute.For<IInstalledContentRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(items);
        var installer = Substitute.For<IContentInstaller>();
        installer.RemoveAsync(Arg.Any<ContentType>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("install.locked", "Is Minecraft running?"));
        var loaders = Substitute.For<ILoaderInstaller>();
        var settings = Substitute.For<ISettingsStore>();
        settings.Current.Returns(new LodestoneSettings());
        var launcher = Substitute.For<ILauncherVisibility>();

        var useCase = new ResetGameUseCase(repo, installer, loaders, settings, launcher);
        Result<ResetSummary> result = await useCase.ExecuteAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("install.locked");
        await loaders.DidNotReceive().RemoveManagedAsync(Arg.Any<CancellationToken>());
    }
}

public class SwitchProfileUseCaseTests
{
    private static (SwitchProfileUseCase UseCase, IContentInstaller Installer, IInstalledContentRepository Repo)
        Build(params InstalledContent[] items)
    {
        var repo = Substitute.For<IInstalledContentRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(items);

        var installer = Substitute.For<IContentInstaller>();
        installer.SetEnabledAsync(Arg.Any<ContentType>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result.Success((string)ci[1])); // echo the file name back

        var inventory = Substitute.For<IGameInventory>();
        var launcher = Substitute.For<ILauncherVisibility>();

        return (new SwitchProfileUseCase(repo, installer, inventory, launcher), installer, repo);
    }

    private static InstalledContent Mod(string id, Loader loader, string version, bool enabled)
    {
        InstalledContent m = Make.Mod(id, loader: loader, versions: [version], enabled: enabled);
        m.FileName = id + ".jar";
        return m;
    }

    [Fact]
    public async Task Enables_the_profile_mods_and_disables_everything_else()
    {
        InstalledContent a = Mod("a", Loader.Fabric, "1.20.1", enabled: false); // belongs → enable
        InstalledContent b = Mod("b", Loader.Fabric, "1.21.4", enabled: true);  // wrong version → disable
        InstalledContent c = Mod("c", Loader.Forge, "1.20.1", enabled: true);   // wrong loader → disable
        (SwitchProfileUseCase useCase, IContentInstaller installer, IInstalledContentRepository repo) = Build(a, b, c);

        Result<ProfileSwitch> result = await useCase.ExecuteAsync(GameVersion.Parse("1.20.1"), Loader.Fabric);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Enabled.ShouldBe(1);
        result.Value.Disabled.ShouldBe(2);
        a.Enabled.ShouldBeTrue();
        b.Enabled.ShouldBeFalse();
        c.Enabled.ShouldBeFalse();
        await installer.Received(1).SetEnabledAsync(ContentType.Mod, "a.jar", true, Arg.Any<CancellationToken>());
        await installer.Received(1).SetEnabledAsync(ContentType.Mod, "b.jar", false, Arg.Any<CancellationToken>());
        await installer.Received(1).SetEnabledAsync(ContentType.Mod, "c.jar", false, Arg.Any<CancellationToken>());
        await repo.Received(3).UpsertAsync(Arg.Any<InstalledContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Leaves_already_correct_mods_and_loader_agnostic_content_untouched()
    {
        InstalledContent a = Mod("a", Loader.Fabric, "1.20.1", enabled: true); // already correct
        InstalledContent pack = Make.Pack("p", enabled: true, versions: ["1.19"]);
        (SwitchProfileUseCase useCase, IContentInstaller installer, IInstalledContentRepository repo) = Build(a, pack);

        Result<ProfileSwitch> result = await useCase.ExecuteAsync(GameVersion.Parse("1.20.1"), Loader.Fabric);

        result.Value.Enabled.ShouldBe(0);
        result.Value.Disabled.ShouldBe(0);
        await installer.DidNotReceive().SetEnabledAsync(Arg.Any<ContentType>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await repo.DidNotReceive().UpsertAsync(Arg.Any<InstalledContent>(), Arg.Any<CancellationToken>());
    }
}

public class RefreshUpdatesUseCaseTests
{
    private static ProjectVersion NewerBuild() => new(
        "v2", "iris", "1.8.1", ContentType.Mod,
        [GameVersion.Parse("1.21.4")], [Loader.Fabric], [], "iris-1.8.1.jar", "https://cdn/iris", "hash", 3.1);

    private static (RefreshUpdatesUseCase UseCase, IUpdateContentUseCase Update, InstalledContent Item)
        Build(bool autoUpdate)
    {
        var item = Make.Mod("iris", projectId: "iris", versions: ["1.21.4"]);
        item.Version = "1.8.0";
        item.Source = "modrinth";

        IReadOnlyList<InstalledContent> all = [item];
        var repo = Substitute.For<IInstalledContentRepository>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(all);

        var source = Substitute.For<IModSource>();
        source.IsConfigured.Returns(true);
        source.GetVersionsAsync("iris", Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<ProjectVersion>>([NewerBuild()]));
        source.GetProjectAsync("iris", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new CatalogProject(
                "iris", "iris", "Iris Shaders", "coderbot", ContentType.Mod, "Shaders",
                1_000_000, 5_000, [], [Loader.Fabric], [GameVersion.Parse("1.21.4")], "modrinth")));

        var registry = Substitute.For<IModSourceRegistry>();
        registry.Find("modrinth").Returns(source);

        var update = Substitute.For<IUpdateContentUseCase>();
        update.ApplyAsync(Arg.Any<InstalledContent>(), Arg.Any<ProjectVersion>(), Arg.Any<IProgress<TransferProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var settings = Substitute.For<ISettingsStore>();
        settings.Current.Returns(new LodestoneSettings { AutoUpdate = autoUpdate });

        return (new RefreshUpdatesUseCase(repo, registry, new VersionResolver(), update, settings), update, item);
    }

    [Fact]
    public async Task Flags_available_updates_when_auto_update_is_off()
    {
        (RefreshUpdatesUseCase useCase, IUpdateContentUseCase update, InstalledContent item) = Build(autoUpdate: false);

        Result<UpdateSummary> result = await useCase.ExecuteAsync(GameVersion.Parse("1.21.4"));

        result.Value.UpdatesAvailable.ShouldBe(1);
        result.Value.Updated.ShouldBe(0);
        item.UpdateAvailable.ShouldBeTrue();
        await update.DidNotReceive().ApplyAsync(Arg.Any<InstalledContent>(), Arg.Any<ProjectVersion>(), Arg.Any<IProgress<TransferProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Applies_updates_when_auto_update_is_on()
    {
        (RefreshUpdatesUseCase useCase, IUpdateContentUseCase update, _) = Build(autoUpdate: true);

        Result<UpdateSummary> result = await useCase.ExecuteAsync(GameVersion.Parse("1.21.4"));

        result.Value.Updated.ShouldBe(1);
        await update.Received(1).ApplyAsync(Arg.Any<InstalledContent>(), Arg.Any<ProjectVersion>(), Arg.Any<IProgress<TransferProgress>?>(), Arg.Any<CancellationToken>());
    }
}

public class ToggleAndUninstallTests
{
    [Fact]
    public async Task Toggle_flips_state_and_updates_filename_from_installer()
    {
        var item = Make.Mod("sodium");
        item.Enabled = true;
        item.FileName = "sodium.jar";

        var repo = Substitute.For<IInstalledContentRepository>();
        repo.FindAsync("sodium", Arg.Any<CancellationToken>()).Returns(item);
        var installer = Substitute.For<IContentInstaller>();
        installer.SetEnabledAsync(ContentType.Mod, "sodium.jar", false, Arg.Any<CancellationToken>())
            .Returns(Result.Success("sodium.jar.disabled"));

        Result result = await new ToggleContentUseCase(repo, installer).ExecuteAsync("sodium");

        result.IsSuccess.ShouldBeTrue();
        item.Enabled.ShouldBeFalse();
        item.FileName.ShouldBe("sodium.jar.disabled");
    }

    [Fact]
    public async Task Uninstall_removes_the_file_then_the_record()
    {
        var item = Make.Mod("sodium");
        item.FileName = "sodium.jar";
        var repo = Substitute.For<IInstalledContentRepository>();
        repo.FindAsync("sodium", Arg.Any<CancellationToken>()).Returns(item);
        var installer = Substitute.For<IContentInstaller>();
        installer.RemoveAsync(ContentType.Mod, "sodium.jar", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        Result result = await new UninstallContentUseCase(repo, installer).ExecuteAsync("sodium");

        result.IsSuccess.ShouldBeTrue();
        await repo.Received(1).RemoveAsync("sodium", Arg.Any<CancellationToken>());
    }
}
