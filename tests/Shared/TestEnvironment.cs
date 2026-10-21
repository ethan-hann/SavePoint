using System.Runtime.CompilerServices;

namespace Savepoint.Testing;

// Linked into every test assembly so that no test writes outside the workspace, however the tests are launched.
internal static class TestEnvironment
{
    internal const string SolutionFileName = "Savepoint.slnx";

    private static readonly string[] _tempVariables = ["TMP", "TEMP", "TMPDIR"];

    // Null when the binaries run outside a checkout; the machine's temp folder stays in use then.
    internal static string? RepoRoot { get; private set; }

    internal static string? FindRepoRoot(string startDirectory, string markerFileName = SolutionFileName)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, markerFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    internal static string TempFolderFor(string repoRoot) => Path.Combine(repoRoot, ".tools", "tmp", OsFolderName());

    internal static string OsFolderName() =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

    // Child processes (the bundled git among them) inherit these, so Path.GetTempPath() agrees everywhere.
    internal static string? ConfineTemp(string startDirectory)
    {
        RepoRoot = FindRepoRoot(startDirectory);
        if (RepoRoot is null)
        {
            return null;
        }

        var temp = TempFolderFor(RepoRoot);
        Directory.CreateDirectory(temp);
        foreach (var name in _tempVariables)
        {
            Environment.SetEnvironmentVariable(name, temp);
        }

        return temp;
    }

    [ModuleInitializer]
    internal static void Initialize() => ConfineTemp(AppContext.BaseDirectory);
}
