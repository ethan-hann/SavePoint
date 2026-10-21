using Savepoint.Core.Platform;

namespace Savepoint.Core.Services.Exe;

/// <summary>Finds the files in an install folder that could launch the game, for the game editor's suggestions.</summary>
/// <remarks>
/// The scan is bounded so a huge install cannot stall the editor: at most <see cref="MaxEntriesVisited"/> entries
/// are looked at, breadth first so shallow files are found before deep ones. Installers, redistributables, crash
/// handlers and anti-cheat helpers are left out, as are hidden folders, links (never followed), and folders that
/// only hold runtimes and third-party binaries.
/// </remarks>
public sealed class ExecutableScanner
{
    /// <summary>The deepest folder level read, counting the install folder itself as level 0.</summary>
    public const int MaxDepth = 4;

    /// <summary>The most folder entries looked at before the scan stops.</summary>
    public const int MaxEntriesVisited = 5000;

    /// <summary>The most paths returned.</summary>
    public const int MaxResults = 50;

    private static readonly string[] _skippedFolderNames = ["_commonredist", "redist", "vc_redist", "support", "__installer", "monobleedingedge"];

    // Compared against the folder's path below the install folder, with forward slashes, in lower case.
    private const string _thirdPartyBinaries = "engine/binaries/thirdparty";

    // Specific helper names rather than the bare words "crash" and "setup", which are also in real game names.
    private static readonly string[] _noiseNamePrefixes =
        ["unins", "dxsetup", "dxwebsetup", "dotnetfx", "directx_", "oalinst", "easyanticheat", "beservice", "crashpad_handler"];

    private static readonly string[] _noiseNameParts = ["installer", "redist", "crashhandler", "crashreport"];
    private static readonly string[] _noiseNameSuffixes = ["_setup", "-setup", " setup"];

    // Libraries and Windows files that carry an execute bit on some file systems but are not programs a player starts.
    private static readonly string[] _notNativeExtensions = [".so", ".dll", ".exe", ".dylib", ".a", ".o", ".pdb"];

