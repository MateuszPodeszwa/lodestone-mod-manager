using System.Collections.ObjectModel;
using System.IO;
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

/// <summary>A row in the Home screen's "Recently added" list (name, type and a relative "when" label).</summary>
public sealed class RecentItemViewModel
{
    public RecentItemViewModel(InstalledContent item, string when)
    {
        Name = item.Name;
        AvatarLetter = char.ToUpperInvariant(item.Name.Length > 0 ? item.Name[0] : '?').ToString();
        TypeLabel = item.Type.ToDisplayName();
        When = when;
        Enabled = item.Enabled;
        EnabledLabel = item.Enabled ? "Enabled" : "Disabled";
    }

    public string Name { get; }
    public string AvatarLetter { get; }
    public string TypeLabel { get; }
    public string When { get; }
    public bool Enabled { get; }
    public string EnabledLabel { get; }
}

/// <summary>A row in the Home screen's "Available updates" list.</summary>
public sealed class UpdateRowViewModel
{
    public UpdateRowViewModel(InstalledContent item)
    {
        Name = item.Name;
        AvatarLetter = char.ToUpperInvariant(item.Name.Length > 0 ? item.Name[0] : '?').ToString();
        Label = $"v{item.Version}  →  latest";
    }

    public string Name { get; }
    public string AvatarLetter { get; }
    public string Label { get; }
}

