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

    // Steam's manifest for a Steam game, read only; null for any other game and for one Steam keeps no manifest for.
    private async Task<SteamInstallState?> ReadSteamInstallAsync(GameDefinition game, string? installPath = null)
    {
        installPath ??= GameFolders.ResolveLoosely(_services.PathTokens, game).FirstOrDefault(folder => folder.Role == RepoRole.Install)?.Path;
        var comparison = _services.Platform.PathComparison;
        return await Task.Run(() => SteamGames.NeedsClient(game, installPath, _services.Platform.Os, comparison) ? SteamInstallState.Read(installPath, comparison) : null).ConfigureAwait(false);
    }

    // Runs once Steam is up and before any folder is switched. An update Steam is already running would write over the version about to be
    // loaded, so the manifest is read with the client running and the launch stops, with nothing changed, when it shows work on the game
    // or a new build since the launch began. The reading taken before Steam started cannot decide this: with Steam closed, a flag left by
    // an update that crashed can sit in the manifest, and starting the client clears it.
    private async Task RefuseBeforeSwitchWhenSteamUpdatesAsync(GameDefinition game, SteamInstallState? before)
    {
        if (await ReadSteamInstallAsync(game).ConfigureAwait(false) is { } now && (now.IsUpdating || now.ChangedSince(before)))
        {
            _services.Log.Information($"Launch of {game.Id} stopped before the switch: Steam's manifest shows an update (flags {now.StateFlags}, build {now.BuildId ?? "none"}, was {before?.BuildId ?? "none"}).");
            throw new SteamUpdatingBeforeSwitchException(game.Id);
        }
    }

    // Runs right before the program starts, after the folders hold the player's version. An update can begin while the folders are switched,
    // so the manifest is read once more, and the program does not start when Steam is working on the game or changed its build or update
    // time since the launch began. This only reads the manifest; it cannot tell whether the files themselves changed, so a refusal says
    // they may have.
    private async Task RefuseWhenSteamUpdatesAsync(GameDefinition game, SteamInstallState? before, string installPath)
    {
        if (await ReadSteamInstallAsync(game, installPath).ConfigureAwait(false) is { } now && (now.IsUpdating || now.ChangedSince(before)))
        {
            _services.Log.Information($"Launch of {game.Id} stopped: Steam's manifest shows an update (flags {now.StateFlags}, build {now.BuildId ?? "none"}, was {before?.BuildId ?? "none"}).");
            throw new SteamUpdatingGameException(game.Id);
        }
    }

    // Runs at the start of the launch job, before the game's folders are touched. When it returns, Steam is running and signed in, and no
    // folder has been read or switched. It does not look for an update; RefuseBeforeSwitchWhenSteamUpdatesAsync does that next, and
    // RefuseWhenSteamUpdatesAsync again once the folders are switched.
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
        context.AddStep(JobSteps.LaunchWaitingForSteam, ("game", game.Name), ("seconds", (int)Math.Ceiling(_steamWait.Timeout.TotalSeconds)));

        // Only the client is started: a game started through Steam would be updated first.
        if (!await Task.Run(_steam.TryStart, token).ConfigureAwait(false))
        {
            throw new SteamCannotStartException(game.Id);
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

        // Ends the screen's "waiting for Steam" card before the folders are read, which can take a while.
        context.AddStep(JobSteps.LaunchSteamReady, ("game", game.Name));
    }
}
