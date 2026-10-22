using Savepoint.Core.Errors;
using Savepoint.Core.Services;
using Savepoint.Core.Tests.Backend;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Diagnostics;

public class ApiFailureLogTests
{
    [Fact]
    public async Task A_failure_SavePoint_raised_on_purpose_is_a_warning_with_its_id_message_and_exception()
    {
        var log = new RecordingDiagnosticLog();
        var raised = new GameNotFoundException("game-7");

        var result = await ApiGuard.RunAsync<int>(log, () => throw raised, CancellationToken.None, "FetchAsync");

        Assert.Equal("error.game.notFound", result.Error?.MessageId);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("FetchAsync", entry.Message, StringComparison.Ordinal);
        Assert.Contains("error.game.notFound", entry.Message, StringComparison.Ordinal);
        Assert.Contains(raised.Message, entry.Message, StringComparison.Ordinal);
        Assert.Same(raised, entry.Exception);
    }

    [Fact]
    public async Task An_exception_nobody_expected_is_an_error_that_keeps_the_exception_for_its_type_and_stack()
    {
        var log = new RecordingDiagnosticLog();
        var broken = new IOException("the disk went away");

        var result = await ApiGuard.RunAsync<int>(log, () => throw broken, CancellationToken.None, "FetchAsync");

        Assert.Equal("error.unknown", result.Error?.MessageId);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("FetchAsync", entry.Message, StringComparison.Ordinal);
        Assert.Contains("error.unknown", entry.Message, StringComparison.Ordinal);
        Assert.Contains("the disk went away", entry.Message, StringComparison.Ordinal);
        Assert.Same(broken, entry.Exception);
    }

    [Fact]
    public async Task A_caller_that_cancelled_is_noted_but_is_not_a_failure()
    {
        var log = new RecordingDiagnosticLog();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var result = await ApiGuard.RunAsync(log, () => Task.FromResult(1), cancelled.Token, "FetchAsync");

        Assert.Equal("error.operation.cancelled", result.Error?.MessageId);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("FetchAsync", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_call_that_works_writes_nothing()
    {
        var log = new RecordingDiagnosticLog();

        var result = await ApiGuard.RunAsync(log, () => Task.FromResult(1), CancellationToken.None, "FetchAsync");

        Assert.True(result.Ok);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task A_failed_call_on_the_real_backend_is_logged_under_the_name_of_the_member_called()
    {
        var log = new RecordingDiagnosticLog();
        using var workspace = new BackendWorkspace(options => options with { Log = log });

        var result = await workspace.Games.GetFolderStatusAsync("no-such-game", workspace.Token);

        Assert.Equal("error.game.notFound", result.Error?.MessageId);
        var entry = Assert.Single(log.Containing("GetFolderStatusAsync", "error.game.notFound"));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<GameNotFoundException>(entry.Exception);
    }
}
