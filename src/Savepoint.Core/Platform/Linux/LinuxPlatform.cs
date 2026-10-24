using Savepoint.Core.Paths;

namespace Savepoint.Core.Platform.Linux;

/// <summary>The Linux tables: app folders (XDG), path tokens, and the denylist.</summary>
public sealed class LinuxPlatform : IPlatform
{
    private static readonly PathSyntax _syntax = PathSyntax.For(OsKind.Linux);

    private static readonly string[] _systemRoots =
    [
        "/etc", "/boot", "/efi", "/proc", "/sys", "/dev", "/usr", "/bin", "/sbin", "/lib", "/lib32", "/lib64", "/libx32",
    ];

    // udisks mounts removable drives under /run/media/<user>, the one part of /run a game may live in.
    private static readonly string[] _removableMedia = ["/run/media"];

    private static readonly string[] _broadRoots = ["/", "/home", "/mnt", "/media", "/opt", "/srv", "/var", "/tmp", "/root"];

    private readonly PlatformPaths _paths;
    private readonly string _xdgConfig;
    private readonly string _xdgData;
    private readonly string _xdgState;
    private readonly string _documents;
    private readonly string _desktop;
    private readonly string _downloads;
    private readonly SteamFolders _steam;

    /// <summary>Builds the tables from <paramref name="environment"/>; throws <see cref="ArgumentException"/> if its home folder is not an absolute path.</summary>
    /// <param name="environment">What to read: home, variables, and the user-dirs file.</param>
    /// <param name="processes">Replaces the real process inspector, for tests.</param>
    /// <param name="clock">Paces the retry of a failed Steam library read; the system clock when null.</param>
    public LinuxPlatform(PlatformEnvironment environment, IProcessInspector? processes = null, TimeProvider? clock = null)
    {
        _paths = new PlatformPaths(_syntax, environment);
        var home = _paths.Home;

        _xdgConfig = _paths.Variable("XDG_CONFIG_HOME") ?? _paths.Under(home, ".config");
        _xdgData = _paths.Variable("XDG_DATA_HOME") ?? _paths.Under(home, ".local/share");
        _xdgState = _paths.Variable("XDG_STATE_HOME") ?? _paths.Under(home, ".local/state");

        var userDirs = XdgUserDirs.Parse(_paths.ReadFile(_paths.Under(_xdgConfig, "user-dirs.dirs")), home, _syntax);
        _documents = userDirs.GetValueOrDefault("XDG_DOCUMENTS_DIR") ?? _paths.Under(home, "Documents");
        _desktop = userDirs.GetValueOrDefault("XDG_DESKTOP_DIR") ?? _paths.Under(home, "Desktop");
        _downloads = userDirs.GetValueOrDefault("XDG_DOWNLOAD_DIR") ?? _paths.Under(home, "Downloads");

        Directories = new AppDirectories(
            CatalogDir: _paths.Under(_xdgConfig, "savepoint"),
            DefaultStorageRoot: _paths.Under(_xdgData, "savepoint/storage"),
            LogsDir: _paths.Under(_xdgState, "savepoint/logs"),
            RuntimeDir: _paths.Under(_xdgState, "savepoint/runtime"),
            HomeDir: home);

        PathTokens = new Dictionary<string, string>
        {
            ["userHome"] = home,
            ["documents"] = _documents,
            ["appData"] = _xdgConfig,
            ["localAppData"] = _xdgData,
        };

        _steam = new SteamFolders(_paths, clock);
        Processes = processes ?? new LinuxProcessInspector();
    }

    /// <inheritdoc />
    public OsKind Os => OsKind.Linux;

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

        builder.Subtree("/run", "System runtime state", _removableMedia);

        foreach (var extra in extraBlockedRoots)
        {
            builder.Subtree(extra, "SavePoint's own data");
        }

        foreach (var root in _broadRoots)
        {
            builder.Exact(root, "Too broad to track");
        }

        // The folders that hold mounted drives are too broad, and so is a whole drive; a folder inside a drive is fine.
        foreach (var mediaRoot in _removableMedia)
        {
            builder.Exact(mediaRoot, "Holds every user's removable drives");
        }

        builder.ExactChildren("/mnt", "A whole mounted drive is too broad to track");
        builder.ExactChildren("/media", "A whole mounted drive is too broad to track");
        foreach (var user in UserNames())
        {
            foreach (var mediaParent in new[] { "/run/media", "/media" })
            {
                var userMedia = _paths.Under(mediaParent, user);
                builder.Exact(userMedia, "Holds the user's removable drives");
                builder.ExactChildren(userMedia, "A whole mounted drive is too broad to track");
            }
        }

        builder.Exact(home, "The home folder holds everything the user owns");
        foreach (var (path, reason) in new[]
        {
            (_paths.Under(home, ".config"), "Holds every app's settings"),
            (_paths.Under(home, ".local"), "Holds every app's data"),
            (_paths.Under(home, ".local/share"), "Holds every app's data"),
            (_paths.Under(home, ".local/state"), "Holds every app's state"),
            (_paths.Under(home, ".cache"), "Holds every app's cache"),
            (_paths.Under(home, ".var"), "Holds every Flatpak app's data"),
            (_paths.Under(home, ".var/app"), "Holds every Flatpak app's data"),
            (_paths.Under(home, ".steam"), "Steam's link farm"),
            (_xdgConfig, "Holds every app's settings"),
            (_xdgData, "Holds every app's data"),
            (_xdgState, "Holds every app's state"),
        })
        {
            builder.Exact(path, reason);
        }

        // Both the fixed folder and the XDG answer: users can move these.
        foreach (var (name, xdg) in new[] { ("Documents", _documents), ("Desktop", _desktop), ("Downloads", _downloads) })
        {
            builder.Exact(_paths.Under(home, name), $"The {name} folder is too broad to track");
            builder.Exact(xdg, $"The {name} folder is too broad to track");
        }

        foreach (var name in new[] { "Pictures", "Music", "Videos" })
        {
            builder.Exact(_paths.Under(home, name), $"The {name} folder is too broad to track");
        }

        foreach (var dataHome in new[] { _paths.Under(home, ".local/share"), _xdgData })
        {
            builder.Exact(_paths.Under(dataHome, "Steam"), "The Steam folder holds every Steam game");
        }

        _steam.AddTo(builder);

        // A Steam library can be on any drive or in a Flatpak, so the library's own folders are matched by name.
        builder.NameRule("steamapps holds every game in a Steam library", "steamapps");
        builder.NameRule("steamapps/common holds every game in a Steam library", "steamapps", "common");
        builder.NameRule("steamapps/compatdata holds every Proton prefix in a Steam library", "steamapps", "compatdata");

        return builder.Build();
    }

    // Every name the mount folders might use for this user: the login name can differ from the home folder's name.
    private IEnumerable<string> UserNames() =>
        new[] { _paths.RawVariable("USER"), _paths.RawVariable("LOGNAME"), _paths.Name(_paths.Home) }
            .OfType<string>()
            .Where(name => name is not ("." or "..") && !name.Contains('/'))
            .Distinct(StringComparer.Ordinal);
}
