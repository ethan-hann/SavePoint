using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A switch note another program holds (a virus scan, a backup tool) is waited for before it is called unreadable or stuck. No test here
// sleeps: the waits are handed to a delay that only records them, and acts on the hold as a program letting go would.
public sealed class SwitchJournalWaitTests : IDisposable
{
    private readonly string _gitDir = Directory.CreateTempSubdirectory("savepoint-journal-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_gitDir, recursive: true);

    private string Note => SwitchJournal.PathIn(_gitDir);

    // Records each wait without taking time; onWait gets the time waited so far.
    private sealed class RecordedWaits(Action<TimeSpan>? onWait = null)
    {
        public List<TimeSpan> Waited { get; } = [];

        public TimeSpan Total => TimeSpan.FromTicks(Waited.Sum(wait => wait.Ticks));

        public Task Delay(TimeSpan wait, CancellationToken ct)
        {
            Waited.Add(wait);
            onWait?.Invoke(Total);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void The_waits_add_up_to_two_or_three_seconds_and_never_shorten()
    {
        var total = TimeSpan.FromTicks(SwitchJournal.Waits.Sum(wait => wait.Ticks));

        Assert.InRange(total, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        Assert.Equal(SwitchJournal.Waits.Order(), SwitchJournal.Waits);
    }

    [Fact]
    public async Task A_note_nobody_holds_is_read_without_waiting()
    {
        SwitchJournal.Write(_gitDir, "aaa", "bbb", "profile-a");
        var waits = new RecordedWaits();

        var note = await SwitchJournal.ReadAsync(_gitDir, waits.Delay, Ct);

        Assert.Equal(new InterruptedSwitch("aaa", "bbb", "profile-a"), note);
        Assert.Empty(waits.Waited);
    }

    [Fact]
    public async Task A_missing_note_is_none_without_waiting()
    {
        var waits = new RecordedWaits();

        Assert.Null(await SwitchJournal.ReadAsync(_gitDir, waits.Delay, Ct));
        Assert.Empty(waits.Waited);
    }

    [Fact]
    public async Task A_note_nobody_holds_is_cleared_without_waiting()
    {
        SwitchJournal.Write(_gitDir, "aaa", "bbb");
        var waits = new RecordedWaits();

        Assert.True(await SwitchJournal.ClearAsync(_gitDir, waits.Delay));

        Assert.False(File.Exists(Note));
        Assert.Empty(waits.Waited);
    }

    [Fact]
    public async Task A_note_held_for_the_whole_wait_is_waited_for_the_whole_schedule_and_then_unknown()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets one program keep another from reading a file");
        SwitchJournal.Write(_gitDir, "aaa", "bbb");
        using var held = new FileStream(Note, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var waits = new RecordedWaits();

        var note = await SwitchJournal.ReadAsync(_gitDir, waits.Delay, Ct);

        Assert.Equal(new InterruptedSwitch(null, null), note);
        Assert.Equal(SwitchJournal.Waits, waits.Waited);
        Assert.True(waits.Total >= TimeSpan.FromSeconds(2), $"gave up after {waits.Total}");
    }

    [Fact]
    public async Task A_note_a_scan_holds_for_a_second_is_read_once_the_scan_ends()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets one program keep another from reading a file");
        SwitchJournal.Write(_gitDir, "aaa", "bbb", "profile-a");
        using var held = new FileStream(Note, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var waits = new RecordedWaits(total =>
        {
            if (total >= TimeSpan.FromSeconds(1))
            {
                held.Dispose();
            }
        });

        var note = await SwitchJournal.ReadAsync(_gitDir, waits.Delay, Ct);

        Assert.Equal(new InterruptedSwitch("aaa", "bbb", "profile-a"), note);
        Assert.True(waits.Total >= TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Waiting_for_a_held_note_hands_the_thread_back_to_the_caller()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets one program keep another from reading a file");
        SwitchJournal.Write(_gitDir, "aaa", "bbb");
        using var held = new FileStream(Note, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var reading = SwitchJournal.ReadAsync(_gitDir, (_, _) => waiting.Task, Ct);

        Assert.False(reading.IsCompleted, "the call held its thread while the note was held");
        held.Dispose();
        waiting.SetResult();
        Assert.Equal(new InterruptedSwitch("aaa", "bbb"), await reading);
    }

    [Fact]
    public async Task A_note_held_for_the_whole_wait_is_waited_for_the_whole_schedule_and_then_called_stuck()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets one program keep another from deleting a file");
        SwitchJournal.Write(_gitDir, "aaa", "bbb");
        using var held = new FileStream(Note, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var waits = new RecordedWaits();

        var cleared = await SwitchJournal.ClearAsync(_gitDir, waits.Delay);

        Assert.False(cleared);
        Assert.Equal(SwitchJournal.Waits, waits.Waited);
        Assert.True(waits.Total >= TimeSpan.FromSeconds(2), $"gave up after {waits.Total}");
    }

    [Fact]
    public async Task A_note_a_scan_holds_for_a_second_is_cleared_once_the_scan_ends()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets one program keep another from deleting a file");
        SwitchJournal.Write(_gitDir, "aaa", "bbb");
        using var held = new FileStream(Note, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var waits = new RecordedWaits(total =>
        {
            if (total >= TimeSpan.FromSeconds(1))
            {
                held.Dispose();
            }
        });

        var cleared = await SwitchJournal.ClearAsync(_gitDir, waits.Delay);

        Assert.True(cleared);
        Assert.False(File.Exists(Note));
    }
}
