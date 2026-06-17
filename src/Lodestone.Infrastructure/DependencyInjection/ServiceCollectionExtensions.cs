using Lodestone.Application.Abstractions;
using Lodestone.Application.Catalog;
using Lodestone.Application.Compatibility;
using Lodestone.Application.Compatibility.Rules;
using Lodestone.Application.Messaging;
using Lodestone.Application.Settings;
using Lodestone.Application.Supporter;
using Lodestone.Application.UseCases;
using Lodestone.Infrastructure.Archives;
using Lodestone.Infrastructure.FileSystem;
using Lodestone.Infrastructure.Loaders;
using Lodestone.Infrastructure.Messaging;
using Lodestone.Infrastructure.Net;
using Lodestone.Infrastructure.Persistence;
using Lodestone.Infrastructure.Sources;
using Lodestone.Infrastructure.Sources.CurseForge;
using Lodestone.Infrastructure.Sources.Modrinth;
using Lodestone.Infrastructure.Supporter;
using Microsoft.Extensions.DependencyInjection;

namespace Lodestone.Infrastructure.DependencyInjection;

/// <summary>Options for wiring Lodestone's services (overridable in tests and at startup).</summary>
public sealed class LodestoneOptions
{
    public string UserAgent { get; set; } = "MateuszPodeszwa/LodestoneModManager/0.1.0 (podinatubie@gmail.com)";
    public string SupporterPublicKey { get; set; } = SupporterKeys.DefaultPublicKey;
    public string? CurseForgeApiKey { get; set; }
    public TimeSpan SearchCacheTtl { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Single composition seam: registers the application services and infrastructure adapters so the UI
/// (or CLI) just calls <c>AddLodestone()</c>. Dependencies are wired to interfaces throughout (DIP).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLodestone(this IServiceCollection services, Action<LodestoneOptions>? configure = null)
    {
        var options = new LodestoneOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // ---- Cross-cutting ----
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IMessageBus, InMemoryMessageBus>();

        // ---- Persistence / file system ----
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IInstalledContentRepository, JsonInstalledContentRepository>();
        services.AddSingleton<IEntitlementStore, JsonEntitlementStore>();
        services.AddSingleton<IGameLocator, MinecraftGameLocator>();
        services.AddSingleton<IGameInventory, MinecraftGameInventory>();
        services.AddSingleton<IContentInstaller, FileSystemContentInstaller>();
        services.AddSingleton<ILauncherVisibility, LauncherProfileStore>();
        services.AddSingleton<ILoaderLedger, JsonLoaderLedger>();
        services.AddSingleton<IArchiveMetadataReader, ArchiveMetadataReader>();

        // ---- HTTP: Modrinth (cached + retrying) and the downloader ----
        services.AddHttpClient("modrinth", client =>
            {
                client.BaseAddress = new Uri("https://api.modrinth.com/");
                // Modrinth asks for a descriptive UA; TryAddWithoutValidation accepts the
                // "user/repo/version (contact)" form that the strict product-token parser rejects.
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler(() => new RetryDelegatingHandler());

        services.AddHttpClient("downloads", client =>
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
                client.Timeout = Timeout.InfiniteTimeSpan; // large files; bounded by CancellationToken
            })
            .AddHttpMessageHandler(() => new RetryDelegatingHandler());

        services.AddSingleton<IModSource>(sp =>
        {
            HttpClient http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("modrinth");
            return new CachingModSource(new ModrinthModSource(http), sp.GetRequiredService<IClock>(), options.SearchCacheTtl);
        });
        services.AddSingleton<IModSource>(_ => new CurseForgeModSource(options.CurseForgeApiKey));
        services.AddSingleton<IModSourceRegistry, ModSourceRegistry>();

        services.AddSingleton<IDownloader>(sp =>
            new HttpDownloader(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("downloads"),
                sp.GetRequiredService<ISettingsStore>()));

        services.AddSingleton<ILoaderInstaller>(sp =>
            new MetaLoaderInstaller(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("downloads"),
                sp.GetRequiredService<ISettingsStore>(),
                sp.GetRequiredService<IGameLocator>(),
                sp.GetRequiredService<IGameInventory>(),
                sp.GetRequiredService<ILoaderLedger>()));

        services.AddSingleton<IExternalLoaderInstaller>(sp =>
            new ForgeInstallerLauncher(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("downloads"),
                sp.GetRequiredService<ILoaderLedger>()));

        // ---- Supporter (offline signed codes) ----
        services.AddSingleton<ISupporterCodeVerifier>(_ => new SignedSupporterCodeVerifier(options.SupporterPublicKey));
        services.AddSingleton<SupporterService>();

        // ---- Compatibility engine (Chain of Responsibility) ----
        services.AddSingleton<ICompatibilityRule, MissingRequiredDependencyRule>();
        services.AddSingleton<ICompatibilityRule, DisabledDependencyRule>();
        services.AddSingleton<ICompatibilityRule, DependencyVersionRule>();
        services.AddSingleton<ICompatibilityRule, IncompatibleModRule>();
        services.AddSingleton<ICompatibilityRule, GameVersionMismatchRule>();
        services.AddSingleton<ICompatibilityRule, GameVersionNotInstalledRule>();
        services.AddSingleton<ICompatibilityRule, LoaderMismatchRule>();
        services.AddSingleton<ICompatibilityRule, DuplicateRule>();
        services.AddSingleton<ICompatibilityRule, OrphanLibraryRule>();
        services.AddSingleton<ICompatibilityRule, UnsortedContentRule>();
        services.AddSingleton<ICompatibilityService, CompatibilityService>();

        // ---- Catalog + use-cases ----
        services.AddSingleton<IVersionResolver, VersionResolver>();
        services.AddTransient<IUpdateContentUseCase, UpdateContentUseCase>();
        services.AddTransient<InstallLocalFileUseCase>();
        services.AddTransient<InstallFromCatalogUseCase>();
        services.AddTransient<ToggleContentUseCase>();
        services.AddTransient<UninstallContentUseCase>();
        services.AddTransient<RefreshUpdatesUseCase>();
        services.AddTransient<UpdateAllUseCase>();
        services.AddTransient<ReconcileLibraryUseCase>();
        services.AddTransient<SwitchProfileUseCase>();
        services.AddTransient<ResetGameUseCase>();

        return services;
    }
}
