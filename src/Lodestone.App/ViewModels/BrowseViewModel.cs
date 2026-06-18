using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lodestone.App.Services;
using Lodestone.Application.Abstractions;
using Lodestone.Application.Messaging;
using Lodestone.Application.Settings;
using Lodestone.Application.UseCases;
using Lodestone.Domain;
using Lodestone.Domain.Common;

namespace Lodestone.App.ViewModels;

/// <summary>The Browse screen: faceted, paged Modrinth search filtered to the active loader + version,
/// with one-click install (gated on a valid game folder).</summary>
public sealed partial class BrowseViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 20;

    private readonly IModSourceRegistry _registry;
    private readonly InstallFromCatalogUseCase _install;
    private readonly IInstalledContentRepository _repository;
    private readonly ISettingsStore _settings;
    private readonly IMessageBus _bus;
    private readonly IUiDispatcher _ui;
    private readonly IGameLocator _locator;
    private readonly IGameInventory _inventory;
    private readonly OperationGate _gate;

    private CancellationTokenSource? _debounce;
    private bool _loadedOnce;

    public BrowseViewModel(
        IModSourceRegistry registry,
        InstallFromCatalogUseCase install,
        IInstalledContentRepository repository,
        ISettingsStore settings,
        IMessageBus bus,
        IUiDispatcher ui,
        IGameLocator locator,
        IGameInventory inventory,
        OperationGate gate)
    {
        _registry = registry;
        _install = install;
        _repository = repository;
        _settings = settings;
        _bus = bus;
        _ui = ui;
        _locator = locator;
        _inventory = inventory;
        _gate = gate;
        bus.Subscribe<LibraryChanged>(m => _ui.Post(() =>
        {
            MarkInstalledFromLibrary();
            RaiseLoaderGate(); // a loader may have just been installed
        }));
        settings.Changed += (_, _) => _ui.Post(() =>
        {
            OnPropertyChanged(nameof(IsGameReady));
            RaiseLoaderGate();
        });
    }

    /// <summary>Set by the shell so cards can open the detail modal.</summary>
    public Action<CatalogProject>? OpenDetailRequested { get; set; }

    public bool IsCurseForgeAvailable => _registry.Find("curseforge")?.IsConfigured == true;

    public bool IsGameReady => _locator.IsValid(_settings.Current.GameDirectory);

    /// <summary>Mods need their loader installed for the target version before they can be added; resource
    /// packs and shaders don't use a loader. When false (mods only), installs are disabled and the gate
    /// banner explains why.</summary>
    public bool IsActiveLoaderReady
    {
        get
        {
            if (BrowseCat is "resource-packs" or "shaders")
            {
                return true; // loader-independent content
            }

            GameVersion? target = ResolveTargetVersion();
            if (target is null)
            {
                return true; // "no Minecraft version yet" is handled separately on install
            }

            Loader loader = _settings.Current.DefaultLoader;
            return loader != Loader.None && _inventory.IsLoaderInstalled(loader, target);
        }
    }

    /// <summary>Installs are allowed only with a valid game folder and (for mods) the loader installed.</summary>
    public bool CanInstallHere => IsGameReady && IsActiveLoaderReady;

    /// <summary>Shows the loader-gate banner: the folder is set but the active loader isn't installed yet.</summary>
    public bool ShowLoaderGate => IsGameReady && !IsActiveLoaderReady;

    public string LoaderGateMessage
    {
        get
        {
            Loader loader = _settings.Current.DefaultLoader;
            string name = loader == Loader.None ? "a mod loader" : $"the {loader.ToDisplayName()} loader";
            GameVersion? target = ResolveTargetVersion();
            return target is null
                ? $"Install {name} in Settings before adding mods."
                : $"Install {name} for {target} in Settings before adding mods.";
        }
    }

    private void RaiseLoaderGate()
    {
        OnPropertyChanged(nameof(IsActiveLoaderReady));
        OnPropertyChanged(nameof(CanInstallHere));
        OnPropertyChanged(nameof(ShowLoaderGate));
        OnPropertyChanged(nameof(LoaderGateMessage));
    }

    /// <summary>Single-flight gate so install buttons disable while any operation runs.</summary>
    public OperationGate Gate => _gate;

    public ObservableCollection<CatalogItemViewModel> Results { get; } = [];

    public ObservableCollection<int> PageNumbers { get; } = [];

    [ObservableProperty] private string _browseSource = "modrinth";
    [ObservableProperty] private string _browseQuery = string.Empty;
    [ObservableProperty] private string _browseSort = "relevance";
    [ObservableProperty] private string _browseCat = "all";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _resultCountLabel = string.Empty;
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _totalPages = 1;

    public bool HasPages => TotalPages > 1;
    public bool CanPrev => CurrentPage > 1;
    public bool CanNext => CurrentPage < TotalPages;

    partial void OnBrowseSourceChanged(string value) => RestartSearch();
    partial void OnBrowseQueryChanged(string value) => RestartSearch();
    partial void OnBrowseSortChanged(string value) => RestartSearch();
    partial void OnBrowseCatChanged(string value)
    {
        RaiseLoaderGate(); // switching between mods and packs/shaders changes whether a loader is required
        RestartSearch();
    }

    partial void OnCurrentPageChanged(int value)
    {
        OnPropertyChanged(nameof(CanPrev));
        OnPropertyChanged(nameof(CanNext));
    }

    partial void OnTotalPagesChanged(int value)
    {
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(CanPrev));
        OnPropertyChanged(nameof(CanNext));
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        await SearchAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (CanNext)
        {
            CurrentPage++;
            await SearchAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (CanPrev)
        {
            CurrentPage--;
            await SearchAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task GoToPageAsync(int page)
    {
        if (page >= 1 && page != CurrentPage)
        {
            CurrentPage = page;
            await SearchAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    private void RestartSearch()
    {
        CurrentPage = 1;
        QueueSearch();
    }

    private void QueueSearch()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = new CancellationTokenSource();
        CancellationToken token = _debounce.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token).ConfigureAwait(true);
                _ui.Post(() => _ = SearchAsync(token));
            }
            catch (TaskCanceledException)
            {
                // superseded by a newer keystroke
            }
        }, token);
    }

    private async Task SearchAsync(CancellationToken ct)
    {
        IModSource? source = _registry.Find(BrowseSource);
        if (source is null || !source.IsConfigured)
        {
            Results.Clear();
            PageNumbers.Clear();
            TotalPages = 1;
            IsEmpty = true;
            ResultCountLabel = BrowseSource == "curseforge" ? "CurseForge isn't configured yet" : "0 results";
            return;
        }

        IsLoading = true;
        try
        {
            Result<ModSearchResult> result = await source.SearchAsync(BuildQuery(), ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            Results.Clear();
            if (result.IsSuccess)
            {
                Loader activeLoader = _settings.Current.DefaultLoader;
                HashSet<string> installed = (await _repository.GetAllAsync(ct).ConfigureAwait(true))
                    .Where(i => i.MatchesLoaderProfile(activeLoader)) // installed *for the active profile*, not globally
                    .Select(i => i.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (CatalogProject project in result.Value.Items)
                {
                    Results.Add(new CatalogItemViewModel(project, installed.Contains(project.Id), InstallAsync, p => OpenDetailRequested?.Invoke(p)));
                }

                int total = result.Value.TotalCount;
                ResultCountLabel = $"{total:N0} result{(total == 1 ? string.Empty : "s")}";
                TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
                RebuildPageWindow();
            }
            else
            {
                ResultCountLabel = "Couldn't reach " + source.Name;
                TotalPages = 1;
                PageNumbers.Clear();
            }

            IsEmpty = Results.Count == 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RebuildPageWindow()
    {
        PageNumbers.Clear();
        int start = Math.Max(1, CurrentPage - 3);
        int end = Math.Min(TotalPages, start + 6);
        start = Math.Max(1, end - 6);
        for (int p = start; p <= end; p++)
        {
            PageNumbers.Add(p);
        }
    }

    private ModSearchQuery BuildQuery()
    {
        ContentType? type = BrowseCat switch
        {
            "resource-packs" => ContentType.ResourcePack,
            "shaders" => ContentType.Shader,
            _ => null,
        };

        string? category = BrowseCat switch
        {
            "all" or "resource-packs" or "shaders" => null,
            "tech" => "technology",
            _ => BrowseCat,
        };

        ModSortOrder sort = BrowseSort switch
        {
            "downloads" => ModSortOrder.Downloads,
            "followers" => ModSortOrder.Followers,
            _ => ModSortOrder.Relevance,
        };

        // Filter to the active profile: version always (when not "All"), and the loader for mods only —
        // so e.g. with Fabric selected you only see Fabric mods for that version.
        GameVersion? version = ActiveProfile.Selected(_settings.Current);

        Loader? loader = type is null && _settings.Current.DefaultLoader != Loader.None
            ? _settings.Current.DefaultLoader
            : null;

        int offset = (CurrentPage - 1) * PageSize;
        return new ModSearchQuery(BrowseQuery, type, category, sort, version, loader, offset, PageSize);
    }

    private async Task InstallAsync(CatalogItemViewModel item)
    {
        if (!IsGameReady)
        {
            _bus.Publish(new ToastMessage("Set your Minecraft folder first", "Lodestone needs a game folder before installing.", ToastKind.Warning));
            return;
        }

        GameVersion? target = ResolveTargetVersion();
        if (target is null)
        {
            _bus.Publish(new ToastMessage("No Minecraft version yet", "Install a Minecraft version (or pick one in My Content) before installing mods.", ToastKind.Warning));
            return;
        }

        // Mods need their loader installed first (resource packs/shaders don't — IsActiveLoaderReady is true for them).
        if (!IsActiveLoaderReady)
        {
            _bus.Publish(new ToastMessage("Loader not installed", LoaderGateMessage, ToastKind.Warning));
            return;
        }

        await _gate.RunInstallAsync($"Installing {item.Name}…", async () =>
        {
            item.Installing = true;
            try
            {
                Result<CatalogInstall> result = await _install
                    .ExecuteAsync(item.Project, target, _settings.Current.DefaultLoader)
                    .ConfigureAwait(true);

                if (result.IsSuccess)
                {
                    item.Installed = true;
                    _bus.Publish(new ToastMessage("Installed", DescribeInstall(item.Name, result.Value)));
                    WarnIfVersionNotInstalled(target);
                    _bus.Publish(new LibraryChanged());
                }
                else
                {
                    _bus.Publish(new ToastMessage("Couldn't install", result.Error.Message, ToastKind.Error));
                }
            }
            finally
            {
                item.Installing = false;
            }
        }).ConfigureAwait(true);
    }

    private static string DescribeInstall(string name, CatalogInstall install)
    {
        string text = $"{name} · {install.Item.Type.ToDisplayName()}";
        int deps = install.InstalledDependencies.Count;
        return deps == 0 ? text : $"{text}  ·  +{deps} dependenc{(deps == 1 ? "y" : "ies")}";
    }

    private async void MarkInstalledFromLibrary()
    {
        Loader activeLoader = _settings.Current.DefaultLoader;
        HashSet<string> installed = (await _repository.GetAllAsync().ConfigureAwait(true))
            .Where(i => i.MatchesLoaderProfile(activeLoader)) // installed *for the active profile*, not globally
            .Select(i => i.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (CatalogItemViewModel item in Results)
        {
            item.Installed = installed.Contains(item.Project.Id);
        }
    }

    /// <summary>A concrete install target (selected version, else newest installed); null when nothing is installed.</summary>
    private GameVersion? ResolveTargetVersion() => ActiveProfile.Target(_settings.Current, _inventory);

    // Mods install even for a version that isn't set up yet (the user may be pre-staging) — but say so.
    private void WarnIfVersionNotInstalled(GameVersion target)
    {
        if (!_inventory.IsVersionInstalled(target))
        {
            _bus.Publish(new ToastMessage("Heads up",
                $"Installed for {target}, but that Minecraft version isn't set up in your launcher yet — it won't load until you install it.",
                ToastKind.Warning));
        }
    }

    public void Dispose() => _debounce?.Dispose();
}
