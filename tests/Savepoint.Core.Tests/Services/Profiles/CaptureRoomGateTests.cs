using System.Runtime.CompilerServices;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Services.Profiles;
using Savepoint.Core.Tests.Git.Engine;

namespace Savepoint.Core.Tests.Services.Profiles;

// The folders of one save report what they would add and are judged together once each has reported or dropped out.
[Collection(EngineCollections.F)]
public sealed class CaptureRoomGateTests : IDisposable
{
    private const long _floor = 1_000;

    // Only a hang gets near this; the waits end as soon as the last folder has decided.
    private static readonly TimeSpan _limit = TimeSpan.FromSeconds(20);

    private readonly EngineWorkspace _ws = new(options => options with { MinFreeBytesForStorageWrites = _floor });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private async Task<CaptureRoomGate> GateAsync()
    {
        var install = await _ws.InitializedRepoAsync(role: RepoRole.Install);
        var saves = await _ws.InitializedRepoAsync(role: RepoRole.External);
        return new CaptureRoomGate(_ws.Engine, [install, saves]);
    }

    [Fact]
    public async Task Folders_that_fit_together_are_all_let_through_once_the_last_has_reported()
    {
        var gate = await GateAsync();
        _ws.Space.Free = _floor + 500_000;

        var install = gate.ReportAsync(RepoRole.Install, 300_000, Ct);
        Assert.False(install.IsCompleted, "the first folder must wait for the other");
        var saves = gate.ReportAsync(RepoRole.External, 200_000, Ct);

        await Task.WhenAll(install, saves).WaitAsync(_limit, Ct);
    }

    [Fact]
    public async Task Folders_that_fit_alone_but_not_together_are_all_refused_with_the_figures_for_the_volume()
    {
        var gate = await GateAsync();
        _ws.Space.Free = _floor + 400_000;

        var install = gate.ReportAsync(RepoRole.Install, 300_000, Ct);
        var saves = gate.ReportAsync(RepoRole.External, 200_000, Ct);

        foreach (var report in new[] { install, saves })
        {
            var error = await Assert.ThrowsAsync<DiskSpaceException>(() => report.WaitAsync(_limit, Ct));
            Assert.Equal(_floor + 500_000, error.Args["requiredBytes"]);
            Assert.Equal(_floor + 400_000, error.Args["freeBytes"]);
        }
    }

    [Fact]
    public async Task A_folder_that_dropped_out_is_not_counted_so_the_others_are_judged_without_it()
    {
        var gate = await GateAsync();
        _ws.Space.Free = _floor + 400_000;

        var saves = gate.ReportAsync(RepoRole.External, 200_000, Ct);
        Assert.False(saves.IsCompleted, "the folder must wait for the one that has not decided");
        gate.DropOut(RepoRole.Install);

        await saves.WaitAsync(_limit, Ct);
    }

    [Fact]
    public async Task A_folder_that_drops_out_after_reporting_has_no_effect_on_the_verdict()
    {
        var gate = await GateAsync();
        _ws.Space.Free = _floor + 500_000;

        var install = gate.ReportAsync(RepoRole.Install, 300_000, Ct);
        gate.DropOut(RepoRole.Install);
        var saves = gate.ReportAsync(RepoRole.External, 200_000, Ct);
        gate.DropOut(RepoRole.External);

        await Task.WhenAll(install, saves).WaitAsync(_limit, Ct);
    }

    [Fact]
    public async Task A_folder_that_stops_waiting_leaves_the_others_to_be_judged_without_blocking_them()
    {
        var gate = await GateAsync();
        _ws.Space.Free = _floor + 500_000;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var install = gate.ReportAsync(RepoRole.Install, 300_000, stop.Token);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => install.WaitAsync(_limit, Ct));
        gate.DropOut(RepoRole.Install);
        var saves = gate.ReportAsync(RepoRole.External, 200_000, Ct);

        await saves.WaitAsync(_limit, Ct);
    }

    [Fact]
    public async Task An_unexpected_failure_while_judging_fails_every_waiting_folder_instead_of_leaving_them_waiting()
    {
        var gate = await GateAsync();
        _ws.Space.PerPath = _ => throw new FormatException("the volume answered nonsense");

        var install = gate.ReportAsync(RepoRole.Install, 300_000, Ct);
        var saves = ReportCapturingFailure(gate, RepoRole.External, 200_000);

        foreach (var report in new[] { install, saves })
        {
            await Assert.ThrowsAsync<FormatException>(() => report.WaitAsync(TimeSpan.FromSeconds(5), Ct));
        }
    }

    [Fact]
    public async Task A_save_stopped_before_the_verdict_leaves_no_unobserved_failure_behind()
    {
        const long free = _floor + 411_111;
        var unobserved = new List<Exception>();
        EventHandler<UnobservedTaskExceptionEventArgs> collect = (_, e) =>
        {
            lock (unobserved)
            {
                unobserved.AddRange(e.Exception.Flatten().InnerExceptions);
            }
        };
        TaskScheduler.UnobservedTaskException += collect;
        try
        {
            _ws.Space.Free = free;
            var gate = StopBothFoldersAndLetTheVolumeRefuse();
            for (var pass = 0; pass < 3; pass++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.False(gate.IsAlive, "the gate must be unreachable, or its tasks are never finalized and the check proves nothing");

            lock (unobserved)
            {
                Assert.DoesNotContain(unobserved, e => e is DiskSpaceException refused && Equals(refused.Args["freeBytes"], free));
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= collect;
        }
    }

    // Runs the whole scenario away from the test's own async frame, so the gate and every task it made are out of reach once this returns.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference StopBothFoldersAndLetTheVolumeRefuse() => Task.Run(StopBothFoldersAndLetTheVolumeRefuseAsync, Ct).GetAwaiter().GetResult();

    private async Task<WeakReference> StopBothFoldersAndLetTheVolumeRefuseAsync()
    {
        var install = await _ws.InitializedRepoAsync(role: RepoRole.Install);
        var saves = await _ws.InitializedRepoAsync(role: RepoRole.External);
        var mods = await _ws.InitializedRepoAsync(role: RepoRole.Mods);
        var gate = new CaptureRoomGate(_ws.Engine, [install, saves, mods]);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await stop.CancelAsync();

        var installWait = gate.ReportAsync(RepoRole.Install, 300_000, stop.Token);
        var savesWait = gate.ReportAsync(RepoRole.External, 200_000, stop.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installWait.WaitAsync(_limit, Ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => savesWait.WaitAsync(_limit, Ct));

        // The last folder to decide triggers the judgment, after nobody is waiting on it any more.
        gate.DropOut(RepoRole.Mods);
        return new WeakReference(gate);
    }

    // A failure thrown straight out of the call is turned into a failed task, so the test sees both outcomes the same way.
    private static async Task ReportCapturingFailure(CaptureRoomGate gate, RepoRole role, long newBytes) => await gate.ReportAsync(role, newBytes, Ct);
}
