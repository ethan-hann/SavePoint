using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

public interface ILaunchApi
{
    // Raised on a background thread with the whole session after each change; the App marshals it to the UI thread.
    event EventHandler<LaunchSessionInfo>? SessionChanged;

    // A finished or failed session stays here until the next launch of that game replaces it.
    LaunchSessionInfo? GetSession(string gameId);

    IReadOnlyList<LaunchSessionInfo> ListSessions();

    Task<OperationResult<LaunchPreview>> GetPreviewAsync(string gameId, LaunchTarget target, CancellationToken cancellationToken = default);

    // The job ends once the game is running; everything after that arrives through SessionChanged.
    Task<OperationResult<JobStarted>> StartAsync(string gameId, LaunchTarget target);

    Task<OperationResult<Unit>> DiscardChangesAsync(string gameId, RepoRole folder);

    // "I'm done playing": ends the wait for the game to exit without stopping the game.
    Task<OperationResult<Unit>> MarkFinishedAsync(string gameId);

    // Skip ends the session at once; every other choice moves it to Completing with a capture job.
    Task<OperationResult<Unit>> CompleteSessionAsync(string gameId, SessionCompletion completion);
}
