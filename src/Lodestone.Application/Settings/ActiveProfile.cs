using Lodestone.Application.Abstractions;
using Lodestone.Domain;

namespace Lodestone.Application.Settings;

/// <summary>
/// The single source of truth for resolving the "active version" from settings. Replaces the
/// hardcoded <c>1.21.4</c> fallbacks that used to be scattered across the view-models: a concrete
/// install target now comes from what the user selected, or — failing that — the newest version they
/// actually have installed, never a fabricated default.
/// </summary>
public static class ActiveProfile
{
    /// <summary>True for the "All versions" view (or an unset selection).</summary>
    public static bool IsAllVersions(string? selected) => selected is "all" or "" or null;

    /// <summary>The version selected in the UI, or null on the "All versions" view / an unparseable value.</summary>
    public static GameVersion? Selected(LodestoneSettings settings)
        => IsAllVersions(settings.SelectedVersion)
            ? null
            : GameVersion.Create(settings.SelectedVersion).Match<GameVersion?>(v => v, _ => null);

    /// <summary>
    /// A concrete version to install against: the explicit selection, else the newest installed
    /// version, else null when nothing is installed (callers should gate the action in that case).
    /// </summary>
    public static GameVersion? Target(LodestoneSettings settings, IGameInventory inventory)
        => Selected(settings) ?? inventory.InstalledVersions().FirstOrDefault();

    /// <summary>
    /// Whether the active loader is ready for the content at hand. Loader-independent content
    /// (resource packs and shaders, <paramref name="usesLoader"/> = false) is always ready, as is any
    /// content when there's no concrete target version yet (that case is gated separately at install
    /// time). Otherwise a loader must be selected (not <see cref="Loader.None"/>) and actually installed
    /// for the target version. Factored out of the view-models so the readiness rule lives in one place.
    /// </summary>
    /// <param name="settings">The current settings (supplies the default loader and selected version).</param>
    /// <param name="inventory">The game inventory, queried for whether the loader is installed.</param>
    /// <param name="usesLoader">True for mods; false for loader-agnostic content (packs/shaders).</param>
    public static bool IsLoaderReady(LodestoneSettings settings, IGameInventory inventory, bool usesLoader)
    {
        if (!usesLoader)
        {
            return true; // loader-independent content
        }

        if (Target(settings, inventory) is not { } target)
        {
            return true; // "no Minecraft version yet" is gated separately on install
        }

        return settings.DefaultLoader != Loader.None && inventory.IsLoaderInstalled(settings.DefaultLoader, target);
    }

    /// <summary>
    /// The user-facing reason the loader gate is up, matching the Browse page's wording so the app reads
    /// consistently: "Install the &lt;loader&gt; loader for &lt;version&gt; in Settings before adding mods."
    /// (or "Install a mod loader…" when no loader is selected; the version is dropped when there's no
    /// concrete target yet).
    /// </summary>
    /// <param name="settings">The current settings (supplies the default loader and selected version).</param>
    /// <param name="inventory">The game inventory, used to resolve the target version.</param>
    public static string LoaderGateMessage(LodestoneSettings settings, IGameInventory inventory)
    {
        Loader loader = settings.DefaultLoader;
        string name = loader == Loader.None ? "a mod loader" : $"the {loader.ToDisplayName()} loader";
        GameVersion? target = Target(settings, inventory);
        return target is null
            ? $"Install {name} in Settings before adding mods."
            : $"Install {name} for {target} in Settings before adding mods.";
    }
}
