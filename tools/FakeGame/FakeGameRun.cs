using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Savepoint.FakeGame;

// One run of the stand-in game: either it starts another program and leaves, or it plays until it is told to stop.
internal sealed class FakeGameRun
{
    private const int _failedToSpawn = 1;
    private const int _lockHeld = 3;

    private readonly Options _options;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly object _saveGate = new();
    private int? _nextSave;

    public FakeGameRun(Options options, TextReader input, TextWriter output, TextWriter error)
    {
        _options = options;
        _input = input;
        _output = output;
        _error = error;
    }

    public async Task<int> RunAsync()
    {
        if (_options.SpawnPath is not null)
        {
            return Spawn();
        }

        FileStream? held = null;
        try
        {
            if (_options.LockFile is not null && !TryHold(_options.LockFile, out held))
            {
                return _lockHeld;
            }

            if (_options.SavesDirectory is not null)
            {
                Directory.CreateDirectory(_options.SavesDirectory);
            }

            WriteReadyFile();
            await PlayAsync();
            return _options.ExitCode;
        }
        finally
        {
            held?.Dispose();
        }
    }

    // A launcher stub: start the real game, do not wait for it, and report ready only once it is running.
    private int Spawn()
    {
        var info = new ProcessStartInfo(_options.SpawnPath!)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(_options.SpawnPath!)) ?? Environment.CurrentDirectory,
        };
        foreach (var argument in _options.SpawnArguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var child = Process.Start(info);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            Say(_error, $"FakeGame: could not start '{_options.SpawnPath}': {e.Message}");
            return _failedToSpawn;
        }

        WriteReadyFile();
        if (_options.LingerMilliseconds > 0)
        {
            Thread.Sleep(_options.LingerMilliseconds);
        }

        return _options.ExitCode;
    }

    // An exclusive handle, held until the game exits, makes the file look in use to anything else that opens it.
    private bool TryHold(string path, out FileStream? held)
    {
        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }

            held = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            held = null;
            Say(_error, $"FakeGame: could not lock '{path}': {e.Message}");
            return false;
        }
    }

    private async Task PlayAsync()
    {
        using var stopTimers = new CancellationTokenSource();
        var quit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(() => ReadCommandsAsync(quit));
        if (_options.SaveEveryMilliseconds > 0 && _options.SavesDirectory is not null)
        {
            _ = Task.Run(() => SavePeriodicallyAsync(stopTimers.Token));
        }

        if (_options.ExitAfterMilliseconds is { } after)
        {
            _ = Task.Delay(after).ContinueWith(_ => quit.TrySetResult(), TaskScheduler.Default);
        }

        await quit.Task;
        await stopTimers.CancelAsync();
    }

    // End of input just means nobody is typing; a game launched without a console must keep running.
    private async Task ReadCommandsAsync(TaskCompletionSource quit)
    {
        while (await _input.ReadLineAsync() is { } line)
        {
            switch (line.Trim().ToLowerInvariant())
            {
                case "save":
                    Save();
                    break;
                case "quit":
                    quit.TrySetResult();
                    return;
            }
        }
    }

    private async Task SavePeriodicallyAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.SaveEveryMilliseconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                Save();
            }
        }
        catch (OperationCanceledException)
        {
            // The game is quitting.
        }
    }

    // Each save is a new file, numbered in order, with nothing in it that depends on the clock.
    private void Save()
    {
        if (_options.SavesDirectory is not { } folder)
        {
            Say(_error, "FakeGame: there is nowhere to save; start it with --saves <dir>.");
            return;
        }

        lock (_saveGate)
        {
            var number = _nextSave ?? NextSequence(folder);
            _nextSave = number + 1;
            var name = string.Create(CultureInfo.InvariantCulture, $"save-{number:D4}.sav");
            File.WriteAllText(Path.Combine(folder, name), $"# SavePoint fake game save\nsequence={number}\n");
            Say(_output, $"saved {name}");
        }
    }

    private static int NextSequence(string folder)
    {
        var highest = 0;
        foreach (var path in Directory.EnumerateFiles(folder, "save-*.sav"))
        {
            var digits = Path.GetFileNameWithoutExtension(path)["save-".Length..];
            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                highest = Math.Max(highest, number);
            }
        }

        return highest + 1;
    }

    // Written whole, then renamed, so a test that sees the file sees all of it.
    private void WriteReadyFile()
    {
        if (_options.ReadyFile is not { } path)
        {
            return;
        }

        var pending = path + ".tmp";
        File.WriteAllText(pending, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        File.Move(pending, path, overwrite: true);
    }

    // The reader of a pipe may be gone; a game does not die for that.
    private static void Say(TextWriter writer, string line)
    {
        try
        {
            writer.WriteLine(line);
            writer.Flush();
        }
        catch (IOException)
        {
            // Nobody is listening.
        }
    }
}
