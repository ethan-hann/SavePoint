using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// After git has written the files a switch still reads them back to check them, which moves no count; the reports say so while it runs.
[Collection(EngineCollections.A)]
public sealed class SwitchWrapUpProgressTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_switch_says_it_is_wrapping_up_before_it_reads_the_files_back_and_clears_it_at_the_end()
    {
        var repo = await TwoProfilesAsync();
        var progress = new ProgressLog();
        var saidBeforeTheRefresh = false;
        _ws.OnInvoke = call =>
        {
            if (call.Arguments[0] == "update-index")
            {
                saidBeforeTheRefresh = progress.Reports.Any(report => report.Finishing);
            }
        };

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", progress, Ct);

        Assert.True(saidBeforeTheRefresh, "expected a report with Finishing set before the files are read back");
        Assert.False(progress.Last.Finishing);
        var wrapUp = progress.Reports.First(report => report.Finishing);
        Assert.Equal(progress.Last.FilesDone, wrapUp.FilesDone);
        Assert.Equal(progress.Last.BytesTotal, wrapUp.BytesTotal);
    }

    [Fact]
    public async Task A_switch_that_fails_while_reading_the_files_back_does_not_leave_the_wrap_up_flag_on()
    {
        var repo = await TwoProfilesAsync();
        var progress = new ProgressLog();
        _ws.OnInvoke = call =>
        {
            if (EngineWorkspace.IsVerifyDiff(call))
            {
                throw new IOException("the disk went away");
            }
        };

        await Assert.ThrowsAsync<IOException>(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", progress, Ct));

        Assert.False(progress.Last.Finishing);
    }

    private async Task<RepoRef> TwoProfilesAsync()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/blob.bin", "the base game");
        await _ws.CaptureAsync(repo, "profile-a");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Write(repo, "Data/extra.pak", "an addition");
        await _ws.CaptureAsync(repo, "profile-b");
        return repo;
    }
}
