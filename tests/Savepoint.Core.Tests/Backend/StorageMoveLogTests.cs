using Savepoint.Core.Api;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Backend;

public class StorageMoveLogTests
{
    private readonly RecordingDiagnosticLog _log = new();

    [Fact(Timeout = 120_000)]
    public async Task A_move_logs_where_it_copies_from_and_to_then_that_it_switched_and_what_became_of_the_old_copies()
    {
        using var workspace = new BackendWorkspace(options => options with { Log = _log });
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var steps = _log.Entries.Where(entry => entry.Message.StartsWith("Storage move:", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, steps.Count);
        Assert.All(steps, step => Assert.Equal(LogLevel.Information, step.Level));
        Assert.Contains(workspace.DefaultStorageRoot, steps[0].Message, StringComparison.Ordinal);
        Assert.Contains(destination, steps[0].Message, StringComparison.Ordinal);
        Assert.Contains("copied and checked", steps[1].Message, StringComparison.Ordinal);
        Assert.Contains($"now using {destination}", steps[2].Message, StringComparison.Ordinal);
        Assert.Contains("0 could not be removed", steps[3].Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 120_000)]
    public async Task An_old_copy_that_stays_behind_is_a_warning_with_its_path_and_why()
    {
        using var workspace = new BackendWorkspace(options => options with { Log = _log });
        await workspace.AddGameAsync();
        var foreign = Path.Combine(workspace.DefaultStorageRoot, "foreign", "install.git");
        BackendWorkspace.WriteFile(Path.Combine(foreign, "family-photos.zip"), "files that are not SavePoint's");
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        await workspace.WaitForJobAsync(started.Value!.JobId);

        var stayed = Assert.Single(_log.Containing("Storage move:", "could not remove", foreign));
        Assert.Equal(LogLevel.Warning, stayed.Level);
        Assert.NotNull(stayed.Exception);
        Assert.Contains("1 could not be removed", Assert.Single(_log.Containing("Storage move:", "old copies")).Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_move_that_fails_says_so_with_the_exception_and_that_it_took_its_copy_back()
    {
        var disk = new IOException("the destination drive went away");
        using var workspace = new BackendWorkspace(options => options with
        {
            Log = _log,
            AfterRelocationCopy = (_, _) => throw disk,
        });
        await workspace.AddGameAsync();
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        var failed = Assert.Single(_log.Containing("Storage move:", "taking back", destination));
        Assert.Equal(LogLevel.Warning, failed.Level);
        Assert.Contains("the destination drive went away", failed.Exception?.ToString(), StringComparison.Ordinal);
        Assert.Empty(_log.Containing("Storage move:", "now using"));
    }
}
