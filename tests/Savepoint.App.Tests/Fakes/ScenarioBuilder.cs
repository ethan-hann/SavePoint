using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Fakes;

// Seed data for a FakeBackend; anything left out falls back to a value computed from the games and profiles given.
public sealed class ScenarioBuilder
{
    public static readonly DateTimeOffset DefaultNow = new(2026, 7, 8, 12, 0, 0, TimeSpan.FromHours(-5));

    private const string _defaultStorageRoot = @"C:\Users\you\AppData\Local\SavePoint\Storage";

    internal DateTimeOffset Now { get; private set; } = DefaultNow;

    internal string DefaultStorageRoot { get; private set; } = _defaultStorageRoot;

    internal AppSettings? Settings { get; private set; }

    internal long VolumeTotalBytes { get; private set; } = 500_000_000_000;

    internal long VolumeFreeBytes { get; private set; } = 400_000_000_000;

    internal string CatalogDirectory { get; private set; } = @"C:\Users\you\AppData\Roaming\SavePoint";

    internal StartupStatus? Startup { get; private set; }

    // The folders that hold the stamped history of the games, which is what the startup choice "Find the folder" looks for.
    internal HashSet<string> StorageFoldersWithHistory { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal List<(GameDefinition Game, long Bytes)> Games { get; } = [];

    internal Dictionary<string, IReadOnlyList<FolderStatus>> FolderStatuses { get; } = [];

    internal Dictionary<string, CapturePreview> CapturePreviews { get; } = [];

    internal List<(Profile Profile, ProfileRowStats? Stats)> Profiles { get; } = [];

    internal List<(string GameId, HistoryPoint Point)> History { get; } = [];

    internal List<(string GameId, RemovedSnapshot Snapshot)> Removed { get; } = [];

    internal Dictionary<(string GameId, LaunchTarget Target), LaunchPreview> LaunchPreviews { get; } = [];

    internal Dictionary<string, ProfileDeletePreview> DeletePreviews { get; } = [];

    internal Dictionary<string, OrphanedHistory> Orphans { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal Dictionary<string, IReadOnlyList<string>> Executables { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal Dictionary<string, GameDefinition> Imports { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal StorageRelocationPreview? RelocationPreview { get; private set; }

    internal ClearAllDataPreview? ClearAllDataPreview { get; private set; }

    // No games: the first-run screen.
    public static ScenarioBuilder Empty() => new();

    // The sample data behind the design screens: the Games screen, launch flow, editor, History and Settings.
    public static ScenarioBuilder Mockup() => MockupScenario.CreateBuilder();

    public ScenarioBuilder WithNow(DateTimeOffset now)
    {
        Now = now;
        return this;
    }

    public ScenarioBuilder WithSettings(AppSettings settings)
    {
        Settings = settings;
        return this;
    }

    // Where storage goes back to after "Clear all data".
    public ScenarioBuilder WithDefaultStorageRoot(string path)
    {
        DefaultStorageRoot = path;
        return this;
    }

    public ScenarioBuilder WithVolume(long totalBytes, long freeBytes)
    {
        VolumeTotalBytes = totalBytes;
        VolumeFreeBytes = freeBytes;
        return this;
    }

    public ScenarioBuilder WithCatalogDirectory(string path)
    {
        CatalogDirectory = path;
        return this;
    }

    public ScenarioBuilder WithStartupStatus(StartupStatus status)
    {
        Startup = status;
        return this;
    }

    // Says these folders hold the history of the games, so the startup choices treat them as a real storage folder would be.
    public ScenarioBuilder WithStorageHistoryIn(params string[] folders)
    {
        StorageFoldersWithHistory.UnionWith(folders);
        return this;
    }

    public ScenarioBuilder WithGame(GameDefinition game, long bytesUsed = 0)
    {
        Games.Add((game, bytesUsed));
        return this;
    }

    public ScenarioBuilder WithFolderStatus(string gameId, params FolderStatus[] folders)
    {
        FolderStatuses[gameId] = folders;
        return this;
    }

    public ScenarioBuilder WithCapturePreview(CapturePreview preview)
    {
        CapturePreviews[preview.GameId] = preview;
        return this;
    }

    public ScenarioBuilder WithProfile(Profile profile, ProfileRowStats? stats = null)
    {
        Profiles.Add((profile, stats));
        return this;
    }

    public ScenarioBuilder WithHistoryPoint(string gameId, HistoryPoint point)
    {
        History.Add((gameId, point));
        return this;
    }

    // A snapshot that left a profile and is kept until the player frees up space; GetRecentlyRemovedAsync lists the game's, most recently removed first.
    public ScenarioBuilder WithRemovedSnapshot(string gameId, RemovedSnapshot snapshot)
    {
        Removed.Add((gameId, snapshot));
        return this;
    }

    // Keyed by the preview's own game and target, which is what GetPreviewAsync and StartAsync look up.
    public ScenarioBuilder WithLaunchPreview(LaunchPreview preview)
    {
        LaunchPreviews[(preview.GameId, preview.Target)] = preview;
        return this;
    }

    public ScenarioBuilder WithDeletePreview(ProfileDeletePreview preview)
    {
        DeletePreviews[preview.ProfileId] = preview;
        return this;
    }

    // AddAsync for a game installed at this folder answers HistoryFound instead of adding it.
    public ScenarioBuilder WithOrphanedHistory(string installPath, OrphanedHistory history)
    {
        Orphans[installPath] = history;
        return this;
    }

    public ScenarioBuilder WithExecutables(string installPath, params string[] executables)
    {
        Executables[installPath] = executables;
        return this;
    }

    public ScenarioBuilder WithImport(string filePath, GameDefinition definition)
    {
        Imports[filePath] = definition;
        return this;
    }

    public ScenarioBuilder WithRelocationPreview(StorageRelocationPreview preview)
    {
        RelocationPreview = preview;
        return this;
    }

    public ScenarioBuilder WithClearAllDataPreview(ClearAllDataPreview preview)
    {
        ClearAllDataPreview = preview;
        return this;
    }
}
