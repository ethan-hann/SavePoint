using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Services;
using Savepoint.Core.Services.Jobs;

namespace Savepoint.Core.Launch.Orchestration;

public sealed partial class LaunchOrchestrator
{
    // True when the game quits without the Steam client and the client is not running and signed in. Asked before a launch reads or
    // switches anything, never for a switch without play.
    private async Task<bool> SteamIsMissingAsync(GameDefinition game)
    {
        var install = GameFolders.ResolveLoosely(_services.PathTokens, game).FirstOrDefault(folder => folder.Role == RepoRole.Install)?.Path;
        return await Task.Run(() => SteamGames.NeedsClient(game, install, _services.Platform.Os, _services.Platform.PathComparison) && !_steam.IsReady()).ConfigureAwait(false);
    }

    // Runs at the start of the launch job, before the game's folders are touched. Steam started here is up before the install is switched,
    // so any update it applies at its own start lands on the version the folder holds now and not on the one about to be loaded.
    private async Task EnsureSteamAsync(JobContext context, GameDefinition game, bool startSteam)
    {
        if (!await SteamIsMissingAsync(game).ConfigureAwait(false))
        {
            return;
        }

        if (!startSteam)
        {
            throw new SteamNotRunningException(game.Id);
        }

        var token = context.CancellationToken;
        context.AddStep(JobSteps.LaunchWaitingForSteam, ("game", game.Name));

        // Only the client is started: a game started through Steam would be updated first.
        if (!await Task.Run(_steam.TryStart, token).ConfigureAwait(false))
        {
            throw new SteamDidNotStartException(game.Id);
        }

        var began = _services.Time.GetTimestamp();
        while (!await Task.Run(_steam.IsReady, token).ConfigureAwait(false))
        {
            if (_services.Time.GetElapsedTime(began) >= _steamWait.Timeout)
            {
                throw new SteamDidNotStartException(game.Id);
            }

            await Task.Delay(_steamWait.PollInterval, _services.Time, token).ConfigureAwait(false);
        }
    }
}
