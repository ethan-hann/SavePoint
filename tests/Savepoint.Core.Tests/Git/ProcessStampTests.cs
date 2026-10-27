using System.Globalization;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

// A git that finished but was never reaped still has a process entry; the stamp must not wait on it.
public sealed class ProcessStampTests : IDisposable
{
    // A wait that returned early, before the process turned zombie, would show within several polls of 10 ms, and only waiting shows that it does not.
    private static readonly TimeSpan _stillWaiting = TimeSpan.FromMilliseconds(100);

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

    [Fact(Timeout = 30_000)]
    public async Task A_wait_returns_once_the_stat_of_a_live_process_turns_zombie()
    {
        var stamp = ProcessStamp.Current();
        WriteStat(stamp.Id, $"{stamp.Id} (git) S 1 1 1 0 -1");

        var wait = stamp.WaitForExitAsync(_proc, TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        await Task.Delay(_stillWaiting, TestContext.Current.CancellationToken);
        Assert.False(wait.IsCompleted);

        WriteStat(stamp.Id, $"{stamp.Id} (git) Z 1 1 1 0 -1");
        await wait;
    }

    [Fact(Timeout = 30_000)]
    public async Task A_wait_on_a_running_process_stops_when_cancelled()
    {
        var stamp = ProcessStamp.Current();
        WriteStat(stamp.Id, $"{stamp.Id} (git) S 1 1 1 0 -1");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stamp.WaitForExitAsync(_proc, TimeSpan.FromMilliseconds(10), cancel.Token));
    }

    [Fact]
    public void Without_a_proc_folder_given_a_linux_stamp_reads_the_real_proc()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux has /proc.");
        var stamp = ProcessStamp.Current();

        Assert.True(stamp.IsRunning());
        Assert.True(File.Exists($"/proc/{stamp.Id}/stat"));
    }

    [Fact]
    public void A_process_without_a_readable_stat_is_judged_by_the_system_alone()
    {
        Assert.True(ProcessStamp.Current().IsRunning(_proc));
    }
}
