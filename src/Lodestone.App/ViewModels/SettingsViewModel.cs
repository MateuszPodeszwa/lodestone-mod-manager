using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lodestone.App.Services;
using Lodestone.Application.Abstractions;
using Lodestone.Application.Common;
using Lodestone.Application.Messaging;
using Lodestone.Application.Settings;
using Lodestone.Application.Supporter;
using Lodestone.Domain;
using Lodestone.Domain.Common;

namespace Lodestone.App.ViewModels;

/// <summary>One selectable accent swatch in Settings (locked for non-supporters when supporter-only).</summary>
public sealed partial class AccentSwatchViewModel(AccentOption option, bool unlocked, bool selected) : ObservableObject
{
    public string Name { get; } = option.Name;
    public string Hex { get; } = option.Hex;
    public bool SupporterOnly { get; } = option.SupporterOnly;

    // Frozen so the brush carries no thread affinity: the swatch is a fixed palette colour (only the shared
    // AccentBrush is mutated for live re-colouring), so it's safe to freeze — and a frozen brush can be
    // created and bound from any thread, immune to WPF's "DependencySource on same Thread" cross-thread trap.
    public Brush Swatch { get; } = Frozen(AccentApplier.Parse(option.Hex));

    [ObservableProperty] private bool _isSelected = selected;
    [ObservableProperty] private bool _isLocked = option.SupporterOnly && !unlocked;

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>The Settings screen — every control is wired to <see cref="LodestoneSettings"/> and saved on change.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialog;
    private readonly IGameLocator _locator;
    private readonly IAppUpdater _updater;
    private readonly IMessageBus _bus;
    private readonly ILoaderInstaller _loaderInstaller;
    private readonly IGameInventory _inventory;
    private readonly SupporterService _supporter;
    private readonly IUiDispatcher _ui;
    private bool _ready;

    public SettingsViewModel(
        ISettingsStore settings,
        IDialogService dialog,
        IGameLocator locator,
        IAppUpdater updater,
        IMessageBus bus,
        ILoaderInstaller loaderInstaller,
        IGameInventory inventory,
        SupporterService supporter,
        IUiDispatcher ui)
    {
        _settings = settings;
        _dialog = dialog;
        _locator = locator;
        _updater = updater;
        _bus = bus;
        _loaderInstaller = loaderInstaller;
        _inventory = inventory;
        _supporter = supporter;
        _ui = ui;
        ReloadFromSettings();
        string version = _updater.CurrentVersion;
        string? codename = ReleaseNames.For(version);
        AppVersionLabel = codename is null ? $"Lodestone {version}" : $"Lodestone {version}  ·  {codename}";
        _ready = true;

        // Keep the screen in sync when the folder/settings are changed elsewhere (e.g. the shell banner).
        // SaveAsync raises Changed on a thread-pool thread (it awaits the file write with ConfigureAwait(false)),
        // so marshal back onto the UI thread before rebuilding bound state — RebuildAccents creates the swatch
        // brushes, and any DependencyObject bound to the UI must be created on the UI thread.
        _settings.Changed += (_, _) => _ui.Post(() =>
        {
            ReloadFromSettings();
            OnPropertyChanged(nameof(IsGameReady));
        });

        // Re-evaluate supporter-gated perks when status changes (redeem/revoke on the Support page).
        _supporter.Changed += (_, _) => _ui.Post(OnSupporterChanged);
    }

    /// <summary>True once a valid Minecraft folder is set; gates the loader picker.</summary>
    public bool IsGameReady => _locator.IsValid(_settings.Current.GameDirectory);

    /// <summary>Early access (beta update channel) is a supporter perk.</summary>
    public bool CanUseEarlyAccess => _supporter.IsSupporter;

    /// <summary>Accent themes beyond the default are a supporter perk.</summary>
    public bool CanUseThemes => _supporter.IsSupporter;

    [ObservableProperty] private string? _gameDir;
    [ObservableProperty] private string _loader = "fabric";
    [ObservableProperty] private string? _loaderGameVersion;
    [ObservableProperty] private bool _autoUpdate;
    [ObservableProperty] private bool _notify;
    [ObservableProperty] private bool _earlyAccess;
    [ObservableProperty] private int _concurrent;
    [ObservableProperty] private bool _curseFallback;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private string _appVersionLabel = "Lodestone";

