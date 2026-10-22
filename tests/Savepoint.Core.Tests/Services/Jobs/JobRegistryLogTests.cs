using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Services.Jobs;
using Savepoint.Testing;
using static Savepoint.Core.Tests.Services.Jobs.JobRegistryTestKit;

namespace Savepoint.Core.Tests.Services.Jobs;

public class JobRegistryLogTests
{
    [Fact]
    public async Task A_started_job_is_logged_with_its_id_kind_and_game()
    {
        var log = new RecordingDiagnosticLog();
        var registry = new JobRegistry(new ManualTimeProvider(), log);

        var held = new HeldJob(registry, JobKind.Capture, "skyrim");

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(held.Id.ToString(), entry.Message, StringComparison.Ordinal);
        Assert.Contains("Capture", entry.Message, StringComparison.Ordinal);
        Assert.Contains("skyrim", entry.Message, StringComparison.Ordinal);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_subscriber_that_throws_is_logged_and_does_not_stop_the_others_or_the_job()
    {
        var log = new RecordingDiagnosticLog();
        var registry = new JobRegistry(new ManualTimeProvider(), log);
        var heard = new List<Guid>();
        registry.JobChanged += (_, _) => throw new InvalidOperationException("a view model broke");
        registry.JobChanged += (_, snapshot) => heard.Add(snapshot.JobId);

        var held = new HeldJob(registry, JobKind.Capture, "skyrim");
        held.Finish();
        await FinishedAsync(registry, held.Id);

        Assert.Contains(held.Id, heard);
        var warning = log.Entries.First(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("subscriber", StringComparison.OrdinalIgnoreCase));
        Assert.IsType<InvalidOperationException>(warning.Exception);
    }

    [Fact]
    public async Task A_subscriber_that_throws_every_time_is_logged_once_for_the_job_and_counted_when_the_job_ends()
    {
        var log = new RecordingDiagnosticLog();
        var clock = new ManualTimeProvider();
        var registry = new JobRegistry(clock, log);
        var heard = 0;
        registry.JobChanged += (_, _) =>
        {
            Interlocked.Increment(ref heard);
            throw new InvalidOperationException("a view model broke");
        };
        var held = new HeldJob(registry, JobKind.Capture, "skyrim");
        var context = await held.Context;

        // Progress goes out every 50 ms, so a long job tells a subscriber this many times.
        for (var i = 1; i <= 100; i++)
        {
            context.Progress.Report(new JobProgress(FilesDone: i, FilesTotal: 100));
            clock.Advance(TimeSpan.FromMilliseconds(50));
            await registry.WhenDeliveredAsync();
        }

        var whileRunning = log.Entries.Where(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("subscriber", StringComparison.OrdinalIgnoreCase)).ToList();
        held.Finish();
        await FinishedAsync(registry, held.Id);

        Assert.True(heard > 100, "the subscriber should have been told of every change");
        Assert.Single(whileRunning);
        Assert.IsType<InvalidOperationException>(whileRunning[0].Exception);
        var warnings = log.Entries.Where(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("subscriber", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains($"{heard} times", warnings[1].Message, StringComparison.Ordinal);
        Assert.Contains(held.Id.ToString(), warnings[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_subscribers_that_throw_are_each_logged_once_and_a_subscriber_that_threw_once_has_no_count()
    {
        var log = new RecordingDiagnosticLog();
        var registry = new JobRegistry(new ManualTimeProvider(), log);
        var thrown = 0;
        registry.JobChanged += (_, _) => throw new InvalidOperationException("first");
        registry.JobChanged += (_, snapshot) =>
        {
            if (snapshot.Status != JobStatus.Running && Interlocked.Increment(ref thrown) == 1)
            {
                throw new InvalidOperationException("second");
            }
        };
        var held = new HeldJob(registry, JobKind.Capture, "skyrim");
        held.Finish();
        await FinishedAsync(registry, held.Id);

        var warnings = log.Entries.Where(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("subscriber", StringComparison.OrdinalIgnoreCase)).ToList();

        // The first subscriber threw for the start and for the end: one line for the first time, one for the count. The second threw once.
        Assert.Equal(3, warnings.Count);
        Assert.Equal(2, warnings.Count(entry => entry.Exception is not null));
        Assert.Single(warnings, entry => entry.Exception is null && entry.Message.Contains("2 times", StringComparison.Ordinal));
    }
    [Fact]
    public async Task A_job_that_spans_every_game_says_so()
    {
        var log = new RecordingDiagnosticLog();
        var registry = new JobRegistry(new ManualTimeProvider(), log);

        var held = new HeldJob(registry, JobKind.ClearData, gameId: null, cancellable: false);

        Assert.Contains("every game", Assert.Single(log.Entries).Message, StringComparison.Ordinal);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_job_that_succeeds_logs_its_status_and_how_long_it_took()
    {
        var log = new RecordingDiagnosticLog();
        var clock = new ManualTimeProvider();
        var registry = new JobRegistry(clock, log);
        var held = new HeldJob(registry, JobKind.Compact, "skyrim");
        await held.Context;
        clock.Advance(TimeSpan.FromSeconds(3.5));

        held.Finish();
        await FinishedAsync(registry, held.Id);

        var finished = Assert.Single(log.Containing(held.Id.ToString(), "Succeeded"));
        Assert.Equal(LogLevel.Information, finished.Level);
        Assert.Contains("Compact", finished.Message, StringComparison.Ordinal);
        Assert.Contains("3.5 s", finished.Message, StringComparison.Ordinal);
        Assert.Equal(2, log.Entries.Count);
    }

    [Fact]
    public async Task A_job_whose_work_threw_logs_an_error_with_the_exception()
    {
        var log = new RecordingDiagnosticLog();
        var registry = new JobRegistry(new ManualTimeProvider(), log);
        var held = new HeldJob(registry);
        await held.Context;
        var broken = new IOException("the disk went away");

        held.Throw(broken);
        await FinishedAsync(registry, held.Id);

        var finished = Assert.Single(log.Containing(held.Id.ToString(), "Failed"));
        Assert.Equal(LogLevel.Error, finished.Level);
        Assert.Contains("error.unknown", finished.Message, StringComparison.Ordinal);
        Assert.Contains("the disk went away", finished.Message, StringComparison.Ordinal);
        Assert.Same(broken, finished.Exception);
    }

    [Fact]
    public async Task A_cancelled_job_is_information_because_the_user_asked_for_it()
    {
        var log = new RecordingDiagnosticLog();
        var registry = new JobRegistry(new ManualTimeProvider(), log);
        var held = new HeldJob(registry);
        var context = await held.Context;

        registry.Cancel(held.Id);
        held.Throw(new OperationCanceledException(context.CancellationToken));
        await FinishedAsync(registry, held.Id);

        var finished = Assert.Single(log.Containing(held.Id.ToString(), "Cancelled"));
        Assert.Equal(LogLevel.Information, finished.Level);
        Assert.Null(finished.Exception);
    }

    [Fact]
    public async Task A_partial_job_is_a_warning_that_names_the_folders_that_failed_and_why()
    {
        var log = new RecordingDiagnosticLog();
        var registry = new JobRegistry(new ManualTimeProvider(), log);
        var held = new HeldJob(registry);
        await held.Context;
        var error = ErrorInfo.From(new DiskSpaceException("/x", 10, 1));

        held.Finish(new CaptureResult("p", null, [new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.External, error)]));
        await FinishedAsync(registry, held.Id);

        var finished = Assert.Single(log.Containing(held.Id.ToString(), "Partial"));
        Assert.Equal(LogLevel.Warning, finished.Level);
        Assert.Contains("External", finished.Message, StringComparison.Ordinal);
        Assert.Contains("error.diskSpace.low", finished.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Install", finished.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clear_that_left_files_behind_names_them()
    {
        var log = new RecordingDiagnosticLog();
        var registry = new JobRegistry(new ManualTimeProvider(), log);
        var held = new HeldJob(registry, JobKind.ClearData, null, cancellable: false);
        await held.Context;
        var error = ErrorInfo.From(new IOException("in use"));

        held.Finish(new ClearAllDataResult(10, [new ClearFailure("/catalog/games.json", error)]));
        await FinishedAsync(registry, held.Id);

        var finished = Assert.Single(log.Containing(held.Id.ToString(), "Partial"));
        Assert.Equal(LogLevel.Warning, finished.Level);
        Assert.Contains("/catalog/games.json", finished.Message, StringComparison.Ordinal);
    }
}
