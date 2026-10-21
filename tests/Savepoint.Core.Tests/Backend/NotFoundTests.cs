using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Backend;

public class NotFoundTests
{
    private const string _unknownId = "no-such-id";

    // Each entry is a call that takes an id the catalog does not hold, and the error it answers with.
    private static async Task AssertAllAnswerAsync(string expectedId, IReadOnlyList<(string Call, Func<Task<ErrorInfo?>> Run)> calls)
    {
        foreach (var (call, run) in calls)
        {
            var error = await run();
            Assert.True(error?.MessageId == expectedId, $"{call} answered {error?.MessageId ?? "success"} instead of {expectedId}.");
        }
    }

    [Fact]
    public async Task Every_games_call_with_an_unknown_game_id_says_the_game_was_not_found()
    {
        using var workspace = new BackendWorkspace();
        var ghost = workspace.NewGame("Ghost") with { Id = _unknownId };
        var export = Path.Combine(workspace.Root, "ghost.json");

        await AssertAllAnswerAsync(
            "error.game.notFound",
            [
                ("GetFolderStatus", async () => (await workspace.Games.GetFolderStatusAsync(_unknownId, workspace.Token)).Error),
                ("GetStorageUsage", async () => (await workspace.Games.GetStorageUsageAsync(_unknownId, workspace.Token)).Error),
                ("GetCapturePreview", async () => (await workspace.Games.GetCapturePreviewAsync(_unknownId, workspace.Token)).Error),
                ("Update", async () => (await workspace.Games.UpdateAsync(ghost)).Error),
                ("Delete", async () => (await workspace.Games.DeleteAsync(_unknownId)).Error),
                ("OptimizeStorage", async () => (await workspace.Games.OptimizeStorageAsync(_unknownId)).Error),
                ("ExportDefinition", async () => (await workspace.Games.ExportDefinitionAsync(_unknownId, export, workspace.Token)).Error),
            ]);
    }

    [Fact]
    public async Task Every_profiles_call_with_an_unknown_game_id_says_the_game_was_not_found()
    {
        using var workspace = new BackendWorkspace();
        var changes = new ProfileChanges("Renamed", null, null);

        await AssertAllAnswerAsync(
            "error.game.notFound",
            [
                ("GetRowStats", async () => (await workspace.Profiles.GetRowStatsAsync(_unknownId, workspace.Token)).Error),
                ("Create", async () => (await workspace.Profiles.CreateAsync(_unknownId, "Vanilla", null, "Start")).Error),
                ("Update", async () => (await workspace.Profiles.UpdateAsync(_unknownId, "profile-1", changes)).Error),
                ("Duplicate", async () => (await workspace.Profiles.DuplicateAsync(_unknownId, "profile-1", "Copy")).Error),
                ("GetDeletePreview", async () => (await workspace.Profiles.GetDeletePreviewAsync(_unknownId, "profile-1", workspace.Token)).Error),
                ("Delete", async () => (await workspace.Profiles.DeleteAsync(_unknownId, "profile-1")).Error),
                ("Capture", async () => (await workspace.Profiles.CaptureAsync(_unknownId, "profile-1", "Save")).Error),
                ("History", async () => (await workspace.History.GetAsync(_unknownId, null, workspace.Token)).Error),
            ]);
    }

    [Fact]
    public async Task Every_profile_call_with_an_unknown_profile_id_says_the_profile_was_not_found()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var changes = new ProfileChanges("Renamed", null, null);

        await AssertAllAnswerAsync(
            "error.profile.notFound",
            [
                ("Update", async () => (await workspace.Profiles.UpdateAsync(game.Id, _unknownId, changes)).Error),
                ("Duplicate", async () => (await workspace.Profiles.DuplicateAsync(game.Id, _unknownId, "Copy")).Error),
                ("GetDeletePreview", async () => (await workspace.Profiles.GetDeletePreviewAsync(game.Id, _unknownId, workspace.Token)).Error),
                ("Delete", async () => (await workspace.Profiles.DeleteAsync(game.Id, _unknownId)).Error),
                ("Capture", async () => (await workspace.Profiles.CaptureAsync(game.Id, _unknownId, "Save")).Error),
            ]);
    }

    [Fact]
    public async Task A_profile_of_another_game_is_not_found_under_this_one()
    {
        using var workspace = new BackendWorkspace();
        var alpha = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var beta = await workspace.AddGameAsync(workspace.NewGame("Beta"));
        var (betaProfile, _) = await workspace.CreateProfileAsync(beta, "Vanilla");

        var result = await workspace.Profiles.DeleteAsync(alpha.Id, betaProfile.Id);

        Assert.Equal("error.profile.notFound", result.Error?.MessageId);
        Assert.Equal(betaProfile.Id, result.Error!.Args["profileId"]);
    }

    [Fact]
    public async Task The_error_names_the_id_that_was_not_found()
    {
        using var workspace = new BackendWorkspace();

        var game = await workspace.Games.GetFolderStatusAsync(_unknownId, workspace.Token);

        Assert.Equal(_unknownId, game.Error!.Args["gameId"]);
    }
}