    /// <summary>The accent swatches shown in Settings (default + supporter-only colours).</summary>
    public ObservableCollection<AccentSwatchViewModel> Accents { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateLoaderLabel))]
    private bool _isUpdatingLoader;

    public string UpdateLoaderLabel => IsUpdatingLoader ? "Updating…" : "Update loader";

    /// <summary>The Minecraft versions actually installed — the choices for which version to set the loader up against.</summary>
    public ObservableCollection<string> GameVersions { get; } = [];

    public void ReloadFromSettings()
    {
        LodestoneSettings s = _settings.Current;
        _gameDir = s.GameDirectory;
        _loader = s.DefaultLoader.ToSlug() is { Length: > 0 } slug ? slug : "fabric";
        _autoUpdate = s.AutoUpdate;
        _notify = s.NotifyUpdates;
        // Early access is a supporter perk: never reflect Beta to a non-supporter.
        _earlyAccess = s.UpdateChannel == UpdateChannel.Beta && _supporter.IsSupporter;
        _concurrent = s.ConcurrentDownloads;
        _curseFallback = s.CurseForgeFallback;
        _closeToTray = s.CloseToTray;
        RefreshGameVersions();
        RebuildAccents();
        OnPropertyChanged(string.Empty); // refresh all bindings
    }

    // Builds the swatch list, marking the active one selected and locking supporter-only colours for non-supporters.
    private void RebuildAccents()
    {
        string? current = _supporter.IsSupporter ? _settings.Current.AccentColor : null;
        Accents.Clear();
        foreach (AccentOption option in SupporterAccents.All)
        {
            bool selected = SupporterAccents.IsDefault(option.Hex)
                ? SupporterAccents.IsDefault(current)
                : string.Equals(option.Hex, current, StringComparison.OrdinalIgnoreCase);
            Accents.Add(new AccentSwatchViewModel(option, _supporter.IsSupporter, selected));
        }
    }

    // When supporter status flips, re-gate the perks and snap back to safe values if it was lost.
    private void OnSupporterChanged()
    {
        OnPropertyChanged(nameof(CanUseEarlyAccess));
        OnPropertyChanged(nameof(CanUseThemes));

        if (!_supporter.IsSupporter)
        {
            if (EarlyAccess)
            {
                EarlyAccess = false; // reverts the channel via OnEarlyAccessChanged
            }

            if (!SupporterAccents.IsDefault(_settings.Current.AccentColor))
            {
                ApplyAccent(SupporterAccents.DefaultHex); // drop the supporter accent
            }
        }

        RebuildAccents();
    }

    // Rebuilds the installed-version choices and defaults the loader target to the active selection
    // (when concrete and installed), else the newest installed version — never a hardcoded guess.
    private void RefreshGameVersions()
    {
        IReadOnlyList<GameVersion> installed = _inventory.InstalledVersions();
        var values = installed.Select(v => v.Value).ToList();
        if (!GameVersions.SequenceEqual(values, StringComparer.OrdinalIgnoreCase))
        {
            GameVersions.Clear();
            foreach (string value in values)
            {
                GameVersions.Add(value);
            }
        }

        bool currentInstalled = _loaderGameVersion is { } cur &&
            installed.Any(v => v.Value.Equals(cur, StringComparison.OrdinalIgnoreCase));
        if (!currentInstalled)
        {
            GameVersion? selected = ActiveProfile.Selected(_settings.Current);
            _loaderGameVersion = selected is not null && installed.Any(v => v.Equals(selected))
                ? selected.Value
                : values.Count > 0 ? values[0] : null;
        }
    }

    partial void OnLoaderChanged(string value)
    {
        Save();
        _ = InstallLoaderAsync(value);
    }

    // When the user picks a loader, install it in the background (Fabric/Quilt) so it's ready in the launcher.
    private async Task InstallLoaderAsync(string loaderSlug)
    {
        if (!_ready || !_locator.IsValid(_settings.Current.GameDirectory))
        {
            return;
        }

        Lodestone.Domain.Loader loader = loaderSlug.ParseLoader();
        if (!_loaderInstaller.Supports(loader))
        {
            _bus.Publish(new ToastMessage($"{loader.ToDisplayName()} loader",
                $"{loader.ToDisplayName()} must be installed with its official installer — Lodestone still manages its mods.", ToastKind.Info));
            return;
        }

        GameVersion? version = ResolveLoaderVersion();
        if (version is null)
        {
            _bus.Publish(new ToastMessage("No Minecraft version found",
                "Run Minecraft once to install a version, then Lodestone can set up the loader for it.", ToastKind.Warning));
            return;
        }

        Result result = await _loaderInstaller.EnsureInstalledAsync(loader, version).ConfigureAwait(true);
        if (result.IsSuccess)
        {
            _bus.Publish(new ToastMessage($"{loader.ToDisplayName()} ready", $"Installed for {version} — pick it in your launcher."));
        }
        else if (result.Error.Code != "game.dir_missing")
        {
            _bus.Publish(new ToastMessage("Loader install failed", result.Error.Message, ToastKind.Error));
        }
    }
    partial void OnAutoUpdateChanged(bool value) => Save();
    partial void OnNotifyChanged(bool value) => Save();
    partial void OnConcurrentChanged(int value) => Save();
    partial void OnCurseFallbackChanged(bool value) => Save();
    partial void OnCloseToTrayChanged(bool value) => Save();
    partial void OnGameDirChanged(string? value) => Save();

    partial void OnEarlyAccessChanged(bool value)
    {
        if (value && !_supporter.IsSupporter)
        {
            // UI gates this, but never let a non-supporter onto the beta channel.
            _earlyAccess = false;
            OnPropertyChanged(nameof(EarlyAccess));
            _bus.Publish(new ToastMessage("Supporter perk", "Early access is a supporter perk — see the Support page.", ToastKind.Info));
            return;
        }

        Save();
        if (_ready)
        {
            _bus.Publish(value
                ? new ToastMessage("Early access on", "You'll get beta builds on the next update check.")
                : new ToastMessage("Early access off", "You're back on the stable channel."));
        }
    }

    [RelayCommand]
    private void SelectAccent(AccentSwatchViewModel? swatch)
    {
        if (swatch is null)
        {
            return;
        }

        if (swatch.IsLocked)
        {
            _bus.Publish(new ToastMessage("Supporter perk", "Accent themes are unlocked for supporters — see the Support page.", ToastKind.Info));
            return;
        }

        ApplyAccent(swatch.Hex);
    }

    // Persists the chosen accent (null = default) and recolours the UI live.
    private void ApplyAccent(string hex)
    {
        bool isDefault = SupporterAccents.IsDefault(hex);
        LodestoneSettings s = _settings.Current.Clone();
        s.AccentColor = isDefault ? null : hex;
        _ = _settings.SaveAsync(s);
        AccentApplier.Apply(s.AccentColor, _supporter.IsSupporter);

        foreach (AccentSwatchViewModel a in Accents)
        {
            a.IsSelected = SupporterAccents.IsDefault(a.Hex)
                ? isDefault
                : string.Equals(a.Hex, hex, StringComparison.OrdinalIgnoreCase);
        }
    }

    [RelayCommand]
    private void ChangeDir()
    {
        string? picked = _dialog.PickFolder(GameDir);
        if (picked is null)
        {
            return;
        }

        if (!_locator.IsValid(picked))
        {
            _bus.Publish(new ToastMessage("That doesn't look right", "Pick the folder that contains your mods/ and versions/ folders.", ToastKind.Warning));
            return;
        }

        GameDir = picked;
        _bus.Publish(new ToastMessage("Folder updated", "Game directory saved."));
    }

    [RelayCommand]
    private void DecreaseConcurrent() => Concurrent = Math.Max(LodestoneSettings.MinConcurrentDownloads, Concurrent - 1);

    [RelayCommand]
    private void IncreaseConcurrent() => Concurrent = Math.Min(LodestoneSettings.MaxConcurrentDownloads, Concurrent + 1);

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        Result<UpdateCheckResult> result = await _updater.CheckAsync(_settings.Current.UpdateChannel).ConfigureAwait(true);
        if (result.IsFailure)
        {
            _bus.Publish(new ToastMessage("Couldn't check for updates", result.Error.Message, ToastKind.Error));
            return;
        }

        if (result.Value.UpdateAvailable)
        {
            string? latest = result.Value.LatestVersion;
            string? name = ReleaseNames.For(latest);
            string detail = latest is null ? "A new version is ready to install."
                : name is null ? $"Version {latest} is ready to install."
                : $"Version {latest} “{name}” is ready to install.";
            _bus.Publish(new ToastMessage("Update available", detail));
        }
        else
        {
            _bus.Publish(new ToastMessage("You're up to date", $"Lodestone {result.Value.CurrentVersion} is the latest version."));
        }
    }

    [RelayCommand]
    private async Task UpdateLoaderAsync()
    {
        if (IsUpdatingLoader)
        {
            return;
        }

        if (!_locator.IsValid(_settings.Current.GameDirectory))
        {
            _bus.Publish(new ToastMessage("Set your Minecraft folder first", "Lodestone needs a game folder before managing loaders.", ToastKind.Warning));
            return;
        }

        Loader loader = _settings.Current.DefaultLoader;
        if (!_loaderInstaller.Supports(loader))
        {
            _bus.Publish(new ToastMessage($"{loader.ToDisplayName()} loader",
                $"{loader.ToDisplayName()} must be updated with its official installer — Lodestone still manages its mods.", ToastKind.Info));
            return;
        }

        GameVersion? version = ResolveLoaderVersion();
        if (version is null)
        {
            _bus.Publish(new ToastMessage("No Minecraft version found",
                "Run Minecraft once to install a version, then Lodestone can manage the loader for it.", ToastKind.Warning));
            return;
        }

        IsUpdatingLoader = true;
        try
        {
            Result<LoaderUpdate> result = await _loaderInstaller.UpdateAsync(loader, version).ConfigureAwait(true);
            if (result.IsFailure)
            {
                _bus.Publish(new ToastMessage("Loader update failed", result.Error.Message, ToastKind.Error));
                return;
            }

            LoaderUpdate update = result.Value;
            if (!update.Changed)
            {
                _bus.Publish(new ToastMessage($"{loader.ToDisplayName()} is up to date", $"Already on the latest build (v{update.Version}) for {version}."));
            }
            else if (update.PreviousVersion is null)
            {
                _bus.Publish(new ToastMessage($"{loader.ToDisplayName()} installed", $"v{update.Version} for {version} — pick it in your launcher."));
            }
            else
            {
                _bus.Publish(new ToastMessage($"{loader.ToDisplayName()} updated", $"v{update.PreviousVersion} → v{update.Version} for {version}."));
            }
        }
        finally
        {
            IsUpdatingLoader = false;
        }
    }

    // The loader is set up against the explicitly picked Minecraft version, falling back to the newest installed.
    private GameVersion? ResolveLoaderVersion()
    {
        GameVersion? picked = string.IsNullOrWhiteSpace(LoaderGameVersion)
            ? null
            : GameVersion.Create(LoaderGameVersion).Match<GameVersion?>(v => v, _ => null);
        return picked ?? ActiveProfile.Target(_settings.Current, _inventory);
    }

    private void Save()
    {
        if (!_ready)
        {
            return;
        }

        LodestoneSettings s = _settings.Current.Clone();
        s.GameDirectory = GameDir;
        s.DefaultLoader = Loader.ParseLoader();
        s.AutoUpdate = AutoUpdate;
        s.NotifyUpdates = Notify;
        s.UpdateChannel = EarlyAccess && _supporter.IsSupporter ? UpdateChannel.Beta : UpdateChannel.Stable;
        s.ConcurrentDownloads = Concurrent;
        s.CurseForgeFallback = CurseFallback;
        s.CloseToTray = CloseToTray;
        _ = _settings.SaveAsync(s);
    }
}
