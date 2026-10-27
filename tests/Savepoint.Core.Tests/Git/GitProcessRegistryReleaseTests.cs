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

    [Fact(Timeout = 60_000)]
    public async Task Releasing_twice_leaves_a_note_another_registry_wrote_in_between()
    {
        using var first = FakeGameProcess.Start();
        using var second = FakeGameProcess.Start();
        var registry = NewRegistry();
        using var listed = registry.Add(StampOf(first.Id));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => NoteNames(first.Id), TimeSpan.FromSeconds(10)));
        registry.Release();
        var other = NewRegistry();
        using var otherListed = other.Add(StampOf(second.Id));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => NoteNames(second.Id), TimeSpan.FromSeconds(10)));

        registry.Release();

        Assert.True(NoteNames(second.Id), "the second release removed a note it had not written");
        other.Release();
    }

    [Fact(Timeout = 60_000)]
    public void Releasing_a_registry_that_wrote_nothing_keeps_the_note_an_earlier_run_left()
    {
        using var leftover = FakeGameProcess.Start();
        RunningGitNote.Write(NotePath, StampOf(leftover.Id), [StampOf(leftover.Id)]);
        var registry = NewRegistry();

        registry.Release();

        Assert.True(File.Exists(NotePath), "a registry that never wrote the note removed the one an earlier run left");
    }

    [Fact(Timeout = 60_000)]
    public async Task Releasing_keeps_the_gits_of_an_earlier_run_that_still_run_on_the_note()
    {
        using var leftover = FakeGameProcess.Start();
        var registry = NewRegistry();
        registry.Carry([StampOf(leftover.Id)]);
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => NoteNames(leftover.Id), TimeSpan.FromSeconds(10)));

        registry.Release();

        Assert.True(NoteNames(leftover.Id), "a git an earlier run left, still running, was dropped from the note");
    }

    [Fact(Timeout = 60_000)]
    public async Task Releasing_one_registry_leaves_the_gits_of_another_on_the_same_folder_on_the_note()
    {
        using var first = FakeGameProcess.Start();
        using var second = FakeGameProcess.Start();
        var released = NewRegistry();
        var kept = NewRegistry();
        using var firstListed = released.Add(StampOf(first.Id));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => NoteNames(first.Id), TimeSpan.FromSeconds(10)));
        using var secondListed = kept.Add(StampOf(second.Id));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => NoteNames(second.Id), TimeSpan.FromSeconds(10)));

        released.Release();

        Assert.True(NoteNames(second.Id), "the surviving registry's git is not on the note");
        kept.Release();
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

    private bool NoteNames(int id) => RunningGitNote.Read(NotePath)?.Gits.Any(git => git.Id == id) == true;

    private static ProcessStamp StampOf(int id)
    {
        using var process = Process.GetProcessById(id);
        return ProcessStamp.Of(process)!.Value;
    }
}
