using System.Diagnostics;
using System.Globalization;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

// Whatever the runner starts must be gone once the call returns, and nothing it started may be left stopped.
public sealed class GitProcessTreeTests : IDisposable
{
    private static readonly TimeSpan _generous = TimeSpan.FromSeconds(30);

    private readonly GitWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Cancelling_leaves_no_process_of_the_tree_running_or_stopped()
    {
        // git runs the alias under a shell, which runs the marked sleeper: three levels below the test.
        var marker = UniqueMarker();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var started = new TaskCompletionSource<ProcessStamp>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = _ws.OutsideAnyRepository(new GitInvocation(["-c", $"alias.hold=!{Sleeper(marker)}", "hold"]) { Started = stamp => started.TrySetResult(stamp) });
        var call = _ws.Runner.RunAsync(invocation, cts.Token);

        await WaitUntilSleeperRunsAsync(marker, call);
        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => call.WaitAsync(_generous, TestContext.Current.CancellationToken));

        var root = await started.Task;
        Assert.False(root.IsRunning(), "the git that was started is still running");
        if (OperatingSystem.IsLinux())
        {
            Assert.Empty(LinuxTreeMembers(marker));
        }
    }

    [Fact]
    public async Task Ending_every_running_git_ends_a_call_nobody_cancelled()
    {
        var marker = UniqueMarker();
        var invocation = _ws.OutsideAnyRepository(new GitInvocation(["-c", $"alias.hold=!{Sleeper(marker)}", "hold"]));
        var call = _ws.Runner.RunAsync(invocation, TestContext.Current.CancellationToken);

        await WaitUntilSleeperRunsAsync(marker, call);
        GitProcessRegistry.KillAll();
        await WaitForCallToEndAsync(call);

        if (OperatingSystem.IsLinux())
        {
            Assert.Empty(LinuxTreeMembers(marker));
        }
    }

    [Fact]
    public void The_tree_below_a_process_is_read_from_parent_ids_and_leaves_out_what_has_exited()
    {
        var proc = Path.Combine(_ws.Root, "fake-proc");
        WriteStat(proc, 10, "git", 'S', 1);
        WriteStat(proc, 11, "sh (-c)", 'S', 10);
        WriteStat(proc, 12, "sleep", 'T', 11);
        WriteStat(proc, 13, "other", 'S', 1);
        WriteStat(proc, 14, "done", 'Z', 10);

        var below = ProcessTree.DescendantsOf(10, proc);

        Assert.Equal([11, 12], below.Order());
    }

    private static void WriteStat(string proc, int id, string name, char state, int parent)
    {
        var folder = Path.Combine(proc, id.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "stat"), $"{id} ({name}) {state} {parent} 1 1 0 -1 0");
    }

    private static async Task WaitForCallToEndAsync(Task call)
    {
        try
        {
            await call.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            Assert.Fail("the call did not end after every git was killed");
        }
        catch (Exception e) when (e is not OperationCanceledException || !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            // Killed from outside: the call may report git's failure.
        }
    }

    private static string UniqueMarker() => (200000 + Random.Shared.Next(0, 99999)).ToString(CultureInfo.InvariantCulture);

    // The number is the marker: it makes the sleeper's command line unique, and it is long enough that it never ends by itself.
    private static string Sleeper(string marker) => OperatingSystem.IsWindows() ? $"ping -n {marker} 127.0.0.1" : $"sleep {marker}";

    private static async Task WaitUntilSleeperRunsAsync(string marker, Task call)
    {
        var until = DateTime.UtcNow + _generous;
        while (DateTime.UtcNow < until && !call.IsCompleted)
        {
            if (OperatingSystem.IsLinux() ? LinuxTreeMembers(marker).Count > 0 : Process.GetProcessesByName("ping").Length > 0)
            {
                return;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail("the sleeper never started");
    }

    // The processes whose command line carries the marker, with their state letter; a zombie has run its course and does not count.
    private static List<string> LinuxTreeMembers(string marker)
    {
        var found = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                continue;
            }

            try
            {
                var command = File.ReadAllText(Path.Combine(dir, "cmdline")).Replace('\0', ' ');
                var stat = File.ReadAllText(Path.Combine(dir, "stat"));
                var state = stat[(stat.LastIndexOf(')') + 2)..][0];
                if (command.Contains(marker, StringComparison.Ordinal) && state is not ('Z' or 'X'))
                {
                    found.Add($"{pid} [{state}] {command.Trim()}");
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Gone while being read.
            }
        }

        return found;
    }
}
