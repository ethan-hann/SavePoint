using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Services;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Launch;

// The launcher for the OS the tests run on, starting the stand-in game for real.
public class RealLauncherTests
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IGameLauncher Launcher => GameLaunchers.ForCurrentOs();

    private static async Task<int> ExitCodeAsync(IGameProcess process)
    {
        await process.WaitForExitAsync(Ct).WaitAsync(_patience, Ct);
        return process.ExitCode!.Value;
    }

    private static void SetMode(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows has no file modes.");
        }

        File.SetUnixFileMode(path, mode);
    }

    private static UnixFileMode ModeOf(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows has no file modes.");
        }

        return File.GetUnixFileMode(path);
    }

    [Fact]
    public async Task A_started_game_reports_its_exit_code_when_it_leaves()
    {
        using var game = new FakeGameInstall();

        using var process = Launcher.StartExecutable(game.ExecutablePath, ["--exit-after", "300", "--exit-code", "5"], game.Folder);

        Assert.Equal(5, await ExitCodeAsync(process));
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task A_running_game_has_a_process_id_and_no_exit_code_until_it_leaves()
    {
        using var game = new FakeGameInstall();
        var ready = game.Combine("ready.txt");

        using var process = Launcher.StartExecutable(game.ExecutablePath, ["--ready-file", ready], game.Folder);
        var pid = await FakeGameProcess.WaitForReadyFileAsync(ready);

        Assert.Equal(pid, process.Pid);
        Assert.False(process.HasExited);
        Assert.Null(process.ExitCode);
        Assert.Contains(process.Pid, game.RunningIds());
    }

    [Fact]
    public async Task The_game_starts_in_the_working_directory_and_gets_each_argument_whole()
    {
        using var game = new FakeGameInstall();

        using var process = Launcher.StartExecutable(game.ExecutablePath, ["--saves", "saves with spaces", "--exit-after", "300"], game.Folder);
        await ExitCodeAsync(process);

        // The folder is made relative to wherever the game started, so it only lands here if the working directory was the install folder.
        Assert.True(Directory.Exists(Path.Combine(game.Folder, "saves with spaces")));
    }

    [Fact]
    public void A_missing_program_is_a_missing_install_path()
    {
        using var temp = new TempFolder();
        var missing = temp.Combine(OperatingSystem.IsWindows() ? "gone.exe" : "gone");

        var error = Assert.Throws<PathMissingException>(() => Launcher.StartExecutable(missing, [], temp.Path));

        Assert.Equal(missing, error.Args["path"]);
        Assert.Equal(RepoRole.Install, error.Args["role"]);
    }

    [Fact]
    public async Task A_shell_script_runs_through_bin_sh_in_the_working_directory_with_its_arguments()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "Needs /bin/sh.");
        using var temp = new TempFolder();
        var script = temp.WriteText("install/run.sh", "pwd > \"$1\"\nshift\nprintf '%s\\n' \"$@\" > args.txt\n");
        var install = Path.GetDirectoryName(script)!;
        var record = temp.Combine("where.txt");

        using var process = Launcher.StartExecutable(script, [record, "two words", "last"], install);
        var code = await ExitCodeAsync(process);

        Assert.Equal(0, code);
        Assert.Equal(Path.GetFullPath(install), (await File.ReadAllTextAsync(record, Ct)).Trim());
        Assert.Equal(["two words", "last"], (await File.ReadAllLinesAsync(Path.Combine(install, "args.txt"), Ct)));
    }

    [Fact]
    public void A_Windows_program_is_refused_where_it_cannot_run()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "Windows runs .exe files.");
        using var temp = new TempFolder();
        var program = temp.WriteText("install/Game.exe", "MZ");

        var error = Assert.Throws<LaunchWindowsProgramException>(() => Launcher.StartExecutable(program, [], temp.Combine("install")));

        Assert.Equal(program, error.Args["path"]);
    }

    [Fact]
    public void A_file_without_an_execute_bit_is_refused_and_left_as_it_was()
    {
        Assert.SkipUnless(TempFolder.HonorsFileModes(), "This file system does not keep execute bits.");
        using var temp = new TempFolder();
        var plain = temp.WriteText("install/game", "#!/bin/sh\nexit 0\n");
        SetMode(plain, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var error = Assert.Throws<LaunchSpawnException>(() => Launcher.StartExecutable(plain, [], temp.Combine("install")));

        Assert.Equal(LaunchMethod.Exe, error.Args["method"]);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, ModeOf(plain));
    }

    [Fact]
    public async Task A_script_with_an_execute_bit_starts_directly()
    {
        Assert.SkipUnless(TempFolder.HonorsFileModes(), "This file system does not keep execute bits.");
        using var temp = new TempFolder();
        var script = temp.WriteText("install/game", "#!/bin/sh\nexit 7\n");
        SetMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        using var process = Launcher.StartExecutable(script, [], temp.Combine("install"));

        Assert.Equal(7, await ExitCodeAsync(process));
    }
}
