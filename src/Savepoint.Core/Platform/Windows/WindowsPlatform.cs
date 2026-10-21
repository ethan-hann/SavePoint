using Savepoint.Core.Paths;

namespace Savepoint.Core.Platform.Windows;

public sealed class WindowsPlatform : IPlatform
{
    private static readonly PathSyntax _syntax = PathSyntax.For(OsKind.Windows);

    private readonly PlatformPaths _paths;
    private readonly string _roaming;
    private readonly string _local;
    private readonly string _localLow;
    private readonly string _documents;
    private readonly string _desktop;
    private readonly string _downloads;
    private readonly string _savedGames;
    private readonly string _systemDriveRoot;

    public WindowsPlatform(PlatformEnvironment environment, IProcessInspector? processes = null)
    {
        _paths = new PlatformPaths(_syntax, environment);
        var home = _paths.Home;

        _roaming = _paths.Variable("APPDATA") ?? _paths.Known(KnownFolder.RoamingAppData) ?? _paths.Under(home, @"AppData\Roaming");
        _local = _paths.Variable("LOCALAPPDATA") ?? _paths.Known(KnownFolder.LocalAppData) ?? _paths.Under(home, @"AppData\Local");
        _localLow = _paths.Under(home, @"AppData\LocalLow");
        _documents = _paths.Known(KnownFolder.Documents) ?? _paths.Under(home, "Documents");
        _desktop = _paths.Known(KnownFolder.Desktop) ?? _paths.Under(home, "Desktop");
        _downloads = _paths.Known(KnownFolder.Downloads) ?? _paths.Under(home, "Downloads");
        _savedGames = _paths.Known(KnownFolder.SavedGames) ?? _paths.Under(home, "Saved Games");
        _systemDriveRoot = ReadSystemDriveRoot();

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
            ["localLow"] = _localLow,
            ["savedGames"] = _savedGames,
        };

        Processes = processes ?? new WindowsProcessInspector();
    }

    public OsKind Os => OsKind.Windows;

    public StringComparison PathComparison => _syntax.Comparison;

    public AppDirectories Directories { get; }

    public IReadOnlyDictionary<string, string> PathTokens { get; }

    public IProcessInspector Processes { get; }

    public PathDenylist Denylist(IEnumerable<string> extraBlockedRoots)
    {
        var home = _paths.Home;
        var builder = new DenylistBuilder(_syntax);

        builder.Subtree(WindowsDirectory(), "Windows system files");
        foreach (var extra in extraBlockedRoots)
        {
            builder.Subtree(extra, "SavePoint's own data");
        }

        foreach (var drive in _paths.DriveRoots())
        {
            builder.Exact(drive, "A whole drive is too broad to track");
        }

        var programFilesX86 = _paths.Variable("ProgramFiles(x86)") ?? _paths.Known(KnownFolder.ProgramFilesX86)
            ?? _paths.Under(_systemDriveRoot, "Program Files (x86)");
        var programFiles = _paths.Variable("ProgramFiles") ?? _paths.Known(KnownFolder.ProgramFiles)
            ?? _paths.Under(_systemDriveRoot, "Program Files");
        var programData = _paths.Variable("ProgramData") ?? _paths.Known(KnownFolder.ProgramData)
            ?? _paths.Under(_systemDriveRoot, "ProgramData");

        builder.Exact(programFiles, "Program Files holds every installed app");
        builder.ExactIfPresent(_paths.Variable("ProgramW6432"), "Program Files holds every installed app");
        builder.Exact(programFilesX86, "Program Files (x86) holds every installed 32-bit app");
        builder.Exact(programData, "ProgramData is shared by all users");
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

        var steam = _paths.Under(programFilesX86, "Steam");
        builder.Exact(steam, "The Steam folder holds every Steam game");
        builder.Exact(_paths.Under(steam, "steamapps"), "steamapps holds every Steam game");
        builder.Exact(_paths.Under(steam, @"steamapps\common"), "steamapps/common holds every Steam game");

        return builder.Build();
    }

    private string WindowsDirectory() =>
        _paths.Variable("SystemRoot") ?? _paths.Variable("windir") ?? _paths.Known(KnownFolder.Windows)
        ?? _paths.Under(_systemDriveRoot, "Windows");

    // %SystemDrive% is "C:" with no separator, so it cannot go through the absolute-path reader.
    private string ReadSystemDriveRoot()
    {
        var drive = _paths.RawVariable("SystemDrive");
        return drive is { Length: 2 } && char.IsAsciiLetter(drive[0]) && drive[1] == ':'
            ? _syntax.Normalize(drive + @"\")
            : @"C:\";
    }
}
