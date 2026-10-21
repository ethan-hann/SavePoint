using Savepoint.Core.Tests.Git;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

public sealed class TestEnvironmentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "savepoint-env-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => GitWorkspace.DeleteDirectory(_root);

    [Fact]
    public void The_repo_root_is_the_nearest_folder_above_that_holds_the_solution_file()
    {
        var inner = Path.Combine(_root, "worktree");
        var deep = Path.Combine(inner, "tests", "bin");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(_root, TestEnvironment.SolutionFileName), string.Empty);
        File.WriteAllText(Path.Combine(inner, TestEnvironment.SolutionFileName), string.Empty);

        Assert.Equal(inner, TestEnvironment.FindRepoRoot(deep));
    }

    [Fact]
    public void The_start_folder_itself_counts_when_it_holds_the_solution_file()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, TestEnvironment.SolutionFileName), string.Empty);

        Assert.Equal(_root, TestEnvironment.FindRepoRoot(_root));
    }

    [Fact]
    public void No_repo_root_is_reported_when_nothing_above_holds_the_marker()
    {
        Directory.CreateDirectory(_root);

        Assert.Null(TestEnvironment.FindRepoRoot(_root, "no-such-marker-" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void The_temp_folder_sits_under_the_tools_folder_and_is_named_for_the_os()
    {
        var expected = Path.Combine(_root, ".tools", "tmp", TestEnvironment.OsFolderName());

        Assert.Equal(expected, TestEnvironment.TempFolderFor(_root));
    }

    [Fact]
    public void The_folder_name_is_one_of_the_three_supported_systems()
    {
        Assert.Contains(TestEnvironment.OsFolderName(), new[] { "windows", "linux", "macos" });
    }

    [Fact]
    public void The_running_tests_found_the_checkout_they_were_built_in()
    {
        Assert.NotNull(TestEnvironment.RepoRoot);
        Assert.True(File.Exists(Path.Combine(TestEnvironment.RepoRoot, TestEnvironment.SolutionFileName)));
    }
}
