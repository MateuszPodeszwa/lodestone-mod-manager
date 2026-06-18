using Lodestone.Application.Abstractions;
using Lodestone.Domain.Common;

namespace Lodestone.Application.UseCases;

/// <summary>Enables/disables an installed item, flipping its on-disk state and persisting the change.</summary>
public sealed class ToggleContentUseCase
{
    private readonly IInstalledContentRepository _repository;
    private readonly IContentInstaller _installer;

    public ToggleContentUseCase(IInstalledContentRepository repository, IContentInstaller installer)
    {
        _repository = repository;
        _installer = installer;
    }

    public async Task<Result> ExecuteAsync(string id, CancellationToken ct = default)
    {
        Domain.InstalledContent? item = await _repository.FindAsync(id, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure("toggle.not_found", "That item is no longer in your library.");
        }

        bool target = !item.Enabled;

        if (!string.IsNullOrWhiteSpace(item.FileName))
        {
            Result<string> changed = await _installer
                .SetEnabledAsync(item.Type, item.FileName!, target, ct)
                .ConfigureAwait(false);
            if (changed.IsFailure)
            {
                return Result.Failure(changed.Error);
            }

            item.FileName = changed.Value; // the on-disk name may have gained/lost the .disabled suffix
        }

        item.Enabled = target;
        // Record the explicit intent: turning a mod off here means "keep it off, even inside its own
        // profile", so a later profile switch-and-return won't silently re-enable it. Enabling clears it.
        item.UserDisabled = !target;
        await _repository.UpsertAsync(item, ct).ConfigureAwait(false);
        return Result.Success();
    }
}
