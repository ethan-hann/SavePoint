using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Hosting;
using Savepoint.App.Localization;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Hosting;

public class NotificationTextTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en"), os: "windows");
    private static readonly DateTimeOffset _at = new(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_title_is_the_app_name()
    {
        var (title, _) = NotificationText.For(Job(JobKind.Capture, JobStatus.Succeeded), "Skyrim", _text);

        Assert.Equal("SavePoint", title);
    }

    [Fact]
    public void A_finished_save_names_the_game()
    {
        var (_, body) = NotificationText.For(Job(JobKind.Capture, JobStatus.Succeeded), "Skyrim", _text);

        Assert.Equal("Took a snapshot of Skyrim.", body);
    }

    [Fact]
    public void A_game_that_is_not_in_the_library_any_more_reads_as_this_game()
    {
        var (_, body) = NotificationText.For(Job(JobKind.Capture, JobStatus.Succeeded), null, _text);

        Assert.Equal("Took a snapshot of this game.", body);
    }

    [Theory]
    [InlineData(JobKind.Compact, JobStatus.Succeeded, "Freed up space for Skyrim.")]
    [InlineData(JobKind.Compact, JobStatus.Cancelled, "Canceled freeing up space for Skyrim.")]
    [InlineData(JobKind.RelocateStorage, JobStatus.Succeeded, "Snapshots moved.")]
    [InlineData(JobKind.RelocateStorage, JobStatus.Cancelled, "Canceled moving your snapshots.")]
    [InlineData(JobKind.ClearData, JobStatus.Succeeded, "SavePoint's data was cleared.")]
    public void Each_kind_and_ending_has_its_sentence(JobKind kind, JobStatus status, string expected)
    {
        var gameId = kind == JobKind.Compact ? "skyrim" : null;

        var (_, body) = NotificationText.For(Job(kind, status, gameId), "Skyrim", _text);

        Assert.Equal(expected, body);
    }

    [Fact]
    public void Optimizing_every_game_does_not_name_one()
    {
        var (_, body) = NotificationText.For(Job(JobKind.Compact, JobStatus.Succeeded), "Skyrim", _text);

        Assert.Equal("Freed up space for all your games.", body);
    }

    [Fact]
    public void A_partial_save_says_why_with_the_first_reason_a_folder_gave()
    {
        var failed = new FolderOutcome(RepoRole.External, new ErrorInfo("error.diskSpace.low", new Dictionary<string, object?>(), string.Empty));
        var result = new CaptureResult("p", null, [new FolderOutcome(RepoRole.Install), failed]);

        var (_, body) = NotificationText.For(Job(JobKind.Capture, JobStatus.Partial, "skyrim", result), "Skyrim", _text);

        Assert.StartsWith("Only part of Skyrim is in the new snapshot.", body, StringComparison.Ordinal);
        Assert.Contains("not enough free disk space", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_storage_move_that_left_old_copies_behind_says_so()
    {
        var stayed = new ClearFailure("/old/game-1/install.git", new ErrorInfo("error.gameService.gameRunning", new Dictionary<string, object?> { ["gameName"] = "Skyrim" }, string.Empty));
        var result = new StorageRelocationResult("/old", "/new", 5, [stayed]);

        var (_, body) = NotificationText.For(Job(JobKind.RelocateStorage, JobStatus.Partial, result: result), null, _text);

        Assert.StartsWith("Snapshots moved, but some old files are still in the old folder.", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_job_says_why_with_its_own_error()
    {
        var error = new ErrorInfo("error.storageRelocation.copyFailed", new Dictionary<string, object?>(), string.Empty);

        var (_, body) = NotificationText.For(Job(JobKind.RelocateStorage, JobStatus.Failed, error: error), null, _text);

        Assert.StartsWith("Something went wrong moving your snapshots.", body, StringComparison.Ordinal);
        Assert.Contains("couldn't copy everything", body, StringComparison.Ordinal);
    }

    private static JobSnapshot Job(JobKind kind, JobStatus status, string? gameId = null, JobResult? result = null, ErrorInfo? error = null) =>
        new(Guid.NewGuid(), kind, gameId, null, status, true, false, JobProgress.None, [], 0, _at, _at, result, error, 3);
}
