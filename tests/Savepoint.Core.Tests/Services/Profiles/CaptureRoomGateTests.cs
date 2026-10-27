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
}
