using Lodestone.Application.Supporter;

namespace Lodestone.Infrastructure.Persistence;

/// <summary>File-backed store for the redeemed supporter entitlement.</summary>
public sealed class JsonEntitlementStore : IEntitlementStore
{
    private readonly string _path;

    public JsonEntitlementStore(string? path = null) => _path = path ?? LodestonePaths.EntitlementsFile;

    public SupporterEntitlement? Current { get; private set; }

    public event EventHandler? Changed;

    public async Task<SupporterEntitlement?> LoadAsync(CancellationToken ct = default)
    {
        Current = await JsonStore.ReadAsync<SupporterEntitlement>(_path, ct).ConfigureAwait(false);
        return Current;
    }

    public async Task SaveAsync(SupporterEntitlement entitlement, CancellationToken ct = default)
    {
        await JsonStore.WriteAsync(_path, entitlement, ct).ConfigureAwait(false);
        Current = entitlement;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
            // Non-fatal: clearing in-memory state below is what matters.
        }

        Current = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
