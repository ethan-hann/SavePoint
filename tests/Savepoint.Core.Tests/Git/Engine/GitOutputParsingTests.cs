using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// The text git prints is parsed in a few places; these pin the shapes seen from the bundled git.
public sealed class GitOutputParsingTests
{
    private const string _zero = "0000000000000000000000000000000000000000";

    [Fact]
    public void Files_git_would_overwrite_are_the_indented_lines_under_the_heading()
    {
        const string stderr =
            "error: Your local changes to the following files would be overwritten by checkout:\n"
            + "\tData/sub dir/a.txt\n"
            + "\tlocked.txt\n"
            + "Please commit your changes or stash them before you switch branches.\n"
            + "Aborting\n";

        Assert.Equal(["Data/sub dir/a.txt", "locked.txt"], GitFailure.OverwrittenPaths(stderr));
        Assert.True(GitFailure.RefusedToOverwrite(stderr));
    }

    [Theory]
    [InlineData("error: Updating the following directories would lose untracked files in them:\n\tx\n\nAborting\n", "x")]
    [InlineData("error: The following untracked working tree files would be removed by checkout:\n\tx/new.sav\nPlease move or remove them before you switch branches.\nAborting\n", "x/new.sav")]
    public void The_other_ways_git_refuses_a_switch_over_untracked_files_are_refusals_too(string stderr, string path)
    {
        Assert.True(GitFailure.RefusedToOverwrite(stderr));
        Assert.Equal([path], GitFailure.OverwrittenPaths(stderr));
    }

    [Fact]
    public void Untracked_files_in_the_way_are_listed_the_same_way()
    {
        const string stderr =
            "error: The following untracked working tree files would be overwritten by checkout:\r\n"
            + "\tonly-a.txt\r\n"
            + "Please move or remove them before you switch branches.\r\n"
            + "Aborting\r\n";

        Assert.Equal(["only-a.txt"], GitFailure.OverwrittenPaths(stderr));
    }

    [Fact]
    public void Other_errors_name_no_overwritten_files()
    {
        Assert.Empty(GitFailure.OverwrittenPaths("fatal: not a git repository\n"));
        Assert.False(GitFailure.RefusedToOverwrite("fatal: not a git repository\n"));
    }

    [Theory]
    [InlineData("fatal: Unable to create 'F:/x/repo.git/index.lock': File exists.\n\nAnother git process seems to be running in this repository", true)]
    [InlineData("fatal: Unable to create '/x/repo.git/refs/heads/a.lock': File exists.", true)]
    [InlineData("Another git process seems to be running in this repository, e.g.", true)]
    [InlineData("fatal: update_ref failed for ref 'refs/heads/a': cannot lock ref 'refs/heads/a': reference already exists", false)]
    [InlineData("error: unable to unlink old 'locked.txt': Invalid argument", false)]
    [InlineData("", false)]
    public void Lock_contention_is_told_from_other_lock_messages(string stderr, bool expected)
    {
        Assert.Equal(expected, GitFailure.IsLockContention(stderr));
    }

    [Theory]
    [InlineData("fatal: Unable to create 'F:/x/repo.git/index.lock': File exists.\n\nAnother", "F:/x/repo.git/index.lock")]
    [InlineData("nothing to see here", null)]
    public void The_lock_file_git_names_is_found(string stderr, string? expected)
    {
        Assert.Equal(expected, GitFailure.LockFilePath(stderr));
    }

    [Theory]
    [InlineData("error: unable to write file x: No space left on device", true)]
    [InlineData("error: unable to create file x: There is not enough space on the disk.", true)]
    [InlineData("error: Disk quota exceeded", true)]
    [InlineData("error: unable to unlink old 'x': Invalid argument", false)]
    public void A_full_disk_is_recognised(string stderr, bool expected)
    {
        Assert.Equal(expected, GitFailure.IsDiskFull(stderr));
    }

    [Fact]
    public void Tree_changes_pair_each_record_with_its_path()
    {
        var records = new[]
        {
            ":100644 000000 1111111111111111111111111111111111111111 " + _zero + " D",
            "Data/gone file.txt",
            ":000000 100644 " + _zero + " 2222222222222222222222222222222222222222 A",
            "new.txt",
            ":100644 100644 3333333333333333333333333333333333333333 4444444444444444444444444444444444444444 M",
            "caf\u00e9.txt",
        };

        var changes = GitEngine.ParseTreeChanges(records);

        Assert.Equal(
            [
                new GitEngine.TreeChange('D', "1111111111111111111111111111111111111111", _zero, "Data/gone file.txt"),
                new GitEngine.TreeChange('A', _zero, "2222222222222222222222222222222222222222", "new.txt"),
                new GitEngine.TreeChange('M', "3333333333333333333333333333333333333333", "4444444444444444444444444444444444444444", "caf\u00e9.txt"),
            ],
            changes);
    }

    [Fact]
    public void Output_that_is_not_a_tree_change_is_reported_not_guessed_at()
    {
        Assert.Throws<GitOperationException>(() => GitEngine.ParseTreeChanges(["not a change", "path"]));
    }

    [Fact]
    public void A_listing_names_each_file_once_and_sets_folders_that_are_repositories_apart()
    {
        var listing = WorkTreeListing.From(["a.txt", "Data/has spaces.txt", "a.txt", "mod/", "z.txt"]);

        Assert.Equal(["a.txt", "Data/has spaces.txt", "z.txt"], listing.Files);
        Assert.Equal(["mod/"], listing.NestedRepositories);
    }
}
