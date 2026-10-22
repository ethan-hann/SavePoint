using System.Collections;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Text;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

/// <summary>
/// The git bundle shipped with SavePoint and the environment every git process starts from. It never consults a git installed
/// on the machine or the user's own git configuration (D13.24): the global configuration is a file SavePoint writes in its
/// own configuration folder, no inherited <c>GIT_*</c> variable survives, and output is pinned to English.
/// </summary>
public sealed class GitBinary
{
    /// <summary>The environment variable that points at a bundle in another place than next to the app (tests and development use it).</summary>
    public const string RootEnvironmentVariable = "SAVEPOINT_GIT_ROOT";

    private const string _emptyExcludesFileName = "excludes";
    private const string _emptyAttributesFileName = "attributes";
    private const string _noHooksFolderName = "no-hooks";

    // MinGit keeps the real git in one of these, by architecture; cmd/git.exe only sets PATH and starts it.
    private static readonly string[] _minGitToolchains = ["ucrt64", "mingw64", "clangarm64"];

    private readonly string? _minGitToolchain;

    internal GitBinary(string root, string configDirectory, IReadOnlyDictionary<string, string>? parentEnvironment, GitHost host)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(configDirectory);

        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        ConfigDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configDirectory));
        Layout = DetectLayout(Root, host);
        _minGitToolchain = Layout == GitBundleLayout.MinGit ? FindMinGitToolchain(Root) : null;
        ExecutablePath = Layout switch
        {
            GitBundleLayout.MinGit when _minGitToolchain is not null => Path.Combine(Root, _minGitToolchain, "bin", "git.exe"),
            GitBundleLayout.MinGit => Path.Combine(Root, "cmd", "git.exe"),
            _ => Path.Combine(Root, "bin", "git"),
        };
        GlobalConfigPath = Path.Combine(ConfigDirectory, "gitconfig");
        EnvironmentComparer = host.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        WriteConfigFiles(host);
        BaseEnvironment = new ReadOnlyDictionary<string, string>(BuildBaseEnvironment(parentEnvironment ?? ProcessEnvironment(host), host));
    }

    /// <summary>The folder the bundle lives in, absolute and without a trailing separator.</summary>
    public string Root { get; }

    /// <summary>The folder where SavePoint keeps the git configuration it writes for the bundle, absolute.</summary>
    public string ConfigDirectory { get; }

    /// <summary>Which shape of bundle <see cref="Root"/> holds; before a bundle is installed, the one this operating system uses.</summary>
    public GitBundleLayout Layout { get; }

    /// <summary>The git executable inside the bundle; it may not exist (see <see cref="VerifyAsync"/>).</summary>
    public string ExecutablePath { get; }

    /// <summary>The global configuration file git is pointed at; it replaces the user's and the system's.</summary>
    public string GlobalConfigPath { get; }

    /// <summary>The empty folder every git process is told to look for hooks in, so a hook a repository carries never runs.</summary>
    public string NoHooksPath => Path.Combine(ConfigDirectory, _noHooksFolderName);

    /// <summary>
    /// The environment every git process starts from: the parent's, minus every <c>GIT_*</c> variable, plus the settings that
    /// isolate git (no system configuration, the SavePoint global configuration, a fixed author, no prompts, English output, and
    /// no hooks or file system monitor: <c>core.hooksPath</c> and <c>core.fsmonitor</c> are set in the environment, which outranks a repository's own config).
    /// </summary>
    public IReadOnlyDictionary<string, string> BaseEnvironment { get; }

    internal StringComparer EnvironmentComparer { get; }

    /// <summary>
    /// Finds the bundle that ships with the app (the <c>git</c> folder next to it, or the folder named by
    /// <see cref="RootEnvironmentVariable"/>) and writes the configuration files git will be pointed at. Nothing is run and
    /// nothing is checked: a missing bundle shows up in <see cref="VerifyAsync"/>.
    /// </summary>
    /// <param name="configDirectory">Where to write the git configuration; created when missing.</param>
    /// <returns>The located bundle.</returns>
    public static GitBinary Locate(string configDirectory) =>
        new(
            ResolveRoot(Environment.GetEnvironmentVariable(RootEnvironmentVariable), AppContext.BaseDirectory),
            configDirectory,
            null,
            GitHost.Current);

    /// <summary>Uses the bundle in a given folder, otherwise like <see cref="Locate"/>.</summary>
    /// <param name="root">The bundle's folder.</param>
    /// <param name="configDirectory">Where to write the git configuration; created when missing.</param>
    /// <returns>The bundle.</returns>
    public static GitBinary At(string root, string configDirectory) => new(root, configDirectory, null, GitHost.Current);

    internal static string ResolveRoot(string? overrideRoot, string baseDirectory) =>
        string.IsNullOrEmpty(overrideRoot) ? Path.Combine(baseDirectory, "git") : overrideRoot;

    /// <summary>Checks that the bundle is there and runs: the executable exists (and is executable on Linux and macOS) and <c>git --version</c> answers.</summary>
    /// <param name="ct">Cancels the check.</param>
    /// <returns>The version line git printed, such as <c>git version 2.53.0</c>.</returns>
    /// <exception cref="Savepoint.Core.Errors.GitUnavailableException">The executable is missing, cannot be run, or does not answer like git.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async Task<string> VerifyAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ExecutablePath))
        {
            throw Unavailable($"git was not found at {ExecutablePath}", null);
        }

        if (!OperatingSystem.IsWindows() && !HasExecuteBit(ExecutablePath))
        {
            throw Unavailable($"{ExecutablePath} is not executable", null);
        }

        try
        {
            var result = await new GitRunner(this).RunAsync(new GitInvocation(["--version"]), ct).ConfigureAwait(false);
            var version = result.StandardOutput.Trim();
            return version.StartsWith("git version ", StringComparison.Ordinal)
                ? version
                : throw Unavailable($"git --version printed '{version}'", null);
        }
        catch (GitCommandException e)
        {
            throw Unavailable($"git --version exited with code {e.ExitCode}: {e.StandardError.Trim()}", e);
        }
    }

    private static GitBundleLayout DetectLayout(string root, GitHost host)
    {
        if (File.Exists(Path.Combine(root, "cmd", "git.exe")))
        {
            return GitBundleLayout.MinGit;
        }

        if (File.Exists(Path.Combine(root, "bin", "git")))
        {
            return GitBundleLayout.Dugite;
        }

        // Nothing there yet: guess from the host so the path in the error message is the one that was expected.
        return host.IsWindows ? GitBundleLayout.MinGit : GitBundleLayout.Dugite;
    }

    private static string? FindMinGitToolchain(string root) =>
        _minGitToolchains.FirstOrDefault(toolchain => File.Exists(Path.Combine(root, toolchain, "bin", "git.exe")));

    [UnsupportedOSPlatform("windows")]
    private static bool HasExecuteBit(string path) =>
        (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;

    private static Dictionary<string, string> ProcessEnvironment(GitHost host)
    {
        var environment = new Dictionary<string, string>(host.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        }

        return environment;
    }

    private GitUnavailableException Unavailable(string message, Exception? inner) => new(ExecutablePath, message, inner);

    private Dictionary<string, string> BuildBaseEnvironment(IReadOnlyDictionary<string, string> parent, GitHost host)
    {
        var environment = new Dictionary<string, string>(EnvironmentComparer);
        var nameComparison = host.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var (name, value) in parent)
        {
            // Any GIT_* variable the parent has could redirect git (GIT_DIR, GIT_INDEX_FILE, GIT_CONFIG_*, ...).
            if (!name.StartsWith("GIT_", nameComparison))
            {
                environment[name] = value;
            }
        }

        environment["GIT_CONFIG_NOSYSTEM"] = "1";
        environment["GIT_ATTR_NOSYSTEM"] = "1";
        environment["GIT_CONFIG_GLOBAL"] = GlobalConfigPath;

        environment["GIT_AUTHOR_NAME"] = "SavePoint";
        environment["GIT_AUTHOR_EMAIL"] = "savepoint@localhost";
        environment["GIT_COMMITTER_NAME"] = "SavePoint";
        environment["GIT_COMMITTER_EMAIL"] = "savepoint@localhost";

        // A repository's own config and hooks folder can name programs git would run (core.fsmonitor, a post-commit hook, a program to
        // sign with, ...). Settings given this way outrank every config file, so no repository, SavePoint's or one found elsewhere, can
        // undo them.
        environment["GIT_CONFIG_COUNT"] = "4";
        environment["GIT_CONFIG_KEY_0"] = "core.hooksPath";
        environment["GIT_CONFIG_VALUE_0"] = NoHooksPath;
        environment["GIT_CONFIG_KEY_1"] = "core.fsmonitor";
        environment["GIT_CONFIG_VALUE_1"] = "false";
        environment["GIT_CONFIG_KEY_2"] = "commit.gpgSign";
        environment["GIT_CONFIG_VALUE_2"] = "false";
        environment["GIT_CONFIG_KEY_3"] = "tag.gpgSign";
        environment["GIT_CONFIG_VALUE_3"] = "false";

        environment["GIT_TERMINAL_PROMPT"] = "0";
        environment["GIT_ASKPASS"] = string.Empty;

        // English, locale-independent output: progress parsing and error text depend on it.
        environment["LANG"] = "C";
        environment["LC_ALL"] = "C";
        environment["LANGUAGE"] = "C";

        if (_minGitToolchain is not null)
        {
            // What cmd/git.exe would do: git starts its own subcommands (repack's pack-objects) by finding git.exe on PATH.
            var bin = Path.Combine(Root, _minGitToolchain, "bin");
            var usrBin = Path.Combine(Root, "usr", "bin");
            environment["PATH"] = environment.TryGetValue("PATH", out var inherited) && inherited.Length > 0
                ? $"{bin};{usrBin};{inherited}"
                : $"{bin};{usrBin}";
            environment["GIT_EXEC_PATH"] = Path.Combine(Root, _minGitToolchain, "libexec", "git-core");
        }

        if (Layout == GitBundleLayout.Dugite)
        {
            environment["GIT_EXEC_PATH"] = Path.Combine(Root, "libexec", "git-core");
            environment["GIT_TEMPLATE_DIR"] = Path.Combine(Root, "share", "git-core", "templates");
            if (host.IsLinux)
            {
                environment["PREFIX"] = Root;
            }
        }

        return environment;
    }

    private void WriteConfigFiles(GitHost host)
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(NoHooksPath);
        var excludes = Path.Combine(ConfigDirectory, _emptyExcludesFileName);
        var attributes = Path.Combine(ConfigDirectory, _emptyAttributesFileName);

        // GIT_CONFIG_GLOBAL leaves ~/.config/git/ignore and attributes readable; empty files stop them dropping files from a capture.
        WriteIfChanged(excludes, string.Empty);
        WriteIfChanged(attributes, string.Empty);
        WriteIfChanged(GlobalConfigPath, BuildConfigText(excludes, attributes, host));
    }

    private static string BuildConfigText(string excludesPath, string attributesPath, GitHost host)
    {
        string[] lines =
        [
            "[safe]",
            "\tdirectory = *",
            "[core]",
            "\tlongpaths = true",
            "\tautocrlf = false",
            "\tquotePath = false",
            $"\texcludesFile = {Quote(excludesPath, host)}",
            $"\tattributesFile = {Quote(attributesPath, host)}",
            "[gc]",
            "\tauto = 0",
            "[maintenance]",
            "\tauto = false",
            "[commit]",
            "\tgpgsign = false",
            "[tag]",
            "\tgpgsign = false",
            "[advice]",
            "\tdetachedHead = false",
            "[init]",
            "\tdefaultBranch = savepoint-unborn",
        ];
        return string.Join('\n', lines) + "\n";
    }

    // Git accepts forward slashes on Windows, which spares the escaping of backslashes.
    private static string Quote(string path, GitHost host)
    {
        var text = host.IsWindows ? path.Replace('\\', '/') : path.Replace("\\", "\\\\", StringComparison.Ordinal);
        return "\"" + text.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    // Leaves an identical file untouched so a git process reading it is never interrupted.
    private static void WriteIfChanged(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content)
        {
            return;
        }

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }
}
