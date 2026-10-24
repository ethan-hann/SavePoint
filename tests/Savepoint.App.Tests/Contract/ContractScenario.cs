using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Contract;

// What the UI would see over one session, written as lines of text so two backends can be compared and a difference reads as a diff.
// Shared: steps both backends must answer alike. Gaps: steps where the fake is simpler than the real backend, kept apart so each
// is pinned on its own (see BackendContractTests).
internal sealed record ScenarioTranscript(IReadOnlyList<string> Shared, IReadOnlyList<string> Gaps);

internal static class ContractScenario
{
    public static async Task<ScenarioTranscript> RunAsync(ContractHarness backend, CancellationToken cancellationToken)
    {
        var shared = new Story();
        await RunSharedAsync(backend, shared, cancellationToken);
        await RunInterruptedSwitchAsync(backend, shared, cancellationToken);
        await RunSnapshotOfAnotherProfileAsync(backend, shared, cancellationToken);
        await RunSwitchWithoutPlayingAsync(backend, shared, cancellationToken);
        var gaps = new Story();
        await RunGapsAsync(backend, gaps, cancellationToken);
        return new ScenarioTranscript(shared.Lines, gaps.Lines);
    }

    private static async Task RunSharedAsync(ContractHarness backend, Story story, CancellationToken cancellationToken)
    {
        var game = backend.NewGame("Alpha");

        story.Add("library starts empty", Show(await backend.Games.ListAsync(cancellationToken), games => $"{games.Count} games"));

        var added = await backend.Games.AddAsync(game);
        story.Add("add game", Show(added, outcome => outcome is AddGameOutcome.Added a ? $"added {a.Game.Name}" : outcome.GetType().Name));
        story.Add("games listed", Show(await backend.Games.ListAsync(cancellationToken), games => string.Join(", ", games.Select(g => g.Name))));
        story.Add("folder status", Show(await backend.Games.GetFolderStatusAsync(game.Id, cancellationToken), statuses =>
            string.Join("; ", statuses.Select(s => $"{s.Role} exists={s.Exists} confirmed={s.Consent.Confirmed}"))));

        var created = await backend.Profiles.CreateAsync(game.Id, "Vanilla", "no mods", "Fresh install");
        story.Add("create profile", Show(created, _ => "capture started"));
        var vanilla = await FinishCaptureAsync(backend, story, created, cancellationToken);

        story.Add("profiles listed", Show(await backend.Profiles.ListAsync(game.Id, cancellationToken), profiles =>
            string.Join("; ", profiles.Select(p => $"{p.Name} notes={p.Notes} branch={story.Mask(p.BranchName)}"))));

        await PlayAsync(backend, game, vanilla ?? "missing", story, cancellationToken);

        var copy = await backend.Profiles.DuplicateAsync(game.Id, vanilla ?? "missing", "Vanilla copy");
        story.Add("duplicate profile", Show(copy, profile => $"{profile.Name} notes={profile.Notes}"));
        if (copy.Ok)
        {
            story.Name(copy.Value.Id, "profile");
            var renamed = await backend.Profiles.UpdateAsync(game.Id, copy.Value.Id, new ProfileChanges("Copy, renamed", "edited", "1.2"));
            story.Add("update profile", Show(renamed, profile => $"{profile.Name} notes={profile.Notes} version={profile.VersionOverride}"));
        }

        story.Add("row stats", Show(await backend.Profiles.GetRowStatsAsync(game.Id, cancellationToken), stats =>
            string.Join("; ", stats.Select(s => $"{story.Name(s.ProfileId, "profile")} running={s.IsRunning} version={s.VersionOverride}").Order(StringComparer.Ordinal))));

        if (copy.Ok)
        {
            story.Add("delete copy", Show(await backend.Profiles.DeleteAsync(game.Id, copy.Value.Id), _ => "deleted"));
        }

        story.Add("profiles after delete", Show(await backend.Profiles.ListAsync(game.Id, cancellationToken), profiles => string.Join(", ", profiles.Select(p => p.Name))));

        story.Add("unknown game", Show(await backend.Games.GetFolderStatusAsync("no-such-game", cancellationToken), _ => "unexpected success"));
        story.Add("unknown game for history", Show(await backend.History.GetAsync("no-such-game", null, cancellationToken), _ => "unexpected success"));
        story.Add("unknown profile", Show(await backend.Profiles.UpdateAsync(game.Id, "no-such-profile", new ProfileChanges("x", null, null)), _ => "unexpected success"));
        story.Add("unknown profile on delete", Show(await backend.Profiles.DeleteAsync(game.Id, "no-such-profile"), _ => "unexpected success"));

        story.Add("recently removed of an unknown game", Show(await backend.History.GetRecentlyRemovedAsync("no-such-game", cancellationToken), _ => "unexpected success"));
        story.Add("recently removed when nothing was removed", Show(await backend.History.GetRecentlyRemovedAsync(game.Id, cancellationToken), removed => $"{removed.Snapshots.Count} snapshots"));
        story.Add("bring back a snapshot that is not there", Show(await backend.Profiles.BringBackAsync(game.Id, new HistoryPointId("no-such-snapshot"), "Brought back"), _ => "unexpected success"));
        story.Add("bring back under a blank name", Show(await backend.Profiles.BringBackAsync(game.Id, new HistoryPointId("no-such-snapshot"), " "), _ => "unexpected success"));
        story.Add("free up space preview of a game with nothing removed", Show(await backend.Games.GetFreeUpSpacePreviewAsync(game.Id, cancellationToken), preview => $"{preview.RecentlyRemovedSnapshots} snapshots"));
        story.Add("free up space preview of an unknown game", Show(await backend.Games.GetFreeUpSpacePreviewAsync("no-such-game", cancellationToken), _ => "unexpected success"));
        story.Add("free up space preview of every game with nothing removed", Show(await backend.Settings.GetFreeUpSpacePreviewAsync(cancellationToken), preview => $"{preview.RecentlyRemovedSnapshots} snapshots"));

        var settings = await backend.Settings.GetAsync(cancellationToken);
        story.Add("settings read", Show(settings, current => $"{current.Theme} {current.Layout} {current.Locale}"));
        if (settings.Ok)
        {
            var saved = await backend.Settings.SaveAsync(settings.Value with { Theme = ThemePreference.Dark, ConfirmSessionCapture = false });
            story.Add("settings saved", Show(saved, current => $"{current.Theme} confirm={current.ConfirmSessionCapture} root unchanged={current.StorageRoot == settings.Value.StorageRoot}"));
            story.Add("settings read back", Show(await backend.Settings.GetAsync(cancellationToken), current => $"{current.Theme} confirm={current.ConfirmSessionCapture}"));
        }

        story.Add("delete game", Show(await backend.Games.DeleteAsync(game.Id), _ => "deleted"));
        story.Add("library ends empty", Show(await backend.Games.ListAsync(cancellationToken), games => $"{games.Count} games"));
    }

