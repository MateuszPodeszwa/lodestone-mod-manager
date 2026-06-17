using System.IO;
using System.Windows;
using System.Windows.Threading;
using Lodestone.App.Services;
using Lodestone.App.ViewModels;
using Lodestone.Application.Abstractions;
using Lodestone.Application.Settings;
using Lodestone.Application.Supporter;
using Lodestone.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Lodestone.App;

/// <summary>
/// WPF composition root. Builds the service provider (core + infrastructure + UI), loads persisted
/// state before any view model is constructed, then shows the shell. There is no background host or
/// timer — everything is request/refresh driven.
/// </summary>
public partial class App : System.Windows.Application
{
    private static readonly string SmokeLogPath = Path.Combine(Path.GetTempPath(), "lodestone-smoke.log");

    private ServiceProvider? _provider;
    private bool _smoke;
    private bool _smokeError;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _smoke = Environment.GetEnvironmentVariable("LODESTONE_SMOKE") == "1";
        DispatcherUnhandledException += OnUnhandledException;

        var services = new ServiceCollection();
        services.AddLodestone();

        // UI-layer services
        services.AddSingleton<IUiDispatcher, UiDispatcher>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IAppUpdater, VelopackAppUpdater>();
        services.AddSingleton<OperationGate>();

        // View models
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<BrowseViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<DonateViewModel>();
        services.AddSingleton<OnboardingViewModel>();
        services.AddSingleton<ToastsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _provider = services.BuildServiceProvider();

        // Load persisted state up front so view models read real values in their constructors.
        ISettingsStore settingsStore = _provider.GetRequiredService<ISettingsStore>();
        await settingsStore.LoadAsync();
        await _provider.GetRequiredService<IEntitlementStore>().LoadAsync();

        // Apply the saved accent (a supporter perk) before any view loads, so it renders themed from the
        // first frame. AccentApplier ignores a custom accent when the user isn't a supporter.
        AccentApplier.Apply(settingsStore.Current.AccentColor, _provider.GetRequiredService<SupporterService>().IsSupporter);

        var main = _provider.GetRequiredService<MainViewModel>();
        var window = _provider.GetRequiredService<MainWindow>();
        window.DataContext = main;
        MainWindow = window;
        window.Show();

        if (_smoke)
        {
            TryDelete(SmokeLogPath);
            StartSmokeWatchdog(main);
        }

        try
        {
            await main.InitializeAsync();
        }
        catch (Exception ex) when (_smoke)
        {
            LogSmoke(ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _provider?.Dispose();
        base.OnExit(e);
    }

    // Renders every screen once (catching runtime resource/binding errors) then exits. Runs
    // independently of InitializeAsync so a slow/failed network load can't stall the check.
    private void StartSmokeWatchdog(MainViewModel main)
    {
        var steps = new Queue<Action>(
        [
            () => main.GoLibraryCommand.Execute(null),
            () => main.GoBrowseCommand.Execute(null),
            () => main.OpenSampleDetailForSmoke(),
            () => main.GoSettingsCommand.Execute(null),
            () => main.GoDonateCommand.Execute(null),
            () => main.GoHomeCommand.Execute(null),
        ]);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (steps.Count > 0)
                {
                    steps.Dequeue()();
                    return;
                }
            }
            catch (Exception ex)
            {
                LogSmoke(ex);
            }

            timer.Stop();
            Shutdown(_smokeError ? 1 : 0);
        };
        timer.Start();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_smoke)
        {
            LogSmoke(e.Exception);
            e.Handled = true;
            return;
        }

        Lodestone.Infrastructure.Persistence.LodestoneLog.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were logged to %AppData%\\Lodestone\\logs.",
            "Lodestone",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void LogSmoke(Exception ex)
    {
        _smokeError = true;
        try
        {
            File.AppendAllText(SmokeLogPath, ex + Environment.NewLine + new string('-', 60) + Environment.NewLine);
        }
        catch (IOException)
        {
            // ignore
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // ignore
        }
    }
}
