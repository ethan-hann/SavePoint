using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>Starts games and follows each play session from launch to the post-play capture.</summary>
/// <remarks>
/// The App subscribes to <see cref="SessionChanged"/> first, then reads <see cref="GetSession"/> or <see cref="ListSessions"/>, and
/// drops any event whose <see cref="LaunchSessionInfo.Revision"/> is not newer than the one it holds. A game has at most one
/// session open at a time. A session is open from <see cref="StartAsync"/> until it reaches Completed or Failed, and that open
/// session is what "the game is running" means for the destructive-operation checks (D13.5). See <see cref="OperationResult{T}"/>
/// for the rules every member follows.
/// </remarks>
public interface ILaunchApi
{
    /// <summary>
    /// Raised with the whole session after every change. It is raised on a background thread, never the UI thread, so the App
    /// marshals it.
    /// </summary>
    event EventHandler<LaunchSessionInfo>? SessionChanged;

    /// <summary>
    /// The game's latest session, or null if none was started this run. A finished or failed session stays here until the next
    /// launch of that game replaces it. Reads memory and cannot fail.
    /// </summary>
    /// <param name="gameId">The game.</param>
    LaunchSessionInfo? GetSession(string gameId);

    /// <summary>Every game's latest session, open or finished. Reads memory and cannot fail.</summary>
    IReadOnlyList<LaunchSessionInfo> ListSessions();

    /// <summary>
    /// Works out what starting the game for a target would do, without changing anything: both folders are checked for unsaved
    /// changes first (D13.8) and the switch is sized. Fails, for example, with <c>error.path.missing</c> when a folder has moved.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="target">What to load.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<LaunchPreview>> GetPreviewAsync(string gameId, LaunchTarget target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the launch: prepare, switch the folders if needed, start the game. Answers with the launch job at once. The job ends
    /// once the game is running (or fails or is cancelled); everything after that arrives through <see cref="SessionChanged"/>.
    /// Fails at once, with no job, with <c>error.launch.alreadyRunning</c> when the game already has an open session. The job
    /// itself can fail with, for example, <c>error.launch.dirtyWorkingTree</c>, <c>error.gitOperation.checkoutFailed</c> (a file
    /// another program holds open, named in its <c>path</c>, including one that only reads as an unsaved change because it cannot
    /// be read), <c>error.diskSpace.low</c>, or <c>error.launch.spawnFailed</c>.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="target">What to load.</param>
    Task<OperationResult<JobStarted>> StartAsync(string gameId, LaunchTarget target);

    /// <summary>
    /// Throws away one folder's unsaved changes, returning it to the last saved point of the profile it is on. It cannot be undone.
    /// Fails with <c>error.profile.noPoints</c> when the folder has no saved point to return to (D29), and with
    /// <c>error.gameService.gameRunning</c> while the game has an open session (including one that waits for the player's choice
    /// after play) or its process is still running; both are checked again once the game's lock is held (D13.14).
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="folder">Which folder to discard changes in.</param>
    Task<OperationResult<Unit>> DiscardChangesAsync(string gameId, RepoRole folder);

    /// <summary>
    /// "I'm done playing": ends the wait for the game to exit without stopping the game, so the session moves to Exited. Does
    /// nothing when the session has already exited or is completing. Fails with <c>error.launch.noActiveSession</c> when the game
    /// has no open session, and also while its session is still preparing or switching, since there is no running game to stop
    /// waiting for yet.
    /// </summary>
    /// <param name="gameId">The game.</param>
    Task<OperationResult<Unit>> MarkFinishedAsync(string gameId);

    /// <summary>
    /// Carries out the user's post-play choice. <see cref="SessionCompletion.Skip"/> ends the session at once. Every other choice
    /// moves it to Completing with a capture job. Fails with <c>error.launch.noActiveSession</c> when the game has no session or
    /// it is already completing, completed, or failed, with <c>error.launch.sessionStillRunning</c> when it has not exited yet, and
    /// with <c>error.launch.completionNotOffered</c> when the choice is not among the exited session's options (D31).
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="completion">What to do with the session.</param>
    Task<OperationResult<Unit>> CompleteSessionAsync(string gameId, SessionCompletion completion);

    /// <summary>
    /// Tries the after-play save again for the folders that did not save, as the same choice and under the same capture id, so the
    /// session still ends as one point (D31). The session goes back to Completing with a new capture job and ends Completed again,
    /// its result joining the folders that had already saved. Fails with <c>error.launch.noActiveSession</c> when the game's
    /// session did not end Completed with a failed or partial save.
    /// </summary>
    /// <param name="gameId">The game.</param>
    Task<OperationResult<Unit>> RetryCompletionAsync(string gameId);
}
