using Savepoint.Core.Errors;
using Savepoint.Core.Services;

namespace Savepoint.Core.Tests.Errors;

public class BackendErrorTests
{
    [Fact]
    public async Task A_call_whose_token_was_cancelled_answers_the_cancelled_message()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var result = await ApiGuard.RunAsync(() => Task.FromResult(1), cancelled.Token);

        Assert.Equal("error.operation.cancelled", result.Error?.MessageId);
    }

    [Fact]
    public async Task A_cancellation_raised_while_the_work_ran_answers_the_cancelled_message_too()
    {
        using var source = new CancellationTokenSource();

        var result = await ApiGuard.RunAsync<int>(
            async () =>
            {
                await source.CancelAsync();
                source.Token.ThrowIfCancellationRequested();
                return 1;
            },
            source.Token);

        Assert.Equal("error.operation.cancelled", result.Error?.MessageId);
    }

    [Fact]
    public async Task A_cancellation_nobody_asked_for_is_an_unknown_failure_not_a_cancelled_one()
    {
        var result = await ApiGuard.RunAsync<int>(() => throw new OperationCanceledException("a timeout inside"), CancellationToken.None);

        Assert.Equal("error.unknown", result.Error?.MessageId);
    }

    [Fact]
    public async Task A_cancellation_the_user_asked_for_keeps_its_own_message()
    {
        var result = await ApiGuard.RunAsync<int>(() => throw new OperationCancelledByUserException(CancelledOperation.Capture, "game-1"), CancellationToken.None);

        Assert.Equal("error.capture.cancelled", result.Error?.MessageId);
    }

    [Fact]
    public async Task An_exception_never_crosses_the_boundary()
    {
        var result = await ApiGuard.RunAsync<int>(() => throw new IOException("the disk went away"), CancellationToken.None);

        Assert.Equal("error.unknown", result.Error?.MessageId);
        Assert.Contains("the disk went away", result.Error!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreachable_storage_folder_names_its_path()
    {
        var info = ErrorInfo.From(new StorageUnreachableException("/storage", new IOException("gone")));

        Assert.Equal("error.storage.unreachable", info.MessageId);
        Assert.Equal("/storage", info.Args["path"]);
    }

    [Fact]
    public void A_failed_copy_names_the_file_it_stopped_on()
    {
        var info = ErrorInfo.From(new StorageRelocationFailedException("/new/a.pack", "size differs", null));

        Assert.Equal("error.storageRelocation.copyFailed", info.MessageId);
        Assert.Equal("/new/a.pack", info.Args["path"]);
    }
}
