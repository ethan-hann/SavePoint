using System.Diagnostics;
using Savepoint.Core.Git;
using Savepoint.Core.Tests.Services;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Git;

// The registry writes the gits it is running to a small note, so a start after SavePoint was stopped from outside can find what was left.
// A git that ends within the delay is never written, so the many short calls cost no disk writes.
public sealed class GitProcessRegistryNoteTests : IDisposable
{
    private static readonly TimeSpan _delay = TimeSpan.FromMilliseconds(150);

    // A note that would be written appears after the registry's delay, so waiting well past it shows that none does. Only waiting can.
    private static readonly TimeSpan _noNoteWindow = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan _noNoteWindowForShortDelay = TimeSpan.FromMilliseconds(1800);

    private readonly TempFolder _temp = new("savepoint-registry-note-");

    public void Dispose() => _temp.Dispose();

    private string NotePath => RunningGitNote.PathIn(_temp.Path);

    [Fact(Timeout = 60_000)]
    public async Task A_git_that_keeps_running_is_written_to_the_note_with_this_program_as_its_owner()
    {
        using var game = FakeGameProcess.Start();
        var registry = new GitProcessRegistry(NotePath, _delay);
        var stamp = StampOf(game.Id);

        using var listed = registry.Add(stamp);
        var written = await FakeGameProcess.WaitUntilAsync(() => RunningGitNote.Read(NotePath) is not null, TimeSpan.FromSeconds(10));

        Assert.True(written, "the note was never written");
        var note = RunningGitNote.Read(NotePath)!.Value;
        Assert.Equal(ProcessStamp.Current(), note.Owner);
        Assert.Equal([stamp], note.Gits);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_git_that_ends_within_the_delay_is_never_written()
    {
        using var game = FakeGameProcess.Start();
        var registry = new GitProcessRegistry(NotePath, TimeSpan.FromMilliseconds(800));

        registry.Add(StampOf(game.Id)).Dispose();
        await Task.Delay(_noNoteWindowForShortDelay, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(NotePath), "a git that had ended was written to the note");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_git_that_is_left_to_finish_is_not_written()
    {
        using var game = FakeGameProcess.Start();
        var registry = new GitProcessRegistry(NotePath, _delay);

        using var listed = registry.Add(StampOf(game.Id), survivesExit: true);
        await Task.Delay(_noNoteWindow, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(NotePath), "a git that survives SavePoint on purpose was written to the note of gits to end");
        Assert.True(registry.Contains(game.Id));
    }

    [Fact(Timeout = 60_000)]
    public async Task The_note_goes_when_the_last_git_ends()
    {
        using var game = FakeGameProcess.Start();
        var registry = new GitProcessRegistry(NotePath, _delay);
        var listed = registry.Add(StampOf(game.Id));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => RunningGitNote.Read(NotePath) is not null, TimeSpan.FromSeconds(10)));

        listed.Dispose();

        Assert.True(await FakeGameProcess.WaitUntilAsync(() => !File.Exists(NotePath), TimeSpan.FromSeconds(10)), "the note stayed after the git ended");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_git_carried_over_from_an_earlier_run_stays_in_the_note_while_it_runs_and_drops_out_when_it_ends()
    {
        using var game = FakeGameProcess.Start();
        var registry = new GitProcessRegistry(NotePath, _delay);
        var stamp = StampOf(game.Id);

        registry.Carry([stamp]);
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => RunningGitNote.Read(NotePath)?.Gits.Contains(stamp) == true, TimeSpan.FromSeconds(10)), "a carried git was not written");

        FakeGameProcess.Kill(game.Id);
        using var other = FakeGameProcess.Start();

        // Any change makes the registry look again.
        registry.Add(StampOf(other.Id)).Dispose();

