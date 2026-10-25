using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Services.Games;

internal sealed partial class GamesApi
{
    public Task<OperationResult<LeaveOutCheck>> CheckLeaveOutAsync(string gameId, IReadOnlyList<string> rules, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                ArgumentNullException.ThrowIfNull(rules);
                var game = await RequireGameAsync(gameId, cancellationToken).ConfigureAwait(false);
                return await _services.Storage.ReadAsync(storage => CheckSavesAsync(storage, game, rules, cancellationToken), cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    // Asked of the game's own repository for the saves folder, so git's matching and letter-case setting decide. A saves folder that is
    // not there holds no saves to lose.
    private async Task<LeaveOutCheck> CheckSavesAsync(StorageSnapshot storage, GameDefinition game, IReadOnlyList<string> rules, CancellationToken cancellationToken)
    {
        var saves = GameFolders.Repos(_services, storage, game).FirstOrDefault(repo => repo.Role == RepoRole.External);
        var safe = new List<string>();
        var blocked = new List<RuleBlocked>();
        foreach (var rule in rules)
        {
            IReadOnlyList<string> named = [];
            if (saves is not null)
            {
                try
                {
                    named = await storage.Engine.ListNamedByAsync(saves, [rule], cancellationToken).ConfigureAwait(false);
                }
                catch (PathMissingException)
                {
                    // Nothing to lose in a folder that is not there.
                }
            }

            if (named.Count == 0)
            {
                safe.Add(rule);
            }
            else
            {
                blocked.Add(new RuleBlocked(rule, [.. named.Take(RuleBlocked.Sample)]));
            }
        }

        return new LeaveOutCheck(safe, blocked);
    }

    // A new game starts with rules for logs and crash dumps. A saves folder that already holds such files keeps them: the rule that
    // would leave them out is not added, so a default can never drop a save.
    private async Task<GameDefinition> WithoutRulesThatHideSavesAsync(StorageSnapshot storage, GameDefinition game)
    {
        var defaults = game.IgnorePaths.Where(rule => RunTimeOutput.DefaultRules.Contains(rule)).ToList();
        if (defaults.Count == 0)
        {
            return game;
        }

        IReadOnlyList<string> hiding;
        try
        {
            var check = await CheckSavesAsync(storage, game, defaults, CancellationToken.None).ConfigureAwait(false);
            hiding = [.. check.Blocked.Select(blocked => blocked.Rule)];
        }
        catch (GitOperationException e)
        {
            // Adding a game must not fail over this, but a default that could not be checked is not worth a lost save: it is left off.
            _services.Log.Warning($"Could not check the saves folder of {game.Id} against the default leave-out rules, so they were left off: {e.Message}", e);
            hiding = defaults;
        }

        return hiding.Count == 0
            ? game
            : game with { IgnorePaths = [.. game.IgnorePaths.Where(rule => !hiding.Contains(rule))] };
    }
}
