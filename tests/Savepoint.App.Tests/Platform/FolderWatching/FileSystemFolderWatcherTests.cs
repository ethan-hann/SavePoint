using Savepoint.App.Platform.FolderWatching;
using Savepoint.Testing;

namespace Savepoint.App.Tests.Platform.FolderWatching;

// These run the operating system's own watcher on a temp folder, so each waits on a condition with a deadline instead of a fixed time.
public sealed class FileSystemFolderWatcherTests : IDisposable
{
    // Long enough that a stalled watcher is told apart from a slow disk on a busy machine.
    private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(20);

    // How long a test looks for a signal that must not come; a watcher that wrongly signalled would do so within the throttle of a tenth of a second.
    private static readonly TimeSpan _quietLook = TimeSpan.FromMilliseconds(600);

    private readonly string _root = Directory.CreateTempSubdirectory("savepoint-watch-").FullName;
    private readonly RecordingDiagnosticLog _log = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow + _deadline;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "The watcher never signalled.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class Signals
    {
        private int _changed;
        private int _faulted;

        public int Changed => Volatile.Read(ref _changed);

        public int Faulted => Volatile.Read(ref _faulted);

        public void Attach(IFolderWatch watch)
        {
            watch.Changed += (_, _) => Interlocked.Increment(ref _changed);
            watch.Faulted += (_, _) => Interlocked.Increment(ref _faulted);
        }
    }

    [Fact]
    public async Task A_new_file_in_a_watched_folder_raises_Changed()
    {
        var folder = Folder("install");
        using var watch = new FileSystemFolderWatcher(_log).Watch([folder]);
        var signals = new Signals();
        signals.Attach(watch);

        await File.WriteAllTextAsync(Path.Combine(folder, "mod.pak"), "x", TestContext.Current.CancellationToken);

        await UntilAsync(() => signals.Changed > 0);
    }

    [Fact]
    public async Task A_file_changed_in_a_subfolder_raises_Changed()
    {
        var folder = Folder("install");
        var nested = Path.Combine(folder, "data", "textures");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "a.dds");
        await File.WriteAllTextAsync(file, "old", TestContext.Current.CancellationToken);
        using var watch = new FileSystemFolderWatcher(_log).Watch([folder]);
        var signals = new Signals();
        signals.Attach(watch);

        await File.WriteAllTextAsync(file, "newer contents", TestContext.Current.CancellationToken);

        await UntilAsync(() => signals.Changed > 0);
    }

    [Fact]
    public async Task Every_folder_in_the_list_is_watched()
    {
        var install = Folder("install");
        var saves = Folder("saves");
        using var watch = new FileSystemFolderWatcher(_log).Watch([install, saves]);
        var signals = new Signals();
        signals.Attach(watch);

        await File.WriteAllTextAsync(Path.Combine(saves, "slot1.sav"), "x", TestContext.Current.CancellationToken);

        await UntilAsync(() => signals.Changed > 0);
    }

    [Fact]
    public async Task A_burst_of_writes_raises_Changed_far_fewer_times_than_there_were_writes()
    {
        var folder = Folder("install");
        using var watch = new FileSystemFolderWatcher(_log).Watch([folder]);
        var signals = new Signals();
        signals.Attach(watch);

        const int writes = 300;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < writes; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(folder, $"f{i}.bin"), "x", TestContext.Current.CancellationToken);
        }

        await UntilAsync(() => signals.Changed > 0);
        await Task.Delay(_quietLook, TestContext.Current.CancellationToken);

        // At most one signal per gap while the writes go on, however slow the disk or a virus scanner makes them.
        var perGap = (int)(timer.Elapsed / FileSystemFolderWatch.SignalGap) + 3;
        Assert.InRange(signals.Changed, 1, Math.Min(perGap, writes - 1));
    }

    [Fact]
    public void A_missing_folder_is_skipped_without_an_error_and_logged_once()
    {
        var present = Folder("saves");
        var missing = Path.Combine(_root, "gone");
        var watcher = new FileSystemFolderWatcher(_log);

        using (watcher.Watch([missing, present]))
        {
        }

        using (watcher.Watch([missing]))
        {
        }

        var lines = _log.Containing(missing);
        Assert.Single(lines);
        Assert.Equal(LogLevel.Warning, lines[0].Level);
    }

    [Fact]
    public async Task The_folders_that_exist_are_still_watched_when_another_is_missing()
    {
        var present = Folder("saves");
        using var watch = new FileSystemFolderWatcher(_log).Watch([Path.Combine(_root, "gone"), present]);
        var signals = new Signals();
        signals.Attach(watch);

        await File.WriteAllTextAsync(Path.Combine(present, "slot1.sav"), "x", TestContext.Current.CancellationToken);

        await UntilAsync(() => signals.Changed > 0);
    }

    [Fact]
    public async Task Nothing_is_raised_after_the_watch_is_disposed()
    {
        var folder = Folder("install");
        var watch = new FileSystemFolderWatcher(_log).Watch([folder]);
        var signals = new Signals();
        signals.Attach(watch);

        watch.Dispose();
        await File.WriteAllTextAsync(Path.Combine(folder, "late.bin"), "x", TestContext.Current.CancellationToken);
        await Task.Delay(_quietLook, TestContext.Current.CancellationToken);

        Assert.Equal(0, signals.Changed);
    }

    [Fact]
    public async Task A_watcher_error_raises_Faulted_and_the_folder_is_still_watched()
    {
        var folder = Folder("install");
        using var watch = (FileSystemFolderWatch)new FileSystemFolderWatcher(_log).Watch([folder]);
        var signals = new Signals();
        signals.Attach(watch);

        watch.HandleError(folder);
        Assert.Equal(1, signals.Faulted);

        await File.WriteAllTextAsync(Path.Combine(folder, "after.bin"), "x", TestContext.Current.CancellationToken);

        await UntilAsync(() => signals.Changed > 0);
    }
}
