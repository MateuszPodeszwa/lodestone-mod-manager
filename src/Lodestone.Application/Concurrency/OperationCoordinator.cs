namespace Lodestone.Application.Concurrency;

/// <summary>
/// Coordinates the app's disk-mutating work into two classes so that downloads can run concurrently
/// without racing the operations that can't:
/// <list type="bullet">
/// <item><b>Shared</b> — content installs and updates. Any number may run at once; the real download
/// parallelism is bounded separately by the "concurrent downloads" setting (the HTTP downloader's
/// semaphore). Installs of distinct items write distinct files and the library repository serializes its
/// own writes, so concurrent installs don't corrupt anything.</item>
/// <item><b>Exclusive</b> — loader install/update, profile switch and reset. These rewrite shared
/// structures (<c>versions/</c>, <c>launcher_profiles.json</c>, or enable/disable files in <c>mods/</c>)
/// that would race with installs or with each other, so they run strictly alone.</item>
/// </list>
/// An exclusive op is refused (<see cref="TryBeginExclusive"/> returns <c>false</c>) while anything is
/// running, so the caller can surface a "please wait" message instead of starting a racing operation; a
/// shared install waits out an in-flight exclusive op and then proceeds. This is the mechanism behind the
/// UI's <c>OperationGate</c>; it carries no UI concern of its own so it can be unit-tested in isolation.
/// </summary>
public sealed class OperationCoordinator
{
    private readonly object _sync = new();
    private int _installs;
    private bool _exclusive;

    // Completes when the current exclusive op finishes; waiting installs park on it. Recreated each time an
    // exclusive op begins so a fresh wait can form. Continuations run asynchronously so a waiter never
    // resumes while we hold the release path, and never inline under <see cref="_sync"/>.
    private TaskCompletionSource _exclusiveReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Raised whenever an operation begins or ends (so the busy state may have changed). Handlers
    /// must not assume a particular thread; read <see cref="IsBusy"/> for the current aggregate state.</summary>
    public event Action? StateChanged;

    /// <summary>True while any operation — a shared install or an exclusive op — is in flight.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_sync)
            {
                return _exclusive || _installs > 0;
            }
        }
    }

    /// <summary>
    /// Registers the start of a shared (install) operation, first waiting out any exclusive op that is in
    /// flight. Pair every successful return with exactly one <see cref="EndInstall"/> (use a try/finally).
    /// If <paramref name="ct"/> is cancelled while waiting, throws and registers nothing.
    /// </summary>
    public async Task BeginInstallAsync(CancellationToken ct = default)
    {
        while (true)
        {
            Task wait;
            lock (_sync)
            {
                if (!_exclusive)
                {
                    _installs++;
                    break;
                }

                wait = _exclusiveReleased.Task;
            }

            await wait.WaitAsync(ct).ConfigureAwait(false);
        }

        StateChanged?.Invoke();
    }

    /// <summary>Marks a shared (install) operation complete. Safe to call once per successful
    /// <see cref="BeginInstallAsync"/>.</summary>
    public void EndInstall()
    {
        lock (_sync)
        {
            if (_installs > 0)
            {
                _installs--;
            }
        }

        StateChanged?.Invoke();
    }

    /// <summary>
    /// Tries to begin an exclusive operation. Returns <c>false</c> without acquiring when anything is
    /// already running (another exclusive op, or one or more installs), so the caller can ask the user to
    /// wait rather than start a racing operation. On <c>true</c>, call <see cref="EndExclusive"/> exactly
    /// once when finished (use a try/finally).
    /// </summary>
    public bool TryBeginExclusive()
    {
        bool acquired;
        lock (_sync)
        {
            acquired = !_exclusive && _installs == 0;
            if (acquired)
            {
                _exclusive = true;
            }
        }

        if (acquired)
        {
            StateChanged?.Invoke();
        }

        return acquired;
    }

    /// <summary>Ends an exclusive operation, releasing any installs that were waiting for it to finish.</summary>
    public void EndExclusive()
    {
        TaskCompletionSource released;
        lock (_sync)
        {
            _exclusive = false;
            released = _exclusiveReleased;
            _exclusiveReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        released.TrySetResult();
        StateChanged?.Invoke();
    }
}
