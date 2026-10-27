using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A reclaim deletes the packs it replaces. A SavePoint that is killed while it runs cannot always take git with it, and a git left
// running would delete packs under the writes of the SavePoint that starts next, so the reclaim notes itself in the git directory and
// the next start waits for the git it names.
[Collection(EngineCollections.E)]
public sealed class ReclaimInterruptedTests : IDisposable
{
    // A settle that did not wait for the live git would finish within moments, and only waiting shows that it does not.
    private static readonly TimeSpan _waitWindow = TimeSpan.FromMilliseconds(750);

    // The settle has to be waiting on the git before it is abandoned. It shows no sign of that, so a pause is the only way to order the two.
    private static readonly TimeSpan _reachWait = TimeSpan.FromMilliseconds(300);

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_reclaim_notes_itself_before_git_starts_and_the_note_follows_each_git_it_starts_and_goes_when_it_ends()
    {
        // The loose objects of the three points are what make the reclaim repack.
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        (ProcessStamp Owner, ProcessStamp? Git)? atReflog = null;
        (ProcessStamp Owner, ProcessStamp? Git)? atRepack = null;
        (ProcessStamp Owner, ProcessStamp? Git)? atPrune = null;
        _ws.OnInvoke = invocation =>
        {
            switch (invocation.Arguments[0])
            {
                case "reflog":
                    atReflog = ReclaimMarker.Read(repo.GitDir);
                    break;
                case "repack":
                    atRepack = ReclaimMarker.Read(repo.GitDir);
                    break;
                case "prune":
                    atPrune = ReclaimMarker.Read(repo.GitDir);
                    break;
            }
        };

        await _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, Ct);

        Assert.NotNull(atReflog);
        Assert.Equal(Environment.ProcessId, atReflog.Value.Owner.Id);
        Assert.Null(atReflog.Value.Git);
        // Each read comes before the next git starts, so it names the one before. A git that has already exited when its start time is
        // read cannot be stamped (Linux reaps it at once) and is not noted, which is safe: there is nothing left to wait for.
        Assert.NotNull(atRepack);
        Assert.NotNull(atPrune);
        Assert.Equal(Environment.ProcessId, atRepack.Value.Owner.Id);
        Assert.Equal(Environment.ProcessId, atPrune.Value.Owner.Id);
        Assert.True(atRepack.Value.Git is not null || atPrune.Value.Git is not null, "no git the reclaim started was noted");
        if (atRepack.Value.Git is { } reflogGit && atPrune.Value.Git is { } repackGit)
        {
            Assert.NotEqual(reflogGit, repackGit);
        }
        Assert.False(File.Exists(ReclaimMarker.PathIn(repo.GitDir)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_reclaim_that_fails_or_is_cancelled_still_takes_its_note_away(bool cancel)
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] != "repack")
            {
                return;
            }

            if (cancel)
            {
                stop.Cancel();
            }
            else
            {
                throw new IOException("The disk reported an error.");
            }
        };

        var thrown = await Record.ExceptionAsync(() => _ws.Engine.RepackAsync(repo, OptimizationMode.Reclaim, stop.Token));

        Assert.NotNull(thrown);
        Assert.False(File.Exists(ReclaimMarker.PathIn(repo.GitDir)));
    }

    [Fact]
    public async Task A_note_that_names_a_git_still_running_under_an_owner_that_is_gone_is_waited_out_and_then_removed()
    {
        var repo = await _ws.InitializedRepoAsync();
        var gone = await LiveOrphan.EndedAsync(_ws.Runner, repo.GitDir);
        var orphan = await LiveOrphan.StartAsync(_ws.Runner, repo.GitDir);
        ReclaimMarker.Write(repo.GitDir, gone, orphan.Stamp);

        var settling = _ws.Engine.SettleReclaimAsync(repo.GitDir, Ct);
        var early = await Task.WhenAny(settling, Task.Delay(_waitWindow, Ct));
        var markerWhileWaiting = File.Exists(ReclaimMarker.PathIn(repo.GitDir));
        await orphan.EndAsync();
        await settling.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.NotSame(settling, early);
        Assert.True(markerWhileWaiting);
        Assert.False(File.Exists(ReclaimMarker.PathIn(repo.GitDir)));
    }

    [Fact]
    public async Task A_note_whose_git_has_already_ended_is_removed_at_once()
    {
        var repo = await _ws.InitializedRepoAsync();
        var gone = await LiveOrphan.EndedAsync(_ws.Runner, repo.GitDir);
        ReclaimMarker.Write(repo.GitDir, gone, await LiveOrphan.EndedAsync(_ws.Runner, repo.GitDir));

        await _ws.Engine.SettleReclaimAsync(repo.GitDir, Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.False(File.Exists(ReclaimMarker.PathIn(repo.GitDir)));
    }

    [Fact]
    public async Task A_git_number_that_another_process_has_taken_since_is_not_waited_for()
    {
        var repo = await _ws.InitializedRepoAsync();
        var gone = await LiveOrphan.EndedAsync(_ws.Runner, repo.GitDir);
        var other = await LiveOrphan.StartAsync(_ws.Runner, repo.GitDir);
        try
        {
            ReclaimMarker.Write(repo.GitDir, gone, other.Stamp with { StartedAtUtcTicks = other.Stamp.StartedAtUtcTicks - TimeSpan.FromHours(2).Ticks });

            await _ws.Engine.SettleReclaimAsync(repo.GitDir, Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);

            Assert.False(File.Exists(ReclaimMarker.PathIn(repo.GitDir)));
        }
        finally
        {
            await other.EndAsync();
        }
    }

    [Fact]
    public async Task A_note_whose_owner_is_still_running_is_a_reclaim_in_progress_and_is_left_alone()
    {
        var repo = await _ws.InitializedRepoAsync();
        var orphan = await LiveOrphan.StartAsync(_ws.Runner, repo.GitDir);
        try
        {
            ReclaimMarker.Write(repo.GitDir, ProcessStamp.Current(), orphan.Stamp);

            await _ws.Engine.SettleReclaimAsync(repo.GitDir, Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);

            Assert.True(File.Exists(ReclaimMarker.PathIn(repo.GitDir)));
        }
        finally
        {
            await orphan.EndAsync();
        }
    }

    [Fact]
    public async Task A_note_nobody_can_read_is_removed_and_holds_nothing_up()
    {
        var repo = await _ws.InitializedRepoAsync();
        await File.WriteAllTextAsync(ReclaimMarker.PathIn(repo.GitDir), "{ not a note", Ct);

        await _ws.Engine.SettleReclaimAsync(repo.GitDir, Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.False(File.Exists(ReclaimMarker.PathIn(repo.GitDir)));
    }

    [Fact]
    public async Task Waiting_for_a_git_that_is_still_running_can_be_abandoned_and_leaves_the_note_for_the_next_try()
    {
        var repo = await _ws.InitializedRepoAsync();
        var gone = await LiveOrphan.EndedAsync(_ws.Runner, repo.GitDir);
        var orphan = await LiveOrphan.StartAsync(_ws.Runner, repo.GitDir);
        try
        {
            ReclaimMarker.Write(repo.GitDir, gone, orphan.Stamp);
            using var abandon = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            var settling = _ws.Engine.SettleReclaimAsync(repo.GitDir, abandon.Token);
            await Task.Delay(_reachWait, Ct);

            await abandon.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => settling.WaitAsync(TimeSpan.FromSeconds(10), Ct));
            Assert.True(File.Exists(ReclaimMarker.PathIn(repo.GitDir)));
        }
        finally
        {
            await orphan.EndAsync();
        }
    }
}
