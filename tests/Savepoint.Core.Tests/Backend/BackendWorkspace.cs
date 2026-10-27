using System.Text;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Platform;
using Savepoint.Core.Services.Storage;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Backend;

// A whole backend over throwaway folders: the platform's home, catalog and default storage root, a games folder and the real bundled git.
internal sealed class BackendWorkspace : IDisposable
{
    private readonly Func<BackendOptions, BackendOptions>? _configure;
    private readonly List<CoreBackend> _built = [];
    private int _games;

    // steamFolder is where the platform says Steam is, which decides what the path judge protects.
    public BackendWorkspace(Func<BackendOptions, BackendOptions>? configure = null, string? steamFolder = null)
    {
        _configure = configure;
        Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "savepoint-backend-" + Guid.NewGuid().ToString("N")));
        Home = Path.Combine(Root, "home");
        GamesRoot = Path.Combine(Root, "games");
        InstallDirectory = Path.Combine(Root, "app");
        Directory.CreateDirectory(GamesRoot);
        Directory.CreateDirectory(InstallDirectory);
        Platform = FakePlatforms.ForHost(Home, steamFolder);
        Backend = Build();
    }

    // A workspace that starts as a copy of another's folders, so a world built once can be handed to many tests. Files that store absolute
    // paths (the catalog, the git config) are pointed at the copy's own root; one that cannot be rewritten is an error, not a stale path.
    private BackendWorkspace(string templateRoot, Func<BackendOptions, BackendOptions>? configure)
    {
        _configure = configure;
        Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "savepoint-backend-" + Guid.NewGuid().ToString("N")));
        Home = Path.Combine(Root, "home");
        GamesRoot = Path.Combine(Root, "games");
        InstallDirectory = Path.Combine(Root, "app");
        try
        {
            CopyTree(templateRoot, Root);
            RewriteRoot(Root, templateRoot);
            AssertNoTemplateName(Root, templateRoot);
            Directory.CreateDirectory(GamesRoot);
            Directory.CreateDirectory(InstallDirectory);
            Platform = FakePlatforms.ForHost(Home);
            _games = Directory.GetDirectories(GamesRoot).Length;
            Backend = Build();
        }
        catch
        {
            Savepoint.Core.Tests.Git.GitWorkspace.DeleteDirectory(Root);
            throw;
        }
    }

    public string Root { get; }

    public string Home { get; }

    public string GamesRoot { get; }

    public string InstallDirectory { get; }

    public IPlatform Platform { get; }

    public FakeDiskSpace Disk { get; } = new();

    public FakeSessions Sessions { get; } = new();

    public CoreBackend Backend { get; private set; }

    public string CatalogDirectory => Platform.Directories.CatalogDir;

    public string DefaultStorageRoot => Platform.Directories.DefaultStorageRoot;

    public IGamesApi Games => Backend.Games;

    public IProfilesApi Profiles => Backend.Profiles;

    public ISettingsApi Settings => Backend.Settings;

    public IHistoryApi History => Backend.History;

    public IAppStatusApi AppStatus => Backend.AppStatus;

    public IJobsApi Jobs => Backend.Jobs;

    public CancellationToken Token => TestContext.Current.CancellationToken;

    // A new workspace, over its own folders, that holds what the template holds now. The template must be idle and stay unchanged.
    // The copy's backend is built with the given options, which the template's own backend need not share.
    public static BackendWorkspace CopyOf(BackendWorkspace template, Func<BackendOptions, BackendOptions>? configure = null) => new(template.Root, configure);

    // A second backend over the same folders, as the app has after a restart: nothing in memory carries over.
    public CoreBackend Restart() => Backend = Build();

    // A game whose install and saves folders exist with a few files in them. Ids are readable so failures name the game.
    public GameDefinition NewGame(string name = "Alpha", bool withSaves = true, IReadOnlyList<string>? modHints = null, IReadOnlyList<string>? ignore = null)
    {
        var slot = Interlocked.Increment(ref _games);
        var id = $"{name.ToLowerInvariant()}-{slot}";
        var install = Path.Combine(GamesRoot, id, "game");
        Directory.CreateDirectory(install);
        WriteFile(Path.Combine(install, "game.exe"), "MZ not a real program");
        WriteFile(Path.Combine(install, "data", "base.pak"), "base content");
        string? saves = null;
        if (withSaves)
        {
            saves = Path.Combine(GamesRoot, id, "saves");
            Directory.CreateDirectory(saves);
            WriteFile(Path.Combine(saves, "slot1.sav"), "save one");
        }

        return new GameDefinition(
            Id: id,
            Name: name,
            Install: new InstallLocation(install),
            External: saves is null ? null : new ExternalLocation(saves),
            Launch: new LaunchConfig(LaunchMethod.Exe, "game.exe"),
            IgnorePaths: ignore,
            ModPathHints: modHints);
    }

    // A folder a GOG install would live in, made under the games folder.
    public string GogFolder(string name)
    {
        var folder = Path.Combine(GamesRoot, "gog", name);
        Directory.CreateDirectory(folder);
        WriteFile(Path.Combine(folder, "DeusEx.exe"), "MZ not a real program");
        return folder;
    }

    // Adds a game and fails the test if it was not simply added.
    public async Task<GameDefinition> AddGameAsync(GameDefinition? definition = null)
    {
        definition ??= NewGame();
        var added = await Games.AddAsync(definition);
        Assert.True(added.Ok, added.Error?.Detail);
        return Assert.IsType<AddGameOutcome.Added>(added.Value).Game;
    }

    // Creates a profile from what is on disk and waits for its capture to end.
    public async Task<(Profile Profile, JobSnapshot Job)> CreateProfileAsync(GameDefinition game, string name = "Vanilla")
    {
        var started = await Profiles.CreateAsync(game.Id, name, null, name);
        Assert.True(started.Ok, started.Error?.Detail);
        var job = await WaitForJobAsync(started.Value.JobId);
        var listed = await Profiles.ListAsync(game.Id, Token);
        Assert.True(listed.Ok, listed.Error?.Detail);
        var capture = Assert.IsType<CaptureResult>(job.Result);
        return (listed.Value.Single(p => p.Id == capture.ProfileId), job);
    }

    public async Task<JobSnapshot> WaitForJobAsync(Guid jobId, TimeSpan? limit = null)
    {
        var finished = new TaskCompletionSource<JobSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, JobSnapshot snapshot)
        {
            if (snapshot.JobId == jobId && snapshot.Status != JobStatus.Running)
            {
                finished.TrySetResult(snapshot);
            }
        }

        Jobs.JobChanged += OnChanged;
        try
        {
            if (Jobs.Get(jobId) is { Status: not JobStatus.Running } already)
            {
                return already;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(limit ?? TimeSpan.FromMinutes(5));
            await using var registration = timeout.Token.Register(() => finished.TrySetCanceled());
            return await finished.Task;
        }
        finally
        {
            Jobs.JobChanged -= OnChanged;
        }
    }

    // Waits until no job is running, for work a capture queued behind the caller's back.
    public async Task WaitForIdleAsync(TimeSpan? limit = null)
    {
        var until = DateTime.UtcNow + (limit ?? TimeSpan.FromMinutes(5));
        while (Jobs.List().Any(job => job.Status == JobStatus.Running))
        {
            Assert.True(DateTime.UtcNow < until, "jobs were still running at the deadline");
            await Task.Delay(25, Token);
        }
    }

    public static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    public static void WriteBytes(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    // Windows can still hold a folder git processes just worked in for a moment after the jobs that used them have ended, and refuses to move it then.
    public static void MoveFolder(string from, string to)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (Exception e) when (attempt < 20 && e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }

    // Every file under a folder with its content hash, so a test can prove nothing in it changed.
    public static IReadOnlyDictionary<string, string> Snapshot(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    file => Path.GetRelativePath(folder, file),
                    file => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))))
            : new Dictionary<string, string>();

    public void Dispose()
    {
        // Every backend this workspace built, so none stays on the list the process-exit handler closes.
        foreach (var backend in _built)
        {
            backend.Dispose();
        }

        Savepoint.Core.Tests.Git.GitWorkspace.DeleteDirectory(Root);
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }

    // Objects are named by their hash, so editing one would break it (the template's game files hold no paths); every other file is
    // searched for the old root, as written plainly, with forward slashes, and escaped for JSON.
    private static void RewriteRoot(string root, string oldRoot)
    {
        var plain = oldRoot;
        var slashes = oldRoot.Replace('\\', '/');
        var escaped = oldRoot.Replace("\\", "\\\\", StringComparison.Ordinal);
        var utf8 = new UTF8Encoding(false);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (IsKeptAsCopied(file, relative))
            {
                continue;
            }

            var text = utf8.GetString(File.ReadAllBytes(file));
            if (!text.Contains(plain, StringComparison.Ordinal) && !text.Contains(slashes, StringComparison.Ordinal) && !text.Contains(escaped, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.False(text.Contains('\0'), $"{relative} stores an absolute path and is binary, so it cannot be rewritten for a copy");
            var rewritten = text
                .Replace(escaped, root.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.Ordinal)
                .Replace(plain, root, StringComparison.Ordinal)
                .Replace(slashes, root.Replace('\\', '/'), StringComparison.Ordinal);
            File.SetAttributes(file, FileAttributes.Normal);
            File.WriteAllBytes(file, utf8.GetBytes(rewritten));
        }
    }

    // Git's untracked cache in the index names the folder it was taken in; git discards the cache when the folder differs.
    // Only an index directly inside a git directory; a game file that happens to be called index is searched like any other.
    private static bool IsKeptAsCopied(string file, string relative)
    {
        if (relative.Contains("/objects/", StringComparison.Ordinal))
        {
            return true;
        }

        var parent = Path.GetDirectoryName(file)!;
        return Path.GetFileName(file) == "index" && File.Exists(Path.Combine(parent, "HEAD")) && Directory.Exists(Path.Combine(parent, "objects"));
    }

    // Whatever form of the root the rewrite did not recognise still names the template's unique folder; a copy that shares it would
    // change the template's data under other tests.
    private static void AssertNoTemplateName(string root, string templateRoot)
    {
        var marker = Path.GetFileName(templateRoot);
        var utf8 = new UTF8Encoding(false);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!IsKeptAsCopied(file, relative))
            {
                Assert.False(utf8.GetString(File.ReadAllBytes(file)).Contains(marker, StringComparison.OrdinalIgnoreCase), $"{relative} still names the template folder {marker}");
            }
        }
    }

    private CoreBackend Build()
    {
        var options = new BackendOptions
        {
            Platform = Platform,
            InstallDirectory = InstallDirectory,
            DiskSpace = Disk,
            Sessions = Sessions,
            Launch = _ => new LaunchComponents(new UnavailableLaunchApi(), Sessions),
            LooseObjectThreshold = int.MaxValue,
            Git = Savepoint.Core.Git.GitBinary.Locate(Path.Combine(Root, "gitconfig")),
            // No test reads the real machine's Steam folder.
            SteamFolder = new Savepoint.Core.Tests.Artwork.FixedSteamFolder(null),
            GogGalaxyCache = new Savepoint.Core.Tests.Artwork.FixedGogGalaxyCache(null),
            GogInstalls = new Savepoint.Core.Tests.Library.FakeGogInstalls(null),
            // Nor Steam's registry or Galaxy's folders; these stay empty unless a test fills them.
            SteamActiveUser = () => null,
            GalaxyProgramData = Path.Combine(Root, "galaxy", "program-data"),
            GalaxyLocalAppData = Path.Combine(Root, "galaxy", "local-app-data"),
            GalaxyTempFolder = Path.Combine(Root, "galaxy", "copies"),
        };
        var backend = CoreBackend.Create(_configure is null ? options : _configure(options));
        _built.Add(backend);
        return backend;
    }
}

