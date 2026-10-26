using System.Diagnostics;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Launch;
using Savepoint.Core.Tests.Services;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Git;

// A folder laid out like a git bundle whose "git" is the stand-in game, so a test can run long-lived processes from a bundle path
// without the real git and without any other test's processes ever living under that path. Disposing ends whatever still runs from it.
internal sealed class StandInGitBundle : IDisposable
{
    private readonly TempFolder _temp;
    private readonly List<int> _strays = [];
    private readonly List<Process> _launchers = [];

    public StandInGitBundle(TempFolder temp, string name)
    {
        _temp = temp;
        Root = temp.Combine(name);
        var executables = Path.Combine(Root, OperatingSystem.IsWindows() ? "cmd" : "bin");
        Directory.CreateDirectory(executables);

        var source = Path.GetDirectoryName(FakeGameProcess.ExecutablePath)!;
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(executables, Path.GetFileName(file)));
        }

        ExecutablePath = Path.Combine(executables, OperatingSystem.IsWindows() ? "git.exe" : "git");
        File.Move(Path.Combine(executables, Path.GetFileName(FakeGameProcess.ExecutablePath)), ExecutablePath);
    }

    public string Root { get; }

    public string ExecutablePath { get; }

    // The stand-in as it runs when the program that started it is gone, as a killed SavePoint's git is: a launcher starts it and exits.
    public async Task<int> StartLeftoverAsync(params string[] gameArguments)
    {
        var (launcher, id) = await LaunchAsync([], gameArguments);
        launcher.WaitForExit(30_000);
        return id;
    }

    // The stand-in with a launcher above it that stays up, so its parent is alive and is a child of this program.
    public async Task<int> StartBelowLiveLauncherAsync()
    {
        var (_, id) = await LaunchAsync(["--linger", "60000"], []);
        return id;
    }

    public void Dispose()
    {
        foreach (var running in FakeGameInstall.Inspector().List().Where(p => IsUnder(p.ExecutablePath)))
        {
            FakeGameProcess.Kill(running.Pid);
        }

        foreach (var id in _strays)
        {
            FakeGameProcess.Kill(id);
        }

        foreach (var launcher in _launchers)
        {
            launcher.Dispose();
        }
    }

    private async Task<(Process Launcher, int GameId)> LaunchAsync(string[] launcherArguments, string[] gameArguments)
    {
        var ready = _temp.Combine("ready-" + Guid.NewGuid().ToString("N"));
        var info = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        string[] arguments = [.. launcherArguments, "--spawn", ExecutablePath, "--ready-file", ready, .. gameArguments];
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var launcher = Process.Start(info)!;
        _launchers.Add(launcher);
        var id = await FakeGameProcess.WaitForReadyFileAsync(ready);
        _strays.Add(id);
        return (launcher, id);
    }

    private bool IsUnder(string? path) =>
        path is not null && path.StartsWith(Root + Path.DirectorySeparatorChar, PlatformFactory.CurrentOs == OsKind.Linux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
