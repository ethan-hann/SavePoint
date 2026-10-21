using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Backend;

public class RelocationTests
{
    [Fact]
    public async Task The_preview_says_what_will_move_and_that_nothing_stands_in_the_way()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var destination = Path.Combine(workspace.Root, "new-storage");

        var preview = (await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token)).Value!;

        Assert.Equal(workspace.DefaultStorageRoot, preview.CurrentRoot);
        Assert.Equal(destination, preview.NewRoot);
        Assert.Equal([game.Id], preview.Games.Select(g => g.GameId));
        Assert.Equal(BackendWorkspace.Snapshot(Path.Combine(workspace.DefaultStorageRoot, game.Id)).Count, preview.FileCount);
        Assert.True(preview.BytesToCopy > 0);
        Assert.Equal(preview.Games.Sum(g => g.Bytes), preview.BytesToCopy);
        Assert.True(preview.DestinationFreeBytes > 0);
        Assert.Null(preview.BlockedBy);
        Assert.False(Directory.Exists(destination), "a preview must not create the destination");
    }

    [Fact]
    public async Task A_destination_that_already_has_files_is_blocked()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();
        var destination = Path.Combine(workspace.Root, "busy");
        BackendWorkspace.WriteFile(Path.Combine(destination, "notes.txt"), "mine");

        var preview = (await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token)).Value!;

        Assert.Equal("error.storageRelocation.destinationNotEmpty", preview.BlockedBy?.MessageId);
    }

    [Fact]
    public async Task An_existing_empty_destination_is_fine()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();
        var destination = Path.Combine(workspace.Root, "empty");
        Directory.CreateDirectory(destination);

        Assert.Null((await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token)).Value!.BlockedBy);
    }

    [Fact]
    public async Task A_destination_inside_a_game_folder_is_blocked()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var preview = (await workspace.Settings.GetRelocationPreviewAsync(Path.Combine(game.Install.Path, "storage-here"), workspace.Token)).Value!;

        Assert.Equal("error.gameService.pathOverlap", preview.BlockedBy?.MessageId);
        Assert.Equal(game.Id, preview.BlockedBy!.Args["conflictingGameId"]);
    }

    [Fact]
    public async Task A_destination_that_holds_a_game_folder_is_blocked()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var preview = (await workspace.Settings.GetRelocationPreviewAsync(workspace.GamesRoot, workspace.Token)).Value!;

        Assert.Equal("error.gameService.pathOverlap", preview.BlockedBy?.MessageId);
        Assert.NotNull(game);
    }

    [Fact]
    public async Task A_destination_inside_the_current_storage_is_blocked_as_too_broad()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();

        var preview = (await workspace.Settings.GetRelocationPreviewAsync(Path.Combine(workspace.DefaultStorageRoot, "nested"), workspace.Token)).Value!;

        Assert.Equal("error.pathSafety.tier1Blocked", preview.BlockedBy?.MessageId);
    }

    [Fact]
    public async Task A_running_game_blocks_the_move()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        workspace.Sessions.Start(game.Id, "any");
        var destination = Path.Combine(workspace.Root, "new-storage");

        var preview = (await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token)).Value!;
        var started = await workspace.Settings.RelocateStorageAsync(destination);

        Assert.Equal("error.gameService.gameRunning", preview.BlockedBy?.MessageId);
        Assert.Equal("error.gameService.gameRunning", started.Error?.MessageId);
        Assert.Empty(workspace.Jobs.List());
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task A_destination_without_room_is_blocked()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();
        var destination = Path.Combine(workspace.Root, "tiny-volume");
        workspace.Disk.FreePerPath = path => path.StartsWith(destination, StringComparison.OrdinalIgnoreCase) ? 1024 : null;

        var preview = (await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token)).Value!;

        Assert.Equal("error.diskSpace.low", preview.BlockedBy?.MessageId);
    }

    [Fact]
    public async Task A_destination_that_is_not_a_path_cannot_even_be_previewed()
    {
        using var workspace = new BackendWorkspace();

        var preview = await workspace.Settings.GetRelocationPreviewAsync("relative/folder", workspace.Token);

        Assert.Equal("error.validation.invalidPayload", preview.Error?.MessageId);
    }

    [Fact]
    public async Task Moving_copies_every_file_exactly_switches_the_settings_and_keeps_the_game_working_from_the_new_place()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync();
        var oldGameFolder = Path.Combine(workspace.DefaultStorageRoot, game.Id);
        Directory.CreateDirectory(Path.Combine(oldGameFolder, "install.git", "an-empty-folder"));
        var before = BackendWorkspace.Snapshot(oldGameFolder);
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(JobKind.RelocateStorage, job.Kind);
        Assert.Null(job.GameId);
        var result = Assert.IsType<StorageRelocationResult>(job.Result);
        Assert.Equal(workspace.DefaultStorageRoot, result.PreviousRoot);
        Assert.Equal(destination, result.NewRoot);
        Assert.True(result.BytesCopied > 0);
        Assert.Equal(before, BackendWorkspace.Snapshot(Path.Combine(destination, game.Id)));
        Assert.True(Directory.Exists(Path.Combine(destination, game.Id, "install.git", "an-empty-folder")));
        Assert.Equal(destination, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
        Assert.Equal(destination, (await workspace.Backend.Services.Storage.GetAsync(workspace.Token)).Root);

        // The game carries on from the new place: history is intact and a new capture lands there, not in the old folder.
        Assert.Single((await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), "patched");
        var capture = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "After the move");
        Assert.Equal(JobStatus.Succeeded, (await workspace.WaitForJobAsync(capture.Value!.JobId)).Status);
        Assert.Equal(before, BackendWorkspace.Snapshot(oldGameFolder));
        Assert.Equal(2, (await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points.Count);
    }

    [Fact]
    public async Task The_old_copies_stay_where_they_were()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.True(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git")));
    }

    [Fact]
    public async Task A_move_survives_a_restart()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var destination = Path.Combine(workspace.Root, "new-storage");
        var started = await workspace.Settings.RelocateStorageAsync(destination);
        await workspace.WaitForJobAsync(started.Value!.JobId);

        var restarted = workspace.Restart();

        Assert.Equal(destination, (await restarted.Services.Storage.GetAsync(workspace.Token)).Root);
        Assert.Single((await restarted.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points);
    }

    [Fact]
    public async Task Other_folders_in_the_storage_root_are_not_taken_along_but_leftover_savepoint_history_is()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        BackendWorkspace.WriteFile(Path.Combine(workspace.DefaultStorageRoot, "my-documents", "notes.txt"), "not savepoint's");
        var leftover = Path.Combine(workspace.DefaultStorageRoot, "removed-game");
        Directory.Move(Path.Combine(workspace.DefaultStorageRoot, game.Id), leftover);
        var game2 = await workspace.AddGameAsync(workspace.NewGame("Beta"));
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.True(Directory.Exists(Path.Combine(destination, game2.Id, "install.git")));
        Assert.True(Directory.Exists(Path.Combine(destination, "removed-game", "install.git")));
        Assert.False(Directory.Exists(Path.Combine(destination, "my-documents")));
    }

    [Fact]
    public async Task A_copy_that_fails_leaves_the_old_storage_in_use_and_the_destination_clean()
    {
        var copied = 0;
        using var workspace = new BackendWorkspace(options => options with
        {
            AfterRelocationCopy = (_, _) =>
            {
                if (Interlocked.Increment(ref copied) == 3)
                {
                    throw new IOException("the disk went away");
                }
            },
        });
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync();
        var before = BackendWorkspace.Snapshot(Path.Combine(workspace.DefaultStorageRoot, game.Id));
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.storageRelocation.copyFailed", job.Error?.MessageId);
        Assert.False(Directory.Exists(destination), "what was copied must be taken back");
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Backend.Services.Storage.GetAsync(workspace.Token)).Root);
        Assert.Equal(before, BackendWorkspace.Snapshot(Path.Combine(workspace.DefaultStorageRoot, game.Id)));
        Assert.Single((await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points);
    }

    [Fact]
    public async Task A_copy_that_does_not_match_its_source_is_caught_before_anything_switches()
    {
        using var workspace = new BackendWorkspace(options => options with
        {
            AfterRelocationCopy = (_, destination) =>
            {
                if (Path.GetFileName(destination) == "HEAD")
                {
                    File.WriteAllText(destination, "ref: refs/heads/tampered\n");
                }
            },
        });
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

    [Fact]
    public async Task Cancelling_a_move_leaves_the_old_storage_in_use_and_the_destination_clean()
    {
        var reached = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        using var workspace = new BackendWorkspace(options => options with
        {
            AfterRelocationCopy = (_, _) =>
            {
                reached.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            },
        });
        var game = await workspace.AddGameAsync();
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        await workspace.Jobs.CancelAsync(started.Value!.JobId);
        release.Set();
        var job = await workspace.WaitForJobAsync(started.Value.JobId);

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal("error.storageRelocation.cancelled", job.Error?.MessageId);
        Assert.False(Directory.Exists(destination));
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Backend.Services.Storage.GetAsync(workspace.Token)).Root);
        Assert.True(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git")));
    }

    [Fact]
    public async Task A_move_reports_files_and_bytes_as_it_goes()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.NotNull(job.Progress.FilesTotal);
        Assert.Equal(job.Progress.FilesTotal, job.Progress.FilesDone);
        Assert.Equal(job.Progress.BytesTotal, job.Progress.BytesDone);
    }

    [Fact]
    public async Task A_symbolic_link_inside_the_storage_is_copied_as_a_link_and_not_followed()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var outside = Path.Combine(workspace.Root, "outside-target.txt");
        await File.WriteAllTextAsync(outside, "the target", workspace.Token);
        var link = Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git", "a-link");
        try
        {
            File.CreateSymbolicLink(link, outside);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip("this account cannot create symbolic links");
        }

        var destination = Path.Combine(workspace.Root, "new-storage");
        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        var copy = new FileInfo(Path.Combine(destination, game.Id, "install.git", "a-link"));
        Assert.Equal(outside, copy.LinkTarget);
    }

    [Fact]
    public async Task A_second_move_started_during_the_first_waits_and_then_moves_from_where_the_first_arrived()
    {
        var reached = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        using var workspace = new BackendWorkspace(options => options with
        {
            AfterRelocationCopy = (_, _) =>
            {
                reached.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            },
        });
        var game = await workspace.AddGameAsync();
        var before = BackendWorkspace.Snapshot(Path.Combine(workspace.DefaultStorageRoot, game.Id));
        var first = await workspace.Settings.RelocateStorageAsync(Path.Combine(workspace.Root, "first"));
        Assert.True(reached.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        var second = await workspace.Settings.RelocateStorageAsync(Path.Combine(workspace.Root, "second"));

        release.Set();
        var firstJob = await workspace.WaitForJobAsync(first.Value!.JobId);
        var secondJob = await workspace.WaitForJobAsync(second.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, firstJob.Status);
        Assert.Equal(JobStatus.Succeeded, secondJob.Status);
        Assert.Equal(Path.Combine(workspace.Root, "second"), (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
        Assert.Equal(before, BackendWorkspace.Snapshot(Path.Combine(workspace.Root, "second", game.Id)));
    }
}
