using System.Diagnostics;
using System.Globalization;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

// Whatever the runner starts must be gone once the call returns, and nothing it started may be left stopped.
public sealed class GitProcessTreeTests : IDisposable
{
    private static readonly TimeSpan _generous = TimeSpan.FromSeconds(30);

    // A kill that reached the bystander would end it within moments, and only waiting shows that it does not.
    private static readonly TimeSpan _bystanderWindow = TimeSpan.FromMilliseconds(500);

    private const string _childEnvironmentVariable = "SAVEPOINT_TEST_EXIT_CHILD_FILE";

    // Its own list: ending "every git" must not reach a process another test class is running.
    private readonly GitProcessRegistry _registry = new();
    private readonly GitWorkspace _ws;

    public GitProcessTreeTests()
    {
        _ws = new GitWorkspace(new GitRunnerOptions { Registry = _registry });
    }

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
        _registry.KillAll();
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

        Assert.Equal([11, 12], below.Select(item => item.Id).Order());
    }

    [Fact]
    public async Task Ending_every_running_git_leaves_a_process_that_took_over_a_number_alone()
    {
        // The stamp says the registered process started a day earlier than the one that now has the number.
        using var bystander = Process.Start(new ProcessStartInfo(OperatingSystem.IsWindows() ? "ping" : "sleep", OperatingSystem.IsWindows() ? "-n 60 127.0.0.1" : "60") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        try
        {
            var real = ProcessStamp.Of(bystander)!.Value;
            using var listed = _registry.Add(real with { StartedAtUtcTicks = real.StartedAtUtcTicks - TimeSpan.FromDays(1).Ticks });

            _registry.KillAll();
            await Task.Delay(_bystanderWindow, TestContext.Current.CancellationToken);

            Assert.False(bystander.HasExited, "a process that only shares the number was killed");
        }
        finally
        {
            bystander.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task A_program_that_exits_while_git_runs_leaves_no_git_behind()
    {
        // A second copy of this test program starts a git through the shared registry and exits at once.
        var file = Path.Combine(_ws.Root, "child-git-id.txt");
        var info = new ProcessStartInfo(Environment.ProcessPath!, ["-method", $"{typeof(GitProcessTreeTests).FullName}.{nameof(Child_for_the_exit_test)}"])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment[_childEnvironmentVariable] = file;
        using var child = new Process { StartInfo = info };
        var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
        child.OutputDataReceived += (_, e) => output.Enqueue(e.Data ?? string.Empty);
        child.ErrorDataReceived += (_, e) => output.Enqueue(e.Data ?? string.Empty);
        child.Start();
        child.BeginOutputReadLine();
        child.BeginErrorReadLine();
        await child.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        Assert.True(File.Exists(file), $"the child never started a git; it printed: {string.Join(" | ", output)}");
        var gitId = int.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (IsRunning(gitId) && DateTime.UtcNow < until)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.False(IsRunning(gitId), "the git the exited program started is still running");
    }

    // Runs only inside the copy the test above starts; anywhere else it has nothing to do.
    [Fact]
    public async Task Child_for_the_exit_test()
    {
        if (Environment.GetEnvironmentVariable(_childEnvironmentVariable) is not { Length: > 0 } file)
        {
            return;
        }

        var started = new TaskCompletionSource<ProcessStamp>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var shared = new GitWorkspace();
        var invocation = shared.OutsideAnyRepository(new GitInvocation(["-c", $"alias.hold=!{Sleeper(UniqueMarker())}", "hold"]) { Started = stamp => started.TrySetResult(stamp) });
        _ = shared.Runner.RunAsync(invocation, CancellationToken.None);
        var stamp = await started.Task.WaitAsync(_generous, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(file, stamp.Id.ToString(CultureInfo.InvariantCulture), TestContext.Current.CancellationToken);
        Environment.Exit(0);
    }

    private static bool IsRunning(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited && !(OperatingSystem.IsLinux() && Savepoint.Core.Platform.Linux.LinuxProcessInspector.HasExited(id));
        }
        catch (ArgumentException)
        {
            return false;
        }
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
            // The git and the shell carry the marker too; only the sleeper itself proves the whole tree exists.
            if (OperatingSystem.IsLinux() ? LinuxTreeMembers(marker).Any(member => member.Contains("] sleep ", StringComparison.Ordinal)) : Process.GetProcessesByName("ping").Length > 0)
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
