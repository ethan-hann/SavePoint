using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Backend;

// Stands in for the launch Api in tests that never start a game: nothing runs, and every call says so.
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

    public Task<OperationResult<JobStarted>> StartAsync(string gameId, LaunchTarget target, bool startSteam = false) => Unavailable<JobStarted>();

    public Task<OperationResult<JobStarted>> SwitchAsync(string gameId, LaunchTarget target) => Unavailable<JobStarted>();

    public Task<OperationResult<Unit>> DiscardChangesAsync(string gameId, RepoRole folder) => Unavailable<Unit>();

    public Task<OperationResult<Unit>> MarkFinishedAsync(string gameId) => Unavailable<Unit>();

    public Task<OperationResult<Unit>> CompleteSessionAsync(string gameId, SessionCompletion completion) => Unavailable<Unit>();

    public Task<OperationResult<Unit>> RetryCompletionAsync(string gameId) => Unavailable<Unit>();

    private static Task<OperationResult<T>> Unavailable<T>() =>
        Task.FromResult(OperationResult.Failure<T>(new LaunchUnavailableException()));

    private sealed class LaunchUnavailableException : SavepointException
    {
        public LaunchUnavailableException()
            : base("error.launch.unavailable", "Launching games is not available in this test backend.")
        {
        }
    }
}