    // Hidden entries are real; the default options would skip them before the scanner could decide.
    private static readonly EnumerationOptions _everything = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
    };

    private readonly ExecutableKinds _kinds;

    /// <summary>Creates a scanner that suggests the kinds of file that suit a platform.</summary>
    /// <param name="platform">The platform whose <see cref="IPlatform.Os"/> picks the kinds, as <see cref="KindsFor"/> does.</param>
    public ExecutableScanner(IPlatform platform)
        : this(KindsFor(platform.Os))
    {
    }

    /// <summary>Creates a scanner that suggests exactly the given kinds of file.</summary>
    /// <param name="kinds">What to suggest; <see cref="KindsFor"/> gives the usual choice for an OS.</param>
    public ExecutableScanner(ExecutableKinds kinds)
    {
        _kinds = kinds;
    }

    /// <summary>The kinds worth suggesting on an OS.</summary>
    /// <param name="os">The OS the game runs on.</param>
    /// <remarks>Linux also suggests <c>.exe</c> files, because Windows games run there through Proton.</remarks>
    public static ExecutableKinds KindsFor(OsKind os) => os switch
    {
        OsKind.Windows => ExecutableKinds.WindowsExe,
        OsKind.MacOS => ExecutableKinds.MacApp,
        _ => ExecutableKinds.LinuxNative | ExecutableKinds.WindowsExe,
    };

    // A seam so tests can reach the limit without creating thousands of files.
    internal int EntryLimit { get; init; } = MaxEntriesVisited;

    internal Action<string>? Visited { get; init; }

    // A seam for tests on systems without a Unix file mode.
    internal Func<string, bool>? HasExecuteBit { get; init; }

    /// <summary>Lists launchable files under <paramref name="installFolder"/>.</summary>
    /// <param name="installFolder">The game's install folder.</param>
    /// <param name="cancellationToken">Stops the scan between entries.</param>
    /// <returns>
    /// Up to <see cref="MaxResults"/> paths relative to the install folder, with the OS separator, most likely game
    /// executable first: shallower before deeper, then larger before smaller, then by name. A macOS bundle is
    /// returned as the bundle's own path and never entered. A missing folder, or a path that is a file, gives an empty list.
    /// </returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <remarks>Runs on the thread pool.</remarks>
    public Task<IReadOnlyList<string>> ListAsync(string installFolder, CancellationToken cancellationToken = default) =>
        Task.Run(() => List(installFolder, cancellationToken), cancellationToken);

    internal IReadOnlyList<string> List(string installFolder, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(installFolder);
        if (!Directory.Exists(root))
        {
            return [];
        }

        var found = new List<Candidate>();
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));
        var visited = 0;
        while (pending.Count > 0 && visited < EntryLimit)
        {
            var (folder, depth) = pending.Dequeue();
            try
            {
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", _everything))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (visited++ >= EntryLimit)
                    {
                        break;
                    }

                    Visited?.Invoke(entry.FullName);
                    Visit(entry, root, depth, pending, found);
                }
            }
            catch (Exception e) when (e is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
            {
                // A folder that vanished or is off limits has nothing to suggest; the rest of the install is still worth scanning.
            }
        }

        return [.. found
            .OrderBy(c => c.Depth)
            .ThenByDescending(c => c.Length)
            .ThenBy(c => c.RelativePath, StringComparer.Ordinal)
            .Take(MaxResults)
            .Select(c => c.RelativePath)];
    }

    private void Visit(FileSystemInfo entry, string root, int depth, Queue<(string Path, int Depth)> pending, List<Candidate> found)
    {
        // Links can point anywhere, including back up the tree.
        if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return;
        }

        if (entry is DirectoryInfo folder)
        {
            if (_kinds.HasFlag(ExecutableKinds.MacApp) && entry.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsNoise(Path.GetFileNameWithoutExtension(entry.Name)))
                {
                    found.Add(new Candidate(Path.GetRelativePath(root, entry.FullName), depth, 0));
                }

                return;
            }

            if (depth < MaxDepth && !IsSkippedFolder(folder, root))
            {
                pending.Enqueue((entry.FullName, depth + 1));
            }

            return;
        }

        if (entry is FileInfo file && IsLaunchable(file))
        {
            found.Add(new Candidate(Path.GetRelativePath(root, file.FullName), depth, file.Length));
        }
    }

    private bool IsLaunchable(FileInfo file)
    {
        if (file.Name.StartsWith('.') || IsNoise(Path.GetFileNameWithoutExtension(file.Name)))
        {
            return false;
        }

        var extension = file.Extension;
        if (_kinds.HasFlag(ExecutableKinds.WindowsExe) && extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!_kinds.HasFlag(ExecutableKinds.LinuxNative))
        {
            return false;
        }

        if (extension.Equals(".sh", StringComparison.OrdinalIgnoreCase) || extension.Equals(".x86_64", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !IsNotNative(file.Name) && ExecuteBitSet(file.FullName) && LooksLikeAProgram(file.FullName);
    }

    private static bool IsNotNative(string name) =>
        _notNativeExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        || name.Contains(".so.", StringComparison.OrdinalIgnoreCase);

    private bool ExecuteBitSet(string path)
    {
        if (HasExecuteBit is { } custom)
        {
            return custom(path);
        }

        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // File systems that mark every file executable (NTFS and exFAT mounts) would otherwise flood the list with data files.
    private static bool LooksLikeAProgram(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> head = stackalloc byte[4];
            var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return read >= 4 && head[..4].SequenceEqual("\u007fELF"u8)
                || read >= 2 && head[..2].SequenceEqual("#!"u8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsNoise(string nameWithoutExtension)
    {
        return _noiseNamePrefixes.Any(prefix => nameWithoutExtension.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            || _noiseNameParts.Any(part => nameWithoutExtension.Contains(part, StringComparison.OrdinalIgnoreCase))
            || _noiseNameSuffixes.Any(suffix => nameWithoutExtension.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            || IsSetupProgram(nameWithoutExtension);
    }

    // "setup", "setup64" and "setup_en" are installers; "SetupWizardGame" is a game whose name starts the same way.
    private static bool IsSetupProgram(string nameWithoutExtension) =>
        nameWithoutExtension.StartsWith("setup", StringComparison.OrdinalIgnoreCase)
        && (nameWithoutExtension.Length == "setup".Length || !char.IsLetter(nameWithoutExtension["setup".Length]));

    private static bool IsSkippedFolder(DirectoryInfo folder, string root)
    {
        if (folder.Name.StartsWith('.') || folder.Attributes.HasFlag(FileAttributes.Hidden))
        {
            return true;
        }

        if (_skippedFolderNames.Contains(folder.Name, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var relative = Path.GetRelativePath(root, folder.FullName).Replace('\\', '/');
        return relative.Equals(_thirdPartyBinaries, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct Candidate(string RelativePath, int Depth, long Length);
}
