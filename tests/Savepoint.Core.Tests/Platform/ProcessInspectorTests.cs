using System.Diagnostics;
using System.Text;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;
using Savepoint.Core.Tests.Paths;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Platform;

public class ProcessInspectorTests
{
    private const string _protonArgv0 = @"Z:\home\u\steamapps\common\Skyrim\SkyrimSE.exe";

    [Fact]
    public void Linux_reads_pid_comm_and_the_first_argument_from_each_numeric_folder()
    {
        using var tree = new TempTree();
        var proc = tree.Dir("proc");
        FakeProcess(proc, "100", "SkyrimSE.exe\n", $"{_protonArgv0}\0-windowed\0");
        FakeProcess(proc, "200", "factorio\n", "/opt/factorio/bin/x64/factorio\0");
        FakeProcess(proc, "self", "ignored\n", "ignored\0");
        File.WriteAllText(Path.Combine(proc, "300"), "a file, not a process folder");
        Directory.CreateDirectory(Path.Combine(proc, "400"));

        var processes = new LinuxProcessInspector(proc).List().OrderBy(p => p.Pid).ToList();

        Assert.Equal(
            [
                new RunningProcess(100, "SkyrimSE.exe", null, _protonArgv0),
                new RunningProcess(200, "factorio", null, "/opt/factorio/bin/x64/factorio"),
            ],
            processes);
    }

    [Fact]
    public void Linux_leaves_the_first_argument_empty_for_a_process_with_no_command_line()
    {
        using var tree = new TempTree();
        var proc = tree.Dir("proc");
        FakeProcess(proc, "2", "kthreadd\n", "");

        var process = Assert.Single(new LinuxProcessInspector(proc).List());

        Assert.Equal(new RunningProcess(2, "kthreadd", null), process);
    }

    [Fact]
    public void Linux_reads_the_executable_from_the_exe_link_and_drops_the_deleted_marker()
    {
        using var tree = new TempTree();
        var proc = tree.Dir("proc");
        FakeProcess(proc, "100", "game\n", "game\0");
        FakeProcess(proc, "101", "game\n", "game\0");
        tree.Symlink(Path.Combine("proc", "100", "exe"), "game-binary", isDirectory: false);
        tree.Symlink(Path.Combine("proc", "101", "exe"), "game-binary (deleted)", isDirectory: false);

        var processes = new LinuxProcessInspector(proc).List();

        Assert.Equal("game-binary", Assert.Single(processes, p => p.Pid == 100).ExecutablePath);
        Assert.Equal("game-binary", Assert.Single(processes, p => p.Pid == 101).ExecutablePath);
    }

    [Fact]
    public void Linux_skips_a_process_it_cannot_read_and_keeps_the_rest()
    {
        using var tree = new TempTree();
        var proc = tree.Dir("proc");
        FakeProcess(proc, "100", "game\n", "game\0");
        Directory.CreateDirectory(Path.Combine(proc, "500", "comm"));
        Directory.CreateDirectory(Path.Combine(proc, "600"));

        var processes = new LinuxProcessInspector(proc).List();

        Assert.Equal([100], processes.Select(p => p.Pid));
    }

    [Theory]
    [InlineData("Z")]
    [InlineData("X")]
    [InlineData("x")]
    public void Linux_leaves_out_a_process_that_has_exited_but_not_been_reaped(string state)
    {
        using var tree = new TempTree();
        var proc = tree.Dir("proc");
        FakeProcess(proc, "100", "game\n", "game\0", $"100 (game) {state} 1 100 100 0 -1 4210700 0 0 0 0 0 0 0 0 20 0 1 0 5 0 0\n");
        FakeProcess(proc, "101", "game\n", "game\0", "101 (game) S 1 101 101 0 -1 4210688 0 0 0 0 0 0 0 0 20 0 1 0 5 0 0\n");

        var processes = new LinuxProcessInspector(proc).List();

        Assert.Equal([101], processes.Select(p => p.Pid));
    }

    [Fact]
    public void Linux_reads_the_state_after_the_last_bracket_so_a_name_with_brackets_cannot_fake_one()
    {
        using var tree = new TempTree();
        var proc = tree.Dir("proc");
        FakeProcess(proc, "100", "a) Z (b\n", "game\0", "100 (a) Z (b) S 1 100 100 0 -1 4210688 0 0 0 0 0 0 0 0 20 0 1 0 5 0 0\n");
        FakeProcess(proc, "101", "g) S (h\n", "game\0", "101 (g) S (h) Z 1 101 101 0 -1 4210700 0 0 0 0 0 0 0 0 20 0 1 0 5 0 0\n");

        var processes = new LinuxProcessInspector(proc).List();

        Assert.Equal([100], processes.Select(p => p.Pid));
    }

