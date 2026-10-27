using System.Diagnostics;
using Savepoint.Core.Diagnostics;
using Savepoint.Core.Git;
using Savepoint.Core.Tests.Services;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Git;

// A registry made for the app is closed when the process exits; one whose backend is done must leave that list, or a test run that
// builds thousands of backends closes thousands of registries at the end.
public sealed class GitProcessRegistryReleaseTests : IDisposable
{
    private readonly TempFolder _temp = new("savepoint-registry-release-");

    public void Dispose() => _temp.Dispose();

    private string NotePath => RunningGitNote.PathIn(_temp.Path);

    private GitProcessRegistry NewRegistry() => GitProcessRegistry.ForApp(NotePath, TimeSpan.FromMilliseconds(150), DiagnosticLog.None);

    [Fact]
    public void A_registry_made_for_the_app_is_closed_at_exit_until_it_is_released()
    {
        var registry = NewRegistry();
        var listedBefore = GitProcessRegistry.IsClosedAtExit(registry);

        registry.Release();

        Assert.True(listedBefore);
        Assert.False(GitProcessRegistry.IsClosedAtExit(registry));
    }

    [Fact]
    public void Releasing_twice_does_nothing_the_second_time()
    {
        var registry = NewRegistry();
        registry.Release();

        registry.Release();

        Assert.False(GitProcessRegistry.IsClosedAtExit(registry));
    }

    [Fact]
    public void Releasing_one_registry_leaves_another_on_the_list()
    {
        var kept = NewRegistry();
        var released = NewRegistry();

        released.Release();

        Assert.True(GitProcessRegistry.IsClosedAtExit(kept));
        kept.Release();
    }

    [Fact(Timeout = 60_000)]
    public async Task Releasing_ends_what_runs_and_removes_the_note()
    {
        using var game = FakeGameProcess.Start();
        var registry = NewRegistry();
        using var listed = registry.Add(StampOf(game.Id));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => RunningGitNote.Read(NotePath) is not null, TimeSpan.FromSeconds(10)));

        registry.Release();

        Assert.False(File.Exists(NotePath), "the note stayed after the registry was released");
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => !FakeGameProcess.IsRunning(game.Id), TimeSpan.FromSeconds(10)));
    }

    private static ProcessStamp StampOf(int id)
    {
        using var process = Process.GetProcessById(id);
        return ProcessStamp.Of(process)!.Value;
    }
}
