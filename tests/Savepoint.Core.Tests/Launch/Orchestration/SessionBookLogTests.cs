using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch.Orchestration;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Launch.Orchestration;

public class SessionBookLogTests
{
    private static readonly Guid _job = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly RecordingDiagnosticLog _log = new();

    private SessionBook NewBook() => new(_log);

    // The pump writes the lines in order on a pool thread, so a test waits for the count it expects.
    private IReadOnlyList<LogEntry> WaitForLines(int count)
    {
        Assert.True(SpinWait.SpinUntil(() => _log.Entries.Count >= count, TimeSpan.FromSeconds(10)), $"Expected {count} lines, saw {_log.Entries.Count}.");
        return _log.Entries;
    }

    [Fact]
    public void A_new_session_is_logged_with_its_game_profile_job_and_what_it_was_asked_to_load()
    {
        var book = NewBook();

        book.Open("skyrim", "modded", new LaunchTarget.Point(new HistoryPointId("abc")), startedFromOlderPoint: true, _job);

        var line = Assert.Single(WaitForLines(1));
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("skyrim", line.Message, StringComparison.Ordinal);
        Assert.Contains("modded", line.Message, StringComparison.Ordinal);
        Assert.Contains(_job.ToString(), line.Message, StringComparison.Ordinal);
        Assert.Contains("Point", line.Message, StringComparison.Ordinal);
        Assert.Contains("older point", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_subscriber_that_throws_is_logged()
    {
        var book = NewBook();
        book.SessionChanged += (_, _) => throw new InvalidOperationException("a view model broke");

        book.Open("skyrim", "modded", new LaunchTarget.ProfileTip("modded"), startedFromOlderPoint: false, _job);

        Assert.True(SpinWait.SpinUntil(() => _log.Entries.Any(entry => entry.Level == LogLevel.Warning), TimeSpan.FromSeconds(10)), "no warning was logged");
        var warning = _log.Entries.First(entry => entry.Level == LogLevel.Warning);
        Assert.Contains("subscriber", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(warning.Exception);
    }

    [Fact]
    public void A_stream_of_switch_progress_is_one_line_not_one_per_report()
    {
        var book = NewBook();
        book.Open("skyrim", "modded", new LaunchTarget.ProfileTip("modded"), startedFromOlderPoint: false, _job);

        for (var done = 1; done <= 5; done++)
        {
            book.TryChange("skyrim", _job, _ => new SessionState.Switching(new JobProgress(done, 5, done, 5, null)));
        }

        book.TryChange("skyrim", _job, _ => new SessionState.Running(DateTimeOffset.UnixEpoch));
        var lines = WaitForLines(3);

        Assert.Equal(3, lines.Count);
        Assert.Contains("switching", lines[1].Message, StringComparison.Ordinal);
        Assert.Contains("running", lines[2].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_game_stopping_logs_how_long_it_ran_and_what_changed()
    {
        var book = NewBook();
        book.Open("skyrim", "modded", new LaunchTarget.ProfileTip("modded"), startedFromOlderPoint: false, _job);
        var options = new PostPlayOptions([CompletionChoice.CaptureNew, CompletionChoice.Skip], CompletionChoice.CaptureNew, null, 0);

        book.TryChange("skyrim", _job, _ => new SessionState.Exited(DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(95), 4, 2, options));

        var line = WaitForLines(2)[1];
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("stopped", line.Message, StringComparison.Ordinal);
        Assert.Contains("1 h 35 min", line.Message, StringComparison.Ordinal);
        Assert.Contains("4 new", line.Message, StringComparison.Ordinal);
        Assert.Contains("2 changed", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Saving_after_play_and_finishing_are_logged_with_the_choice_made()
    {
        var book = NewBook();
        book.Open("skyrim", "modded", new LaunchTarget.ProfileTip("modded"), startedFromOlderPoint: false, _job);
        var options = new PostPlayOptions([CompletionChoice.CaptureNew], CompletionChoice.CaptureNew, null, 0);
        book.TryChange("skyrim", _job, _ => new SessionState.Exited(DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(1), 1, 0, options));
        var saveJob = Guid.NewGuid();

        book.Complete("skyrim", _ => new SessionState.Completing(new SessionCompletion.CaptureNew("Evening"), saveJob));
        book.TryChangeByJob(saveJob, _ => new SessionState.Completed(new SessionCompletion.CaptureNew("Evening"), null, null));

        var lines = WaitForLines(4);
        Assert.Contains("CaptureNew", lines[2].Message, StringComparison.Ordinal);
        Assert.Contains(saveJob.ToString(), lines[2].Message, StringComparison.Ordinal);
        Assert.Contains("finished", lines[3].Message, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Information, lines[3].Level);
    }

    [Fact]
    public void A_launch_that_failed_on_purpose_is_a_warning_and_a_surprise_is_an_error()
    {
        var book = NewBook();
        book.Open("skyrim", "modded", new LaunchTarget.ProfileTip("modded"), startedFromOlderPoint: false, _job);
        book.TryChange("skyrim", _job, _ => new SessionState.Failed(ErrorInfo.From(new LaunchSpawnException(LaunchMethod.Exe, "no such program", null))));
        var other = Guid.NewGuid();
        book.Open("fallout", "vanilla", new LaunchTarget.ProfileTip("vanilla"), startedFromOlderPoint: false, other);
        book.TryChange("fallout", other, _ => new SessionState.Failed(ErrorInfo.From(new InvalidOperationException("boom"))));

        var lines = WaitForLines(4);

        Assert.Equal(LogLevel.Warning, lines[1].Level);
        Assert.Contains("error.launch.spawnFailed", lines[1].Message, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Error, lines[3].Level);
        Assert.Contains("error.unknown", lines[3].Message, StringComparison.Ordinal);
        Assert.Contains("boom", lines[3].Message, StringComparison.Ordinal);
    }
}
