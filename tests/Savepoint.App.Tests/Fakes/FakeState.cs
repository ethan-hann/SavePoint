using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Fakes;

// Everything the fake knows, guarded by FakeBackend's lock; only the fake's Api classes touch it.
internal sealed class FakeState
{
    private const string _defaultGitPath = @"C:\Program Files\SavePoint\git\cmd\git.exe";

    private static readonly IReadOnlyDictionary<string, string> _tokens = new Dictionary<string, string>
    {
        ["{documents}"] = @"C:\Users\you\Documents",
        ["{appData}"] = @"C:\Users\you\AppData\Roaming",
        ["{localAppData}"] = @"C:\Users\you\AppData\Local",
        ["{userHome}"] = @"C:\Users\you",
        ["{savedGames}"] = @"C:\Users\you\Saved Games",
    };

    private long _pointSequence;

    public FakeState(ScenarioBuilder scenario)
    {
        DefaultStorageRoot = scenario.DefaultStorageRoot;
        Settings = scenario.Settings ?? AppSettings.Defaults(scenario.DefaultStorageRoot);
        VolumeTotalBytes = scenario.VolumeTotalBytes;
        VolumeFreeBytes = scenario.VolumeFreeBytes;
        CatalogDirectory = scenario.CatalogDirectory;
        Startup = scenario.Startup ?? new StartupStatus(
            new GitStatus(true, _defaultGitPath, "2.56.0.windows.2", null),
            new CatalogStatus(scenario.CatalogDirectory, []));
        foreach (var (game, bytes) in scenario.Games)
        {
            Games.Add(game);
            GameBytes[game.Id] = bytes;
        }

        foreach (var (profile, stats) in scenario.Profiles)
        {
            Profiles.Add(profile);
            if (stats is not null)
            {
                RowStats[profile.Id] = stats;
            }
        }

        foreach (var (gameId, point) in scenario.History)
        {
            History.Add((gameId, point));
        }

        Removed.AddRange(scenario.Removed);
        Copy(scenario.FolderStatuses, FolderStatuses);
        Copy(scenario.CapturePreviews, CapturePreviews);
        Copy(scenario.LaunchPreviews, LaunchPreviews);
        Copy(scenario.DeletePreviews, DeletePreviews);
        Copy(scenario.Orphans, Orphans);
        Copy(scenario.Executables, Executables);
        Copy(scenario.Imports, Imports);
        RelocationPreview = scenario.RelocationPreview;
        StorageFoldersWithHistory.UnionWith(scenario.StorageFoldersWithHistory);
        ClearAllDataPreview = scenario.ClearAllDataPreview;
    }

    public string DefaultStorageRoot { get; }

    public AppSettings Settings { get; set; }

    public long VolumeTotalBytes { get; }

    public long VolumeFreeBytes { get; }

    // Whether the storage folder lies where the Windows installer clears it.
    public bool StorageInInstallerFolder { get; set; }

    public string CatalogDirectory { get; }

    public StartupStatus Startup { get; set; }

    // What the startup check reports before it answers, and a task it then waits on, to hold a check open the way a leftover cleanup does.
    public List<StartupProgress> StartupSteps { get; } = [];

    public Task? StartupGate { get; set; }

