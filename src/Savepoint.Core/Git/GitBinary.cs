using System.Collections;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Text;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// Never consults a git installed on the machine or the user's own git configuration (D13.24).
public sealed class GitBinary
{
    public const string RootEnvironmentVariable = "SAVEPOINT_GIT_ROOT";

    private const string _emptyExcludesFileName = "excludes";
    private const string _emptyAttributesFileName = "attributes";

    internal GitBinary(string root, string configDirectory, IReadOnlyDictionary<string, string>? parentEnvironment, GitHost host)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(configDirectory);

        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        ConfigDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configDirectory));
        Layout = DetectLayout(Root, host);
        ExecutablePath = Layout == GitBundleLayout.MinGit
            ? Path.Combine(Root, "cmd", "git.exe")
            : Path.Combine(Root, "bin", "git");
        GlobalConfigPath = Path.Combine(ConfigDirectory, "gitconfig");
        EnvironmentComparer = host.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        WriteConfigFiles(host);
        BaseEnvironment = new ReadOnlyDictionary<string, string>(BuildBaseEnvironment(parentEnvironment ?? ProcessEnvironment(host), host));
    }

    public string Root { get; }

    public string ConfigDirectory { get; }

    public GitBundleLayout Layout { get; }

    public string ExecutablePath { get; }

    public string GlobalConfigPath { get; }

    public IReadOnlyDictionary<string, string> BaseEnvironment { get; }

    internal StringComparer EnvironmentComparer { get; }

    public static GitBinary Locate(string configDirectory) =>
        new(
            ResolveRoot(Environment.GetEnvironmentVariable(RootEnvironmentVariable), AppContext.BaseDirectory),
            configDirectory,
            null,
            GitHost.Current);

    public static GitBinary At(string root, string configDirectory) => new(root, configDirectory, null, GitHost.Current);

    internal static string ResolveRoot(string? overrideRoot, string baseDirectory) =>
        string.IsNullOrEmpty(overrideRoot) ? Path.Combine(baseDirectory, "git") : overrideRoot;

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

        environment["GIT_TERMINAL_PROMPT"] = "0";
        environment["GIT_ASKPASS"] = string.Empty;

        // English, locale-independent output: progress parsing and error text depend on it.
        environment["LANG"] = "C";
        environment["LC_ALL"] = "C";
        environment["LANGUAGE"] = "C";

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
