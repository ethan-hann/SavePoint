using System.Diagnostics;
using System.Reflection;

namespace Savepoint.Core.Tests.Services.Exe;

// Runs the real FakeGame executable as a child process, always cleaned up when the test ends.
internal sealed class FakeGameProcess : IDisposable
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(30);

    private readonly Process _process;
    private readonly List<string> _output = [];
    private readonly List<string> _errors = [];
    private readonly List<int> _strays = [];

    private FakeGameProcess(Process process)
    {
        _process = process;
    }

    // The test project's build writes where the tool is built; the executable name follows the OS.
    public static string ExecutablePath { get; } = Path.Combine(
        Path.GetFullPath(typeof(FakeGameProcess).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "FakeGameDirectory").Value!),
        OperatingSystem.IsWindows() ? "FakeGame.exe" : "FakeGame");

    public int Id => _process.Id;

    public bool HasExited => _process.HasExited;

    public IReadOnlyList<string> Output
    {
        get
        {
            lock (_output)
            {
                return [.. _output];
            }
        }
    }

    public IReadOnlyList<string> Errors
    {
        get
        {
            lock (_errors)
            {
                return [.. _errors];
            }
        }
    }

    public static FakeGameProcess StartIn(string? workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (workingDirectory is not null)
        {
            info.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = info };
        var game = new FakeGameProcess(process);
        process.OutputDataReceived += (_, e) => Collect(game._output, e.Data);
        process.ErrorDataReceived += (_, e) => Collect(game._errors, e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return game;
    }

    public static FakeGameProcess Start(params string[] arguments) => StartIn(null, arguments);

    // Child processes the launcher stub left behind; killed when the test ends.
    public void Adopt(int processId) => _strays.Add(processId);

    public async Task SendAsync(string line)
    {
        await _process.StandardInput.WriteLineAsync(line);
        await _process.StandardInput.FlushAsync();
    }

    public void CloseInput() => _process.StandardInput.Close();

    public async Task<int> WaitForExitAsync()
    {
        // Waits for the exit only: a child the launcher stub spawned still holds the redirected pipes, so waiting for their end would hang.
        var exited = await Task.Run(() => _process.WaitForExit((int)_patience.TotalMilliseconds), TestContext.Current.CancellationToken);
        if (!exited)
        {
            throw new TimeoutException($"FakeGame {_process.Id} did not exit within {_patience}.");
        }

        // A moment for the redirected streams to deliver what was written just before the exit.
        await Task.Delay(150, TestContext.Current.CancellationToken);
        return _process.ExitCode;
    }

    public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < (timeout ?? _patience))
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        return condition();
    }

    public static async Task<int> WaitForReadyFileAsync(string path)
    {
        Assert.True(await WaitUntilAsync(() => File.Exists(path)), $"the ready file {path} never appeared");

        // The game renames the file into place whole, but on Windows a scanner or indexer can still hold the new file open for a moment.
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return int.Parse((await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Trim());
            }
            catch (IOException) when (deadline.Elapsed < _patience)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
    }

    public void Dispose()
    {
        foreach (var id in _strays)
        {
            Kill(id);
        }

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

    public static void Kill(int processId)
    {
        try
        {
            using var stray = Process.GetProcessById(processId);
            stray.Kill(entireProcessTree: true);
            stray.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // No such process any more.
        }
        catch (InvalidOperationException)
        {
            // Exited while being killed.
        }
    }

    public static bool IsRunning(int processId)
    {
        try
        {
            using var other = Process.GetProcessById(processId);
            return !other.HasExited && !IsZombie(processId);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // A process nobody has reaped (a container's PID 1 may never) still answers to its id, though it ran its last instruction.
    private static bool IsZombie(int processId)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            return Savepoint.Core.Platform.Linux.LinuxProcessInspector.IsExitedState(File.ReadAllText($"/proc/{processId}/stat"));
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void Collect(List<string> lines, string? data)
    {
        if (data is null)
        {
            return;
        }

        lock (lines)
        {
            lines.Add(data);
        }
    }
}
