using Savepoint.Core.Api;
using Savepoint.Core.Tests.Git.Engine;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Backend;

public class ClearAllLogTests
{
    private readonly RecordingDiagnosticLog _log = new();

    [Fact(Timeout = 120_000)]
    public async Task A_clear_logs_each_step_in_the_order_it_takes_them()
    {
        using var workspace = new BackendWorkspace(options => options with { Log = _log });
        var game = await workspace.AddGameAsync();

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var steps = _log.Entries.Where(entry => entry.Message.StartsWith("Clear all data:", StringComparison.Ordinal)).ToList();
        Assert.All(steps, step => Assert.Equal(LogLevel.Information, step.Level));
        int At(string fragment) => steps.FindIndex(step => step.Message.Contains(fragment, StringComparison.Ordinal));
        Assert.Equal(0, At("starting"));
        Assert.True(At($"history of game {game.Id}") > At("starting"));
        Assert.True(At("leftover history folders") > At($"history of game {game.Id}"));
        Assert.True(At("catalog files") > At("leftover history folders"));
        Assert.True(At("artwork") > At("catalog files"));
        Assert.True(At("log folder") > At("artwork"));
        Assert.Equal(steps.Count - 1, At("finished; 0 items"));
    }

    [Fact(Timeout = 120_000)]
    public async Task Something_that_cannot_be_removed_is_a_warning_with_its_path_why_and_the_exception()
    {
        using var workspace = new BackendWorkspace(options => options with { Log = _log });
        await workspace.AddGameAsync();
        var lookalike = Path.Combine(workspace.DefaultStorageRoot, "lookalike", "install.git");
        BackendWorkspace.WriteFile(Path.Combine(lookalike, "family-photos.zip"), "files that are not SavePoint's");

        var started = await workspace.Settings.ClearAllDataAsync();
        await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));

        var stayed = Assert.Single(_log.Containing("Clear all data:", "could not remove", lookalike));
        Assert.Equal(LogLevel.Warning, stayed.Level);
        Assert.NotNull(stayed.Exception);
        Assert.Single(_log.Containing("finished; 1 items"));
    }

    [Fact(Timeout = 120_000)]
    public async Task The_log_folder_is_emptied_except_the_file_in_use()
    {
        var home = Path.Combine(Path.GetTempPath(), "savepoint-clearlog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var platform = Savepoint.Core.Tests.Platform.FakePlatforms.ForHost(home);
            var logs = platform.Directories.LogsDir;
            Assert.StartsWith(home, logs, StringComparison.Ordinal);
            var inUse = Path.Combine(logs, "savepoint-20261021.log");
            BackendWorkspace.WriteFile(inUse, "today");
            BackendWorkspace.WriteFile(Path.Combine(logs, "savepoint-20261020.log"), "yesterday");
            BackendWorkspace.WriteFile(Path.Combine(logs, "savepoint-20261020_001.log"), "yesterday, rolled");
            BackendWorkspace.WriteFile(Path.Combine(logs, "sent-to-support", "copy.log"), "a copy the user made");
            using var workspace = new BackendWorkspace(options => options with { Platform = platform, Log = new RecordingDiagnosticLog { CurrentFile = inUse } });

            var started = await workspace.Settings.ClearAllDataAsync();
            var job = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));

            Assert.Equal(JobStatus.Succeeded, job.Status);
            Assert.Empty(Assert.IsType<ClearAllDataResult>(job.Result).NotRemoved);
            Assert.Equal([inUse], Directory.EnumerateFileSystemEntries(logs));
            Assert.Equal("today", await File.ReadAllTextAsync(inUse, workspace.Token));
        }
        finally
        {
            Savepoint.Core.Tests.Git.GitWorkspace.DeleteDirectory(home);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task A_log_that_writes_no_file_leaves_nothing_to_spare()
    {
        using var workspace = new BackendWorkspace();
        var logs = workspace.Platform.Directories.LogsDir;
        Assert.StartsWith(workspace.Root, logs, StringComparison.Ordinal);
        BackendWorkspace.WriteFile(Path.Combine(logs, "savepoint-20261020.log"), "yesterday");

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(logs));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_log_file_that_cannot_be_removed_is_listed_like_any_other_item()
    {
        using var workspace = new BackendWorkspace();
        HeldFiles.SkipUnlessPermissionsHold(workspace.Root);
        var logs = workspace.Platform.Directories.LogsDir;
        Assert.StartsWith(workspace.Root, logs, StringComparison.Ordinal);
        var held = Path.Combine(logs, "savepoint-20261020.log");
        BackendWorkspace.WriteFile(held, "yesterday");
        using var open = HeldFiles.PreventDeleting(held);

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));

        Assert.Equal(JobStatus.Partial, job.Status);
        Assert.Equal(held, Assert.Single(Assert.IsType<ClearAllDataResult>(job.Result).NotRemoved).Path);
    }
}
