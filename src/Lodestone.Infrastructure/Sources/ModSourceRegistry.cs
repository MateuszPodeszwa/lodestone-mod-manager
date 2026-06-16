using Lodestone.Application.Abstractions;
using Lodestone.Application.Settings;

namespace Lodestone.Infrastructure.Sources;

/// <summary>
/// Selects active mod sources honouring the "use CurseForge as fallback" setting (Factory + Strategy).
/// Modrinth is always primary; CurseForge is appended only when the setting is on and it's configured.
/// </summary>
public sealed class ModSourceRegistry : IModSourceRegistry
{
    private readonly List<IModSource> _sources;
    private readonly ISettingsStore _settings;

    public ModSourceRegistry(IEnumerable<IModSource> sources, ISettingsStore settings)
    {
        _sources = sources.ToList();
        _settings = settings;
    }

    public IModSource Primary =>
        Find("modrinth") ?? _sources.FirstOrDefault(s => s.IsConfigured) ?? _sources[0];

    public IModSource? Find(string name)
        => _sources.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<IModSource> GetActiveSources()
    {
        var active = new List<IModSource>();

        if (Find("modrinth") is { IsConfigured: true } modrinth)
        {
            active.Add(modrinth);
        }

        if (_settings.Current.CurseForgeFallback &&
            Find("curseforge") is { IsConfigured: true } curseforge)
        {
            active.Add(curseforge);
        }

        if (active.Count == 0)
        {
            active.AddRange(_sources.Where(s => s.IsConfigured));
        }

        return active;
    }
}
