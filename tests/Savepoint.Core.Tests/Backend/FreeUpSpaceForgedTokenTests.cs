using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Backend;

// Only a token the preview issued may drive a run, so a caller cannot name records of its own.
public class FreeUpSpaceForgedTokenTests
{
    [Fact(Timeout = 240_000)]
    public async Task A_token_built_by_hand_from_a_previews_own_records_is_refused_and_nothing_is_deleted()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, world.Workspace.Token)).Value!;
        Assert.NotEmpty(preview.Token.Records);
        var listed = (await world.ListAsync()).Count;

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, Forge(preview.Token.Records));

        Assert.False(started.Ok);
        Assert.Equal(listed, (await world.ListAsync()).Count);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_token_whose_record_value_was_edited_after_issue_is_refused()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, world.Workspace.Token)).Value!;
        var listed = (await world.ListAsync()).Count;
        var edited = preview.Token.Records.Select(record => record with { Value = new string('0', record.Value.Length) });

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, Forge([.. edited]));

        Assert.False(started.Ok);
        Assert.Equal(listed, (await world.ListAsync()).Count);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_token_a_preview_issued_is_accepted()
    {
        using var world = await RemovedWorld.CreateAsync();
        await world.ContinueFromFirstAsync();
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, world.Workspace.Token)).Value!;

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Token);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.True(started.Ok);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(await world.ListAsync());
    }

    // A seal the process never issued, which is all a caller outside the preview can offer.
    private static FreeUpSpaceToken Forge(IReadOnlyList<FreeUpSpaceRecord> records) => new(records, new byte[32]);
}
