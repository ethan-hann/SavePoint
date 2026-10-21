using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A switch that is stopped part way: the folder is a mixture, the status says so, and discarding then switching again completes it (G-28).
[Collection(EngineCollections.E)]
public sealed class InterruptedCheckoutTests : IDisposable
{
    private const int _files = 2500;

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_switch_cancelled_while_git_writes_files_is_reported_discardable_and_finishes_after_the_discard()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", _files, content: "a");
        await _ws.CaptureAsync(repo, "profile-a");
        var a = TreeSnapshot.Take(repo.WorkTree);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.WriteMany(repo, "Data", _files, content: "b");
        await _ws.CaptureAsync(repo, "profile-b");
        var b = TreeSnapshot.Take(repo.WorkTree);
        var from = await _ws.TipAsync(repo, "profile-b");
        var to = await _ws.TipAsync(repo, "profile-a");

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "checkout")
            {
                _ = Task.Delay(450, Ct).ContinueWith(_ => cancel.Cancel(), TaskScheduler.Default);
            }
        };

        try
        {
            await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: cancel.Token);
            Assert.Skip("the switch finished before the cancellation landed; nothing to recover from");
        }
        catch (OperationCanceledException)
        {
            // Expected: git and its workers were stopped.
        }

        _ws.OnInvoke = null;
        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.Equal(new InterruptedSwitch(from, to!), status.InterruptedSwitch);

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.Null((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);
        var afterDiscard = await _ws.Engine.GetStatusAsync(repo, Ct);
        Assert.True(afterDiscard.IsClean);
        Assert.Equal(afterDiscard.HeadCommit == from ? b : a, TreeSnapshot.Take(repo.WorkTree));

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);
    }

    [Fact]
    public async Task A_switch_cancelled_after_git_finished_is_completed_by_repeating_it()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-a");
        var a = TreeSnapshot.Take(repo.WorkTree);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Write(repo, "a.txt", "b");
        await _ws.CaptureAsync(repo, "profile-b");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _ws.OnInvoke = invocation =>
        {
            if (EngineWorkspace.IsVerifyDiff(invocation))
            {
                cancel.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: cancel.Token));

        Assert.NotNull((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);
        _ws.OnInvoke = null;
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);
        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);
    }

    [Fact]
    public async Task Discarding_clears_the_unfinished_switch_note()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        Directory.CreateDirectory(repo.GitDir);
        await File.WriteAllTextAsync(Path.Combine(repo.GitDir, "savepoint-switch.json"), "{\"from\":null,\"to\":\"abc\"}", Ct);
        Assert.NotNull((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.Null((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);
    }

    [Fact]
    public async Task A_note_that_cannot_be_read_is_ignored()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.GitDir, "savepoint-switch.json"), "not json", Ct);

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.Null(status.InterruptedSwitch);
        Assert.True(status.IsClean);
    }
}
