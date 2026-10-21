using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Services.Profiles;

/// <summary>The real <see cref="IProfilesApi"/>: a profile is a pair of branches, one per folder, plus a catalog row.</summary>
internal sealed partial class ProfilesApi : IProfilesApi
{
    private readonly BackendServices _services;

    public ProfilesApi(BackendServices services)
    {
        _services = services;
    }

    public Task<OperationResult<IReadOnlyList<Profile>>> ListAsync(string? gameId = null, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<IReadOnlyList<Profile>>();

    public Task<OperationResult<IReadOnlyList<ProfileRowStats>>> GetRowStatsAsync(string gameId, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<IReadOnlyList<ProfileRowStats>>();

    public Task<OperationResult<JobStarted>> CreateAsync(string gameId, string name, string? notes, string pointName) =>
        ApiGuard.NotBuilt<JobStarted>();

    public Task<OperationResult<Profile>> UpdateAsync(string gameId, string profileId, ProfileChanges changes) =>
        ApiGuard.NotBuilt<Profile>();

    public Task<OperationResult<Profile>> DuplicateAsync(string gameId, string sourceProfileId, string name) =>
        ApiGuard.NotBuilt<Profile>();

    public Task<OperationResult<ProfileDeletePreview>> GetDeletePreviewAsync(string gameId, string profileId, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<ProfileDeletePreview>();

    public Task<OperationResult<Unit>> DeleteAsync(string gameId, string profileId) => ApiGuard.NotBuilt<Unit>();

    public Task<OperationResult<JobStarted>> CaptureAsync(string gameId, string profileId, string pointName, IReadOnlyCollection<RepoRole>? folders = null) =>
        ApiGuard.NotBuilt<JobStarted>();
}
