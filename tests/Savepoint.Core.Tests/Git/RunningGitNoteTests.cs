using Savepoint.Core.Git;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Git;

public sealed class RunningGitNoteTests : IDisposable
{
    private readonly TempFolder _temp = new("savepoint-note-");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void What_is_written_is_read_back()
    {
        var path = RunningGitNote.PathIn(_temp.Path);
        var owner = new ProcessStamp(41, 1234567890);
        var first = new ProcessStamp(77, 99);
        var second = new ProcessStamp(78, 100);

        RunningGitNote.Write(path, owner, [first, second]);
        var note = RunningGitNote.Read(path);

        Assert.NotNull(note);
        Assert.Equal(owner, note.Value.Owner);
        Assert.Equal([first, second], note.Value.Gits);
    }

    // The exact shape 0.9.16 wrote: an owner and one list of gits, no per-registry parts.
    [Fact]
    public void A_note_in_the_form_0_9_16_wrote_is_read()
    {
        var path = RunningGitNote.PathIn(_temp.Path);
        File.WriteAllText(path, """{"owner":{"id":41,"startedAtUtcTicks":1234567890},"gits":[{"id":77,"startedAtUtcTicks":99}]}""");

        var note = RunningGitNote.Read(path);

        Assert.NotNull(note);
        Assert.Equal(new ProcessStamp(41, 1234567890), note.Value.Owner);
        Assert.Equal([new ProcessStamp(77, 99)], note.Value.Gits);
    }

    [Fact]
    public void A_missing_or_unreadable_note_reads_as_nothing()
    {
        var path = RunningGitNote.PathIn(_temp.Path);
        Assert.Null(RunningGitNote.Read(path));

        File.WriteAllText(path, "{ not json");

        Assert.Null(RunningGitNote.Read(path));
    }

    [Fact]
    public void Clearing_removes_the_note_and_clearing_a_missing_one_is_fine()
    {
        var path = RunningGitNote.PathIn(_temp.Path);
        RunningGitNote.Write(path, new ProcessStamp(1, 1), [new ProcessStamp(2, 2)]);

        RunningGitNote.Clear(path);
        RunningGitNote.Clear(path);

        Assert.False(File.Exists(path));
    }

    // A run that is ending can reach the note after a newer SavePoint has started and written its own; that note is not the ending run's to touch.
    // This test's own process stands for the newer SavePoint, which is running.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_update_leaves_a_note_alone_when_another_process_that_still_runs_owns_it(bool withGits)
    {
        var path = RunningGitNote.PathIn(_temp.Path);
        var live = ProcessStamp.Current();
        var key = Guid.NewGuid();
        RunningGitNote.Update(path, live, key, [new ProcessStamp(77, 99)]);
        var before = File.ReadAllText(path);
        var ending = new ProcessStamp(41, 1234567890);

        RunningGitNote.Update(path, ending, Guid.NewGuid(), withGits ? [new ProcessStamp(78, 100)] : []);
        RunningGitNote.Update(path, ending, key, []);

        Assert.True(File.Exists(path), "the live run's note was deleted");
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void An_update_replaces_a_note_whose_owner_has_ended()
    {
        var path = RunningGitNote.PathIn(_temp.Path);
        var gone = new ProcessStamp(41, 1234567890);
        RunningGitNote.Update(path, gone, Guid.NewGuid(), [new ProcessStamp(77, 99)]);

        RunningGitNote.Update(path, ProcessStamp.Current(), Guid.NewGuid(), [new ProcessStamp(78, 100)]);

        var note = RunningGitNote.Read(path);
        Assert.NotNull(note);
        Assert.Equal(ProcessStamp.Current(), note.Value.Owner);
        Assert.Equal([new ProcessStamp(78, 100)], note.Value.Gits);
    }

    // The note is read on a timer thread, where an exception ends the process; a part that cannot be understood costs only itself.
    [Theory]
    [InlineData("null")]
    [InlineData("\"not-a-guid\"")]
    [InlineData("42")]
    public void A_part_with_a_key_that_is_not_an_id_is_skipped_and_the_others_stay(string badKey)
    {
        var path = RunningGitNote.PathIn(_temp.Path);
        var owner = ProcessStamp.Current();
        var keptGit = ProcessStamp.Current();
        File.WriteAllText(
            path,
            $$"""{"owner":{"id":{{owner.Id}},"startedAtUtcTicks":{{owner.StartedAtUtcTicks}}},"gits":[],"registries":[{"key":{{badKey}},"gits":[{"id":{{keptGit.Id}},"startedAtUtcTicks":{{keptGit.StartedAtUtcTicks}}}]},{"key":"{{Guid.NewGuid()}}","gits":[{"id":{{keptGit.Id}},"startedAtUtcTicks":{{keptGit.StartedAtUtcTicks}}}]}]}""");

        var thrown = Record.Exception(() => RunningGitNote.Update(path, owner, Guid.NewGuid(), [new ProcessStamp(78, 100)]));

        Assert.Null(thrown);
        var note = RunningGitNote.Read(path);
        Assert.NotNull(note);
        Assert.Contains(keptGit, note.Value.Gits);
        Assert.Contains(new ProcessStamp(78, 100), note.Value.Gits);
    }

    // The exit hook clears the note of every backend this process built; one whose folder is long gone must cost nothing, or the process
    // is slow to end (a test run built thousands, and a host that does not end in time is stopped as if a thread were left running).
    [Fact]
    public void Clearing_a_note_in_a_folder_that_is_gone_does_not_wait()
    {
        var path = RunningGitNote.PathIn(Path.Combine(_temp.Path, "removed", "runtime"));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        RunningGitNote.Clear(path);

        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(250), $"clearing took {watch.ElapsedMilliseconds} ms for a note that cannot exist");
    }
}
