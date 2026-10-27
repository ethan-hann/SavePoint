using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Diagnostics;
using Savepoint.Core.Errors;
using Savepoint.Core.Services.Jobs;
using Savepoint.Testing;
using static Savepoint.Core.Tests.Services.Jobs.JobRegistryTestKit;

namespace Savepoint.Core.Tests.Services.Jobs;

// The finished jobs are kept in a small file, so Activity still lists them after SavePoint is closed and opened again. Only finished
// jobs are written, and the file never grows past the newest 100 or past 30 days.
public sealed class JobRegistryHistoryTests : IDisposable
{
    private readonly TempFolder _temp = new("savepoint-job-history-");
    private readonly ManualTimeProvider _clock = new();

    public void Dispose() => _temp.Dispose();

    private string HistoryPath => Path.Combine(_temp.Path, "settings", "activity-history.json");

    private JobRegistry NewRegistry(IDiagnosticLog? log) => new(_clock, log, HistoryPath);

    [Fact]
    public async Task A_finished_job_is_listed_by_a_registry_started_later_on_the_same_file()
    {
        var first = NewRegistry(null);
        var profile = new JobProfile("p1", "Modded");
        var id = first.Start(JobKind.Capture, "skyrim", cancellable: true, _ => Task.FromResult<JobResult?>(null), profile);
        var finished = await FinishedAsync(first, id);

        var second = NewRegistry(null);

        var restored = Assert.Single(second.List());
        Assert.Equal(id, restored.JobId);
        Assert.Equal(JobKind.Capture, restored.Kind);
        Assert.Equal("skyrim", restored.GameId);
        Assert.Equal(profile, restored.Profile);
        Assert.Equal(JobStatus.Succeeded, restored.Status);
        Assert.Equal(finished.StartedAt, restored.StartedAt);
        Assert.Equal(finished.FinishedAt, restored.FinishedAt);
        Assert.False(restored.Cancellable);
        Assert.False(restored.CancelRequested);
        Assert.Empty(restored.RecentLines);
        AssertSame(restored, second.Get(id));
        Assert.Empty(second.GetLines(id));
    }

    [Fact]
    public async Task The_file_is_written_before_the_job_reads_as_finished()
    {
        var registry = NewRegistry(null);
        var held = new HeldJob(registry);
        await held.Context;
        Assert.False(File.Exists(HistoryPath), "a job that was still running was already written");

        held.Finish();
        await FinishedAsync(registry, held.Id);

        Assert.True(File.Exists(HistoryPath), "a job that had finished was not yet in the file");
    }

    [Fact]
    public async Task A_job_still_running_is_not_in_the_file()
    {
        var first = NewRegistry(null);
        var done = await StartAndFinishAsync(first);
        var held = new HeldJob(first, gameId: "other");
        await held.Context;

        var second = NewRegistry(null);

        Assert.Equal([done.JobId], second.List().Select(job => job.JobId));
        held.Finish();
        await FinishedAsync(first, held.Id);
    }

    [Fact]
    public void A_job_the_file_names_as_running_is_never_restored_as_running()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
        File.WriteAllText(HistoryPath, $$"""{"version":1,"jobs":[{"id":"{{Guid.NewGuid()}}","kind":"Capture","gameId":"g","status":"Running","startedAt":"2026-10-20T11:00:00+00:00","finishedAt":"2026-10-20T11:30:00+00:00"}]}""");

        var registry = NewRegistry(null);

