using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Backend;

// Free up space, for one game or for all, is the only thing that deletes recently removed snapshots: it clears them first and then reclaims
// the space as it always did. The count it gives beforehand is what the confirmation tells the player.
public class FreeUpSpaceRemovedTests
{
    [Fact(Timeout = 240_000)]
    public async Task Free_up_space_for_a_game_deletes_its_removed_snapshots_and_what_they_held_and_leaves_the_other_games_alone()
    {
        using var alpha = await RemovedWorld.CreateAsync();
        using var beta = await RemovedWorld.CreateAsync(shared: alpha.Workspace, name: "Beta");
        await alpha.ContinueFromFirstAsync();
        await beta.ContinueFromFirstAsync();
        var alphaInstall = (await alpha.RecordedCommitsAsync(RepoRole.Install)).Single();
        var alphaSaves = (await alpha.RecordedCommitsAsync(RepoRole.External)).Single();
        var betaInstall = (await beta.RecordedCommitsAsync(RepoRole.Install)).Single();

        var started = await alpha.Workspace.Games.OptimizeStorageAsync(alpha.Game.Id);
        var job = await alpha.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(await alpha.ListAsync());
        Assert.False(await alpha.ObjectExistsAsync(RepoRole.Install, alphaInstall));
        Assert.False(await alpha.ObjectExistsAsync(RepoRole.External, alphaSaves));
        Assert.Equal(2, (await beta.ListAsync()).Count);
        Assert.True(await beta.ObjectExistsAsync(RepoRole.Install, betaInstall));

        // What the profile still holds is never touched.
        var history = (await alpha.Workspace.History.GetAsync(alpha.Game.Id, null, alpha.Workspace.Token)).Value!;
        Assert.Equal(["Vanilla"], history.Points.Select(point => point.Name));
    }

    [Fact(Timeout = 240_000)]
    public async Task Free_up_space_for_every_game_deletes_every_games_removed_snapshots()
    {
        using var alpha = await RemovedWorld.CreateAsync();
        using var beta = await RemovedWorld.CreateAsync(shared: alpha.Workspace, name: "Beta");
        await alpha.ContinueFromFirstAsync();
        await beta.ContinueFromFirstAsync();
        var betaSaves = (await beta.RecordedCommitsAsync(RepoRole.External)).Single();

        var started = await alpha.Workspace.Settings.OptimizeAllStorageAsync();
        var job = await alpha.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(await alpha.ListAsync());
        Assert.Empty(await beta.ListAsync());
        Assert.False(await beta.ObjectExistsAsync(RepoRole.External, betaSaves));
    }

    [Fact(Timeout = 240_000)]
    public async Task Deleting_a_game_removes_its_removed_snapshots_with_the_rest_and_leaves_the_other_game_s()
    {
        using var alpha = await RemovedWorld.CreateAsync();
        using var beta = await RemovedWorld.CreateAsync(shared: alpha.Workspace, name: "Beta");
        await alpha.ContinueFromFirstAsync();
        await beta.ContinueFromFirstAsync();

        var storage = await alpha.Workspace.Backend.Services.Storage.GetAsync(alpha.Workspace.Token);
        var alphaStore = storage.GameDirectory(alpha.Game.Id);
        var betaStore = storage.GameDirectory(beta.Game.Id);
        Assert.True(Directory.Exists(alphaStore));

        var deleted = await alpha.Workspace.Games.DeleteAsync(alpha.Game.Id);
        var gone = await alpha.Workspace.History.GetRecentlyRemovedAsync(alpha.Game.Id, alpha.Workspace.Token);

        Assert.True(deleted.Ok, deleted.Error?.Detail);
        Assert.False(gone.Ok);
        Assert.False(Directory.Exists(alphaStore));
        Assert.True(Directory.Exists(betaStore));
        Assert.Equal(2, (await beta.ListAsync()).Count);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_preview_counts_the_removed_snapshots_free_up_space_would_delete_for_a_game_and_for_every_game()
    {
        using var alpha = await RemovedWorld.CreateAsync();
        using var beta = await RemovedWorld.CreateAsync(shared: alpha.Workspace, name: "Beta");
        var token = alpha.Workspace.Token;
        var noneRemoved = await alpha.Workspace.Games.GetFreeUpSpacePreviewAsync(alpha.Game.Id, token);
        await alpha.ContinueFromFirstAsync();
        await beta.ContinueFromFirstAsync();

        var forAlpha = await alpha.Workspace.Games.GetFreeUpSpacePreviewAsync(alpha.Game.Id, token);
        var forAll = await alpha.Workspace.Settings.GetFreeUpSpacePreviewAsync(token);

        Assert.Equal(0, noneRemoved.Value!.RecentlyRemovedSnapshots);

        // Two snapshots left each game's profile, one in each folder.
        Assert.Equal(2, forAlpha.Value!.RecentlyRemovedSnapshots);
        Assert.Equal(4, forAll.Value!.RecentlyRemovedSnapshots);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_preview_of_an_unknown_game_fails_with_the_game_not_found_error()
    {
        using var world = await RemovedWorld.CreateAsync();

        var result = await world.Workspace.Games.GetFreeUpSpacePreviewAsync("no-such-game", world.Workspace.Token);

        Assert.False(result.Ok);
        Assert.Equal(new GameNotFoundException("no-such-game").MessageId, result.Error.MessageId);
    }
}
