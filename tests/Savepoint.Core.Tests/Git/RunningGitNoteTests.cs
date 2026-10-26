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
}
