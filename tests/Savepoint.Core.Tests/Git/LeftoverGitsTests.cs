using System.Diagnostics;
using System.Globalization;
using Savepoint.Core.Git;
using Savepoint.Core.Tests.Services;
using Savepoint.Core.Tests.Services.Exe;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Git;

// A SavePoint that is stopped from outside can leave git running. The SavePoint that ran it kept a note of the gits it started, with each
// one's start time; the next start ends the ones in that note that are still the very process the note names, and nothing else.
public sealed class LeftoverGitsTests : IDisposable
{
    private static readonly TimeSpan _short = TimeSpan.FromMilliseconds(400);

    // A program that does not exist: the SavePoint that wrote the note is gone.
    private static readonly ProcessStamp _deadOwner = new(int.MaxValue - 7, 1);

    private readonly TempFolder _temp = new("savepoint-leftovers-");
    private readonly RecordingDiagnosticLog _log = new();

    private string NotePath => RunningGitNote.PathIn(_temp.Path);

    public void Dispose() => _temp.Dispose();

    [Fact(Timeout = 120_000)]
    public async Task A_git_the_note_names_is_ended_and_a_program_it_does_not_name_is_left_alone()
    {
        using var leftover = FakeGameProcess.Start();
        using var bystander = FakeGameProcess.Start();
        RunningGitNote.Write(NotePath, _deadOwner, [StampOf(leftover.Id)]);

        var clear = await Leftovers().EndAsync(null, TestContext.Current.CancellationToken);

        Assert.True(clear, "the sweep reported a process still running");
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => !FakeGameProcess.IsRunning(leftover.Id), TimeSpan.FromSeconds(10)), "the leftover is still running");
        Assert.True(FakeGameProcess.IsRunning(bystander.Id), "a program the note does not name was ended");
        Assert.NotEmpty(_log.Containing("leftover", leftover.Id.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_process_that_only_has_the_number_of_a_git_the_note_names_is_left_alone()
    {
        using var other = FakeGameProcess.Start();
        var stamp = StampOf(other.Id);
        RunningGitNote.Write(NotePath, _deadOwner, [stamp with { StartedAtUtcTicks = stamp.StartedAtUtcTicks - TimeSpan.FromDays(1).Ticks }]);
        var leftovers = Leftovers();

        var clear = await leftovers.EndAsync(null, TestContext.Current.CancellationToken);

        Assert.True(clear);
        Assert.Empty(leftovers.Pending);
        Assert.True(FakeGameProcess.IsRunning(other.Id), "a process that took over a git's number was ended");
        Assert.Empty(_log.Entries);
    }

    [Fact(Timeout = 60_000)]
    public async Task Ending_a_process_checks_its_start_time_at_that_moment()
    {
        using var other = FakeGameProcess.Start();
        var stamp = StampOf(other.Id);

        var endedWrong = LeftoverGits.EndIfSame(stamp with { StartedAtUtcTicks = stamp.StartedAtUtcTicks - TimeSpan.FromDays(1).Ticks });
        var aliveAfterWrong = FakeGameProcess.IsRunning(other.Id);
        var endedRight = LeftoverGits.EndIfSame(stamp);

        Assert.False(endedWrong);
        Assert.True(aliveAfterWrong, "a process with another start time was ended");
        Assert.True(endedRight);
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => !FakeGameProcess.IsRunning(other.Id), TimeSpan.FromSeconds(10)));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_note_whose_owner_still_runs_belongs_to_a_live_SavePoint_and_names_no_leftovers()
    {
        using var game = FakeGameProcess.Start();
        RunningGitNote.Write(NotePath, ProcessStamp.Current(), [StampOf(game.Id)]);
        var leftovers = Leftovers();

        var clear = await leftovers.EndAsync(null, TestContext.Current.CancellationToken);

        Assert.True(clear);
        Assert.Empty(leftovers.Pending);
        Assert.True(FakeGameProcess.IsRunning(game.Id));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_leftover_that_ends_by_itself_within_the_wait_is_not_ended_by_force()
    {
        using var leftover = FakeGameProcess.Start("--exit-after", "1500");
        RunningGitNote.Write(NotePath, _deadOwner, [StampOf(leftover.Id)]);
        var ended = new List<ProcessStamp>();

        var clear = await Leftovers(wait: TimeSpan.FromSeconds(30), kill: ended.Add).EndAsync(null, TestContext.Current.CancellationToken);

        Assert.True(clear);
        Assert.Empty(ended);
        Assert.False(FakeGameProcess.IsRunning(leftover.Id));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_leftover_that_cannot_be_ended_is_reported_remembered_and_carried_into_the_new_note()
    {
        using var leftover = FakeGameProcess.Start();
        var stamp = StampOf(leftover.Id);
        RunningGitNote.Write(NotePath, _deadOwner, [stamp]);
        var otherNote = RunningGitNote.PathIn(Path.Combine(_temp.Path, "this-run"));
        var registry = new GitProcessRegistry(otherNote, TimeSpan.FromMilliseconds(100));
        var leftovers = Leftovers(registry: registry, kill: _ => { });

        var clear = await leftovers.EndAsync(null, TestContext.Current.CancellationToken);

        Assert.False(clear, "a process that was not ended was reported as gone");
        Assert.True(leftovers.AnyRunning());
        Assert.NotEmpty(_log.Containing("could not be ended"));
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => RunningGitNote.Read(otherNote)?.Gits.Contains(stamp) == true, TimeSpan.FromSeconds(10)), "the survivor was not kept in this run's note");

        FakeGameProcess.Kill(leftover.Id);

        Assert.False(leftovers.AnyRunning());
    }

    [Fact(Timeout = 120_000)]
    public async Task Nothing_is_said_and_nobody_waits_when_the_note_names_nothing_that_runs()
    {
        var told = 0;

        var clear = await Leftovers(wait: TimeSpan.FromMinutes(5)).EndAsync(() => told++, TestContext.Current.CancellationToken);

        Assert.True(clear);
        Assert.Equal(0, told);
        Assert.Empty(_log.Entries);
    }

    [Fact(Timeout = 120_000)]
    public async Task The_screen_is_told_before_the_wait_begins()
    {
        using var leftover = FakeGameProcess.Start();
        RunningGitNote.Write(NotePath, _deadOwner, [StampOf(leftover.Id)]);
        var leftovers = Leftovers();
        var stillRunningWhenTold = false;

        await leftovers.EndAsync(() => stillRunningWhenTold = leftovers.AnyRunning(), TestContext.Current.CancellationToken);

        Assert.True(stillRunningWhenTold);
    }

    private LeftoverGits Leftovers(GitProcessRegistry? registry = null, TimeSpan? wait = null, Action<ProcessStamp>? kill = null) =>
        new(NotePath, _log, registry, wait ?? _short, _short, TimeSpan.FromMilliseconds(50), kill);

    private static ProcessStamp StampOf(int id)
    {
        using var process = Process.GetProcessById(id);
        return ProcessStamp.Of(process)!.Value;
    }
}
