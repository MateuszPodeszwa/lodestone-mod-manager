using Lodestone.Application.Settings;

namespace Lodestone.Infrastructure.Persistence;

/// <summary>File-backed <see cref="ISettingsStore"/> with atomic writes and corrupt-file self-heal.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private LodestoneSettings _current = new();

    public JsonSettingsStore(string? path = null) => _path = path ?? LodestonePaths.SettingsFile;

    public LodestoneSettings Current => _current;

    public event EventHandler<LodestoneSettings>? Changed;

    public async Task<LodestoneSettings> LoadAsync(CancellationToken ct = default)
    {
        LodestoneSettings loaded = await JsonStore.ReadAsync<LodestoneSettings>(_path, ct).ConfigureAwait(false)
                                   ?? new LodestoneSettings();
        loaded.Normalize();
        _current = loaded;
        return _current;
    }

    public async Task SaveAsync(LodestoneSettings settings, CancellationToken ct = default)
    {
        settings.Normalize();
        await JsonStore.WriteAsync(_path, settings, ct).ConfigureAwait(false);
        _current = settings;
        Changed?.Invoke(this, settings);
    }
}
