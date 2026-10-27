using Savepoint.Core.Api;
using Savepoint.Core.Services.Settings;
using Savepoint.Core.Tests.Git.Engine;

namespace Savepoint.Core.Tests.Backend;

// What a storage move does after the new copy is verified: the switch is the commit point, the old copies are removed whatever happens
// next and reported when they stay, and a destination an unfinished move wrote into is refused (D31).
public class RelocationLeftoversTests
{
    // The move has reached its wait for the reader before it is cancelled. The job shows no sign of that wait, so a pause is the only way to order the two.
    private static readonly TimeSpan _reachSwitchWait = TimeSpan.FromMilliseconds(300);

    [Fact(Timeout = 120_000)]
    public async Task Cancelling_once_the_move_has_switched_does_not_stop_the_removal_of_the_other_old_copies()
    {
        using var workspace = new BackendWorkspace();
        HeldFiles.SkipUnlessPermissionsHold(workspace.Root);
        var first = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var second = await workspace.AddGameAsync(workspace.NewGame("Beta"));
        await workspace.CreateProfileAsync(first);
        await workspace.CreateProfileAsync(second);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var held = Path.Combine(workspace.DefaultStorageRoot, first.Id, "install.git", "HEAD");
        var destination = Path.Combine(workspace.Root, "new-storage");

        // Readable by the copy, but not deletable by anyone until it is let go; the removal waits on it before giving up.
        JobSnapshot job;
        using (HeldFiles.PreventDeleting(held))
        {
            var started = await workspace.Settings.RelocateStorageAsync(destination);
            await WaitForSwitchAsync(workspace, destination);
            await workspace.Jobs.CancelAsync(started.Value!.JobId);
            job = await workspace.WaitForJobAsync(started.Value.JobId);
        }

        Assert.Equal(JobStatus.Partial, job.Status);
        var left = Assert.IsType<StorageRelocationResult>(job.Result).NotRemoved;
        Assert.Equal(Path.Combine(workspace.DefaultStorageRoot, first.Id, "install.git"), Assert.Single(left).Path);
        Assert.False(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, second.Id)));
        Assert.Equal(destination, (await workspace.Backend.Services.Storage.GetAsync(workspace.Token)).Root);
        Assert.True(Directory.Exists(Path.Combine(destination, first.Id, "install.git")));
        Assert.True(Directory.Exists(Path.Combine(destination, second.Id, "install.git")));
    }

    [Fact(Timeout = 120_000)]
    public async Task Cancelling_while_the_move_waits_for_the_exclusive_gate_after_the_last_copy_leaves_everything_as_it_was()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var destination = Path.Combine(workspace.Root, "new-storage");

        // A reader holds the storage, so the switch cannot start once every file is copied and checked.
        var lease = await workspace.Backend.Services.Storage.Gate.EnterSharedAsync(workspace.Token);
        JobSnapshot job;
        try
        {
            var started = await workspace.Settings.RelocateStorageAsync(destination);
            await WaitForEveryFileAsync(workspace, started.Value!.JobId);
            await Task.Delay(_reachSwitchWait, workspace.Token);
            await workspace.Jobs.CancelAsync(started.Value.JobId);

            // The cancel has to end the wait by itself; the reader is let go only when it has not.
            var finished = workspace.WaitForJobAsync(started.Value.JobId);
            await Task.WhenAny(finished, Task.Delay(TimeSpan.FromSeconds(5), workspace.Token));
            lease.Dispose();
            job = await finished;
        }
        finally
        {
            lease.Dispose();
        }

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.False(Directory.Exists(destination), "what was copied must be taken back");
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Backend.Services.Storage.GetAsync(workspace.Token)).Root);
        Assert.True(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git")));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_destination_an_unfinished_move_wrote_into_is_refused_with_its_own_message()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();
        var destination = Path.Combine(workspace.Root, "half-copied");
        BackendWorkspace.WriteFile(Path.Combine(destination, StorageRelocator.MarkerFileName), "an earlier move was here");
        BackendWorkspace.WriteFile(Path.Combine(destination, "some-game", "install.git", "HEAD"), "ref: refs/heads/x\n");

        var preview = (await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token)).Value!;
        var started = await workspace.Settings.RelocateStorageAsync(destination);

        Assert.Equal("error.storageRelocation.moveUnfinished", preview.BlockedBy?.MessageId);
        Assert.Equal(destination, preview.BlockedBy!.Args["path"]);
        Assert.Equal("error.storageRelocation.moveUnfinished", started.Error?.MessageId);
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Backend.Services.Storage.GetAsync(workspace.Token)).Root);
    }

    [Fact(Timeout = 120_000)]
    public async Task The_destination_carries_a_marker_while_the_copy_runs_and_not_after_the_move_ends()
    {
        var destination = Path.Combine(Path.GetTempPath(), "savepoint-marker-" + Guid.NewGuid().ToString("N"));
        var markerSeen = new List<bool>();
        using var workspace = new BackendWorkspace(options => options with
        {
            AfterRelocationCopy = (_, _) => markerSeen.Add(File.Exists(Path.Combine(destination, StorageRelocator.MarkerFileName))),
        });
        try
        {
            await workspace.AddGameAsync();

            var started = await workspace.Settings.RelocateStorageAsync(destination);
            var job = await workspace.WaitForJobAsync(started.Value!.JobId);

            Assert.Equal(JobStatus.Succeeded, job.Status);
            Assert.NotEmpty(markerSeen);
            Assert.All(markerSeen, Assert.True);
            Assert.False(File.Exists(Path.Combine(destination, StorageRelocator.MarkerFileName)));
        }
        finally
        {
            Savepoint.Core.Tests.Git.GitWorkspace.DeleteDirectory(destination);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task A_settings_write_that_failed_after_it_took_effect_completes_the_move_instead_of_undoing_it()
    {
        using var workspace = new BackendWorkspace(options => options with
        {
            AfterRelocationSettings = _ => throw new IOException("the catalog folder was locked right after the rename"),
        });
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(destination, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
        Assert.Equal(destination, (await workspace.Backend.Services.Storage.GetAsync(workspace.Token)).Root);
        Assert.True(Directory.Exists(Path.Combine(destination, game.Id, "install.git")));
        Assert.Single((await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_settings_write_that_failed_without_taking_effect_still_takes_the_copy_back()
    {
        BackendWorkspace? self = null;
        using var workspace = new BackendWorkspace(options => options with
        {
            AfterRelocationSettings = _ =>
            {
                // The write had not applied after all: the stored root is still the old one.
                var old = self!.DefaultStorageRoot;
                Task.Run(() => self.Backend.Services.Catalog.UpdateSettingsAsync(current => current! with { StorageRoot = old })).GetAwaiter().GetResult();
                throw new IOException("the write failed");
            },
        });
        self = workspace;
        var game = await workspace.AddGameAsync();
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.storageRelocation.copyFailed", job.Error?.MessageId);
        Assert.False(Directory.Exists(destination));
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Backend.Services.Storage.GetAsync(workspace.Token)).Root);
        Assert.True(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git")));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_take_back_that_cannot_remove_the_whole_copy_leaves_the_marker_so_the_next_move_there_says_the_earlier_one_did_not_finish()
    {
        HeldFiles.SkipUnlessPermissionsHold(Path.GetTempPath());
        BackendWorkspace? self = null;
        IDisposable? hold = null;
        using var workspace = new BackendWorkspace(options => options with
        {
            // The first git file copied cannot be deleted afterwards: held open on Windows, in a folder nobody may write to elsewhere.
            AfterRelocationCopy = (_, target) => hold ??= Path.GetFileName(target) == "HEAD" ? HeldFiles.PreventDeleting(target) : null,
            AfterRelocationSettings = _ =>
            {
                var old = self!.DefaultStorageRoot;
                Task.Run(() => self.Backend.Services.Catalog.UpdateSettingsAsync(current => current! with { StorageRoot = old })).GetAwaiter().GetResult();
                throw new IOException("the write failed");
            },
        });
        self = workspace;
        await workspace.AddGameAsync();
        var destination = Path.Combine(workspace.Root, "new-storage");

        try
        {
            var started = await workspace.Settings.RelocateStorageAsync(destination);
            var job = await workspace.WaitForJobAsync(started.Value!.JobId);
            var next = (await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token)).Value!;

            Assert.Equal(JobStatus.Failed, job.Status);
            Assert.True(File.Exists(Path.Combine(destination, StorageRelocator.MarkerFileName)), "the marker must stay while part of the copy does");
            Assert.Equal("error.storageRelocation.moveUnfinished", next.BlockedBy?.MessageId);
        }
        finally
        {
            hold?.Dispose();
        }
    }

    private static async Task WaitForEveryFileAsync(BackendWorkspace workspace, Guid jobId)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (workspace.Jobs.Get(jobId)?.Progress is not { FilesTotal: > 0 } progress || progress.FilesDone < progress.FilesTotal)
        {
            Assert.True(DateTime.UtcNow < until, "the move never copied every file");
            await Task.Delay(5, workspace.Token);
        }
    }

    private static async Task WaitForSwitchAsync(BackendWorkspace workspace, string destination)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!string.Equals((await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot, destination, StringComparison.Ordinal))
        {
            Assert.True(DateTime.UtcNow < until, "the move never switched to the new folder");
            await Task.Delay(5, workspace.Token);
        }
    }
}