    // The profile is launched, played and left as it is: what the launch flow reads at each step.
    private static async Task PlayAsync(ContractHarness backend, GameDefinition game, string profileId, Story story, CancellationToken cancellationToken)
    {
        backend.ExpectLaunch(game, profileId);
        var tip = new LaunchTarget.ProfileTip(profileId);
        story.Add("launch preview", Show(await backend.Launch.GetPreviewAsync(game.Id, tip, cancellationToken), preview =>
            $"banner={preview.ShowBanner} older={preview.TargetIsOlderPoint} install matches={preview.Install.MatchesTarget} dirty={preview.Install.Dirty} saves matches={preview.External?.MatchesTarget} saves={preview.AvailableSaves.Count}"));

        var started = await backend.Launch.StartAsync(game.Id, tip);
        story.Add("start launch", Show(started, _ => "job started"));
        if (!started.Ok)
        {
            return;
        }

        await backend.GameRunningAsync(game.Id, cancellationToken);
        story.Add("session while the game runs", Describe(backend.Launch.GetSession(game.Id), story));
        story.Add("second start while it runs", Show(await backend.Launch.StartAsync(game.Id, tip), _ => "unexpected success"));
        story.Add("switch while it runs", Show(await backend.Launch.SwitchAsync(game.Id, tip), _ => "unexpected success"));
        story.Add("complete while it runs", Show(await backend.Launch.CompleteSessionAsync(game.Id, new SessionCompletion.Skip()), _ => "unexpected success"));
        story.Add("discard while it runs", Show(await backend.Launch.DiscardChangesAsync(game.Id, RepoRole.External), _ => "unexpected success"));

        await backend.GameExitedAsync(game.Id, cancellationToken);
        story.Add("session after the game exits", Describe(backend.Launch.GetSession(game.Id), story));
        story.Add("discard while it waits for the choice", Show(await backend.Launch.DiscardChangesAsync(game.Id, RepoRole.External), _ => "unexpected success"));

        story.Add("complete with skip", Show(await backend.Launch.CompleteSessionAsync(game.Id, new SessionCompletion.Skip()), _ => "ended"));
        story.Add("session after skip", Describe(backend.Launch.GetSession(game.Id), story));
        story.Add("complete again", Show(await backend.Launch.CompleteSessionAsync(game.Id, new SessionCompletion.Skip()), _ => "unexpected success"));
        story.Add("finish with no game running", Show(await backend.Launch.MarkFinishedAsync(game.Id), _ => "unexpected success"));
        story.Add("sessions listed", $"{backend.Launch.ListSessions().Count} sessions");
        story.Add("unknown game", Show(await backend.Launch.StartAsync("no-such-game", tip), _ => "unexpected success"));
        story.Add("switch of an unknown game", Show(await backend.Launch.SwitchAsync("no-such-game", tip), _ => "unexpected success"));
    }