    // Folders that hold the stamped history of the listed games; see ScenarioBuilder.WithStorageHistoryIn.
    public HashSet<string> StorageFoldersWithHistory { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<GameDefinition> Games { get; } = [];

    public Dictionary<string, long> GameBytes { get; } = [];

    public Dictionary<string, IReadOnlyList<FolderStatus>> FolderStatuses { get; } = [];

    public Dictionary<string, CapturePreview> CapturePreviews { get; } = [];

    public List<Profile> Profiles { get; } = [];

    public Dictionary<string, ProfileRowStats> RowStats { get; } = [];

    public List<(string GameId, HistoryPoint Point)> History { get; } = [];

    // Snapshots that left a profile; they belong to no profile, so nothing else counts them.
    public List<(string GameId, RemovedSnapshot Snapshot)> Removed { get; } = [];

    public Dictionary<(string GameId, LaunchTarget Target), LaunchPreview> LaunchPreviews { get; } = [];

    public Dictionary<string, ProfileDeletePreview> DeletePreviews { get; } = [];

    public Dictionary<string, OrphanedHistory> Orphans { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, IReadOnlyList<string>> Executables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, GameDefinition> Imports { get; } = new(StringComparer.OrdinalIgnoreCase);

    // What a library scan finds; null means no store is installed. Each game's added flag is worked out from the games the fake holds.
    public LibraryScan? Library { get; set; }

    // Games a test wants refused when added from a library, by key.
    public Dictionary<string, ErrorInfo> LibraryRefusals { get; } = new(StringComparer.Ordinal);

    public StorageRelocationPreview? RelocationPreview { get; }

    public ClearAllDataPreview? ClearAllDataPreview { get; }

    public Dictionary<string, LaunchSessionInfo> Sessions { get; } = [];

    // Seeded so generated profile ids are the same on every run.
    public Random Random { get; } = new(20260708);

    public GameDefinition Game(string gameId) =>
        Games.Find(g => g.Id == gameId) ?? throw new GameNotFoundException(gameId);

    public Profile Profile(string gameId, string profileId) =>
        Profiles.Find(p => p.Id == profileId && p.GameId == gameId)
        ?? throw new ProfileNotFoundException(gameId, profileId);

    public HistoryPoint Point(string gameId, HistoryPointId pointId) =>
        History.Find(h => h.GameId == gameId && h.Point.Id == pointId).Point
        ?? throw new KeyNotFoundException($"The fake has no point '{pointId}' for game '{gameId}'.");

    public IEnumerable<HistoryPoint> PointsOf(string gameId) => History.Where(h => h.GameId == gameId).Select(h => h.Point);

    public ProfileRowStats StatsFor(Profile profile)
    {
        if (RowStats.TryGetValue(profile.Id, out var stats))
        {
            return stats;
        }

        var snapshots = History.Count(h => h.GameId == profile.GameId && h.Point.ProfileId == profile.Id);
        return new ProfileRowStats(profile.Id, false, [], snapshots, null, null, profile.VersionOverride, profile.LastLaunchedAt, false, snapshots == 0 ? SnapshotNowAvailability.FirstSnapshot : SnapshotNowAvailability.NotLoaded);
    }

    public static IReadOnlyList<RepoRole> FoldersOf(GameDefinition game) =>
        game.External is null ? [RepoRole.Install] : [RepoRole.Install, RepoRole.External];

    public HistoryPointId NewPointId() => new($"point-{++_pointSequence}");

    public string Resolve(string rawPath)
    {
        foreach (var (token, value) in _tokens)
        {
            rawPath = rawPath.Replace(token, value, StringComparison.OrdinalIgnoreCase);
        }

        return rawPath;
    }

    // A session counts as open from Preparing until it completes or fails.
    public bool IsOpen(string gameId) =>
        Sessions.TryGetValue(gameId, out var session) && session.State is not (SessionState.Completed or SessionState.Failed);

    public ErrorInfo? RunningGameError()
    {
        var open = Games.Find(g => IsOpen(g.Id));
        return open is null ? null : ErrorInfo.From(new GameRunningException(open.Id, open.Name));
    }

    public long TotalBytes => GameBytes.Values.Sum();

    public void RemoveGame(string gameId)
    {
        foreach (var profile in Profiles.Where(p => p.GameId == gameId).ToList())
        {
            RemoveProfile(profile);
        }

        Games.RemoveAll(g => g.Id == gameId);
        GameBytes.Remove(gameId);
        FolderStatuses.Remove(gameId);
        CapturePreviews.Remove(gameId);
        History.RemoveAll(h => h.GameId == gameId);
        Removed.RemoveAll(r => r.GameId == gameId);
        Sessions.Remove(gameId);
        foreach (var key in LaunchPreviews.Keys.Where(k => k.GameId == gameId).ToList())
        {
            LaunchPreviews.Remove(key);
        }
    }

    // What a free-up would delete, for one game or every game, with a token that names each snapshot as the preview counted it.
    public FreeUpSpacePreview FreeUpPreview(string? gameId)
    {
        var counted = Removed.Where(removed => gameId is null || removed.GameId == gameId).ToList();
        return new FreeUpSpacePreview(counted.Count, new FreeUpSpaceToken([.. counted.Select(removed => new FreeUpSpaceRecord(removed.GameId, RepoRole.Install, removed.Snapshot.Id.Value, removed.Snapshot.Id.Value))]));
    }

    // A run deletes the snapshots its token names and no others, so one removed after the preview survives it.
    public void DeleteRemoved(FreeUpSpaceToken? confirmed, string? gameId = null) =>
        Removed.RemoveAll(removed => (gameId is null || removed.GameId == gameId)
            && confirmed is not null
            && confirmed.Records.Any(record => record.GameId == removed.GameId && record.RefName == removed.Snapshot.Id.Value));

    // The profile's points go with it: in the real app they are gone from history once no profile holds them.
    public void RemoveProfile(Profile profile)
    {
        Profiles.Remove(profile);
        RowStats.Remove(profile.Id);
        DeletePreviews.Remove(profile.Id);
        History.RemoveAll(h => h.GameId == profile.GameId && h.Point.ProfileId == profile.Id);

        // A removed snapshot outlives the profile it left, which it then no longer names.
        for (var i = 0; i < Removed.Count; i++)
        {
            if (Removed[i].GameId == profile.GameId && Removed[i].Snapshot.FromProfileId == profile.Id)
            {
                Removed[i] = (Removed[i].GameId, Removed[i].Snapshot with { FromProfileId = null, FromProfileName = null });
            }
        }

        foreach (var key in LaunchPreviews.Where(p => p.Value.GameId == profile.GameId && p.Value.ProfileId == profile.Id).Select(p => p.Key).ToList())
        {
            LaunchPreviews.Remove(key);
        }
    }

    public void ClearLibrary()
    {
        foreach (var game in Games.ToList())
        {
            RemoveGame(game.Id);
        }

        Settings = AppSettings.Defaults(DefaultStorageRoot);
    }

    private static void Copy<TKey, TValue>(Dictionary<TKey, TValue> from, Dictionary<TKey, TValue> to)
        where TKey : notnull
    {
        foreach (var (key, value) in from)
        {
            to[key] = value;
        }
    }
}
