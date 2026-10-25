using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Services.Games;

internal sealed partial class GamesApi
{
    // A game-wide rule for logs or crash dumps reaches the saves folder too. The defaults for new games are install-only, so this
    // guards the other way such a rule gets in (a definition file, or an older default written game-wide): a rule that would leave out
    // a file of the saves folder, recorded or not, in any profile, is saved for the game's own folder only. Anything a player typed that is not one
    // of the default rules is theirs and is kept; the editor says it applies to the saves folder as well.
    private async Task<(GameDefinition Game, RulesLeftOff? LeftOff)> WithoutRulesThatHideSavesAsync(StorageSnapshot storage, GameDefinition game)
    {
        var asked = game.IgnorePaths.Where(rule => RunTimeOutput.DefaultRules.Contains(rule)).Distinct(StringComparer.Ordinal).ToList();
        var saves = GameFolders.Repos(_services, storage, game).FirstOrDefault(repo => repo.Role == RepoRole.External);
        if (asked.Count == 0 || saves is null)
        {
            return (game, null);
        }

        IReadOnlyList<string> hiding;
        var checkedSaves = true;
        try
        {
            var found = await storage.Engine.ListNamedByAsync(saves, asked, CancellationToken.None).ConfigureAwait(false);
            hiding = [.. asked.Where(rule => found[rule].Count > 0)];
        }
        catch (PathMissingException)
        {
            // Nothing to lose in a folder that is not there.
            return (game, null);
        }
        catch (Exception e) when (e is SavepointException or IOException or UnauthorizedAccessException)
        {
            // Adding a game must not fail over this, but a rule that could not be checked is not worth a lost save: it is kept for the game's own folder only.
            _services.Log.Warning($"Could not check the saves folder of {game.Id} against the leave-out rules, so they apply to the game's own folder only: {e.Message}", e);
            hiding = asked;
            checkedSaves = false;
        }

        // The rule moves to the game's own folder only, which can never hold a save, so its logs still do not look like an update there.
        return hiding.Count == 0
            ? (game, null)
            : (game with
            {
                IgnorePaths = [.. game.IgnorePaths.Where(rule => !hiding.Contains(rule))],
                InstallIgnorePaths = [.. RunTimeOutput.Merge(game.InstallIgnorePaths, hiding)],
            }, new RulesLeftOff(hiding, checkedSaves));
    }
}
