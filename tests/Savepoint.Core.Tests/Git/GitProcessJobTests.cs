using System.Diagnostics;
using System.Globalization;
using Savepoint.Core.Git;
using Savepoint.Core.Tests.Services;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Git;

// On Windows a git must not outlive the SavePoint that started it, however that SavePoint ends (Task Manager, a crash, a shutdown).
public sealed class GitProcessJobTests
{
    private const string _bundleVariable = "SAVEPOINT_TEST_JOB_CHILD_BUNDLE";
    private const string _fileVariable = "SAVEPOINT_TEST_JOB_CHILD_FILE";

    [Fact(Timeout = 180_000)]
    public async Task A_git_ends_with_the_program_that_started_it_even_when_that_program_is_killed_outright()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows has a job that ends its members with the program that made it");
        using var temp = new TempFolder("savepoint-job-");
        using var bundle = new StandInGitBundle(temp, "bundle");
        var file = temp.Combine("child-git-id.txt");
        var info = new ProcessStartInfo(Environment.ProcessPath!, ["-method", $"{typeof(GitProcessJobTests).FullName}.{nameof(Child_for_the_kill_test)}"])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment[_bundleVariable] = bundle.Root;
        info.Environment[_fileVariable] = file;
        using var child = new Process { StartInfo = info };
        var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
        child.OutputDataReceived += (_, e) => output.Enqueue(e.Data ?? string.Empty);
        child.ErrorDataReceived += (_, e) => output.Enqueue(e.Data ?? string.Empty);
        child.Start();
        child.BeginOutputReadLine();
        child.BeginErrorReadLine();

        var gitId = 0;
        try
        {
            Assert.True(await FakeGameProcess.WaitUntilAsync(() => File.Exists(file) || child.HasExited, TimeSpan.FromSeconds(90)), "the child never started a git");
            Assert.True(File.Exists(file), $"the child ended before it started a git; it printed: {string.Join(" | ", output)}");
            gitId = int.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
            Assert.True(FakeGameProcess.IsRunning(gitId), "the git was not running while its program lived");

            // No exit handler runs for this: it is what Task Manager's End task does.
            child.Kill();
            child.WaitForExit(30_000);

            Assert.True(await FakeGameProcess.WaitUntilAsync(() => !FakeGameProcess.IsRunning(gitId), TimeSpan.FromSeconds(15)), "the git is still running after its program was killed");
        }
        finally
        {
            if (gitId != 0)
            {
                FakeGameProcess.Kill(gitId);
            }

            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }
        }
    }

    // Runs only inside the copy the test above starts; anywhere else it has nothing to do.
    [Fact]
    public async Task Child_for_the_kill_test()
    {
        if (Environment.GetEnvironmentVariable(_bundleVariable) is not { Length: > 0 } root || Environment.GetEnvironmentVariable(_fileVariable) is not { Length: > 0 } file)
        {
            return;
        }

        var binary = GitBinary.At(root, Path.Combine(Path.GetDirectoryName(root)!, "config"));
        var started = new TaskCompletionSource<ProcessStamp>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = new GitRunner(binary).RunAsync(new GitInvocation([]) { Started = stamp => started.TrySetResult(stamp) }, CancellationToken.None);
        var stamp = await started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(file, stamp.Id.ToString(CultureInfo.InvariantCulture), TestContext.Current.CancellationToken);

        // The parent kills this copy; the delay only keeps a forgotten copy from running for ever.
        await Task.Delay(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_process_that_cannot_be_put_in_the_job_is_reported_and_not_thrown_about()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows has the job");
        using var temp = new TempFolder("savepoint-job-");
        using var bundle = new StandInGitBundle(temp, "bundle");
        using var process = Process.Start(new ProcessStartInfo(bundle.ExecutablePath, ["--exit-after", "1"]) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        var problem = GitProcessJob.TryAssign(process);

        Assert.NotNull(problem);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_running_process_is_put_in_the_job_without_a_problem()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows has the job");
        using var temp = new TempFolder("savepoint-job-");
        using var bundle = new StandInGitBundle(temp, "bundle");
        var id = await bundle.StartLeftoverAsync();
        using var process = Process.GetProcessById(id);

        var problem = GitProcessJob.TryAssign(process);

        Assert.Null(problem);
    }
}
