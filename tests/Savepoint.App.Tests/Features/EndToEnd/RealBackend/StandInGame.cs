using System.Diagnostics;
using System.Reflection;

namespace Savepoint.App.Tests.Features.EndToEnd.RealBackend;

// The stand-in game as a real program: where its build output is, how it is installed into a game folder, and how to find and stop it.
internal static class StandInGame
{
    // The test project's build writes where the tool is built; the program's name follows the OS.
    public static string ExecutablePath { get; } = Path.Combine(
        Path.GetFullPath(typeof(StandInGame).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "FakeGameDirectory").Value!),
        OperatingSystem.IsWindows() ? "FakeGame.exe" : "FakeGame");

    // Copies the tool's output into an install folder and renames the program, so its process name belongs to this folder alone.
    // The name stays within eight characters, which the kernel's 15-character process name keeps whole.
    public static string InstallInto(string installFolder)
    {
        var name = "fg" + Guid.NewGuid().ToString("N")[..6];
        var file = name + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
        Directory.CreateDirectory(installFolder);
        foreach (var source in Directory.GetFiles(Path.GetDirectoryName(ExecutablePath)!))
        {
            File.Copy(source, Path.Combine(installFolder, Path.GetFileName(source)), overwrite: true);
        }

        File.Move(Path.Combine(installFolder, Path.GetFileName(ExecutablePath)), Path.Combine(installFolder, file), overwrite: true);
        return file;
    }

    // The ids of every running copy of the program installed under <paramref name="programFile"/>.
    public static IReadOnlyList<int> ProcessIds(string programFile)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(programFile));
        try
        {
            return [.. processes.Select(process => process.Id)];
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    // Ends every running copy of the program and returns once they are gone or the wait runs out.
    public static void KillAll(string programFile, TimeSpan wait)
    {
        foreach (var id in ProcessIds(programFile))
        {
            Kill(id);
        }

        SpinWait.SpinUntil(() => ProcessIds(programFile).Count == 0, wait);
    }

    public static void Kill(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // No such process any more.
        }
        catch (InvalidOperationException)
        {
            // It exited while being stopped.
        }
    }
}

// One copy of the stand-in game that a test starts itself, outside SavePoint, for example to hold a file open. Disposing stops it.
internal sealed class StandInProcess : IDisposable
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(30);

    private readonly Process _process;
    private readonly string _readyFile;

    private StandInProcess(Process process, string readyFile)
    {
        _process = process;
        _readyFile = readyFile;
    }

    public static StandInProcess Start(string readyFile, params string[] arguments)
    {
        var info = new ProcessStartInfo(StandInGame.ExecutablePath) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--ready-file", readyFile }.Concat(arguments))
        {
            info.ArgumentList.Add(argument);
        }

        var process = Process.Start(info) ?? throw new InvalidOperationException("The stand-in game did not start.");
        return new StandInProcess(process, readyFile);
    }

    // The game writes its ready file once it is running, and everything it was asked to hold is held by then.
    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + _patience;
        while (!File.Exists(_readyFile))
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException($"The stand-in game exited with code {_process.ExitCode} before it was ready.");
            }

            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"The stand-in game never wrote {_readyFile}.");
            }

            await Task.Delay(20, cancellationToken);
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        _process.Dispose();
    }
}