// Answers free-space questions from a table, passing real figures through unless a test says otherwise.
internal sealed class FakeDiskSpace : IDiskSpace
{
    private readonly DiskSpace _real = new();

    public long? Free { get; set; }

    public Func<string, long?>? FreePerPath { get; set; }

    public long? GetFreeBytes(string path) => FreePerPath?.Invoke(path) ?? Free ?? _real.GetFreeBytes(path);

    public long? GetTotalBytes(string path) => _real.GetTotalBytes(path);

    // Overrides the volume a path is on, for a test that puts two folders on different drives.
    public Func<string, string?>? VolumePerPath { get; set; }

    public string? GetVolumeId(string path) => VolumePerPath?.Invoke(path) ?? _real.GetVolumeId(path);
}

// Sessions a test opens and closes by hand.
internal sealed class FakeSessions : IGameSessions
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _running = [];

    public event EventHandler? Changed;

    public void Start(string gameId, string profileId)
    {
        lock (_gate)
        {
            _running[gameId] = profileId;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void End(string gameId)
    {
        lock (_gate)
        {
            _running.Remove(gameId);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool IsGameRunning(string gameId)
    {
        lock (_gate)
        {
            return _running.ContainsKey(gameId);
        }
    }

    public string? RunningProfileId(string gameId)
    {
        lock (_gate)
        {
            return _running.GetValueOrDefault(gameId);
        }
    }

    public IReadOnlyList<string> RunningGameIds()
    {
        lock (_gate)
        {
            return [.. _running.Keys];
        }
    }
}
