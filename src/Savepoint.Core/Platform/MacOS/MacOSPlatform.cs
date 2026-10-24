using Savepoint.Core.Paths;

namespace Savepoint.Core.Platform.MacOS;

/// <summary>The macOS tables: app folders, path tokens, and the denylist.</summary>
public sealed class MacOSPlatform : IPlatform
{
    private static readonly PathSyntax _syntax = PathSyntax.For(OsKind.MacOS);

    // /etc is a link to /private/etc; both spellings are listed so a lexical check holds on any host.
    private static readonly string[] _systemRoots =
    [
        "/System", "/Library", "/bin", "/sbin", "/usr", "/private/etc", "/etc", "/dev", "/cores", "/private/var/db",
    ];

    // /private/var is not a subtree root: the user temp folder lives under it. /var and /tmp are links into it.
    private static readonly string[] _broadRoots =
    [
        "/", "/Users", "/Applications", "/Volumes", "/private", "/private/var", "/private/tmp", "/var", "/tmp", "/opt", "/Users/Shared",
    ];

    private readonly PlatformPaths _paths;
    private readonly string _appSupport;
    private readonly string _documents;
    private readonly SteamFolders _steam;

    /// <summary>Builds the tables from <paramref name="environment"/>; throws <see cref="ArgumentException"/> if its home folder is not an absolute path.</summary>
    /// <param name="environment">What to read: the home folder.</param>
    /// <param name="processes">Replaces the real process inspector, for tests.</param>
    /// <param name="clock">Paces the retry of a failed Steam library read; the system clock when null.</param>
    public MacOSPlatform(PlatformEnvironment environment, IProcessInspector? processes = null, TimeProvider? clock = null)
    {
        _paths = new PlatformPaths(_syntax, environment);
        var home = _paths.Home;

        _appSupport = _paths.Under(home, "Library/Application Support");
        _documents = _paths.Known(KnownFolder.Documents) ?? _paths.Under(home, "Documents");

        Directories = new AppDirectories(
            CatalogDir: _paths.Under(_appSupport, "SavePoint"),
            DefaultStorageRoot: _paths.Under(_appSupport, "SavePoint/Storage"),
            LogsDir: _paths.Under(home, "Library/Logs/SavePoint"),
            RuntimeDir: _paths.Under(home, "Library/Caches/SavePoint"),
            HomeDir: home);

        PathTokens = new Dictionary<string, string>
        {
            ["userHome"] = home,
            ["documents"] = _documents,
            ["appData"] = _appSupport,
            ["localAppData"] = _appSupport,
        };

        _steam = new SteamFolders(_paths, clock);
        Processes = processes ?? new MacOSProcessInspector();
    }

    /// <inheritdoc />
    public OsKind Os => OsKind.MacOS;

    /// <inheritdoc />
    public StringComparison PathComparison => _syntax.Comparison;

    /// <inheritdoc />
    public AppDirectories Directories { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> PathTokens { get; }

    /// <inheritdoc />
    public IProcessInspector Processes { get; }

    /// <inheritdoc />
    public PathDenylist Denylist(IEnumerable<string> extraBlockedRoots)
    {
        var home = _paths.Home;
        var builder = new DenylistBuilder(_syntax);

        foreach (var root in _systemRoots)
        {
            builder.Subtree(root, "System files");
        }

        foreach (var extra in extraBlockedRoots)
        {
            builder.Subtree(extra, "SavePoint's own data");
        }

        foreach (var root in _broadRoots)
        {
            builder.Exact(root, "Too broad to track");
        }

        // Every external drive mounts directly under /Volumes; a whole drive is too broad, a folder inside it is fine.
        builder.ExactChildren("/Volumes", "A whole mounted drive is too broad to track");

        builder.Exact(home, "The home folder holds everything the user owns");
        builder.Exact(_paths.Under(home, "Applications"), "Holds every app the user installed");
        builder.Exact(_paths.Under(home, "Library"), "Holds every app's data");
        builder.Exact(_appSupport, "Holds every app's data");
        builder.Exact(_paths.Under(home, "Library/Containers"), "Holds every sandboxed app's data");
        builder.Exact(_paths.Under(home, "Library/Preferences"), "Holds every app's settings");
        builder.Exact(_paths.Under(home, "Library/Caches"), "Holds every app's cache");
        builder.Exact(_paths.Under(home, "Library/Mobile Documents"), "iCloud Drive syncs everything beneath it");
        builder.Exact(_paths.Under(home, "Library/Mobile Documents/com~apple~CloudDocs"), "The iCloud Drive root syncs everything beneath it");

        foreach (var (name, known) in new[]
        {
            ("Documents", _documents), ("Desktop", _paths.Under(home, "Desktop")), ("Downloads", _paths.Under(home, "Downloads")),
        })
        {
            builder.Exact(_paths.Under(home, name), $"The {name} folder is too broad to track");
            builder.Exact(known, $"The {name} folder is too broad to track");
        }

        foreach (var name in new[] { "Pictures", "Music", "Movies" })
        {
            builder.Exact(_paths.Under(home, name), $"The {name} folder is too broad to track");
        }

        builder.Exact(_paths.Under(_appSupport, "Steam"), "The Steam folder holds every Steam game");

        _steam.AddTo(builder);

        // A Steam library can be on any drive, so the library's own folders are matched by name.
        builder.NameRule("steamapps holds every game in a Steam library", "steamapps");
        builder.NameRule("steamapps/common holds every game in a Steam library", "steamapps", "common");
        builder.NameRule("steamapps/compatdata holds every Proton prefix in a Steam library", "steamapps", "compatdata");

        return builder.Build();
    }
}
