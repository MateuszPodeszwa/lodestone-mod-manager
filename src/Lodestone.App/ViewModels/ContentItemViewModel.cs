using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lodestone.App.Services;
using Lodestone.Domain;
using Lodestone.Domain.Compatibility;

namespace Lodestone.App.ViewModels;

/// <summary>A single row in "My Content": the item plus its compatibility issues, each shown as a labelled badge.</summary>
public sealed partial class ContentItemViewModel : ObservableObject
{
    private readonly InstalledContent _model;
    private readonly Func<string, Task> _onToggle;
    private readonly Func<string, Task> _onUninstall;
    private readonly Func<string, string, Task> _onAssign;

    public ContentItemViewModel(
        InstalledContent model,
        CompatibilityReport? report,
        bool showVersions,
        IReadOnlyList<ProfileOption> assignTargets,
        Func<string, Task> onToggle,
        Func<string, Task> onUninstall,
        Func<string, string, Task> onAssign)
    {
        _model = model;
        _onToggle = onToggle;
        _onUninstall = onUninstall;
        _onAssign = onAssign;
        AssignTargets = assignTargets;
        Issues = report is { HasIssues: true }
            ? report.Issues.OrderByDescending(i => i.Severity).ToList()
            : [];
        ShowVersions = showVersions;
        _enabled = model.Enabled;
    }

    /// <summary>The profiles this item can be assigned to (a leading "Assign to…" prompt, then each profile).</summary>
    public IReadOnlyList<ProfileOption> AssignTargets { get; }

    /// <summary>A mod adopted without a known version — eligible for manual sorting in My Content.</summary>
    public bool IsUnsorted => _model.Type.UsesLoader() && _model.GameVersions.Count == 0;

    /// <summary>Show the inline "assign to a profile" picker only when it's unsorted and there's a target.</summary>
    public bool CanAssign => IsUnsorted && AssignTargets.Count > 1;

    [ObservableProperty]
    private string _assignTargetKey = "";

    partial void OnAssignTargetKeyChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _ = _onAssign(Id, value); // user picked a profile from the prompt — sort it there
        }
    }

    public string Id => _model.Id;

    public string Name => _model.Name;

    public string AvatarLetter => char.ToUpperInvariant(Name.Length > 0 ? Name[0] : '?').ToString();

    public string? IconUrl => _model.IconUrl;

    public bool HasIcon => !string.IsNullOrWhiteSpace(_model.IconUrl);

    public bool HasLoader => _model.Loader != Loader.None;

    public string LoaderLabel => _model.Loader.ToDisplayName();

    public string MetaLabel =>
        $"v{_model.Version}" + (HasLoader ? $"   ·   {LoaderLabel}" : string.Empty) + $"   ·   {Format.Size(_model.SizeMb)}";

    public bool UpdateAvailable => _model.UpdateAvailable;

    public bool ShowVersions { get; }

    public string VersionsLabel => "Supports " + string.Join(" · ", _model.GameVersions.Select(v => v.Value));

    [ObservableProperty]
    private bool _enabled;

    // ---- compatibility verdict ----

    /// <summary>
    /// Every problem found for this item, worst severity first. Each is rendered as its own labelled
    /// badge next to the name (<see cref="CompatibilityIssue.ShortLabel"/>), with the full
    /// <see cref="CompatibilityIssue.Message"/> in the badge's tooltip.
    /// </summary>
    public IReadOnlyList<CompatibilityIssue> Issues { get; }

    public bool HasIssues => Issues.Count > 0;

    [RelayCommand]
    private Task ToggleAsync() => _onToggle(Id);

    [RelayCommand]
    private Task UninstallAsync() => _onUninstall(Id);
}
