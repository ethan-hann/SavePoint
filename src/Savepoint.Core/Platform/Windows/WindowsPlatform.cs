using Savepoint.Core.Paths;

namespace Savepoint.Core.Platform.Windows;

/// <summary>The Windows tables: app folders, path tokens, and the denylist.</summary>
public sealed class WindowsPlatform : IPlatform
{
    private static readonly PathSyntax _syntax = PathSyntax.For(OsKind.Windows);

    private readonly PlatformPaths _paths;
    private readonly string _roaming;
    private readonly string _local;
    private readonly string _documents;
    private readonly string _desktop;
    private readonly string _downloads;
    private readonly string _savedGames;
    private readonly string _systemDriveRoot;
    private readonly SteamFolders _steam;

    /// <summary>Builds the tables from <paramref name="environment"/>; throws <see cref="ArgumentException"/> if its home folder is not an absolute Windows path.</summary>
    /// <param name="environment">What to read: home, variables, known folders, drives.</param>
    /// <param name="processes">Replaces the real process inspector, for tests.</param>
    /// <param name="clock">Paces the retry of a failed Steam library read; the system clock when null.</param>
    public WindowsPlatform(PlatformEnvironment environment, IProcessInspector? processes = null, TimeProvider? clock = null)
    {
        _paths = new PlatformPaths(_syntax, environment);
        var home = _paths.Home;

        _roaming = _paths.Variable("APPDATA") ?? _paths.Known(KnownFolder.RoamingAppData) ?? _paths.Under(home, @"AppData\Roaming");
        _local = _paths.Variable("LOCALAPPDATA") ?? _paths.Known(KnownFolder.LocalAppData) ?? _paths.Under(home, @"AppData\Local");
        _documents = _paths.Known(KnownFolder.Documents) ?? _paths.Under(home, "Documents");
        _desktop = _paths.Known(KnownFolder.Desktop) ?? _paths.Under(home, "Desktop");
        _downloads = _paths.Known(KnownFolder.Downloads) ?? _paths.Under(home, "Downloads");
        _savedGames = _paths.Known(KnownFolder.SavedGames) ?? _paths.Under(home, "Saved Games");
        _systemDriveRoot = ReadSystemDriveRoot();
        _steam = new SteamFolders(_paths, clock);

        Directories = new AppDirectories(
            CatalogDir: _paths.Under(_roaming, "SavePoint"),
            DefaultStorageRoot: _paths.Under(_local, @"SavePoint\Storage"),
            LogsDir: _paths.Under(_local, @"SavePoint\Logs"),
            RuntimeDir: _paths.Under(_local, @"SavePoint\Runtime"),
            HomeDir: home);

        PathTokens = new Dictionary<string, string>
        {
            ["userHome"] = home,
            ["documents"] = _documents,
            ["appData"] = _roaming,
            ["localAppData"] = _local,
            ["localLow"] = _paths.Known(KnownFolder.LocalLow) ?? _paths.Under(home, @"AppData\LocalLow"),
            ["savedGames"] = _savedGames,
        };

        Processes = processes ?? new WindowsProcessInspector();
    }

    /// <inheritdoc />
    public OsKind Os => OsKind.Windows;

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

        foreach (var windows in Sources([_paths.Variable("SystemRoot"), _paths.Variable("windir"), _paths.Known(KnownFolder.Windows)], "Windows"))
        {
            builder.Subtree(windows, "Windows system files");
        }

        foreach (var extra in extraBlockedRoots)
        {
            builder.Subtree(extra, "SavePoint's own data");
        }

        builder.LocalHostNames(_paths.LocalHostNames());

        foreach (var drive in _paths.DriveRoots())
        {
            builder.Exact(drive, "A whole drive is too broad to track");
        }

        var programFilesX86 = Sources([_paths.Variable("ProgramFiles(x86)"), _paths.Known(KnownFolder.ProgramFilesX86)], "Program Files (x86)");
        foreach (var programFiles in Sources([_paths.Variable("ProgramFiles"), _paths.Variable("ProgramW6432"), _paths.Known(KnownFolder.ProgramFiles)], "Program Files"))
        {
            builder.Exact(programFiles, "Program Files holds every installed app");
        }

        foreach (var x86 in programFilesX86)
        {
            builder.Exact(x86, "Program Files (x86) holds every installed 32-bit app");
            builder.Exact(_paths.Under(x86, "Steam"), "The Steam folder holds every Steam game");
        }

        foreach (var programData in Sources([_paths.Variable("ProgramData"), _paths.Known(KnownFolder.ProgramData)], "ProgramData"))
        {
            builder.Exact(programData, "ProgramData is shared by all users");
        }

        builder.ExactIfPresent(_paths.Parent(home), "The Users folder holds every account");
        builder.Exact(home, "The user profile holds everything the user owns");
        builder.ExactIfPresent(_paths.Variable("PUBLIC"), "The Public profile is shared by all users");

        builder.Exact(_paths.Under(home, "AppData"), "AppData holds every app's data");
        builder.Exact(_paths.Under(home, @"AppData\Roaming"), "Roaming holds every app's settings");
        builder.Exact(_paths.Under(home, @"AppData\Local"), "Local holds every app's data");
        builder.Exact(_paths.Under(home, @"AppData\LocalLow"), "LocalLow holds every sandboxed app's data");
        builder.Exact(_roaming, "Roaming holds every app's settings");
        builder.Exact(_local, "Local holds every app's data");

        // Both the fixed folder and the shell's answer: OneDrive backup moves the real folder elsewhere.
        foreach (var (name, known) in new[]
        {
            ("Documents", _documents), ("Desktop", _desktop), ("Downloads", _downloads), ("Saved Games", _savedGames),
        })
        {
            builder.Exact(_paths.Under(home, name), $"The {name} folder is too broad to track");
            builder.Exact(known, $"The {name} folder is too broad to track");
        }

        builder.ExactIfPresent(_paths.Known(KnownFolder.LocalLow), "LocalLow holds every sandboxed app's data");
        foreach (var name in new[] { "Pictures", "Music", "Videos" })
        {
            builder.Exact(_paths.Under(home, name), $"The {name} folder is too broad to track");
        }

        foreach (var oneDrive in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            builder.ExactIfPresent(_paths.Variable(oneDrive), "The OneDrive root syncs everything beneath it");
        }

        _steam.AddTo(builder);

        foreach (var folder in _paths.GogFolders())
        {
            builder.Exact(folder, "GOG Galaxy's folders hold every GOG game");
        }

        // A Steam library can be on any drive, so the library's own folders are matched by name.
        builder.NameRule("steamapps holds every game in a Steam library", "steamapps");
        builder.NameRule("steamapps/common holds every game in a Steam library", "steamapps", "common");
        builder.NameRule("steamapps/compatdata holds every Proton prefix in a Steam library", "steamapps", "compatdata");

        return builder.Build();
    }

    // Every source found is protected, since each one is a place a program may reach the folder by; the system-drive default
    // applies only when nothing says where the folder is.
    private IEnumerable<string> Sources(IEnumerable<string?> found, string defaultName)
    {
        var sources = found.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return sources.Count > 0 ? sources : [_paths.Under(_systemDriveRoot, defaultName)];
    }

    // %SystemDrive% is "C:" with no separator, so it cannot go through the absolute-path reader.
    private string ReadSystemDriveRoot()
    {
        var drive = _paths.RawVariable("SystemDrive");
        return drive is { Length: 2 } && char.IsAsciiLetter(drive[0]) && drive[1] == ':'
            ? _syntax.Normalize(drive + @"\")
            : @"C:\";
    }
}