    // A switch stopped part way leaves the install folder unfinished, the preview names the target that finishes it, and starting that
    // target brings the folders to where they were heading; the play that follows is saved as a new point.
    private static async Task RunInterruptedSwitchAsync(ContractHarness backend, Story story, CancellationToken cancellationToken)
    {
        var game = backend.NewGame("Gamma");
        await backend.Games.AddAsync(game);
        var vanilla = await FinishCaptureAsync(backend, story, await backend.Profiles.CreateAsync(game.Id, "Vanilla", null, "Fresh install"), cancellationToken) ?? "missing";
        File.WriteAllText(Path.Combine(game.Install.Path, "data", "mod.pak"), "a mod");
        var modded = await FinishCaptureAsync(backend, story, await backend.Profiles.CreateAsync(game.Id, "Modded", null, "With a mod"), cancellationToken) ?? "missing";
        backend.ExpectLaunch(game, vanilla);
        backend.ExpectLaunch(game, modded);
        var moddedTip = new LaunchTarget.ProfileTip(modded);
        story.Add("preview before the switch", Show(await backend.Launch.GetPreviewAsync(game.Id, moddedTip, cancellationToken), preview => DescribeFolders(preview, story)));

        await backend.InterruptSwitchAsync(game, modded, vanilla, cancellationToken);
        var stopped = await backend.Launch.GetPreviewAsync(game.Id, moddedTip, cancellationToken);
        story.Add("session after the switch was stopped", Describe(backend.Launch.GetSession(game.Id), story));
        story.Add("preview after the switch was stopped", Show(stopped, preview => DescribeFolders(preview, story)));

        var recorded = stopped.Ok ? stopped.Value.Install.UnfinishedSwitchTarget : null;
        if (recorded is null)
        {
            story.Add("finishing the switch", "no target records where it was heading");
            return;
        }

        var finishing = await backend.Launch.StartAsync(game.Id, recorded);
        story.Add("start the recorded target", Show(finishing, _ => "job started"));
        await backend.GameRunningAsync(game.Id, cancellationToken);
        backend.SwitchFinished(game, vanilla);
        story.Add("preview after the switch was finished", Show(await backend.Launch.GetPreviewAsync(game.Id, new LaunchTarget.ProfileTip(vanilla), cancellationToken), preview => DescribeFolders(preview, story)));

        ContractHarness.PlayerSaves(game);
        await backend.GameExitedAsync(game.Id, cancellationToken);
        story.Add("complete with a new save", Show(await backend.Launch.CompleteSessionAsync(game.Id, new SessionCompletion.CaptureNew("After play")), _ => "started"));
        if (backend.Launch.GetSession(game.Id)?.State is SessionState.Completing saving)
        {
            await backend.FinishAsync(saving.JobId, cancellationToken);
        }

        await backend.WaitForCompletedAsync(game.Id, cancellationToken);
        var session = backend.Launch.GetSession(game.Id);
        var saved = (session?.State as SessionState.Completed)?.Result;
        story.Add("session after the new save", Describe(session, story));
        story.Add("what the new save recorded", saved is null
            ? "no result"
            : $"{string.Join(", ", saved.Folders.Select(folder => $"{folder.Role}:{(folder.Ok ? "ok" : folder.Error!.MessageId)}"))} point={(saved.PointId is null ? "none" : "named")}");
        story.Add("session error after the new save", (session?.State as SessionState.Completed)?.Error?.MessageId ?? "none");
    }

