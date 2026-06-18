using Lodestone.Application.Concurrency;

namespace Lodestone.Application.Tests;

public class OperationCoordinatorTests
{
    [Fact]
    public async Task Concurrent_installs_all_begin_and_report_busy()
    {
        var c = new OperationCoordinator();

        await c.BeginInstallAsync();
        await c.BeginInstallAsync();
        await c.BeginInstallAsync();

        c.IsBusy.ShouldBeTrue();

        c.EndInstall();
        c.EndInstall();
        c.EndInstall();

        c.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task Exclusive_is_refused_while_an_install_is_in_flight_and_granted_after_it_ends()
    {
        var c = new OperationCoordinator();

        // An install holds the shared lane: an exclusive op must wait its turn (be refused).
        await c.BeginInstallAsync();
        c.TryBeginExclusive().ShouldBeFalse();

        c.EndInstall();
        c.TryBeginExclusive().ShouldBeTrue();
        c.EndExclusive();
    }

    [Fact]
    public void Exclusive_is_single_flight()
    {
        var c = new OperationCoordinator();

        c.TryBeginExclusive().ShouldBeTrue();
        c.TryBeginExclusive().ShouldBeFalse(); // a second exclusive op can't start
        c.EndExclusive();
        c.TryBeginExclusive().ShouldBeTrue();   // available again once the first ends
        c.EndExclusive();
    }

    [Fact]
    public async Task An_install_started_during_an_exclusive_op_waits_until_it_ends()
    {
        var c = new OperationCoordinator();
        c.TryBeginExclusive().ShouldBeTrue();

        Task t = c.BeginInstallAsync();
        t.IsCompleted.ShouldBeFalse(); // parked behind the exclusive op

        c.EndExclusive();
        await t; // released and proceeds

        c.IsBusy.ShouldBeTrue();
        c.EndInstall();
        c.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task BeginInstall_with_an_already_cancelled_token_throws_and_registers_nothing()
    {
        var c = new OperationCoordinator();
        c.TryBeginExclusive().ShouldBeTrue(); // force BeginInstallAsync to park, then observe cancellation

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => c.BeginInstallAsync(cts.Token));

        c.EndExclusive();
        c.IsBusy.ShouldBeFalse(); // the cancelled wait registered no install
    }

    [Fact]
    public async Task IsBusy_is_false_once_every_operation_has_ended()
    {
        var c = new OperationCoordinator();

        await c.BeginInstallAsync();
        await c.BeginInstallAsync();
        c.EndInstall();
        c.IsBusy.ShouldBeTrue(); // one install still running

        c.EndInstall();
        c.IsBusy.ShouldBeFalse();
    }
}
