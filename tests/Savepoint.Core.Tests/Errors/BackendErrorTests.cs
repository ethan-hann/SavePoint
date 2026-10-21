using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Errors;

public class BackendErrorTests
{
    [Fact]
    public void A_cancelled_token_maps_to_the_cancelled_read_message()
    {
        var info = ErrorInfo.From(new OperationCanceledException());

        Assert.Equal("error.operation.cancelled", info.MessageId);
    }

    [Fact]
    public void A_task_cancellation_maps_like_any_other_cancellation()
    {
        var info = ErrorInfo.From(new TaskCanceledException());

        Assert.Equal("error.operation.cancelled", info.MessageId);
    }

    [Fact]
    public void A_cancellation_the_user_asked_for_keeps_its_own_message()
    {
        var info = ErrorInfo.From(new OperationCancelledByUserException(CancelledOperation.Capture, "game-1"));

        Assert.Equal("error.capture.cancelled", info.MessageId);
    }

    [Fact]
    public void Launch_unavailable_has_its_own_message_and_no_args()
    {
        var info = ErrorInfo.From(new LaunchUnavailableException());

        Assert.Equal("error.launch.unavailable", info.MessageId);
        Assert.Empty(info.Args);
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