/// <summary>The Home screen: stats, drag-and-drop install, recently added and available updates.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private static readonly string[] WhenLabels = ["Just now", "2h ago", "Yesterday", "3d ago"];

    private readonly IInstalledContentRepository _repository;
    private readonly InstallLocalFileUseCase _installLocal;
    private readonly UpdateAllUseCase _updateAll;
    private readonly RefreshUpdatesUseCase _refresh;
    private readonly ISettingsStore _settings;
    private readonly IMessageBus _bus;
    private readonly IUiDispatcher _ui;
    private readonly IDialogService _dialog;
    private readonly IGameLocator _locator;
    private readonly IGameInventory _inventory;
    private readonly OperationGate _gate;

    public HomeViewModel(
        IInstalledContentRepository repository,
        InstallLocalFileUseCase installLocal,
        UpdateAllUseCase updateAll,
        RefreshUpdatesUseCase refresh,
        ISettingsStore settings,
        IMessageBus bus,
        IUiDispatcher ui,
        IDialogService dialog,
        IGameLocator locator,
        IGameInventory inventory,
        OperationGate gate)
    {
        _repository = repository;
        _installLocal = installLocal;
        _updateAll = updateAll;
        _refresh = refresh;
        _settings = settings;
        _bus = bus;
        _ui = ui;
        _dialog = dialog;
        _locator = locator;
        _inventory = inventory;
        _gate = gate;
        bus.Subscribe<LibraryChanged>(m => _ui.Post(() => _ = LoadAsync()));
        // Re-evaluate the updates surface when "Notify me about updates" is toggled in Settings,
        // without a full library reload (the pending count is already in memory).
        _settings.Changed += (_, _) => _ui.Post(ApplyUpdatesSurface);
    }

    [ObservableProperty] private int _modCount;
    [ObservableProperty] private int _packCount;
    [ObservableProperty] private int _shaderCount;
    [ObservableProperty] private string _activeVersion = "All";
    [ObservableProperty] private bool _hasUpdates;
    [ObservableProperty] private string _updatesLabel = string.Empty;
    [ObservableProperty] private string _updatesEmptyTitle = "You're all caught up";
    [ObservableProperty] private string _updatesEmptySubtitle = "Every mod is on its latest version.";

    // The number of installed items with a pending update, regardless of the notify setting.
    private int _pendingUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckUpdatesLabel))]
    private bool _isCheckingUpdates;

    public string CheckUpdatesLabel => IsCheckingUpdates ? "Checking…" : "Check for updates";

    [ObservableProperty] private bool _dragActive;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private string _installName = string.Empty;
    [ObservableProperty] private string _installTypeLabel = string.Empty;

    public ObservableCollection<RecentItemViewModel> RecentItems { get; } = [];

    public ObservableCollection<UpdateRowViewModel> UpdateItems { get; } = [];

    public async Task LoadAsync()
    {
        IReadOnlyList<InstalledContent> all = await _repository.GetAllAsync().ConfigureAwait(true);

        ModCount = all.Count(i => i.Type == ContentType.Mod);
        PackCount = all.Count(i => i.Type == ContentType.ResourcePack);
        ShaderCount = all.Count(i => i.Type == ContentType.Shader);

        // Show the real selection: the concrete version, "All" when any version is installed, or "—" when none.
        ActiveVersion = ActiveProfile.Selected(_settings.Current)?.Value
            ?? (_inventory.InstalledVersions().Count > 0 ? "All" : "—");

        RecentItems.Clear();
        int index = 0;
        foreach (InstalledContent item in all.Take(4))
        {
            RecentItems.Add(new RecentItemViewModel(item, WhenLabels[Math.Min(index, WhenLabels.Length - 1)]));
            index++;
        }

        UpdateItems.Clear();
        foreach (InstalledContent item in all.Where(i => i.UpdateAvailable))
        {
            UpdateItems.Add(new UpdateRowViewModel(item));
        }

        _pendingUpdates = UpdateItems.Count;
        ApplyUpdatesSurface();
    }

    // "Notify me about updates" gates whether pending updates are surfaced on Home. When it's off we
    // don't badge them — but we stay honest about it rather than claiming everything is up to date.
    private void ApplyUpdatesSurface()
    {
        int pending = _pendingUpdates;
        bool notify = _settings.Current.NotifyUpdates;

        HasUpdates = pending > 0 && notify;
        UpdatesLabel = $"{pending} update{(pending == 1 ? string.Empty : "s")} available";

        bool muted = pending > 0 && !notify;
        UpdatesEmptyTitle = muted ? "Update alerts are off" : "You're all caught up";
        UpdatesEmptySubtitle = muted
            ? $"{pending} update{(pending == 1 ? " is" : "s are")} waiting — turn alerts on in Settings."
            : "Every mod is on its latest version.";
    }

    /// <summary>True once a valid Minecraft folder is configured; gates the install actions.</summary>
    public bool IsGameReady => _locator.IsValid(_settings.Current.GameDirectory);

    /// <summary>Installs a batch of dropped/picked files into the active version.</summary>
    public async Task HandleFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        if (!IsGameReady)
        {
            _bus.Publish(new ToastMessage("Set your Minecraft folder first", "Lodestone needs a game folder before installing. Use the banner's “Locate Minecraft”.", ToastKind.Warning));
            return;
        }

        bool ran = await _gate.RunAsync(async () =>
        {
            GameVersion? target = ResolveTargetVersion();

            foreach (string path in paths)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                IsInstalling = true;
                InstallName = name;
                InstallTypeLabel = "Reading…";

                Result<InstalledContent> result = await _installLocal.ExecuteAsync(path, target).ConfigureAwait(true);

                if (result.IsSuccess)
                {
                    _bus.Publish(new ToastMessage("Installed", $"{result.Value.Name} · {result.Value.Type.ToDisplayName()}"));
                }
                else
                {
                    _bus.Publish(new ToastMessage("Couldn't install", result.Error.Message, ToastKind.Error));
                }
            }

            IsInstalling = false;
            _bus.Publish(new LibraryChanged());
        }).ConfigureAwait(true);

        if (!ran)
        {
            _bus.Publish(new ToastMessage("Please wait", "Another install is still running — try again in a moment.", ToastKind.Info));
        }
    }

    [RelayCommand]
    private void BrowseFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = "Mods, packs & shaders|*.jar;*.zip;*.litemod;*.mcpack|All files|*.*",
            Title = "Choose files to install",
        };

        if (dialog.ShowDialog() == true)
        {
            _ = HandleFilesAsync(dialog.FileNames);
        }
    }

    [RelayCommand]
    private async Task UpdateAllAsync()
    {
        Result<int> result = await _updateAll.ExecuteAsync(ResolveTargetVersion()).ConfigureAwait(true);
        if (result.IsSuccess && result.Value > 0)
        {
            _bus.Publish(new ToastMessage("Updated", $"{result.Value} mod{(result.Value == 1 ? string.Empty : "s")} updated to the latest version"));
            _bus.Publish(new LibraryChanged());
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdates)
        {
            return;
        }

        IsCheckingUpdates = true;
        try
        {
            Result<UpdateSummary> result = await _refresh.ExecuteAsync(ActiveVersionOrNull()).ConfigureAwait(true);
            if (result.IsFailure)
            {
                _bus.Publish(new ToastMessage("Couldn't check for updates", result.Error.Message, ToastKind.Error));
                return;
            }

            UpdateSummary summary = result.Value;
            if (summary.Updated > 0)
            {
                _bus.Publish(new ToastMessage("Updated", $"{summary.Updated} mod{(summary.Updated == 1 ? string.Empty : "s")} updated to the latest version"));
            }
            else if (summary.UpdatesAvailable > 0)
            {
                _bus.Publish(new ToastMessage("Updates available", $"{summary.UpdatesAvailable} mod{(summary.UpdatesAvailable == 1 ? string.Empty : "s")} can be updated"));
            }
            else
            {
                _bus.Publish(new ToastMessage("You're up to date", "Every mod is on its latest compatible version."));
            }

            _bus.Publish(new LibraryChanged());
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    /// <summary>A concrete install target (selected version, else newest installed); null when nothing is installed.</summary>
    private GameVersion? ResolveTargetVersion() => ActiveProfile.Target(_settings.Current, _inventory);

    /// <summary>Null on the "All versions" view, so each mod is checked against its own latest version.</summary>
    private GameVersion? ActiveVersionOrNull() => ActiveProfile.Selected(_settings.Current);
}
