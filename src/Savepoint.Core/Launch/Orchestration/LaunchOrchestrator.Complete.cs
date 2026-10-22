using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services;
using Savepoint.Core.Services.Profiles;

namespace Savepoint.Core.Launch.Orchestration;

public sealed partial class LaunchOrchestrator
{
    private const int _idAttempts = 8;

    /// <inheritdoc />
    public Task<OperationResult<Unit>> CompleteSessionAsync(string gameId, SessionCompletion completion) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            ArgumentNullException.ThrowIfNull(completion);
            var record = _book.Record(gameId);
            if (SessionBook.CannotComplete(gameId, record) is { } refusal)
            {
                throw refusal;
            }

            // The session offered certain choices; the Api is where input is judged, so an unoffered one never starts a capture.
            if (record!.Info.State is SessionState.Exited { Options.Choices: var offered } && !offered.Contains(ChoiceOf(completion)))
            {
                throw new CompletionNotOfferedException(gameId, ChoiceOf(completion));
            }

            // Skipping saves nothing: the session ends at once and no job runs.
            if (completion is SessionCompletion.Skip)
            {
                _book.Complete(gameId, _ => new SessionState.Completed(completion, null, null));
                return Unit.Value;
            }

            var game = await RequireGameAsync(gameId).ConfigureAwait(false);
            var profile = await _services.Catalog.GetProfileAsync(record!.Info.ProfileId).ConfigureAwait(false)
                ?? throw new ProfileNotFoundException(gameId, record.Info.ProfileId);
            var forked = completion is SessionCompletion.ForkNewProfile fork ? await NewProfileAsync(gameId, fork.ProfileName).ConfigureAwait(false) : null;
            var roles = GameFolders.RolesOf(game);

            // The capture job takes the game's lock and ends by itself; the session follows it through the job registry.
            _book.Complete(gameId, current =>
            {
                var target = forked ?? profile;
                var name = PointNameOf(completion);
                var run = new CompletionRun(
                    completion,
                    target,
                    string.IsNullOrWhiteSpace(name) ? target.Name : name,
                    roles,
                    _services.Capture.NewEvent(),
                    Creating: forked is not null,
                    PlansFor(completion, roles, current.StartCommits, (current.Info.State as SessionState.Exited)?.Options.UpdateTarget?.Id.Value));
                var jobId = StartRun(game, run, roles, creating: run.Creating);
                current.Run = run;
                current.SavedSoFar = null;
                return new SessionState.Completing(completion, jobId);
            });
            return Unit.Value;
        });

    /// <inheritdoc />
    public Task<OperationResult<Unit>> RetryCompletionAsync(string gameId) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            var game = await RequireGameAsync(gameId).ConfigureAwait(false);
            if (_book.Record(gameId) is not { Run: { } run, Info.State: SessionState.Completed completed } || !DidNotAllSave(completed))
            {
                throw new NoActiveSessionException(gameId);
            }

            // Only the folders that did not save are tried again; a new profile's row exists once any folder of it saved.
            var missing = completed.Result is { } result ? result.Folders.Where(folder => !folder.Ok).Select(folder => folder.Role).ToList() : [.. run.Roles];
            var creating = run.Creating && await _services.Catalog.GetProfileAsync(run.Target.Id).ConfigureAwait(false) is null;
            _book.Retry(gameId, _ => new SessionState.Completing(run.Completion, StartRun(game, run, missing, creating)));
            return Unit.Value;
        });

    private static bool DidNotAllSave(SessionState.Completed completed) =>
        completed.Error is not null || completed.Result is { } result && result.Folders.Any(folder => !folder.Ok);

    // Every try of one after-play choice saves under the capture id the choice began with, so the session stays one point.
    private Guid StartRun(GameDefinition game, CompletionRun run, IReadOnlyList<RepoRole> roles, bool creating) =>
        _services.Capture.Start(game, run.Target, run.PointName, roles, run.Capture, creating, run.Plans);

    // How each folder is saved. A new point needs no plan; the other choices change history, so each folder is told what the session
    // began from, and an update rewrites only the point it names and leaves a folder with nothing unsaved alone.
    private static Dictionary<RepoRole, FolderCapturePlan>? PlansFor(SessionCompletion completion, IReadOnlyList<RepoRole> roles, IReadOnlyDictionary<RepoRole, string> starts, string? updated) =>
        completion switch
        {
            SessionCompletion.UpdateExisting => roles.ToDictionary(role => role, _ => new FolderCapturePlan(CaptureMode.Amend, SkipWhenClean: true, AmendPointId: updated)),
            SessionCompletion.ForkNewProfile => roles.ToDictionary(role => role, role => new FolderCapturePlan(CaptureMode.Fork, starts.GetValueOrDefault(role))),
            SessionCompletion.MoveForward => roles.ToDictionary(role => role, role => new FolderCapturePlan(CaptureMode.MoveForward, starts.GetValueOrDefault(role))),
            _ => null,
        };

    private static CompletionChoice ChoiceOf(SessionCompletion completion) => completion switch
    {
        SessionCompletion.CaptureNew => CompletionChoice.CaptureNew,
        SessionCompletion.UpdateExisting => CompletionChoice.UpdateExisting,
        SessionCompletion.ForkNewProfile => CompletionChoice.ForkNewProfile,
        SessionCompletion.MoveForward => CompletionChoice.MoveForward,
        _ => CompletionChoice.Skip,
    };

    private static string? PointNameOf(SessionCompletion completion) => completion switch
    {
        SessionCompletion.CaptureNew capture => capture.PointName,
        SessionCompletion.UpdateExisting update => update.PointName,
        SessionCompletion.ForkNewProfile fork => fork.PointName,
        SessionCompletion.MoveForward move => move.PointName,
        _ => null,
    };

    // A fresh id that no profile of any game has; the branch is named after it.
    private async Task<Profile> NewProfileAsync(string gameId, string name)
    {
        for (var attempt = 0; attempt < _idAttempts; attempt++)
        {
            var profile = CatalogIds.NewProfile(gameId, name);
            if (await _services.Catalog.GetProfileAsync(profile.Id).ConfigureAwait(false) is null)
            {
                return CatalogValidator.RequireValid(profile);
            }
        }

        throw new InvalidOperationException("No unused profile id was found.");
    }

    /// <inheritdoc />
    public Task<OperationResult<Unit>> DiscardChangesAsync(string gameId, RepoRole folder) =>
        ApiGuard.RunAsync(_services.Log, async () =>
        {
            var game = await RequireGameAsync(gameId).ConfigureAwait(false);
            await RefuseWhileOpenAsync(game).ConfigureAwait(false);

            return await _services.RunForGameAsync(
                gameId,
                async (storage, _) =>
                {
                    // The lock may have been queued behind a launch: the game can be open by now.
                    await RefuseWhileOpenAsync(game).ConfigureAwait(false);
                    if (GameFolders.Repos(_services, storage, game).FirstOrDefault(repo => repo.Role == folder) is { } repo)
                    {
                        await storage.Engine.DiscardChangesAsync(repo).ConfigureAwait(false);
                    }

                    return Unit.Value;
                }).ConfigureAwait(false);
        });

    // The files of a game that is starting, running, or waiting for the player's choice are in use; throwing changes away under
    // it would corrupt the session, and so would doing it under a game left open after its session was ended.
    private async Task RefuseWhileOpenAsync(GameDefinition game)
    {
        if (_book.IsGameRunning(game.Id) || await IsGameOpenAsync(game).ConfigureAwait(false))
        {
            throw new GameRunningException(game.Id, game.Name);
        }
    }
}