    // A switch without playing keeps the game taken for as long as its job runs, opens no session, and lets the game go when it ends.
    private static async Task RunSwitchWithoutPlayingAsync(ContractHarness backend, Story story, CancellationToken cancellationToken)
    {
        var game = backend.NewGame("Epsilon");
        await backend.Games.AddAsync(game);
        var vanilla = await FinishCaptureAsync(backend, story, await backend.Profiles.CreateAsync(game.Id, "Vanilla", null, "Fresh install"), cancellationToken) ?? "missing";
        File.WriteAllText(Path.Combine(game.Install.Path, "data", "mod.pak"), "a mod");
        var modded = await FinishCaptureAsync(backend, story, await backend.Profiles.CreateAsync(game.Id, "Modded", null, "With a mod"), cancellationToken) ?? "missing";
        backend.ExpectLaunch(game, vanilla);
        backend.ExpectLaunch(game, modded);
        var vanillaTip = new LaunchTarget.ProfileTip(vanilla);

        var job = await backend.HoldSwitchAsync(game, vanilla, cancellationToken);
        story.Add("start while a switch job runs", Show(await backend.Launch.StartAsync(game.Id, vanillaTip), _ => "unexpected success"));
        story.Add("switch while a switch job runs", Show(await backend.Launch.SwitchAsync(game.Id, vanillaTip), _ => "unexpected success"));
        story.Add("discard while a switch job runs", Show(await backend.Launch.DiscardChangesAsync(game.Id, RepoRole.External), _ => "unexpected success"));
        story.Add("session while a switch job runs", backend.Launch.GetSession(game.Id) is null ? "no session" : "a session");

        backend.ReleaseSwitch();
        var finished = await backend.FinishAsync(job, cancellationToken);
        story.Add("the switch job ends", $"{finished.Kind} {finished.Status}");
        story.Add("session after the switch job", backend.Launch.GetSession(game.Id) is null ? "no session" : "a session");

        var again = await backend.Launch.SwitchAsync(game.Id, new LaunchTarget.ProfileTip(modded));
        story.Add("switch after the switch job ended", Show(again, _ => "job started"));
        if (again.Ok)
        {
            story.Add("the second switch job ends", $"{(await backend.FinishAsync(again.Value.JobId, cancellationToken)).Status}");
        }
    }

    private static string DescribeFolders(LaunchPreview preview, Story story) =>
        $"install unfinished={preview.Install.UnfinishedSwitch} dirty={preview.Install.Dirty} heading for {DescribeTarget(preview.Install.UnfinishedSwitchTarget, story)}; "
        + $"saves unfinished={preview.External?.UnfinishedSwitch} dirty={preview.External?.Dirty}";

    private static string DescribeTarget(LaunchTarget? target, Story story) => target switch
    {
        null => "nothing",
        LaunchTarget.ProfileTip tip => "the tip of " + story.Name(tip.ProfileId, "profile"),
        _ => target.GetType().Name,
    };

    private static string Describe(LaunchSessionInfo? session, Story story) => session is null
        ? "no session"
        : $"{session.State.GetType().Name} profile={story.Name(session.ProfileId, "profile")} older={session.StartedFromOlderPoint} completion={(session.State as SessionState.Completed)?.Completion.GetType().Name ?? "none"} result={(session.State as SessionState.Completed)?.Result is null}";

