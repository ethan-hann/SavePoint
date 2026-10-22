using Savepoint.Testing;

namespace Savepoint.Core.Tests.Backend;

// "SavePoint deleted my save" has to be answerable from the log (D34, D41): a profile or a game that was deleted leaves a line saying
// which, and what went with it.
public class DestructiveActionLogTests
{
    private readonly RecordingDiagnosticLog _log = new();

    [Fact(Timeout = 120_000)]
    public async Task Deleting_a_profile_logs_which_profile_and_the_points_its_branches_held()
    {
        using var workspace = new BackendWorkspace(options => options with { Log = _log });
        var game = await workspace.AddGameAsync();
        var (spare, _) = await workspace.CreateProfileAsync(game, "Spare");
        await workspace.CreateProfileAsync(game, "Active");
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var tip = await workspace.Backend.Services.Storage.ReadAsync(
            storage => storage.Engine.GetBranchTipAsync(Savepoint.Core.Services.GameFolders.Repos(workspace.Backend.Services, storage, game).First(), spare.BranchName, workspace.Token),
            workspace.Token);

        var deleted = await workspace.Profiles.DeleteAsync(game.Id, spare.Id);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));

        Assert.True(deleted.Ok, deleted.Error?.Detail);
        var line = Assert.Single(_log.Containing("Profile deleted", game.Id, spare.Id));
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("Spare", line.Message, StringComparison.Ordinal);
        Assert.Contains(tip!, line.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 120_000)]
    public async Task Deleting_a_game_logs_the_game_the_folders_whose_history_went_and_how_much_was_removed()
    {
        using var workspace = new BackendWorkspace(options => options with { Log = _log });
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game, "Vanilla");
        await workspace.CreateProfileAsync(game, "Modded");
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));

        var deleted = await workspace.Games.DeleteAsync(game.Id);

        Assert.True(deleted.Ok, deleted.Error?.Detail);
        var line = Assert.Single(_log.Containing("Game deleted", game.Id));
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains(game.Name, line.Message, StringComparison.Ordinal);
        Assert.Contains("2 profiles", line.Message, StringComparison.Ordinal);
        Assert.Contains("install", line.Message, StringComparison.Ordinal);
        Assert.Contains("saves", line.Message, StringComparison.Ordinal);
        Assert.Contains("bytes", line.Message, StringComparison.Ordinal);
    }
}