        Assert.Empty(registry.List());
    }

    [Fact]
    public async Task A_failed_job_keeps_what_went_wrong_in_the_restored_list()
    {
        var first = NewRegistry(null);
        var id = first.Start(JobKind.Switch, "g", cancellable: true, _ => throw new InvalidOperationException("the disk went away"));
        var failed = await FinishedAsync(first, id);

        var restored = Assert.Single(NewRegistry(null).List());

        Assert.Equal(JobStatus.Failed, restored.Status);
        Assert.Equal(failed.Error!.MessageId, restored.Error!.MessageId);
        Assert.Contains("the disk went away", restored.Error.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_canceled_job_is_restored_as_canceled()
    {
        var first = NewRegistry(null);
        var held = new HeldJob(first);
        await held.Context;
        first.Cancel(held.Id);
        held.Throw(new OperationCanceledException());
        var cancelled = await FinishedAsync(first, held.Id);

        var restored = Assert.Single(NewRegistry(null).List());

        Assert.Equal(JobStatus.Cancelled, cancelled.Status);
        Assert.Equal(JobStatus.Cancelled, restored.Status);
        Assert.Equal(cancelled.Error!.MessageId, restored.Error!.MessageId);
    }

    [Fact]
    public async Task A_partly_done_job_keeps_the_first_folder_that_failed_with_its_arguments()
    {
        var reason = new ErrorInfo("error.capture.folderLocked", new Dictionary<string, object?> { ["path"] = @"C:\Games\Grounded", ["count"] = 3L, ["flag"] = true }, "locked by another program");
        var result = new CaptureResult("p1", null, [new FolderOutcome(RepoRole.External), new FolderOutcome(RepoRole.Install, reason)]);
        var first = NewRegistry(null);
        var done = await StartAndFinishAsync(first, result);

        var restored = Assert.Single(NewRegistry(null).List());

        Assert.Equal(JobStatus.Partial, done.Status);
        Assert.Equal(JobStatus.Partial, restored.Status);
        Assert.Equal(reason, restored.Error);
    }

    [Fact]
    public void A_missing_file_starts_with_nothing_listed()
    {
        var log = new RecordingDiagnosticLog();

        var registry = NewRegistry(log);

        Assert.Empty(registry.List());
        Assert.Empty(log.Entries);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"version\":1,\"jobs\":\"nope\"}")]
    public void A_file_that_cannot_be_read_starts_with_nothing_listed_and_is_mentioned_in_the_log(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
        File.WriteAllText(HistoryPath, content);
        var log = new RecordingDiagnosticLog();

        var registry = NewRegistry(log);

        Assert.Empty(registry.List());
        Assert.Single(log.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task A_job_in_the_file_that_cannot_be_understood_is_skipped_and_the_others_stay()
    {
        var first = NewRegistry(null);
        var good = await StartAndFinishAsync(first);
        var text = File.ReadAllText(HistoryPath);
        var withBad = text.Replace("\"jobs\":[", "\"jobs\":[{\"id\":\"not-a-guid\",\"kind\":\"Nope\"},{\"kind\":5},", StringComparison.Ordinal);
        File.WriteAllText(HistoryPath, withBad);

        var registry = NewRegistry(null);

        Assert.Equal([good.JobId], registry.List().Select(job => job.JobId));
    }

    [Fact]
    public async Task A_new_finished_job_is_written_after_the_restored_ones()
    {
        var first = NewRegistry(null);
        var old = await StartAndFinishAsync(first);
        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = NewRegistry(null);

        var fresh = await StartAndFinishAsync(second);

        Assert.Equal([old.JobId, fresh.JobId], NewRegistry(null).List().Select(job => job.JobId));
    }

    [Fact]
    public async Task Each_game_still_keeps_only_its_five_newest_finished_jobs_after_a_restart()
    {
        var first = NewRegistry(null);
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add((await StartAndFinishAsync(first)).JobId);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        var second = NewRegistry(null);
        ids.Add((await StartAndFinishAsync(second)).JobId);

        Assert.Equal(ids.Skip(1), second.List().Select(job => job.JobId));
        Assert.Equal(ids.Skip(1), NewRegistry(null).List().Select(job => job.JobId));
    }

    [Fact]
    public async Task No_more_than_the_newest_100_finished_jobs_are_kept()
    {
        var registry = NewRegistry(null);
        var ids = new List<Guid>();
        for (var game = 0; game < 21; game++)
        {
            for (var i = 0; i < 5; i++)
            {
                var id = registry.Start(JobKind.Capture, $"game{game}", cancellable: true, _ => Task.FromResult<JobResult?>(null));
                ids.Add((await FinishedAsync(registry, id)).JobId);
                _clock.Advance(TimeSpan.FromSeconds(1));
            }
        }

        var restored = NewRegistry(null).List().Select(job => job.JobId).ToList();

        Assert.Equal(100, restored.Count);
        Assert.Equal(ids.Skip(5), restored);
    }

    [Fact]
    public async Task A_job_that_finished_more_than_30_days_ago_is_not_listed_after_a_restart()
    {
        var first = NewRegistry(null);
        await StartAndFinishAsync(first, null);
        _clock.Advance(TimeSpan.FromDays(20));
        var recent = await StartAndFinishAsync(first, null);
        _clock.Advance(TimeSpan.FromDays(11));

        var restored = NewRegistry(null).List().Select(job => job.JobId).ToList();

        Assert.Equal([recent.JobId], restored);
    }

    [Fact]
    public async Task A_job_that_finished_29_days_ago_is_still_listed_after_a_restart()
    {
        var first = NewRegistry(null);
        var done = await StartAndFinishAsync(first, null);
        _clock.Advance(TimeSpan.FromDays(29));

        Assert.Equal([done.JobId], NewRegistry(null).List().Select(job => job.JobId));
    }

    [Fact]
    public async Task A_file_that_cannot_be_written_is_mentioned_in_the_log_and_the_job_still_finishes()
    {
        var blocker = Path.Combine(_temp.Path, "a-file");
        File.WriteAllText(blocker, "in the way");
        var log = new RecordingDiagnosticLog();
        var registry = new JobRegistry(_clock, log, Path.Combine(blocker, "activity-history.json"));

        var done = await StartAndFinishAsync(registry);

        Assert.Equal(JobStatus.Succeeded, done.Status);
        Assert.Contains(log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("history", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Clearing_the_saved_history_removes_the_file_and_nothing_finished_before_it_is_written_again()
    {
        var registry = NewRegistry(null);
        var before = await StartAndFinishAsync(registry);

        registry.ClearSavedHistory();
        Assert.False(File.Exists(HistoryPath), "the file stayed after the history was cleared");
        var after = await StartAndFinishAsync(registry);

        Assert.Equal([after.JobId], NewRegistry(null).List().Select(job => job.JobId));
        Assert.Contains(before.JobId, registry.List().Select(job => job.JobId));
    }

    [Fact]
    public async Task A_job_that_clears_the_data_is_never_written()
    {
        var registry = NewRegistry(null);
        var id = registry.Start(JobKind.ClearData, null, cancellable: false, _ => Task.FromResult<JobResult?>(null));
        await FinishedAsync(registry, id);

        Assert.Empty(NewRegistry(null).List());
    }

    [Fact]
    public void Clearing_the_saved_history_of_a_registry_with_no_file_does_nothing()
    {
        var registry = new JobRegistry(_clock);

        registry.ClearSavedHistory();

        Assert.Empty(registry.List());
    }
}
