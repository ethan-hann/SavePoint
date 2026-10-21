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

    // The second game is saved once and then asked what the first save did, which is where the fake only plays its part.
    private static async Task RunGapsAsync(ContractHarness backend, Story story, CancellationToken cancellationToken)
    {
        var game = backend.NewGame("Beta");
        await backend.Games.AddAsync(game);
        var created = await backend.Profiles.CreateAsync(game.Id, "Vanilla", null, "Fresh install");
        var profileId = await FinishCaptureAsync(backend, new Story(), created, cancellationToken) ?? "missing";

        story.Add("history after the first save", Show(await backend.History.GetAsync(game.Id, null, cancellationToken), history => $"{history.Points.Count} points"));
        story.Add("row stats after the first save", Show(await backend.Profiles.GetRowStatsAsync(game.Id, cancellationToken), stats =>
            string.Join("; ", stats.Select(s => $"active={s.IsActive} saves={s.SaveCount}"))));
        story.Add("storage used", Show(await backend.Games.GetStorageUsageAsync(game.Id, cancellationToken), usage => $"{usage.BytesUsed > 0}"));
        story.Add("delete preview of the active profile", Show(await backend.Profiles.GetDeletePreviewAsync(game.Id, profileId, cancellationToken), preview =>
            $"points={preview.PointsOnlyHere} blocked={preview.BlockedBy?.MessageId ?? "no"}"));
        story.Add("delete of the active profile", Show(await backend.Profiles.DeleteAsync(game.Id, profileId), _ => "deleted"));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        story.Add("read with a cancelled token", Show(await backend.Games.ListAsync(cancelled.Token), _ => "success"));
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
