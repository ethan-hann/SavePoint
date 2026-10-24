using System.Globalization;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

// A git that finished but was never reaped still has a process entry; the stamp must not wait on it.
public sealed class ProcessStampTests : IDisposable
{
    private readonly string _proc = Path.Combine(Path.GetTempPath(), "savepoint-procstamp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_proc))
        {
            Directory.Delete(_proc, recursive: true);
        }
    }

    private void WriteStat(int pid, string stat)
    {
        var dir = Path.Combine(_proc, pid.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "stat"), stat);
    }

    [Fact]
    public void A_live_process_whose_stat_says_zombie_counts_as_gone()
    {
        var stamp = ProcessStamp.Current();
        WriteStat(stamp.Id, $"{stamp.Id} (git) Z 1 1 1 0 -1");

        Assert.False(stamp.IsRunning(_proc));
    }

    [Fact]
    public void A_live_process_whose_stat_says_sleeping_counts_as_running()
    {
        var stamp = ProcessStamp.Current();
        WriteStat(stamp.Id, $"{stamp.Id} (git) S 1 1 1 0 -1");

        Assert.True(stamp.IsRunning(_proc));
    }

    [Fact]
    public void A_process_without_a_readable_stat_is_judged_by_the_system_alone()
    {
        Assert.True(ProcessStamp.Current().IsRunning(_proc));
    }
}
