using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

// Forwards to the fake but can hold back the profile list of one game, so a test decides when a slow read finishes.
internal sealed class GatedProfilesApi(IProfilesApi inner) : IProfilesApi
{
    private readonly Dictionary<string, TaskCompletionSource> _held = [];
    private readonly Dictionary<string, TaskCompletionSource> _heldStats = [];

    // Reads of this game's profiles wait until the returned action is called.
    public Action Hold(string gameId)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _held[gameId] = gate;
        return () => gate.TrySetResult();
    }

    public async Task<OperationResult<IReadOnlyList<Profile>>> ListAsync(string? gameId = null, CancellationToken cancellationToken = default)
    {
        if (gameId is not null && _held.TryGetValue(gameId, out var gate))
        {
            await gate.Task;
        }

        return await inner.ListAsync(gameId, cancellationToken);
    }

    // Row stats of this game wait until the returned action is called; the profile list is not held.
    public Action HoldStats(string gameId)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _heldStats[gameId] = gate;
        return () => gate.TrySetResult();
    }

    public async Task<OperationResult<IReadOnlyList<ProfileRowStats>>> GetRowStatsAsync(string gameId, CancellationToken cancellationToken = default)
    {
        if (_heldStats.TryGetValue(gameId, out var gate))
        {
            await gate.Task;
        }

        return await inner.GetRowStatsAsync(gameId, cancellationToken);
    }

    public Task<OperationResult<JobStarted>> CreateAsync(string gameId, string name, string? notes, string pointName) =>
        inner.CreateAsync(gameId, name, notes, pointName);

    public Task<OperationResult<Profile>> UpdateAsync(string gameId, string profileId, ProfileChanges changes) =>
        inner.UpdateAsync(gameId, profileId, changes);

    public Task<OperationResult<Profile>> DuplicateAsync(string gameId, string sourceProfileId, string name) =>
        inner.DuplicateAsync(gameId, sourceProfileId, name);

    public Task<OperationResult<Profile>> BringBackAsync(string gameId, HistoryPointId snapshotId, string name) =>
        inner.BringBackAsync(gameId, snapshotId, name);

    public Task<OperationResult<ProfileDeletePreview>> GetDeletePreviewAsync(string gameId, string profileId, CancellationToken cancellationToken = default) =>
        inner.GetDeletePreviewAsync(gameId, profileId, cancellationToken);

    public Task<OperationResult<Unit>> DeleteAsync(string gameId, string profileId) => inner.DeleteAsync(gameId, profileId);

    public Task<OperationResult<JobStarted>> CaptureAsync(string gameId, string profileId, string pointName, IReadOnlyCollection<RepoRole>? folders = null) =>
        inner.CaptureAsync(gameId, profileId, pointName, folders);
}
