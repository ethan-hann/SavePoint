using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Services.Jobs;

namespace Savepoint.Core.Tests.Services.Jobs;

public class JobOutcomeTests
{
    private static readonly ErrorInfo _diskFull = ErrorInfo.From(new DiskSpaceException("/x", 10, 1));
    private static readonly ErrorInfo _otherFailure = ErrorInfo.From(new InvalidOperationException("other"));
    private static readonly ErrorInfo _cancelled = ErrorInfo.From(new OperationCancelledByUserException(CancelledOperation.Capture, "g"));

    private static FolderOutcome Ok(RepoRole role) => new(role);

    private static FolderOutcome Failed(RepoRole role, ErrorInfo error) => new(role, error);

    private static CaptureResult Capture(params FolderOutcome[] folders) => new("p", new HistoryPointId("pt"), folders);

    [Fact]
    public void Work_with_no_result_succeeds()
    {
        Assert.Equal((JobStatus.Succeeded, (ErrorInfo?)null), JobOutcome.Judge(null));
    }

    [Fact]
    public void A_capture_where_every_folder_worked_succeeds()
    {
        var (status, error) = JobOutcome.Judge(Capture(Ok(RepoRole.Install), Ok(RepoRole.External)));

        Assert.Equal(JobStatus.Succeeded, status);
        Assert.Null(error);
    }

    [Fact]
    public void A_capture_with_no_folders_succeeds()
    {
        Assert.Equal(JobStatus.Succeeded, JobOutcome.Judge(Capture()).Status);
    }

    [Fact]
    public void A_capture_where_one_folder_failed_is_partial_and_never_success()
    {
        var (status, error) = JobOutcome.Judge(Capture(Ok(RepoRole.Install), Failed(RepoRole.External, _diskFull)));

        Assert.Equal(JobStatus.Partial, status);
        Assert.Null(error);
    }

    [Fact]
    public void A_capture_where_every_folder_failed_fails_with_the_first_error()
    {
        var (status, error) = JobOutcome.Judge(Capture(Failed(RepoRole.Install, _diskFull), Failed(RepoRole.External, _otherFailure)));

        Assert.Equal(JobStatus.Failed, status);
        Assert.Equal(_diskFull, error);
    }

    [Fact]
    public void A_capture_where_every_failure_is_a_cancellation_is_cancelled()
    {
        var (status, error) = JobOutcome.Judge(Capture(Failed(RepoRole.Install, _cancelled), Failed(RepoRole.External, _cancelled)));

        Assert.Equal(JobStatus.Cancelled, status);
        Assert.Equal(_cancelled, error);
    }

    [Fact]
    public void A_cancellation_mixed_with_a_real_failure_is_a_failure()
    {
        var (status, error) = JobOutcome.Judge(Capture(Failed(RepoRole.Install, _cancelled), Failed(RepoRole.External, _diskFull)));

        Assert.Equal(JobStatus.Failed, status);
        Assert.Equal(_cancelled, error);
    }

    [Fact]
    public void Every_kind_of_cancellation_message_counts_as_a_cancellation()
    {
        foreach (var op in Enum.GetValues<CancelledOperation>())
        {
            var error = ErrorInfo.From(new OperationCancelledByUserException(op, null));

            Assert.Equal(JobStatus.Cancelled, JobOutcome.Judge(Capture(Failed(RepoRole.Install, error))).Status);
        }
    }

    [Fact]
    public void Optimization_is_judged_across_the_folders_of_every_game()
    {
        var result = new OptimizeResult(
        [
            new GameOptimizeOutcome("a", [Ok(RepoRole.Install)], 10),
            new GameOptimizeOutcome("b", [Failed(RepoRole.Install, _otherFailure), Ok(RepoRole.External)], null),
        ]);

        Assert.Equal(JobStatus.Partial, JobOutcome.Judge(result).Status);
    }

    [Fact]
    public void Optimization_where_every_folder_of_every_game_failed_fails()
    {
        var result = new OptimizeResult(
        [
            new GameOptimizeOutcome("a", [Failed(RepoRole.Install, _diskFull)], null),
            new GameOptimizeOutcome("b", [Failed(RepoRole.Install, _otherFailure)], null),
        ]);

        var (status, error) = JobOutcome.Judge(result);

        Assert.Equal(JobStatus.Failed, status);
        Assert.Equal(_diskFull, error);
    }

    [Fact]
    public void Clearing_all_data_is_partial_when_something_was_left_behind()
    {
        var result = new ClearAllDataResult(100, [new ClearFailure("/left", _otherFailure)]);

        var (status, error) = JobOutcome.Judge(result);

        Assert.Equal(JobStatus.Partial, status);
        Assert.Null(error);
    }

    [Fact]
    public void Clearing_all_data_succeeds_when_nothing_was_left_behind()
    {
        Assert.Equal(JobStatus.Succeeded, JobOutcome.Judge(new ClearAllDataResult(100, [])).Status);
    }

    [Fact]
    public void A_storage_move_that_left_nothing_behind_succeeded()
    {
        Assert.Equal(JobStatus.Succeeded, JobOutcome.Judge(new StorageRelocationResult("/old", "/new", 5, [])).Status);
    }

    [Fact]
    public void A_storage_move_that_left_an_old_copy_behind_is_partial_with_no_error()
    {
        var left = new ClearFailure("/old/game-1/install.git", ErrorInfo.From(new InvalidOperationException("in use")));

        var (status, error) = JobOutcome.Judge(new StorageRelocationResult("/old", "/new", 5, [left]));

        Assert.Equal(JobStatus.Partial, status);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(JobKind.Launch, "error.launch.canceled")]
    [InlineData(JobKind.Capture, "error.capture.cancelled")]
    [InlineData(JobKind.Compact, "error.compaction.cancelled")]
    [InlineData(JobKind.RelocateStorage, "error.storageRelocation.cancelled")]
    public void A_cancelled_job_reports_the_cancelled_message_for_its_kind(JobKind kind, string messageId)
    {
        var error = JobOutcome.CancelledError(kind, "game-1");

        Assert.Equal(messageId, error.MessageId);
        Assert.Equal("game-1", error.Args["gameId"]);
    }

    [Fact]
    public void A_job_that_cannot_be_cancelled_has_no_cancelled_message()
    {
        Assert.Throws<InvalidOperationException>(() => JobOutcome.CancelledError(JobKind.ClearData, null));
    }
}
