using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

// A recently removed snapshot comes back as a new profile: its folders become the new profile's history, and the profile it came from is not touched.
public class BringBackTests
{
    [Fact(Timeout = 120_000)]
    public async Task A_snapshot_a_save_replaced_comes_back_as_a_profile_whose_history_is_that_snapshot_and_the_old_profile_is_not_touched()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ReplaceLatestAsync("Replaced");
        var installBefore = await world.CommitsAsync(world.Profile.BranchName, RepoRole.Install);
        var savesBefore = await world.CommitsAsync(world.Profile.BranchName, RepoRole.External);

        var brought = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.InstallOnly, "Old install");

        Assert.True(brought.Ok, brought.Error?.Detail);
        var profile = brought.Value;
        Assert.Equal("Old install", profile.Name);
        Assert.Equal(world.Game.Id, profile.GameId);
        Assert.Contains(profile.Id, (await world.Workspace.Profiles.ListAsync(world.Game.Id, world.Workspace.Token)).Value!.Select(p => p.Id));

        // The install folder starts where the removed commit was; the saves folder, which the snapshot did not record, at that profile's latest commit before it.
        var install = await world.CommitsAsync(profile.BranchName, RepoRole.Install);
        Assert.Equal(["Added a mod", "Vanilla"], install.Select(commit => commit.Subject));
        var saves = await world.CommitsAsync(profile.BranchName, RepoRole.External);
        Assert.Equal(["Vanilla"], saves.Select(commit => commit.Subject));

        // Nothing of the profile it came from moved.
        Assert.Equal(installBefore, await world.CommitsAsync(world.Profile.BranchName, RepoRole.Install));
        Assert.Equal(savesBefore, await world.CommitsAsync(world.Profile.BranchName, RepoRole.External));
    }

    [Fact(Timeout = 120_000)]
    public async Task The_new_profile_owns_the_snapshot_in_History_and_the_snapshot_leaves_the_recently_removed_list()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();

        var brought = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.InstallOnly, "Modded again");

        Assert.True(brought.Ok, brought.Error?.Detail);
        var history = (await world.Workspace.History.GetAsync(world.Game.Id, brought.Value.Id, world.Workspace.Token)).Value!;
        Assert.Equal(["Added a mod"], history.Points.Select(point => point.Name));
        Assert.Equal(world.InstallOnly, history.Points.Single().Id);
        Assert.Equal([world.SavesOnly], (await world.ListAsync()).Select(snapshot => snapshot.Id));
        var stats = (await world.Workspace.Profiles.GetRowStatsAsync(world.Game.Id, world.Workspace.Token)).Value!;
        Assert.Equal(1, stats.Single(row => row.ProfileId == brought.Value.Id).SnapshotCount);
        Assert.Equal(1, stats.Single(row => row.ProfileId == world.Profile.Id).SnapshotCount);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_snapshot_that_recorded_only_saves_starts_its_install_at_the_latest_install_taken_off_the_profile_before_it()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        var removedInstall = (await world.RecordedCommitsAsync(RepoRole.Install)).Single();

        var brought = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.SavesOnly, "Second save");

        Assert.True(brought.Ok, brought.Error?.Detail);
        Assert.Equal(removedInstall, await world.BranchTipAsync(brought.Value.BranchName, RepoRole.Install));
        var saves = await world.CommitsAsync(brought.Value.BranchName, RepoRole.External);
        Assert.Equal(["Second save", "Vanilla"], saves.Select(commit => commit.Subject));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_second_snapshot_brought_back_starts_its_install_at_the_one_it_was_taken_with_even_after_the_first_was_brought_back()
    {
        using var world = await RemovedWorld.CreateAsync();
        var installWhenTheSavesWereTaken = (await world.CommitsAsync(world.Profile.BranchName, RepoRole.Install)).Single(commit => commit.CaptureId == world.InstallOnly.Value).Id;
        await world.ContinueFromFirstAsync();

        var mods = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.InstallOnly, "Mods");
        var saves = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.SavesOnly, "Saves");

        Assert.True(mods.Ok, mods.Error?.Detail);
        Assert.True(saves.Ok, saves.Error?.Detail);
        Assert.Equal(installWhenTheSavesWereTaken, await world.BranchTipAsync(mods.Value.BranchName, RepoRole.Install));
        Assert.Equal(installWhenTheSavesWereTaken, await world.BranchTipAsync(saves.Value.BranchName, RepoRole.Install));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_snapshot_every_folder_of_which_a_profile_holds_again_is_refused_as_not_removed()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        Assert.True((await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.InstallOnly, "Mods")).Ok);

        var again = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.InstallOnly, "Mods again");

        Assert.False(again.Ok);
        Assert.Equal("error.removed.notFound", again.Error.MessageId);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_saves_only_snapshot_still_starts_at_the_install_it_was_taken_with_after_that_install_became_another_profile_and_the_profile_both_came_from_was_deleted()
    {
        using var world = await RemovedWorld.CreateAsync();
        var installWhenTheSavesWereTaken = (await world.CommitsAsync(world.Profile.BranchName, RepoRole.Install)).Single(commit => commit.CaptureId == world.InstallOnly.Value).Id;
        await world.ContinueFromFirstAsync();
        await world.Workspace.CreateProfileAsync(world.Game, "Other");
        await world.Workspace.WaitForIdleAsync();
        Assert.True((await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.InstallOnly, "Mods")).Ok);
        Assert.True((await world.Workspace.Profiles.DeleteAsync(world.Game.Id, world.Profile.Id)).Ok);
        await world.Workspace.WaitForIdleAsync();

        var brought = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.SavesOnly, "Saves");

        Assert.True(brought.Ok, brought.Error?.Detail);
        Assert.Equal(installWhenTheSavesWereTaken, await world.BranchTipAsync(brought.Value.BranchName, RepoRole.Install));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_snapshot_with_no_install_to_start_from_is_refused_and_nothing_is_made()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueSavesFromFirstAsync();
        await world.Workspace.CreateProfileAsync(world.Game, "Other");
        await world.Workspace.WaitForIdleAsync();

        // The profile the saves came from is deleted and the install folder never recorded a commit of it, so nothing says which install the saves-only snapshot was played with.
        Assert.True((await world.Workspace.Profiles.DeleteAsync(world.Game.Id, world.Profile.Id)).Ok);
        await world.Workspace.WaitForIdleAsync();
        var profilesBefore = (await world.Workspace.Profiles.ListAsync(world.Game.Id, world.Workspace.Token)).Value!.Select(profile => profile.Id).ToList();
        var branchesBefore = await BranchesAsync(world, RepoRole.External);

        var refused = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.SavesOnly, "Saves");

        Assert.False(refused.Ok);
        Assert.Equal("error.removed.notRestorable", refused.Error.MessageId);
        Assert.Equal(world.SavesOnly.Value, refused.Error.Args["snapshotId"]);
        Assert.Equal(profilesBefore, (await world.Workspace.Profiles.ListAsync(world.Game.Id, world.Workspace.Token)).Value!.Select(profile => profile.Id));
        Assert.Equal(branchesBefore, await BranchesAsync(world, RepoRole.External));
        Assert.Contains(world.SavesOnly, (await world.ListAsync()).Select(snapshot => snapshot.Id));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_snapshot_that_is_not_in_the_list_fails_with_the_removed_not_found_error_and_nothing_is_made()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        var before = (await world.Workspace.Profiles.ListAsync(world.Game.Id, world.Workspace.Token)).Value!.Count;

        var missing = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, new HistoryPointId("no-such-snapshot"), "Nothing");
        var notRemoved = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.First, "Still on its profile");

        Assert.False(missing.Ok);
        Assert.Equal("error.removed.notFound", missing.Error.MessageId);
        Assert.Equal("no-such-snapshot", missing.Error.Args["snapshotId"]);
        Assert.False(notRemoved.Ok);
        Assert.Equal("error.removed.notFound", notRemoved.Error.MessageId);
        Assert.Equal(before, (await world.Workspace.Profiles.ListAsync(world.Game.Id, world.Workspace.Token)).Value!.Count);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_name_the_catalog_refuses_fails_before_any_branch_is_made()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();

        var refused = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.InstallOnly, "   ");

        Assert.False(refused.Ok);
        Assert.Equal("error.validation.invalidPayload", refused.Error.MessageId);
        Assert.Equal([world.Profile.BranchName], await BranchesAsync(world, RepoRole.Install));
        Assert.Equal([world.SavesOnly, world.InstallOnly], (await world.ListAsync()).Select(snapshot => snapshot.Id));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_failure_in_the_second_folder_takes_back_the_first_folders_branch_and_a_retry_finishes()
    {
        var failSecondBranch = 0;
        using var world = await RemovedWorld.CreateAsync(options => options with
        {
            Engine = new GitEngineOptions
            {
                InvocationObserver = invocation =>
                {
                    // The branch is created with a ref update that names the all-zero old value, first in the install folder, then in the saves folder.
                    if (Volatile.Read(ref failSecondBranch) == 1 && invocation.Arguments[0] == "update-ref" && invocation.Arguments.Contains("0000000000000000000000000000000000000000") && invocation.GitDir?.Contains("external.git", StringComparison.Ordinal) == true)
                    {
                        throw new IOException("The disk reported an error.");
                    }
                },
            },
        });
        await world.ContinueFromFirstAsync();

        Volatile.Write(ref failSecondBranch, 1);
        var failed = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.InstallOnly, "Mods");
        Volatile.Write(ref failSecondBranch, 0);

        Assert.False(failed.Ok);
        Assert.Equal([world.Profile.BranchName], await BranchesAsync(world, RepoRole.Install));
        Assert.Equal([world.Profile.BranchName], await BranchesAsync(world, RepoRole.External));
        Assert.Single((await world.Workspace.Profiles.ListAsync(world.Game.Id, world.Workspace.Token)).Value!);

        var retried = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.InstallOnly, "Mods");

        Assert.True(retried.Ok, retried.Error?.Detail);
        Assert.Equal(2, (await world.Workspace.Profiles.ListAsync(world.Game.Id, world.Workspace.Token)).Value!.Count);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_brought_back_snapshot_returns_to_the_list_when_the_profile_made_from_it_is_deleted()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        var brought = (await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.InstallOnly, "Mods")).Value!;
        Assert.DoesNotContain(world.InstallOnly, (await world.ListAsync()).Select(snapshot => snapshot.Id));

        var deleted = await world.Workspace.Profiles.DeleteAsync(world.Game.Id, brought.Id);
        await world.Workspace.WaitForIdleAsync();

        Assert.True(deleted.Ok, deleted.Error?.Detail);
        Assert.Contains(world.InstallOnly, (await world.ListAsync()).Select(snapshot => snapshot.Id));
    }

    [Fact(Timeout = 120_000)]
    public async Task Deleting_a_brought_back_profile_is_not_warned_about_as_a_loss_for_good_because_its_snapshot_goes_back_to_the_list()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        var brought = (await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.InstallOnly, "Mods")).Value!;

        var preview = (await world.Workspace.Profiles.GetDeletePreviewAsync(world.Game.Id, brought.Id, world.Workspace.Token)).Value!;

        Assert.Equal(0, preview.PointsOnlyHere);
        Assert.Equal(1, preview.PointsBackToRemoved);
        Assert.All(preview.Folders, folder => Assert.Equal(0, folder.PointCount));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_brought_back_profile_with_a_snapshot_of_its_own_warns_only_about_that_one()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        var brought = (await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.InstallOnly, "Mods")).Value!;

        // The profile is played on and takes a snapshot of its own.
        var storage = await world.Workspace.Backend.Services.Storage.GetAsync(world.Workspace.Token);
        var saves = world.RepoOf(storage, RepoRole.External);
        await storage.Engine.CheckoutBranchAsync(saves, brought.BranchName, ct: world.Workspace.Token);
        BackendWorkspace.WriteFile(Path.Combine(world.Game.External!.Path, "slot9.sav"), "a save made on the brought back profile");
        await storage.Engine.CaptureAsync(saves, new CaptureRequest(brought.BranchName, "Played on", Guid.NewGuid().ToString("D")), ct: world.Workspace.Token);

        var preview = (await world.Workspace.Profiles.GetDeletePreviewAsync(world.Game.Id, brought.Id, world.Workspace.Token)).Value!;

        Assert.Equal(1, preview.PointsOnlyHere);
        Assert.Equal(1, preview.PointsBackToRemoved);
        Assert.Equal(1, preview.Folders.Single(folder => folder.Role == RepoRole.External).PointCount);
        Assert.Equal(0, preview.Folders.Single(folder => folder.Role == RepoRole.Install).PointCount);
    }

    private static async Task<IReadOnlyList<string>> BranchesAsync(RemovedWorld world, RepoRole role)
    {
        var storage = await world.Workspace.Backend.Services.Storage.GetAsync(world.Workspace.Token);
        return await storage.Engine.ListBranchesAsync(world.RepoOf(storage, role), world.Workspace.Token);
    }
}
