using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

// "Take a snapshot now" copies what the game folders hold now, which is the loaded profile's files. It works on the loaded profile and on a
// profile with no snapshot yet; on any other profile it is refused with its own error before anything is written, and the row says so.
public class SnapshotNowTests
{
    private static async Task<(BackendWorkspace Workspace, GameDefinition Game, Profile Vanilla, Profile Modded)> TwoProfilesAsync()
    {
        var workspace = new BackendWorkspace();
        try
        {
            var game = await workspace.AddGameAsync();
            var (vanilla, _) = await workspace.CreateProfileAsync(game, "Vanilla");
            BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "mod.pak"), "a mod");
            var (modded, _) = await workspace.CreateProfileAsync(game, "Modded");
            await workspace.WaitForIdleAsync();
            return (workspace, game, vanilla, modded);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static async Task<IReadOnlyList<string?>> TipsAsync(BackendWorkspace workspace, GameDefinition game, Profile profile)
    {
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        var tips = new List<string?>();
        foreach (var role in new[] { RepoRole.Install, RepoRole.External })
        {
            tips.Add(await storage.Engine.GetBranchTipAsync(Repo(storage, game, role), profile.BranchName, workspace.Token));
        }

        return tips;
    }

    private static RepoRef Repo(StorageSnapshot storage, GameDefinition game, RepoRole role) =>
        new(storage.GitDirectory(game.Id, role), role == RepoRole.Install ? game.Install.Path : game.External!.Path, game.Id, role);

    private static async Task<string?> CheckedOutAsync(BackendWorkspace workspace, GameDefinition game, RepoRole role)
    {
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        var status = await storage.Engine.GetStatusAsync(Repo(storage, game, role), workspace.Token);
        return status.Branch;
    }

    [Fact(Timeout = 120_000)]
    public async Task The_loaded_profile_takes_a_snapshot_of_what_the_folders_hold_now()
    {
        var (workspace, game, _, modded) = await TwoProfilesAsync();
        using var _ = workspace;
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second save");

        var started = await workspace.Profiles.CaptureAsync(game.Id, modded.Id, "Now");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.True(started.Ok, started.Error?.Detail);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        var history = (await workspace.History.GetAsync(game.Id, modded.Id, workspace.Token)).Value!;
        Assert.Contains("Now", history.Points.Select(point => point.Name));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_profile_the_folders_do_not_hold_is_refused_with_its_own_error_before_anything_is_queued_or_written()
    {
        var (workspace, game, vanilla, modded) = await TwoProfilesAsync();
        using var _ = workspace;
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "what Vanilla would get a copy of");
        var vanillaTips = await TipsAsync(workspace, game, vanilla);
        var jobsBefore = workspace.Jobs.List().Count;

        var refused = await workspace.Profiles.CaptureAsync(game.Id, vanilla.Id, "Not Vanilla's");

        Assert.False(refused.Ok);
        Assert.Equal("error.profile.notLoaded", refused.Error.MessageId);
        Assert.Equal(vanilla.Id, refused.Error.Args["profileId"]);
        Assert.Equal("install", refused.Error.Args["role"]);
        Assert.Equal(jobsBefore, workspace.Jobs.List().Count);
        Assert.Equal(vanillaTips, await TipsAsync(workspace, game, vanilla));
        Assert.Equal(modded.BranchName, await CheckedOutAsync(workspace, game, RepoRole.Install));
        Assert.Equal(modded.BranchName, await CheckedOutAsync(workspace, game, RepoRole.External));
    }

