using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lodestone.App.Services;
using Lodestone.Application.Abstractions;
using Lodestone.Application.Messaging;
using Lodestone.Application.Settings;
using Lodestone.Application.Supporter;
using Lodestone.Application.UseCases;
using Lodestone.Domain;
using Lodestone.Domain.Common;

namespace Lodestone.App.ViewModels;

/// <summary>The shell: navigation, the detail modal, onboarding overlay, toasts and startup wiring.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IEntitlementStore _entitlements;
    private readonly SupporterService _supporter;
    private readonly IMessageBus _bus;
    private readonly IUiDispatcher _ui;
    private readonly InstallFromCatalogUseCase _install;
    private readonly IInstalledContentRepository _repository;
    private readonly RefreshUpdatesUseCase _refresh;
    private readonly IGameLocator _locator;
    private readonly IDialogService _dialog;
    private readonly ReconcileLibraryUseCase _reconcile;
    private readonly ILoaderInstaller _loaderInstaller;
    private readonly IModSourceRegistry _registry;
    private readonly IGameInventory _inventory;
    private readonly OperationGate _gate;

    public MainViewModel(
        HomeViewModel home,
        LibraryViewModel library,
        BrowseViewModel browse,
        SettingsViewModel settings,
        DonateViewModel donate,
        OnboardingViewModel onboarding,
        ToastsViewModel toasts,
        ISettingsStore settingsStore,
        IEntitlementStore entitlements,
        SupporterService supporter,
        IMessageBus bus,
        IUiDispatcher ui,
        InstallFromCatalogUseCase install,
        IInstalledContentRepository repository,
        RefreshUpdatesUseCase refresh,
        IGameLocator locator,
        IDialogService dialog,
        ReconcileLibraryUseCase reconcile,
        ILoaderInstaller loaderInstaller,
        IModSourceRegistry registry,
        IGameInventory inventory,
        OperationGate gate)
    {
        Home = home;
        Library = library;
        Browse = browse;
        Settings = settings;
        Donate = donate;
        Onboarding = onboarding;
        Toasts = toasts;
        _settings = settingsStore;
        _entitlements = entitlements;
        _supporter = supporter;
        _bus = bus;
        _ui = ui;
        _install = install;
        _repository = repository;
        _refresh = refresh;
        _locator = locator;
        _dialog = dialog;
        _reconcile = reconcile;
        _loaderInstaller = loaderInstaller;
        _registry = registry;
        _inventory = inventory;
        _gate = gate;

        Browse.OpenDetailRequested = OpenDetail;
        Onboarding.Completed += OnOnboardingCompleted;
        _entitlements.Changed += (_, _) => _ui.Post(RefreshSupporter);
        _settings.Changed += (_, _) => _ui.Post(() => OnPropertyChanged(nameof(IsGameReady)));
        _currentScreen = home;
    }

    public HomeViewModel Home { get; }
    public LibraryViewModel Library { get; }
    public BrowseViewModel Browse { get; }
    public SettingsViewModel Settings { get; }
    public DonateViewModel Donate { get; }
    public OnboardingViewModel Onboarding { get; }
    public ToastsViewModel Toasts { get; }

    /// <summary>The app-wide operation gate, surfaced so the shell can show the global activity bar.</summary>
    public OperationGate Gate => _gate;

    [ObservableProperty] private object _currentScreen;
    [ObservableProperty] private string _route = "home";
    [ObservableProperty] private bool _showOnboarding;
    [ObservableProperty] private DetailViewModel? _currentDetail;

    public bool IsSupporter => _supporter.IsSupporter;

    /// <summary>True once a valid Minecraft folder is configured; gates all install actions.</summary>
    public bool IsGameReady => _locator.IsValid(_settings.Current.GameDirectory);

    public bool IsModalOpen => CurrentDetail is not null;

    partial void OnCurrentDetailChanged(DetailViewModel? value) => OnPropertyChanged(nameof(IsModalOpen));

    public async Task InitializeAsync()
    {
        ShowOnboarding = !_settings.Current.OnboardingCompleted;
        RefreshSupporter();
        OnPropertyChanged(nameof(IsGameReady));

        // Auto-discovery: import any mods already sitting in the game folders before showing the library.
        if (IsGameReady)
        {
            AnnounceImport(await _reconcile.ExecuteAsync(ResolveTarget()).ConfigureAwait(true));
        }

        // Local state loads fast; await it so the first screen is populated.
        await Home.LoadAsync().ConfigureAwait(true);
        await Library.LoadAsync().ConfigureAwait(true);

        // Network-backed work is fire-and-forget so the shell never blocks on it.
        _ = Browse.EnsureLoadedAsync();

        // Per spec: the mod updater runs on app start (and on manual refresh) — never on a timer.
        _ = RunStartupRefreshAsync();

        // Make sure the configured loader is actually installed (Fabric/Quilt) on start — only when we have a
        // concrete version, the loader is one we install directly, and it isn't already there (so the activity
        // bar shows a real install, never a flash for a no-op).
        Loader startupLoader = _settings.Current.DefaultLoader;
        if (IsGameReady && ResolveTarget() is { } startupVersion
            && _loaderInstaller.Supports(startupLoader)
            && !_loaderInstaller.IsInstalled(startupLoader, startupVersion))
        {
            _ = _gate.RunAsync($"Setting up {startupLoader.ToDisplayName()}…",
                () => _loaderInstaller.EnsureInstalledAsync(startupLoader, startupVersion));
        }
    }

    [RelayCommand]
    private async Task LocateGameAsync()
    {
        string? picked = _dialog.PickFolder(_settings.Current.GameDirectory);
        if (picked is null)
        {
            return;
        }

        if (!_locator.IsValid(picked))
        {
            _bus.Publish(new ToastMessage("That doesn't look right", "Pick the folder that holds your mods/ and versions/.", ToastKind.Warning));
            return;
        }

        LodestoneSettings s = _settings.Current.Clone();
        s.GameDirectory = picked;
        await _settings.SaveAsync(s).ConfigureAwait(true);
        OnPropertyChanged(nameof(IsGameReady));
        _bus.Publish(new ToastMessage("Minecraft folder set", picked));

        GameVersion? version = ResolveTarget();
        AnnounceImport(await _reconcile.ExecuteAsync(version).ConfigureAwait(true));
        Loader defaultLoader = _settings.Current.DefaultLoader;
        if (version is { } loaderVersion
            && _loaderInstaller.Supports(defaultLoader)
            && !_loaderInstaller.IsInstalled(defaultLoader, loaderVersion))
        {
            _ = _gate.RunAsync($"Setting up {defaultLoader.ToDisplayName()}…",
                () => _loaderInstaller.EnsureInstalledAsync(defaultLoader, loaderVersion));
        }

        _bus.Publish(new LibraryChanged());
    }

    [RelayCommand] private void GoHome() => Navigate("home", Home);
    [RelayCommand] private void GoLibrary() => Navigate("library", Library);
    [RelayCommand] private void GoBrowse() => Navigate("browse", Browse);
    [RelayCommand] private void GoDonate() => Navigate("donate", Donate);
    [RelayCommand] private void GoSettings() => Navigate("settings", Settings);

    private void Navigate(string route, object screen)
    {
        Route = route;
        CurrentScreen = screen;
        CurrentDetail = null;
    }

    private async Task RunStartupRefreshAsync()
    {
        Result<UpdateSummary> result = await _refresh.ExecuteAsync(ActiveVersion()).ConfigureAwait(true);
        if (result.IsSuccess && (result.Value.UpdatesAvailable > 0 || result.Value.Updated > 0))
        {
            _bus.Publish(new LibraryChanged());
        }
    }

    private async void OpenDetail(CatalogProject project)
    {
        bool installed = await _repository.FindAsync(project.Id).ConfigureAwait(true) is not null;
        var detail = new DetailViewModel(project, installed, InstallFromDetailAsync, () => CurrentDetail = null, () => _dialog.OpenUrl(BuildProjectUrl(project)));
        CurrentDetail = detail;

        // Enrich with the full project (long description + screenshot gallery) once it loads.
        IModSource source = _registry.Find(project.Source) ?? _registry.Primary;
        Result<CatalogProject> full = await source.GetProjectAsync(project.Id).ConfigureAwait(true);
        if (full.IsSuccess && ReferenceEquals(CurrentDetail, detail))
        {
            CatalogProject merged = project with { Body = full.Value.Body, GalleryUrls = full.Value.GalleryUrls };
            CurrentDetail = new DetailViewModel(merged, installed, InstallFromDetailAsync, () => CurrentDetail = null, () => _dialog.OpenUrl(BuildProjectUrl(merged)));
        }
    }

    /// <summary>Smoke-test hook: opens the detail modal with a sample markdown body to exercise rendering.
    /// The body deliberately mixes Markdown, a width-pinned HTML image and a linked image so the
    /// Markdig → WebView2 description pipeline is exercised end to end.</summary>
    public void OpenSampleDetailForSmoke()
    {
        const string body =
            "# Heading\n\nSome **bold**, _italic_ and a [link](https://modrinth.com).\n\n" +
            "![wide banner](https://modrinth.com/data/sample/banner.png)\n\n" +
            "<img src=\"https://modrinth.com/data/sample/wide.png\" width=\"1280\" height=\"400\" />\n\n" +
            "[![linked banner](https://modrinth.com/data/sample/icon.png)](https://modrinth.com)\n\n" +
            "- one\n- two";

        var sample = new CatalogProject(
            "sample", "sodium", "Sample Mod", "Author", ContentType.Mod,
            "A short description.", 12_400_000, 41_000,
            ["optimization"], [Loader.Fabric], [GameVersion.Parse("1.21.4")], "modrinth",
            IconUrl: null, LatestVersion: "1.0.0",
            Body: body,
            GalleryUrls: []);
        OpenDetail(sample);
    }

    private static string BuildProjectUrl(CatalogProject project) => project.Source == "curseforge"
        ? $"https://www.curseforge.com/minecraft/search?search={Uri.EscapeDataString(project.Slug)}"
        : $"https://modrinth.com/project/{Uri.EscapeDataString(project.Slug)}";

    private async Task InstallFromDetailAsync(DetailViewModel detail)
    {
        if (!IsGameReady)
        {
            _bus.Publish(new ToastMessage("Set your Minecraft folder first", "Lodestone needs to know where to install. Use “Locate Minecraft”.", ToastKind.Warning));
            return;
        }

        GameVersion? target = ResolveTarget();
        if (target is null)
        {
            _bus.Publish(new ToastMessage("No Minecraft version yet", "Install a Minecraft version (or pick one in My Content) before installing mods.", ToastKind.Warning));
            return;
        }

        bool ran = await _gate.RunAsync($"Installing {detail.Name}…", async () =>
        {
            detail.Installing = true;
            var progress = new Progress<TransferProgress>(p =>
                _ui.Post(() => detail.InstallPercent = (int)Math.Round((p.Fraction ?? 0) * 100)));

            Result<CatalogInstall> result = await _install
                .ExecuteAsync(detail.Project, target, _settings.Current.DefaultLoader, progress)
                .ConfigureAwait(true);

            detail.Installing = false;
            if (result.IsSuccess)
            {
                detail.Installed = true;
                _bus.Publish(new ToastMessage("Installed", DescribeInstall(detail.Name, result.Value)));
                if (!_inventory.IsVersionInstalled(target))
                {
                    _bus.Publish(new ToastMessage("Heads up",
                        $"Installed for {target}, but that Minecraft version isn't set up in your launcher yet — it won't load until you install it.",
                        ToastKind.Warning));
                }

                _bus.Publish(new LibraryChanged());
            }
            else
            {
                _bus.Publish(new ToastMessage("Couldn't install", result.Error.Message, ToastKind.Error));
            }
        }).ConfigureAwait(true);

        if (!ran)
        {
            _bus.Publish(new ToastMessage("Please wait", "Another install is still running — try again in a moment.", ToastKind.Info));
        }
    }

    private static string DescribeInstall(string name, CatalogInstall install)
    {
        string text = $"{name} · {install.Item.Type.ToDisplayName()}";
        int deps = install.InstalledDependencies.Count;
        return deps == 0 ? text : $"{text}  ·  +{deps} dependenc{(deps == 1 ? "y" : "ies")}";
    }

    private void OnOnboardingCompleted()
    {
        ShowOnboarding = false;
        _bus.Publish(new ToastMessage("Welcome to Lodestone", "Drag any mod, pack or shader here to install it"));
        _bus.Publish(new LibraryChanged());
    }

    // A gentle heads-up that pre-existing content was adopted from the game folder into the library.
    private void AnnounceImport(Result<int> imported)
    {
        if (imported.IsSuccess && imported.Value > 0)
        {
            _bus.Publish(new ToastMessage("Imported existing content",
                $"Added {imported.Value} item{(imported.Value == 1 ? string.Empty : "s")} already in your game folder to your library."));
        }
    }

    private void RefreshSupporter() => OnPropertyChanged(nameof(IsSupporter));

    /// <summary>The selected version, or null on the "All versions" view (per-mod latest semantics).</summary>
    private GameVersion? ActiveVersion() => ActiveProfile.Selected(_settings.Current);

    /// <summary>A concrete install/loader target: the selection, else newest installed, else null when none.</summary>
    private GameVersion? ResolveTarget() => ActiveProfile.Target(_settings.Current, _inventory);
}
