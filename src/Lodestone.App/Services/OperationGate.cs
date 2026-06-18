using CommunityToolkit.Mvvm.ComponentModel;
using Lodestone.Application.Concurrency;

namespace Lodestone.App.Services;

/// <summary>
/// App-wide gate for disk-mutating operations: it surfaces a single busy/label state for the global
/// activity bar and for disabling controls that must not run mid-operation, and classifies work via an
/// <see cref="OperationCoordinator"/> as either concurrent <i>installs</i> (content downloads — their real
/// parallelism is bounded by the "concurrent downloads" setting in the HTTP downloader) or <i>exclusive</i>
/// operations (loader install/update, profile switch, reset) that run strictly alone. Observable state is
/// always raised on the UI thread.
/// </summary>
public sealed partial class OperationGate : ObservableObject
{
    private readonly OperationCoordinator _coordinator = new();
    private readonly IUiDispatcher _ui;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    /// <summary>A short description of the most recent in-flight operation, shown by the activity bar.</summary>
    [ObservableProperty]
    private string _statusLabel = "Working…";

    public OperationGate(IUiDispatcher ui)
    {
        _ui = ui;
        // Reflect the coordinator's aggregate busy state on the observable, marshalled to the UI thread (an
        // install can begin/end on a worker thread). Reading IsBusy in the handler keeps it correct even if
        // notifications race.
        _coordinator.StateChanged += () => _ui.Post(() => IsBusy = _coordinator.IsBusy);
    }

    /// <summary>The inverse of <see cref="IsBusy"/>, for binding a control's <c>IsEnabled</c>.</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>
    /// Runs a content install/update. Any number run concurrently; the real download parallelism is bounded
    /// by the "concurrent downloads" setting. Waits out any in-flight exclusive operation first.
    /// </summary>
    public async Task RunInstallAsync(string label, Func<Task> operation, CancellationToken ct = default)
    {
        await _coordinator.BeginInstallAsync(ct).ConfigureAwait(true);
        StatusLabel = label;
        try
        {
            await operation().ConfigureAwait(true);
        }
        finally
        {
            _coordinator.EndInstall();
        }
    }

    /// <summary>
    /// Runs an exclusive operation (loader install/update, profile switch, reset) strictly alone. If
    /// anything else is already running this does nothing and returns <c>false</c>, so the caller can show a
    /// "please wait" message instead of starting a racing operation.
    /// </summary>
    public async Task<bool> RunExclusiveAsync(string label, Func<Task> operation)
    {
        if (!_coordinator.TryBeginExclusive())
        {
            return false;
        }

        StatusLabel = label;
        try
        {
            await operation().ConfigureAwait(true);
            return true;
        }
        finally
        {
            _coordinator.EndExclusive();
        }
    }
}