    [Theory(Timeout = 120_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_snapshot_where_only_the_saves_folder_holds_another_profile_is_refused_and_writes_neither_folder(bool startedBehindTheApi)
    {
        var (workspace, game, vanilla, modded) = await TwoProfilesAsync();
        using var _ = workspace;

        // The install folder is on Modded, which is the profile asked for; the saves folder is on Vanilla's branch.
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        await storage.Engine.CheckoutBranchAsync(Repo(storage, game, RepoRole.External), vanilla.BranchName, ct: workspace.Token);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "more.pak"), "an install change that would be saved first");
        var moddedTips = await TipsAsync(workspace, game, modded);
        var services = workspace.Backend.Services;

        string? refusal;
        if (startedBehindTheApi)
        {
            // What a call that passed the Api's check and then waited for the game's lock does: the check is made again once the lock is held.
            var jobId = services.Capture.Start(game, modded, "Not Modded's", [RepoRole.Install, RepoRole.External], services.Capture.NewEvent(), creating: false);
            var job = await workspace.WaitForJobAsync(jobId);
            Assert.Equal(JobStatus.Failed, job.Status);
            refusal = job.Error?.MessageId;
        }
        else
        {
            var refused = await workspace.Profiles.CaptureAsync(game.Id, modded.Id, "Not Modded's");
            Assert.False(refused.Ok);
            refusal = refused.Error.MessageId;
        }

        Assert.Equal("error.profile.notLoaded", refusal);
        Assert.Equal(moddedTips, await TipsAsync(workspace, game, modded));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_folder_on_an_older_snapshot_is_refused_with_the_older_snapshot_error_before_the_other_folder_is_written()
    {
        var (workspace, game, _, modded) = await TwoProfilesAsync();
        using var _ = workspace;
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "more.pak"), "more");
        await RemovedWorld.CaptureAsync(workspace, game, modded, "Second", RepoRole.Install);
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        var install = Repo(storage, game, RepoRole.Install);
        var older = (await storage.Engine.ListCommitsAsync(install, modded.BranchName, ct: workspace.Token))[1].Id;
        await storage.Engine.CheckoutCommitAsync(install, older, ct: workspace.Token);
        var savesTip = (await TipsAsync(workspace, game, modded))[1];
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "new save");

        var refused = await workspace.Profiles.CaptureAsync(game.Id, modded.Id, "From an older snapshot");

        Assert.False(refused.Ok);
        Assert.Equal("error.launch.detachedCaptureNotSupported", refused.Error.MessageId);
        Assert.Equal(savesTip, (await TipsAsync(workspace, game, modded))[1]);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_profile_with_no_snapshot_yet_takes_its_first_snapshot_from_what_the_folders_hold_now()
    {
        var (workspace, game, vanilla, _) = await TwoProfilesAsync();
        using var _ = workspace;
        await TakeAllBranchesAwayAsync(workspace, game, vanilla);

        var started = await workspace.Profiles.CaptureAsync(game.Id, vanilla.Id, "First snapshot");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.True(started.Ok, started.Error?.Detail);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.All(await TipsAsync(workspace, game, vanilla), tip => Assert.NotNull(tip));
        var history = (await workspace.History.GetAsync(game.Id, vanilla.Id, workspace.Token)).Value!;
        Assert.Equal(["First snapshot"], history.Points.Select(point => point.Name));
        Assert.Equal(vanilla.BranchName, await CheckedOutAsync(workspace, game, RepoRole.Install));
    }

    // Leaves the profile in the catalog with no branch in either folder, as a profile is after the storage was started over (the folders hold another profile's files).
    private static async Task TakeAllBranchesAwayAsync(BackendWorkspace workspace, GameDefinition game, Profile profile)
    {
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        foreach (var role in new[] { RepoRole.Install, RepoRole.External })
        {
            await storage.Engine.DeleteBranchAsync(Repo(storage, game, role), profile.BranchName, workspace.Token);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task The_row_of_the_loaded_profile_says_a_snapshot_can_be_taken_even_with_new_changes_and_the_rows_of_the_others_say_it_cannot()
    {
        var (workspace, game, vanilla, modded) = await TwoProfilesAsync();
        using var _ = workspace;
        var duplicate = (await workspace.Profiles.DuplicateAsync(game.Id, modded.Id, "Copy")).Value!;
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "new changes");

        var rows = (await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token)).Value!;

        Assert.Equal(SnapshotNowAvailability.Available, rows.Single(row => row.ProfileId == modded.Id).SnapshotNow);

        // A duplicate starts at its source's snapshots, so it has branches, and it is not what the folders hold.
        Assert.Equal(SnapshotNowAvailability.NotLoaded, rows.Single(row => row.ProfileId == duplicate.Id).SnapshotNow);
        Assert.Equal(SnapshotNowAvailability.NotLoaded, rows.Single(row => row.ProfileId == vanilla.Id).SnapshotNow);
    }

    [Fact(Timeout = 120_000)]
    public async Task The_row_of_a_profile_with_no_snapshot_yet_says_the_first_one_is_what_the_folders_hold_now()
    {
        var (workspace, game, vanilla, modded) = await TwoProfilesAsync();
        using var _ = workspace;
        await TakeAllBranchesAwayAsync(workspace, game, vanilla);

        var rows = (await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token)).Value!;

        Assert.Equal(SnapshotNowAvailability.FirstSnapshot, rows.Single(row => row.ProfileId == vanilla.Id).SnapshotNow);
        Assert.Equal(SnapshotNowAvailability.Available, rows.Single(row => row.ProfileId == modded.Id).SnapshotNow);
    }

    [Fact(Timeout = 120_000)]
    public async Task The_row_of_a_profile_whose_saves_folder_has_no_snapshot_of_it_yet_says_that_folder_takes_a_first_snapshot_too()
    {
        var (workspace, game, vanilla, modded) = await TwoProfilesAsync();
        using var _ = workspace;
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        var saves = Repo(storage, game, RepoRole.External);

        // The saves folder sits on another profile's branch and the loaded profile has none there: its first snapshot would take that folder over.
        await storage.Engine.CheckoutBranchAsync(saves, vanilla.BranchName, ct: workspace.Token);
        await storage.Engine.DeleteBranchAsync(saves, modded.BranchName, workspace.Token);

        var rows = (await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token)).Value!;

        Assert.Equal(SnapshotNowAvailability.FirstSnapshot, rows.Single(row => row.ProfileId == modded.Id).SnapshotNow);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_folder_on_an_older_snapshot_makes_the_profile_not_loaded()
    {
        var (workspace, game, _, modded) = await TwoProfilesAsync();
        using var _ = workspace;
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "more.pak"), "more");
        await RemovedWorld.CaptureAsync(workspace, game, modded, "Second", RepoRole.Install);
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        var install = Repo(storage, game, RepoRole.Install);
        await storage.Engine.CheckoutCommitAsync(install, (await storage.Engine.ListCommitsAsync(install, modded.BranchName, ct: workspace.Token))[1].Id, ct: workspace.Token);

        var row = (await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token)).Value!.Single(stats => stats.ProfileId == modded.Id);

        Assert.Equal(SnapshotNowAvailability.NotLoaded, row.SnapshotNow);
    }
}
