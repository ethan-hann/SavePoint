using Savepoint.Core.Git;
using Savepoint.Core.Tests.Git;

namespace Savepoint.Core.Tests.Build;

// Tests make their temp files inside the workspace, so nothing lands in the machine's temp folder.
public sealed class TempConfinementTests
{
    private static readonly StringComparer _paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    [Fact]
    public void The_temp_folder_is_the_per_os_folder_under_the_workspace_tools_folder()
    {
        Assert.Equal(ExpectedTempFolder(), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), _paths);
    }

    [Fact]
    public void The_temp_folder_exists_before_the_first_test_asks_for_it()
    {
        Assert.True(Directory.Exists(ExpectedTempFolder()));
    }

    [Theory]
    [InlineData("TMP")]
    [InlineData("TEMP")]
    [InlineData("TMPDIR")]
    public void Every_variable_a_program_may_read_points_at_the_same_folder(string name)
    {
        var actual = Environment.GetEnvironmentVariable(name);

        Assert.NotNull(actual);
        Assert.Equal(ExpectedTempFolder(), Path.TrimEndingDirectorySeparator(actual), _paths);
    }

    [Fact]
    public void Git_child_processes_inherit_the_confined_folder()
    {
        using var workspace = new GitWorkspace();
        var variable = OperatingSystem.IsWindows() ? "TEMP" : "TMPDIR";

        var actual = workspace.Binary.BaseEnvironment.GetValueOrDefault(variable);

        Assert.NotNull(actual);
        Assert.Equal(ExpectedTempFolder(), Path.TrimEndingDirectorySeparator(actual), _paths);
    }

    [Fact]
    public async Task A_git_command_that_needs_no_repository_cannot_discover_the_checkout_above_the_temp_folder()
    {
        using var workspace = new GitWorkspace();

        var result = await workspace.RunPlainAsync(new GitInvocation(["rev-parse", "--git-dir"]) { AllowedExitCodes = [0, 128] });

        Assert.Equal(128, result.ExitCode);
        Assert.Contains("not a git repository", result.StandardError);
    }

    private static string ExpectedTempFolder() => Path.Combine(RepoRootAboveTheTestBinaries(), ".tools", "tmp", OsFolderName());

    private static string OsFolderName() =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

    private static string RepoRootAboveTheTestBinaries()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Savepoint.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Savepoint.slnx was not found above the test binaries.");
    }
}
