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
        ApiGuard.RunAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(completion);
            var record = _book.Record(gameId);
            if (SessionBook.CannotComplete(gameId, record) is { } refusal)
            {
                throw refusal;
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
                var point = string.IsNullOrWhiteSpace(name) ? target.Name : name;
                var jobId = _capture.Start(game, target, point, roles, Guid.NewGuid().ToString("D"), creating: forked is not null, plans: PlansFor(completion, roles, current.StartCommits, (current.Info.State as SessionState.Exited)?.Options.UpdateTarget?.Id.Value));
                return new SessionState.Completing(completion, jobId);
            });
            return Unit.Value;
        });

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
        ApiGuard.RunAsync(async () =>
        {
            var game = await RequireGameAsync(gameId).ConfigureAwait(false);

            // The files of a game that is starting or running are in use; throwing changes away under it would corrupt the session.
            if (_book.IsInUse(gameId))
            {
                throw new GameRunningException(gameId, game.Name);
            }

            return await _services.RunForGameAsync(
                gameId,
                async (storage, _) =>
                {
                    if (GameFolders.Repos(_services, storage, game).FirstOrDefault(repo => repo.Role == folder) is { } repo)
                    {
                        await storage.Engine.DiscardChangesAsync(repo).ConfigureAwait(false);
                    }

                    return Unit.Value;
                }).ConfigureAwait(false);
        });
}
