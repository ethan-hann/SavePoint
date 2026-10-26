using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

// A setting git wrote when the repository was made is read from the config file, without a git process.
public sealed class RepoConfigReadValueTests
{
    private const string _config = "[core]\n\trepositoryformatversion = 0\n\tfilemode = false\n\tIgnoreCase = true\n[savepoint]\n\trepoConfigVersion = 7\n[other]\n\tfilemode = true\n";

    [Fact]
    public void A_key_is_found_in_its_own_section_whatever_the_case_of_its_name()
    {
        Assert.Equal("false", RepoConfigFile.ReadValue(_config, "core", "filemode"));
        Assert.Equal("true", RepoConfigFile.ReadValue(_config, "CORE", "ignorecase"));
    }

    [Fact]
    public void The_same_key_in_another_section_is_not_taken()
    {
        Assert.Equal("true", RepoConfigFile.ReadValue(_config, "other", "filemode"));
        Assert.Null(RepoConfigFile.ReadValue(_config, "savepoint", "filemode"));
    }

    [Fact]
    public void A_key_the_file_does_not_hold_is_null_and_the_stamp_is_still_read()
    {
        Assert.Null(RepoConfigFile.ReadValue(_config, "core", "symlinks"));
        Assert.Equal("7", RepoConfigFile.ReadStamp(_config));
    }
}
