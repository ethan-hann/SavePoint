using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Backend;

/// <summary>What the launch side hands the backend: its Api, and the sessions the services consult.</summary>
/// <param name="Api">Answers <see cref="CoreBackend.Launch"/>.</param>
/// <param name="Sessions">Tells the services which games and profiles are being played.</param>
public sealed record LaunchComponents(ILaunchApi Api, IGameSessions Sessions);

/// <summary>Stands in for the launch Api while the backend has no launch component: nothing runs, and every call says so.</summary>
internal sealed class UnavailableLaunchApi : ILaunchApi
{
    public event EventHandler<LaunchSessionInfo>? SessionChanged
    {
        add { }
        remove { }
    }

    public LaunchSessionInfo? GetSession(string gameId) => null;

    public IReadOnlyList<LaunchSessionInfo> ListSessions() => [];

    public Task<OperationResult<LaunchPreview>> GetPreviewAsync(string gameId, LaunchTarget target, CancellationToken cancellationToken = default) =>
        Unavailable<LaunchPreview>();

    public Task<OperationResult<JobStarted>> StartAsync(string gameId, LaunchTarget target) => Unavailable<JobStarted>();

    public Task<OperationResult<Unit>> DiscardChangesAsync(string gameId, RepoRole folder) => Unavailable<Unit>();

    public Task<OperationResult<Unit>> MarkFinishedAsync(string gameId) => Unavailable<Unit>();

    public Task<OperationResult<Unit>> CompleteSessionAsync(string gameId, SessionCompletion completion) => Unavailable<Unit>();

    public Task<OperationResult<Unit>> RetryCompletionAsync(string gameId) => Unavailable<Unit>();

    private static Task<OperationResult<T>> Unavailable<T>() =>
        Task.FromResult(OperationResult.Failure<T>(new LaunchUnavailableException()));
}