    [Fact]
    public void Linux_reads_the_parent_and_the_start_time_from_the_stat_line_after_the_last_bracket()
    {
        using var tree = new TempTree();
        var proc = tree.Dir("proc");
        File.WriteAllText(Path.Combine(proc, "stat"), "cpu  1 2 3\nbtime 1000\nprocesses 5\n");
        FakeProcess(proc, "100", "a) S (b\n", "game\0", "100 (a) S (b) S 42 100 100 0 -1 4210688 0 0 0 0 0 0 0 0 20 0 1 0 250 0 0\n");

        var process = Assert.Single(new LinuxProcessInspector(proc).List());

        Assert.Equal(42, process.ParentPid);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_002_500), process.StartedAt);
    }

    [Fact]
    public void Linux_orders_start_times_even_when_the_boot_time_cannot_be_read()
    {
        using var tree = new TempTree();
        var proc = tree.Dir("proc");
        FakeProcess(proc, "100", "old\n", "old\0", "100 (old) S 1 100 100 0 -1 4210688 0 0 0 0 0 0 0 0 20 0 1 0 250 0 0\n");
        FakeProcess(proc, "101", "new\n", "new\0", "101 (new) S 100 101 101 0 -1 4210688 0 0 0 0 0 0 0 0 20 0 1 0 251 0 0\n");

        var processes = new LinuxProcessInspector(proc).List().ToDictionary(p => p.Pid);

        Assert.True(processes[100].StartedAt < processes[101].StartedAt);
        Assert.Equal(100, processes[101].ParentPid);
    }

    [Fact]
    public void Linux_leaves_the_parent_and_start_time_empty_for_a_stat_line_it_cannot_read()
    {
        using var tree = new TempTree();
        var proc = tree.Dir("proc");
        FakeProcess(proc, "100", "game\n", "game\0", "100 (game) S\n");

        var process = Assert.Single(new LinuxProcessInspector(proc).List());

        Assert.Null(process.ParentPid);
        Assert.Null(process.StartedAt);
    }

    [Fact]
    public async Task A_real_child_is_listed_with_this_process_as_its_parent_and_a_start_time_after_this_process_started()
    {
        using var child = FakeGameProcess.Start();
        await FakeGameProcess.WaitUntilAsync(() => Find(child.Id) is not null);

        var listed = Find(child.Id);
        using var self = Process.GetCurrentProcess();

        Assert.NotNull(listed);
        if (OperatingSystem.IsMacOS())
        {
            Assert.Null(listed.ParentPid);
            Assert.Null(listed.StartedAt);
            return;
        }

        Assert.Equal(Environment.ProcessId, listed.ParentPid);
        Assert.NotNull(listed.StartedAt);
        Assert.True(listed.StartedAt >= self.StartTime.ToUniversalTime().AddSeconds(-1), $"started at {listed.StartedAt:O}");
        Assert.True(listed.StartedAt <= DateTimeOffset.UtcNow.AddSeconds(1), $"started at {listed.StartedAt:O}");
    }

    private static RunningProcess? Find(int pid)
    {
        IProcessInspector inspector = OperatingSystem.IsWindows() ? new WindowsProcessInspector()
            : OperatingSystem.IsMacOS() ? new MacOSProcessInspector()
            : new LinuxProcessInspector();
        return inspector.List().FirstOrDefault(p => p.Pid == pid);
    }

    [Fact]
    public void Linux_gives_an_empty_list_when_the_proc_folder_is_missing()
    {
        using var tree = new TempTree();

        Assert.Empty(new LinuxProcessInspector(Path.Combine(tree.Root, "no-proc")).List());
    }

    [Fact]
    public void Linux_lists_the_running_test_process_with_its_executable()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Needs a real /proc.");

        var self = Assert.Single(new LinuxProcessInspector().List(), p => p.Pid == Environment.ProcessId);

        Assert.Equal(Environment.ProcessPath, self.ExecutablePath);
        Assert.False(string.IsNullOrEmpty(self.Name));
    }

    [Fact]
    public void Windows_lists_the_running_test_process_with_its_executable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Reads the Windows process table.");

        var self = Assert.Single(new WindowsProcessInspector().List(), p => p.Pid == Environment.ProcessId);

        using var current = Process.GetCurrentProcess();
        Assert.Equal(current.ProcessName, self.Name);
        Assert.Equal(Environment.ProcessPath, self.ExecutablePath, ignoreCase: true);
    }

    [Fact]
    public void MacOS_lists_the_running_test_process()
    {
        // The macOS inspector uses the cross-platform process API, so it can be checked on any host.
        var self = Assert.Single(new MacOSProcessInspector().List(), p => p.Pid == Environment.ProcessId);

        using var current = Process.GetCurrentProcess();
        Assert.Equal(current.ProcessName, self.Name);
    }

    [Fact]
    public void Each_default_platform_inspector_is_the_one_for_its_os()
    {
        Assert.IsType<WindowsProcessInspector>(FakePlatforms.For(OsKind.Windows).Processes);
        Assert.IsType<LinuxProcessInspector>(FakePlatforms.For(OsKind.Linux).Processes);
        Assert.IsType<MacOSProcessInspector>(FakePlatforms.For(OsKind.MacOS).Processes);
    }

    private static void FakeProcess(string proc, string pid, string comm, string cmdline, string? stat = null)
    {
        var dir = Directory.CreateDirectory(Path.Combine(proc, pid)).FullName;
        File.WriteAllText(Path.Combine(dir, "comm"), comm);
        File.WriteAllBytes(Path.Combine(dir, "cmdline"), Encoding.UTF8.GetBytes(cmdline));
        if (stat is not null)
        {
            File.WriteAllText(Path.Combine(dir, "stat"), stat);
        }
    }
}
