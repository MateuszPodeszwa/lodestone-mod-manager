using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lodestone.App.Services;
using Lodestone.Application.Abstractions;
using Lodestone.Application.Compatibility;
using Lodestone.Application.Library;
using Lodestone.Application.Messaging;
using Lodestone.Application.Settings;
using Lodestone.Application.UseCases;
using Lodestone.Domain;
using Lodestone.Domain.Common;
using Lodestone.Domain.Compatibility;

namespace Lodestone.App.ViewModels;

/// <summary>One entry in the My Content profile selector: a stable key and its display label.</summary>
public sealed record ProfileOption(string Key, string Label);

/// <summary>
/// "My Content": switch between installed (version + loader) profiles, type tabs, search,
/// toggle/uninstall, and the compatibility symbols from the rule engine. Selecting a concrete profile
/// swaps the shared mods/ folder so only that profile's mods are live (other loaders/versions are set
/// aside, reversibly); "All profiles" is a view-only filter that changes nothing on disk.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private const string AllKey = "all";
    private const string UnknownKey = "unknown";

    private readonly IInstalledContentRepository _repository;
    private readonly ICompatibilityService _compatibility;
    private readonly ToggleContentUseCase _toggle;
    private readonly UninstallContentUseCase _uninstall;
    private readonly SwitchProfileUseCase _switch;
    private readonly ISettingsStore _settings;
    private readonly IMessageBus _bus;
    private readonly IUiDispatcher _ui;
    private readonly IGameInventory _inventory;
    private readonly OperationGate _gate;

    private IReadOnlyList<InstalledContent> _all = [];
    private IReadOnlyList<GameVersion> _installedVersions = [];
    private IReadOnlyDictionary<string, CompatibilityReport> _reports = new Dictionary<string, CompatibilityReport>();
    private bool _suppressSwitch;

    public LibraryViewModel(
        IInstalledContentRepository repository,
        ICompatibilityService compatibility,
        ToggleContentUseCase toggle,
        UninstallContentUseCase uninstall,
        SwitchProfileUseCase switchProfile,
        ISettingsStore settings,
        IMessageBus bus,
        IUiDispatcher ui,
        IGameInventory inventory,
        OperationGate gate)
    {
        _repository = repository;
        _compatibility = compatibility;
        _toggle = toggle;
        _uninstall = uninstall;
        _switch = switchProfile;
        _settings = settings;
        _bus = bus;
        _ui = ui;
        _inventory = inventory;
        _gate = gate;
        _selectedProfileKey = KeyFor(settings.Current);
        bus.Subscribe<LibraryChanged>(m => _ui.Post(() => _ = LoadAsync()));
    }

    /// <summary>Single-flight gate so the profile selector disables while any operation runs.</summary>
    public OperationGate Gate => _gate;

    /// <summary>"All profiles" plus every installed (version + loader) profile, newest first.</summary>
    public ObservableCollection<ProfileOption> Profiles { get; } = [new(AllKey, "All profiles")];

    public ObservableCollection<ContentItemViewModel> Items { get; } = [];

    [ObservableProperty] private string _libTab = "mods";
    [ObservableProperty] private string _selectedProfileKey;
    [ObservableProperty] private string _libSearch = string.Empty;
    [ObservableProperty] private string _countLabel = string.Empty;
    [ObservableProperty] private bool _isEmpty;

    partial void OnLibTabChanged(string value) => Rebuild();

    partial void OnLibSearchChanged(string value) => Rebuild();

    partial void OnSelectedProfileKeyChanged(string value)
    {
        // A transient null/empty can arrive while the dropdown's ItemsSource is rebuilt — ignore it.
        if (string.IsNullOrEmpty(value) || _suppressSwitch)
        {
            return;
        }

        _ = ApplyProfileAsync(value);
    }

    // Persists the chosen profile and, when it's a concrete (version + loader), swaps the mods/ folder so
    // only that profile's mods are live. "All profiles" just re-filters the view and touches nothing.
    private async Task ApplyProfileAsync(string key)
    {
        if (key == UnknownKey)
        {
            // A view-only bucket for unattributed content — never changes the active profile or touches disk.
            Rebuild();
            return;
        }

        (string version, Loader loader) = Parse(key);

        LodestoneSettings s = _settings.Current.Clone();
        s.SelectedVersion = version;
        s.SelectedLoader = loader;
        if (loader != Loader.None)
        {
            s.DefaultLoader = loader; // installs and Browse follow the active profile's loader
        }

        await _settings.SaveAsync(s).ConfigureAwait(true);

        if (loader != Loader.None &&
            GameVersion.Create(version).Match<GameVersion?>(v => v, _ => null) is { } gameVersion)
        {
            await _gate.RunAsync($"Switching to {version} · {loader.ToDisplayName()}…", async () =>
            {
                Result<ProfileSwitch> switched = await _switch.ExecuteAsync(gameVersion, loader).ConfigureAwait(true);
                if (switched.IsFailure)
                {
                    _bus.Publish(new ToastMessage("Couldn't switch profile", switched.Error.Message, ToastKind.Error));
                }
                else if (switched.Value.Enabled + switched.Value.Disabled > 0)
                {
                    _bus.Publish(new ToastMessage(
                        $"Switched to {version} · {loader.ToDisplayName()}",
                        $"{switched.Value.Enabled} mod(s) active, {switched.Value.Disabled} set aside."));
                }
            }).ConfigureAwait(true);
        }

        _bus.Publish(new LibraryChanged()); // refresh Home/Browse against the new active profile
        await LoadAsync().ConfigureAwait(true);
    }

    public async Task LoadAsync()
    {
        _all = await _repository.GetAllAsync().ConfigureAwait(true);
        RefreshProfileOptions();

        GameVersion? activeVersion = ActiveProfile.Selected(_settings.Current);
        _reports = _compatibility.Analyze(new CompatibilityContext(_all, activeVersion, _settings.Current.DefaultLoader)
        {
            InstalledGameVersions = _installedVersions,
        });
        Rebuild();
    }

    // Rebuilds the selector from the installed profiles and repairs a stale stored selection — so the
    // list only ever offers profiles the user actually has a loader installed for.
    private void RefreshProfileOptions()
    {
        _installedVersions = _inventory.InstalledVersions();

        var desired = new List<ProfileOption> { new(AllKey, "All profiles") };
        desired.AddRange(_inventory.InstalledProfiles()
            .Where(p => !p.IsVanilla)
            .Select(p => new ProfileOption(p.Key, p.Label)));

        // A bucket for adopted mods Lodestone couldn't attribute to a version — only shown when some exist.
        if (_all.Any(i => i.Type.UsesLoader() && i.GameVersions.Count == 0))
        {
            desired.Add(new ProfileOption(UnknownKey, "Unknown (needs sorting)"));
        }

        string target = desired.Any(o => o.Key.Equals(SelectedProfileKey, StringComparison.OrdinalIgnoreCase))
            ? SelectedProfileKey
            : AllKey;

        _suppressSwitch = true;
        try
        {
            if (!Profiles.Select(p => p.Key).SequenceEqual(desired.Select(p => p.Key), StringComparer.OrdinalIgnoreCase))
            {
                Profiles.Clear();
                foreach (ProfileOption option in desired)
                {
                    Profiles.Add(option);
                }
            }

            // Re-assert the selection (clearing the list above can null the bound SelectedValue).
            SelectedProfileKey = target;
        }
        finally
        {
            _suppressSwitch = false;
        }
    }

    private void Rebuild()
    {
        ContentType type = _libTab switch
        {
            "resourcepacks" => ContentType.ResourcePack,
            "shaders" => ContentType.Shader,
            _ => ContentType.Mod,
        };

        bool allProfiles;
        IReadOnlyList<InstalledContent> filtered;
        if (SelectedProfileKey == UnknownKey)
        {
            // The "Unknown" bucket: items of this type with no attributed version, for manual sorting.
            allProfiles = false;
            string? search = string.IsNullOrWhiteSpace(LibSearch) ? null : LibSearch;
            filtered = _all.Where(i => i.Type == type && i.GameVersions.Count == 0 && Matches(i, search)).ToList();
        }
        else
        {
            (string versionValue, Loader loader) = Parse(SelectedProfileKey);
            allProfiles = versionValue is AllKey or "";
            GameVersion? version = allProfiles ? null : GameVersion.Create(versionValue).Match<GameVersion?>(v => v, _ => null);

            var filter = new LibraryFilter(
                type,
                version,
                string.IsNullOrWhiteSpace(LibSearch) ? null : LibSearch,
                allProfiles ? null : loader);
            filtered = LibraryQuery.Apply(_all, filter);
        }

        Items.Clear();
        foreach (InstalledContent item in filtered)
        {
            _reports.TryGetValue(item.Id, out CompatibilityReport? report);
            Items.Add(new ContentItemViewModel(item, report, allProfiles, ToggleAsync, UninstallAsync));
        }

        string tabLabel = _libTab switch
        {
            "resourcepacks" => "resource packs",
            "shaders" => "shaders",
            _ => "mods",
        };
        string profileLabel = Profiles
            .FirstOrDefault(p => p.Key.Equals(SelectedProfileKey, StringComparison.OrdinalIgnoreCase))?.Label ?? "All profiles";
        CountLabel = $"{Items.Count} {tabLabel}" + (allProfiles ? string.Empty : $"   ·   {profileLabel}");
        IsEmpty = Items.Count == 0;
    }

    private static string KeyFor(LodestoneSettings s)
        => ActiveProfile.IsAllVersions(s.SelectedVersion) || s.SelectedLoader == Loader.None
            ? AllKey
            : $"{s.SelectedVersion}|{s.SelectedLoader.ToSlug()}";

    private static (string Version, Loader Loader) Parse(string key)
    {
        if (string.IsNullOrEmpty(key) || key == AllKey)
        {
            return (AllKey, Loader.None);
        }

        int bar = key.IndexOf('|');
        return bar < 0 ? (key, Loader.None) : (key[..bar], key[(bar + 1)..].ParseLoader());
    }

    private static bool Matches(InstalledContent item, string? search)
        => search is null
           || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
           || item.Author.Contains(search, StringComparison.OrdinalIgnoreCase);

    private async Task ToggleAsync(string id)
    {
        Result result = await _toggle.ExecuteAsync(id).ConfigureAwait(true);
        if (result.IsFailure)
        {
            _bus.Publish(new ToastMessage("Couldn't change that", result.Error.Message, ToastKind.Error));
        }

        _bus.Publish(new LibraryChanged());
    }

    private async Task UninstallAsync(string id)
    {
        InstalledContent? item = await _repository.FindAsync(id).ConfigureAwait(true);
        Result result = await _uninstall.ExecuteAsync(id).ConfigureAwait(true);
        if (result.IsSuccess && item is not null)
        {
            _bus.Publish(new ToastMessage("Uninstalled", item.Name));
        }
        else if (result.IsFailure)
        {
            _bus.Publish(new ToastMessage("Couldn't uninstall", result.Error.Message, ToastKind.Error));
        }

        _bus.Publish(new LibraryChanged());
    }
}
