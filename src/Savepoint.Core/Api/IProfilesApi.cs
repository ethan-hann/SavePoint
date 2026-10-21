using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

public interface IProfilesApi
{
    // A null gameId lists every game's profiles, which the rail uses for its counts.
    Task<OperationResult<IReadOnlyList<Profile>>> ListAsync(string? gameId = null, CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<ProfileRowStats>>> GetRowStatsAsync(string gameId, CancellationToken cancellationToken = default);

    // A profile is born from what is on disk now, for the wizard and for "Keep as a new profile" alike.
    Task<OperationResult<JobStarted>> CreateAsync(string gameId, string name, string? notes, string pointName);

    Task<OperationResult<Profile>> UpdateAsync(string gameId, string profileId, ProfileChanges changes);

    Task<OperationResult<Profile>> DuplicateAsync(string gameId, string sourceProfileId, string name);

    Task<OperationResult<ProfileDeletePreview>> GetDeletePreviewAsync(string gameId, string profileId, CancellationToken cancellationToken = default);

    Task<OperationResult<Unit>> DeleteAsync(string gameId, string profileId);

    // A null folders list saves every configured folder; one folder is how "Retry the saves" and "Save them first" work.
    Task<OperationResult<JobStarted>> CaptureAsync(string gameId, string profileId, string pointName, IReadOnlyCollection<RepoRole>? folders = null);
}
