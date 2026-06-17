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

/// <summary>One entry in the My Content category filter: the source category slug (or a sentinel) and its label.</summary>
public sealed record CategoryOption(string Key, string Label);

/// <summary>One non-collapsible category section in the grouped My Content list: an uppercase header and its rows.</summary>
public sealed record CategorySection(string Header, IReadOnlyList<ContentItemViewModel> Items);

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
    private const string UncategorizedKey = LibraryGrouping.UncategorizedKey;

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
    private bool _suppressCategory;

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

    /// <summary>"All categories" plus every category present on the mods in the current profile/tab; a
    /// trailing "Uncategorized" bucket appears when some items declare no category. Empty (and hidden)
    /// when nothing is categorized.</summary>
    public ObservableCollection<CategoryOption> Categories { get; } = [new(AllKey, "All categories")];

    public ObservableCollection<ContentItemViewModel> Items { get; } = [];

    /// <summary>The category sections shown on "All categories" (issue #5); see <see cref="IsGrouped"/>.</summary>
    public ObservableCollection<CategorySection> Sections { get; } = [];

    [ObservableProperty] private string _libTab = "mods";
    [ObservableProperty] private string _selectedProfileKey;
    [ObservableProperty] private string _selectedCategoryKey = AllKey;
    [ObservableProperty] private string _libSearch = string.Empty;
    [ObservableProperty] private string _countLabel = string.Empty;
    [ObservableProperty] private bool _isEmpty;

    /// <summary>True when the list is laid out as category sections (<see cref="Sections"/>) rather than the flat <see cref="Items"/>.</summary>
    [ObservableProperty] private bool _isGrouped;

    /// <summary>The category filter is only useful once at least one item carries a category — otherwise hidden.</summary>
    [ObservableProperty] private bool _showCategoryFilter;

    partial void OnLibTabChanged(string value) => Rebuild();

    partial void OnLibSearchChanged(string value) => Rebuild();

    partial void OnSelectedCategoryKeyChanged(string value)
    {
        // Ignore the transient null/empty that arrives while the dropdown's ItemsSource is rebuilt, and
        // the programmatic resets we make while refreshing the options (guarded by _suppressCategory).
        if (string.IsNullOrEmpty(value) || _suppressCategory)
        {
            return;
        }

        Rebuild();
    }

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

        // The set in scope for this profile + tab, before the search and category facets are applied —
        // it's what both the category dropdown and the visible list are derived from.
        bool allProfiles;
        IReadOnlyList<InstalledContent> baseSet;
        if (SelectedProfileKey == UnknownKey)
        {
            // The "Unknown" bucket: items of this type with no attributed version, for manual sorting.
            allProfiles = false;
            baseSet = _all.Where(i => i.Type == type && i.GameVersions.Count == 0).ToList();
        }
        else
        {
            (string versionValue, Loader loader) = Parse(SelectedProfileKey);
            allProfiles = versionValue is AllKey or "";
            GameVersion? version = allProfiles ? null : GameVersion.Create(versionValue).Match<GameVersion?>(v => v, _ => null);

            baseSet = LibraryQuery.Apply(_all, new LibraryFilter(type, version, null, allProfiles ? null : loader));
        }

        RefreshCategoryOptions(baseSet);

        // Narrow to the visible list by the search text and the selected category.
        string? search = string.IsNullOrWhiteSpace(LibSearch) ? null : LibSearch;
        List<InstalledContent> filtered = baseSet
            .Where(i => Matches(i, search) && MatchesCategory(i))
            .ToList();

        // Targets an unsorted mod can be assigned to: a prompt, then each concrete (version + loader) profile.
        var assignTargets = new List<ProfileOption> { new(string.Empty, "Assign to…") };
        assignTargets.AddRange(Profiles.Where(p => p.Key != AllKey && p.Key != UnknownKey));

        ContentItemViewModel BuildItem(InstalledContent item)
        {
            _reports.TryGetValue(item.Id, out CompatibilityReport? report);
            return new ContentItemViewModel(item, report, allProfiles, assignTargets, ToggleAsync, UninstallAsync, AssignAsync);
        }

        // On "All categories", lay the list out as labelled category sections (issue #5) instead of one flat
        // list — but only when there's a category to group by and the split yields more than one section
        // (a single section would just be the flat list under a redundant header).
        IReadOnlyList<CategoryGroup> groups = SelectedCategoryKey is AllKey or "" && ShowCategoryFilter
            ? LibraryGrouping.ByPrimaryCategory(filtered)
            : [];
        bool grouped = groups.Count > 1;

        Items.Clear();
        Sections.Clear();
        if (grouped)
        {
            foreach (CategoryGroup group in groups)
            {
                Sections.Add(new CategorySection(HeaderFor(group.Key), group.Items.Select(BuildItem).ToList()));
            }
        }
        else
        {
            foreach (InstalledContent item in filtered)
            {
                Items.Add(BuildItem(item));
            }
        }

        IsGrouped = grouped;

        string tabLabel = _libTab switch
        {
            "resourcepacks" => "resource packs",
            "shaders" => "shaders",
            _ => "mods",
        };
        string profileLabel = Profiles
            .FirstOrDefault(p => p.Key.Equals(SelectedProfileKey, StringComparison.OrdinalIgnoreCase))?.Label ?? "All profiles";
        CountLabel = $"{filtered.Count} {tabLabel}" + (allProfiles ? string.Empty : $"   ·   {profileLabel}");
        IsEmpty = filtered.Count == 0;
    }

    // The uppercase, greyed section header for a category key — the same label the dropdown shows (so the two
    // stay in sync, per issue #5), upper-cased for the section-title aesthetic.
    private static string HeaderFor(string key)
        => (key == UncategorizedKey ? "Uncategorized" : Prettify(key)).ToUpperInvariant();

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

    private bool MatchesCategory(InstalledContent item) => SelectedCategoryKey switch
    {
        AllKey or "" => true,
        UncategorizedKey => item.Categories.Count == 0,
        var key => item.Categories.Any(c => string.Equals(c, key, StringComparison.OrdinalIgnoreCase)),
    };

    // Builds the category dropdown from the categories actually present on the base set. The filter is
    // hidden entirely when nothing is categorized (everything would read "uncategorized"), per the design:
    // the ability only appears once it can do something. Repairs a now-absent selection back to "All".
    private void RefreshCategoryOptions(IReadOnlyList<InstalledContent> baseSet)
    {
        List<string> present = baseSet
            .SelectMany(i => i.Categories)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToLowerInvariant())
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        ShowCategoryFilter = present.Count > 0;

        var desired = new List<CategoryOption> { new(AllKey, "All categories") };
        if (ShowCategoryFilter)
        {
            desired.AddRange(present.Select(c => new CategoryOption(c, Prettify(c))));
            if (baseSet.Any(i => i.Categories.Count == 0))
            {
                desired.Add(new CategoryOption(UncategorizedKey, "Uncategorized"));
            }
        }

        string target = desired.Any(o => o.Key.Equals(SelectedCategoryKey, StringComparison.OrdinalIgnoreCase))
            ? SelectedCategoryKey
            : AllKey;

        _suppressCategory = true;
        try
        {
            if (!Categories.Select(o => o.Key).SequenceEqual(desired.Select(o => o.Key), StringComparer.OrdinalIgnoreCase))
            {
                Categories.Clear();
                foreach (CategoryOption option in desired)
                {
                    Categories.Add(option);
                }
            }

            SelectedCategoryKey = target; // re-assert (clearing the list can null the bound SelectedValue)
        }
        finally
        {
            _suppressCategory = false;
        }
    }

    // "game-mechanics" → "Game Mechanics"; already-cased display categories pass through unchanged.
    private static string Prettify(string slug)
    {
        string[] words = slug.Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    private async Task ToggleAsync(string id)
    {
        Result result = await _toggle.ExecuteAsync(id).ConfigureAwait(true);
        if (result.IsFailure)
        {
            _bus.Publish(new ToastMessage("Couldn't change that", result.Error.Message, ToastKind.Error));
        }

        _bus.Publish(new LibraryChanged());
    }

    // Manual sort: pin an unattributed mod to a chosen (version + loader) profile and re-evaluate the library.
    private async Task AssignAsync(string id, string profileKey)
    {
        InstalledContent? item = await _repository.FindAsync(id).ConfigureAwait(true);
        if (item is null)
        {
            return;
        }

        (string versionValue, Loader loader) = Parse(profileKey);
        if (GameVersion.Create(versionValue).Match<GameVersion?>(v => v, _ => null) is not { } version)
        {
            return;
        }

        item.GameVersions = [version];
        if (loader != Loader.None)
        {
            item.Loader = loader;
        }

        await _repository.UpsertAsync(item).ConfigureAwait(true);
        _bus.Publish(new ToastMessage("Sorted",
            $"{item.Name} → {version.Value}" + (loader != Loader.None ? $" · {loader.ToDisplayName()}" : string.Empty)));
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
