using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

public class FileLockProbeTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    public void The_two_windows_numbers_for_a_file_another_program_has_open_mean_held_on_windows_only(int number)
    {
        // The same numbers mean something else on Linux and macOS, where a failed read must not be taken for a held file.
        var failure = new IOException("failed", number);

        Assert.Equal(OperatingSystem.IsWindows(), FileLockProbe.IsSharingViolation(failure));
    }

    [Fact]
    public void Any_other_number_is_not_a_held_file_on_any_system()
    {
        Assert.False(FileLockProbe.IsSharingViolation(new IOException("failed", 5)));
    }
}