    // The second game is saved once and then asked what the first save did, which is where the fake only plays its part.
    private static async Task RunGapsAsync(ContractHarness backend, Story story, CancellationToken cancellationToken)
    {
        var game = backend.NewGame("Beta");
        await backend.Games.AddAsync(game);
        var created = await backend.Profiles.CreateAsync(game.Id, "Vanilla", null, "Fresh install");
        var profileId = await FinishCaptureAsync(backend, new Story(), created, cancellationToken) ?? "missing";

        // What the game offers after play depends on the saves the profile holds, which the fake does not track.
        backend.ExpectLaunch(game, profileId);
        await backend.Launch.StartAsync(game.Id, new LaunchTarget.ProfileTip(profileId));
        await backend.GameRunningAsync(game.Id, cancellationToken);
        await backend.GameExitedAsync(game.Id, cancellationToken);
        story.Add("choices after play with one save", backend.Launch.GetSession(game.Id)?.State is SessionState.Exited exited
            ? string.Join(",", exited.Options.Choices)
            : "no exit");
        await backend.Launch.CompleteSessionAsync(game.Id, new SessionCompletion.Skip());

        story.Add("history after the first save", Show(await backend.History.GetAsync(game.Id, null, cancellationToken), history => $"{history.Points.Count} points"));
        story.Add("row stats after the first save", Show(await backend.Profiles.GetRowStatsAsync(game.Id, cancellationToken), stats =>
            string.Join("; ", stats.Select(s => $"active={s.IsActive} snapshots={s.SnapshotCount}"))));
        story.Add("storage used", Show(await backend.Games.GetStorageUsageAsync(game.Id, cancellationToken), usage => $"{usage.BytesUsed > 0}"));
        story.Add("delete preview of the active profile", Show(await backend.Profiles.GetDeletePreviewAsync(game.Id, profileId, cancellationToken), preview =>
            $"points={preview.PointsOnlyHere} blocked={preview.BlockedBy?.MessageId ?? "no"}"));
        story.Add("delete of the active profile", Show(await backend.Profiles.DeleteAsync(game.Id, profileId), _ => "deleted"));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        story.Add("read with a cancelled token", Show(await backend.Games.ListAsync(cancelled.Token), _ => "success"));
    }

    // A snapshot copies what the folders hold, which is the profile made last, so one asked for on the profile made before it is refused before a job starts.
    private static async Task RunSnapshotOfAnotherProfileAsync(ContractHarness backend, Story story, CancellationToken cancellationToken)
    {
        var game = backend.NewGame("Delta");
        await backend.Games.AddAsync(game);
        var first = await FinishCaptureAsync(backend, new Story(), await backend.Profiles.CreateAsync(game.Id, "Vanilla", null, "Fresh install"), cancellationToken) ?? "missing";
        await FinishCaptureAsync(backend, new Story(), await backend.Profiles.CreateAsync(game.Id, "Modded", null, "With a mod"), cancellationToken);
        story.Add("snapshot of a profile the folders do not hold", Show(await backend.Profiles.CaptureAsync(game.Id, first, "Not mine"), _ => "started"));
    }

    // Ends the job a CreateAsync started and describes what it reports; the new profile's id for later steps.
    private static async Task<string?> FinishCaptureAsync(ContractHarness backend, Story story, OperationResult<JobStarted> started, CancellationToken cancellationToken)
    {
        if (!started.Ok)
        {
            return null;
        }

        var job = await backend.FinishAsync(started.Value.JobId, cancellationToken);
        var result = job.Result as CaptureResult;
        if (result is not null)
        {
            story.Name(result.ProfileId, "profile");
        }

        var folders = result is null ? "none" : string.Join(", ", result.Folders.Select(f => $"{f.Role}:{(f.Ok ? "ok" : f.Error!.MessageId)}"));
        story.Add("capture finished", $"{job.Kind} {job.Status} folders={folders} point={(result?.PointId is null ? "none" : "named")}");

        // The details say what the job did, in the same first words, whichever backend ran it.
        story.Add("capture details begin with", job.RecentLines.FirstOrDefault() is JobLine.Step first ? first.MessageId : "nothing");
        return result?.ProfileId;
    }

    private static string Show<T>(OperationResult<T> result, Func<T, string> describe) =>
        result.Ok ? describe(result.Value) : "error " + result.Error.MessageId;

    // Ids differ between backends, so the story calls each by the order it first appeared in.
    private sealed class Story
    {
        private readonly List<string> _lines = [];
        private readonly Dictionary<string, string> _names = [];

        public IReadOnlyList<string> Lines => _lines;

        public void Add(string step, string seen) => _lines.Add($"{step}: {seen}");

        public string Name(string id, string kind)
        {
            if (!_names.TryGetValue(id, out var name))
            {
                name = $"{kind}#{_names.Values.Count(n => n.StartsWith(kind, StringComparison.Ordinal)) + 1}";
                _names[id] = name;
            }

            return name;
        }

        // A branch name carries its profile's id.
        public string Mask(string text) => _names.Aggregate(text, (current, pair) => current.Replace(pair.Key, pair.Value, StringComparison.Ordinal));
    }
}
