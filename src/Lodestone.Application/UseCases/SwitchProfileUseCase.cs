using Lodestone.Application.Abstractions;
using Lodestone.Domain;
using Lodestone.Domain.Common;

namespace Lodestone.Application.UseCases;

/// <summary>The outcome of activating a profile: how many mods the swap enabled (they belong to the
/// profile) and how many it disabled (they belong to another loader or game version).</summary>
public sealed record ProfileSwitch(int Enabled, int Disabled);

/// <summary>
/// Activates a (game version + loader) profile by swapping the shared <c>mods/</c> folder so only that
/// profile's mods are live: every mod that supports the target version on the target loader is enabled,
/// and every other mod is disabled. Enable/disable just toggles the file's <c>.disabled</c> suffix, so
/// the swap is fully reversible and nothing is ever deleted — switching back re-enables the previous
/// set in seconds. Resource packs and shaders are loader-agnostic and chosen in-game, so they're left
/// exactly as the user set them.
/// </summary>
public sealed class SwitchProfileUseCase
{
    private readonly IInstalledContentRepository _repository;
    private readonly IContentInstaller _installer;
    private readonly IGameInventory _inventory;
    private readonly ILauncherVisibility _launcher;

    public SwitchProfileUseCase(
        IInstalledContentRepository repository,
        IContentInstaller installer,
        IGameInventory inventory,
        ILauncherVisibility launcher)
    {
        _repository = repository;
        _installer = installer;
        _inventory = inventory;
        _launcher = launcher;
    }

    public async Task<Result<ProfileSwitch>> ExecuteAsync(GameVersion version, Loader loader, CancellationToken ct = default)
    {
        IReadOnlyList<InstalledContent> all = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        int enabled = 0;
        int disabled = 0;

        foreach (InstalledContent item in all)
        {
            if (!item.Type.UsesLoader())
            {
                continue; // resource packs / shaders aren't loader-bound — leave them as the user set them
            }

            bool belongs = item.Loader == loader && item.SupportsVersion(version);

            // A mod the user deliberately turned off stays off, even inside the profile it belongs to —
            // otherwise switching away and back would silently re-enable it (issue #40). Only mods set
            // aside for a different loader/version are the switch's to flip.
            bool desiredEnabled = belongs && !item.UserDisabled;
            if (item.Enabled == desiredEnabled)
            {
                continue; // already in the desired state — don't churn the disk or the repo
            }

            if (!string.IsNullOrWhiteSpace(item.FileName))
            {
                Result<string> changed = await _installer
                    .SetEnabledAsync(item.Type, item.FileName!, desiredEnabled, ct)
                    .ConfigureAwait(false);
                if (changed.IsFailure)
                {
                    return Result.Failure<ProfileSwitch>(changed.Error);
                }

                item.FileName = changed.Value; // the on-disk name gains/loses the .disabled suffix
            }

            item.Enabled = desiredEnabled;
            await _repository.UpsertAsync(item, ct).ConfigureAwait(false);

            if (desiredEnabled)
            {
                enabled++;
            }
            else
            {
                disabled++;
            }
        }

        // Make the launcher show only this profile (plus vanilla), stashing the other modded profiles.
        // Best-effort: the mods swap is what makes the game load correctly, so a launcher hiccup here
        // must not fail the switch.
        IReadOnlyList<LoaderProfile> modded = _inventory.InstalledProfiles().Where(p => !p.IsVanilla).ToList();
        LoaderProfile? active = modded.FirstOrDefault(p => p.GameVersion.Equals(version) && p.Loader == loader);
        if (active is not null)
        {
            _launcher.Apply(active, modded);
        }

        return new ProfileSwitch(enabled, disabled);
    }
}
