using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Starting git costs about as much as the work it does for a small folder, so each engine operation has a ceiling on how many
// processes it starts. The numbers are what the operation needs today; a change that adds a start has to say why in this file.
[Collection(EngineCollections.E)]
public sealed class GitStartCountTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private sealed class NoProgress : IProgress<FileProgress>
    {
        public void Report(FileProgress value)
        {
        }
    }

    // How many git processes the action starts.
    private async Task<int> StartsOfAsync(Func<Task> action)
    {
        var before = _ws.Invocations.Count;
        await action();
        return _ws.Invocations.Count - before;
    }

    [Fact]
    public async Task The_first_capture_of_a_folder_starts_five_git_processes_at_most()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);

        var starts = await StartsOfAsync(() => _ws.CaptureAsync(repo));

        Assert.True(starts <= 5, $"started {starts} git processes");
    }

    [Fact]
    public async Task A_later_capture_starts_five_git_processes_at_most()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);
        await _ws.CaptureAsync(repo);
        _ws.Write(repo, "Data/f1.txt", "changed");

        var starts = await StartsOfAsync(() => _ws.CaptureAsync(repo));

        Assert.True(starts <= 5, $"started {starts} git processes");
    }

    [Fact]
    public async Task A_watched_capture_starts_six_git_processes_at_most()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);
        await _ws.CaptureAsync(repo);
        _ws.Write(repo, "Data/f1.txt", "changed");

        var starts = await StartsOfAsync(() => _ws.CaptureAsync(repo, progress: new NoProgress()));

        Assert.True(starts <= 6, $"started {starts} git processes");
    }

    [Fact]
    public async Task Sizing_a_folder_before_its_first_capture_starts_no_git_process()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);
        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);

        var starts = await StartsOfAsync(() => _ws.Engine.EstimateNewBytesAsync(repo, candidates, [], Ct));

        Assert.Equal(0, starts);
    }

    [Fact]
    public async Task Reading_the_status_of_a_folder_starts_two_git_processes_at_most()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);
        await _ws.CaptureAsync(repo);
        _ws.Write(repo, "Data/f1.txt", "changed");

        var starts = await StartsOfAsync(() => _ws.Engine.GetStatusAsync(repo, Ct));

        Assert.True(starts <= 2, $"started {starts} git processes");
    }

    [Fact]
    public async Task A_switch_starts_nine_git_processes_at_most()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 10);
        await _ws.CaptureAsync(repo, "profile-1");
        await _ws.Engine.CreateBranchAsync(repo, "profile-2", "profile-1", Ct);
        _ws.Write(repo, "Data/f1.txt", "on profile 1");
        await _ws.CaptureAsync(repo, "profile-1", "Later");

        var starts = await StartsOfAsync(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-2", ct: Ct));

        Assert.True(starts <= 9, $"started {starts} git processes");
    }
}
