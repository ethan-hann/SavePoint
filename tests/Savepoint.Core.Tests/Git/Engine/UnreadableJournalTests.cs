using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A note that a switch started and did not finish is there whether or not it can be read: a capture that could not understand it
// would record a folder that holds files of two points.
[Collection(EngineCollections.C)]
public sealed class UnreadableJournalTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private async Task AssertCaptureRefusedAsync(RepoRef repo, string tip)
    {
        _ws.Write(repo, "c.txt", "a file the capture must not record");

        var error = await Assert.ThrowsAsync<InterruptedSwitchException>(() => _ws.CaptureAsync(repo, "profile-1", "Next"));

        Assert.Equal("error.launch.switchInterrupted", error.MessageId);
        Assert.Equal(RepoRole.Install, error.Args["role"]);
        Assert.Null(error.Args["targetPoint"]);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments.Contains("add") || invocation.Arguments.Contains("commit") || invocation.Arguments.Contains("commit-tree"));
        Assert.Equal(tip, await _ws.TipAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_capture_refuses_a_folder_whose_switch_note_is_not_valid_json()
    {
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        File.WriteAllText(SwitchJournal.PathIn(repo.GitDir), "{ this is not a note");

        await AssertCaptureRefusedAsync(repo, points[1]);
    }

    [Fact]
    public async Task A_capture_refuses_a_folder_whose_switch_note_names_no_target()
    {
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        File.WriteAllText(SwitchJournal.PathIn(repo.GitDir), "{\"from\":null}");

        await AssertCaptureRefusedAsync(repo, points[1]);
    }

    [Fact]
    public async Task A_capture_refuses_a_folder_whose_switch_note_another_program_holds_open()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets one program keep another from reading a file");
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        var note = SwitchJournal.PathIn(repo.GitDir);
        SwitchJournal.Write(repo.GitDir, points[0], points[1]);
        using var held = new FileStream(note, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await AssertCaptureRefusedAsync(repo, points[1]);
    }

    // The note's text is checked afterwards when the test can read it (a note another program holds open cannot be read until it lets go).
    private async Task AssertSwitchRefusedAsync(RepoRef repo, string point, string earlier, bool force, string? noteText)
    {
        var error = await Assert.ThrowsAsync<InterruptedSwitchException>(
            () => force ? _ws.Engine.ForceCheckoutCommitAsync(repo, earlier, Ct) : _ws.Engine.CheckoutCommitAsync(repo, earlier, ct: Ct));

        Assert.Equal("error.launch.switchInterrupted", error.MessageId);
        Assert.Null(error.Args["targetPoint"]);
        Assert.DoesNotContain(_ws.Invocations, invocation => !invocation.ReadOnly && invocation.Arguments[0] is "checkout" or "reset" or "clean");
        Assert.Equal(point, await _ws.TipAsync(repo, "profile-1"));
        Assert.True(File.Exists(_ws.FileOf(repo, "b.txt")), "the switch changed the folder");
        Assert.True(File.Exists(SwitchJournal.PathIn(repo.GitDir)));
        if (noteText is not null)
        {
            Assert.Equal(noteText, File.ReadAllText(SwitchJournal.PathIn(repo.GitDir)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_switch_refuses_a_folder_whose_switch_note_is_not_valid_json_and_leaves_the_note_alone(bool force)
    {
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        File.WriteAllText(SwitchJournal.PathIn(repo.GitDir), "{ this is not a note");

        await AssertSwitchRefusedAsync(repo, points[1], points[0], force, "{ this is not a note");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_switch_refuses_a_folder_whose_switch_note_another_program_holds_open(bool force)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets one program keep another from reading a file");
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        SwitchJournal.Write(repo.GitDir, points[0], points[1]);
        using var held = new FileStream(SwitchJournal.PathIn(repo.GitDir), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await AssertSwitchRefusedAsync(repo, points[1], points[0], force, null);
    }

    [Fact]
    public async Task A_discard_clears_a_switch_note_that_cannot_be_understood_so_the_player_is_not_stuck()
    {
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        File.WriteAllText(SwitchJournal.PathIn(repo.GitDir), "{ this is not a note");
        _ws.Write(repo, "a.txt", "edited");

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.False(File.Exists(SwitchJournal.PathIn(repo.GitDir)));
        Assert.Equal("one", File.ReadAllText(_ws.FileOf(repo, "a.txt")));

        // And with the note gone the switch goes ahead.
        await _ws.Engine.CheckoutCommitAsync(repo, points[0], ct: Ct);
        Assert.False(File.Exists(_ws.FileOf(repo, "b.txt")));
    }

    [Fact]
    public async Task The_status_of_a_folder_with_a_switch_note_that_cannot_be_understood_says_a_switch_is_unfinished_with_no_known_target()
    {
        var (repo, _) = await _ws.TwoPointsRepoAsync();
        File.WriteAllText(SwitchJournal.PathIn(repo.GitDir), "{ this is not a note");

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        var note = Assert.IsType<InterruptedSwitch>(status.InterruptedSwitch);
        Assert.Null(note.ToCommit);
        Assert.Null(note.FromCommit);
        Assert.Null(note.ToBranch);
        Assert.True(status.IsClean);
    }

    [Fact]
    public async Task The_status_of_a_folder_whose_switch_note_another_program_holds_open_says_a_switch_is_unfinished()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets one program keep another from reading a file");
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        SwitchJournal.Write(repo.GitDir, points[0], points[1]);
        using var held = new FileStream(SwitchJournal.PathIn(repo.GitDir), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.Null(Assert.IsType<InterruptedSwitch>(status.InterruptedSwitch).ToCommit);
    }

    [Fact]
    public async Task A_note_another_program_holds_for_a_moment_is_read_once_it_lets_go()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets one program keep another from reading a file");
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        SwitchJournal.Write(repo.GitDir, points[0], points[1]);
        var held = new FileStream(SwitchJournal.PathIn(repo.GitDir), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // The other program lets go during the first wait, whatever the machine's speed.
        var waits = 0;
        var note = SwitchJournal.Read(repo.GitDir, _ =>
        {
            waits++;
            held.Dispose();
        });

        Assert.Equal(new InterruptedSwitch(points[0], points[1]), note);
        Assert.Equal(1, waits);
    }

    [Fact]
    public async Task A_discard_that_cannot_remove_the_note_says_so_and_the_next_discard_removes_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets one program keep another from deleting a file");
        var (repo, _) = await _ws.TwoPointsRepoAsync();
        var note = SwitchJournal.PathIn(repo.GitDir);
        File.WriteAllText(note, "{ this is not a note");
        _ws.Write(repo, "a.txt", "edited");
        var held = new FileStream(note, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var error = await Assert.ThrowsAsync<SwitchNoteStuckException>(() => _ws.Engine.DiscardChangesAsync(repo, Ct));

            Assert.Equal("error.launch.switchNoteStuck", error.MessageId);
            Assert.Equal(RepoRole.Install, error.Args["role"]);
            Assert.Equal(note, error.Args["path"]);
            Assert.Equal("one", File.ReadAllText(_ws.FileOf(repo, "a.txt")));
        }
        finally
        {
            held.Dispose();
        }

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.False(File.Exists(note));
    }

    [Fact]
    public async Task A_capture_still_works_when_there_is_no_note()
    {
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        _ws.Write(repo, "c.txt", "new");

        var outcome = await _ws.CaptureAsync(repo, "profile-1", "Next");

        Assert.NotEqual(points[1], outcome.TipCommitId);
    }
}