        Assert.True(await FakeGameProcess.WaitUntilAsync(() => !File.Exists(NotePath), TimeSpan.FromSeconds(10)), "a carried git that had ended stayed in the note");
    }

    [Fact(Timeout = 60_000)]
    public async Task Closing_ends_what_runs_and_removes_the_note()
    {
        using var game = FakeGameProcess.Start();
        var registry = new GitProcessRegistry(NotePath, _delay);
        using var listed = registry.Add(StampOf(game.Id));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => RunningGitNote.Read(NotePath) is not null, TimeSpan.FromSeconds(10)));

        registry.Close();

        Assert.False(File.Exists(NotePath), "the note stayed after SavePoint closed");
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => !FakeGameProcess.IsRunning(game.Id), TimeSpan.FromSeconds(10)));
    }

    [Fact(Timeout = 60_000)]
    public async Task A_git_added_after_the_registry_closed_is_ended()
    {
        using var game = FakeGameProcess.Start();
        var registry = new GitProcessRegistry(NotePath, _delay);
        registry.Close();

        using var listed = registry.Add(StampOf(game.Id));

        Assert.True(await FakeGameProcess.WaitUntilAsync(() => !FakeGameProcess.IsRunning(game.Id), TimeSpan.FromSeconds(10)), "a git started after the registry closed was left running");
    }

    [Fact(Timeout = 60_000)]
    public async Task Closing_leaves_a_git_that_is_left_to_finish_running_and_off_the_note()
    {
        using var game = FakeGameProcess.Start();
        var registry = new GitProcessRegistry(NotePath, _delay);
        using var listed = registry.Add(StampOf(game.Id), survivesExit: true);

        registry.Close();
        await Task.Delay(_noNoteWindow, TestContext.Current.CancellationToken);

        Assert.True(FakeGameProcess.IsRunning(game.Id), "a git that finishes after SavePoint exits was ended when the registry closed");
        Assert.False(File.Exists(NotePath));
    }

    // Two backends on one runtime folder (a test restarts one on the same folder) write the same note; neither may drop the other's gits while both run.
    [Fact(Timeout = 60_000)]
    public async Task Two_live_registries_on_one_folder_both_keep_their_gits_on_the_note()
    {
        using var first = FakeGameProcess.Start();
        using var second = FakeGameProcess.Start();
        var one = new GitProcessRegistry(NotePath, _delay);
        var other = new GitProcessRegistry(NotePath, _delay);

        using var firstListed = one.Add(StampOf(first.Id));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => NoteNames(first.Id), TimeSpan.FromSeconds(10)));
        using var secondListed = other.Add(StampOf(second.Id));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => NoteNames(second.Id), TimeSpan.FromSeconds(10)));

        Assert.True(NoteNames(first.Id), "the second registry's write dropped the first registry's git from the note");
        one.Close();
        other.Close();
    }

    [Fact(Timeout = 60_000)]
    public async Task A_registry_that_ends_a_git_leaves_the_gits_of_the_other_registry_on_the_note()
    {
        using var first = FakeGameProcess.Start();
        using var second = FakeGameProcess.Start();
        var one = new GitProcessRegistry(NotePath, _delay);
        var other = new GitProcessRegistry(NotePath, _delay);
        var firstListed = one.Add(StampOf(first.Id));
        using var secondListed = other.Add(StampOf(second.Id));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => NoteNames(first.Id) && NoteNames(second.Id), TimeSpan.FromSeconds(10)));

        firstListed.Dispose();

        Assert.True(await FakeGameProcess.WaitUntilAsync(() => !NoteNames(first.Id), TimeSpan.FromSeconds(10)), "the git that ended stayed on the note");
        Assert.True(NoteNames(second.Id), "ending a git of one registry dropped the other registry's git");
        one.Close();
        other.Close();
    }

    [Fact(Timeout = 60_000)]
    public async Task Closing_one_registry_leaves_the_gits_of_the_other_registry_on_the_note()
    {
        using var first = FakeGameProcess.Start();
        using var second = FakeGameProcess.Start();
        var one = new GitProcessRegistry(NotePath, _delay);
        var other = new GitProcessRegistry(NotePath, _delay);
        using var firstListed = one.Add(StampOf(first.Id));
        using var secondListed = other.Add(StampOf(second.Id));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => NoteNames(first.Id) && NoteNames(second.Id), TimeSpan.FromSeconds(10)));

        one.Close();

        Assert.False(NoteNames(first.Id), "the closed registry's git stayed on the note");
        Assert.True(NoteNames(second.Id), "closing one registry dropped the other registry's git");
        other.Close();
    }

    // A start of 0.9.16 reads only the owner and one list of gits, so the merged note keeps both at the top level.
    [Fact(Timeout = 60_000)]
    public async Task The_note_of_two_registries_keeps_the_owner_and_one_list_of_every_git_for_an_older_start()
    {
        using var first = FakeGameProcess.Start();
        using var second = FakeGameProcess.Start();
        var one = new GitProcessRegistry(NotePath, _delay);
        var other = new GitProcessRegistry(NotePath, _delay);
        using var firstListed = one.Add(StampOf(first.Id));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => NoteNames(first.Id), TimeSpan.FromSeconds(10)));
        using var secondListed = other.Add(StampOf(second.Id));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => NoteNames(second.Id), TimeSpan.FromSeconds(10)));

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(NotePath));

        var ids = document.RootElement.GetProperty("gits").EnumerateArray().Select(git => git.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(ProcessStamp.Current().Id, document.RootElement.GetProperty("owner").GetProperty("id").GetInt32());
        Assert.Contains(first.Id, ids);
        Assert.Contains(second.Id, ids);
        one.Close();
        other.Close();
    }

    private bool NoteNames(int id) => RunningGitNote.Read(NotePath)?.Gits.Any(git => git.Id == id) == true;

    private static ProcessStamp StampOf(int id)
    {
        using var process = Process.GetProcessById(id);
        return ProcessStamp.Of(process)!.Value;
    }
}
