using System.Security.Cryptography;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Jobs;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.Settings;

/// <summary>
/// Moves SavePoint's storage to a new folder (D13.4): everything is copied and checked byte for byte first, only then do the
/// settings and the engine switch to the new root, and then the old copies are removed. A move that fails or is cancelled
/// takes back what it copied and changes nothing else, so the old copies stay.
/// </summary>
internal sealed class StorageRelocator
{
    /// <summary>The file a move writes in its destination before the first copy and removes when it ends; one that is found there means an earlier move did not finish.</summary>
    internal const string MarkerFileName = "savepoint-move-in-progress.txt";

    // Velopack's updater, which sits beside the folder the app runs from.
    private const string _updaterFileName = "Update.exe";

    private const int _bufferSize = 1024 * 1024;
    private const long _progressInterval = 16L * 1024 * 1024;

    // Room the destination must keep free beyond the data itself, the same floor the engine keeps for its own writes.
    private const long _spareBytes = 100L * 1024 * 1024;

    // A process that was just stopped can hold a file of an old git directory for a moment.
    private static readonly IReadOnlyList<TimeSpan> _removeDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000),
    ];

    private readonly BackendServices _services;
    private readonly Action<string, string>? _afterCopy;
    private readonly Action<string>? _afterSettings;

    public StorageRelocator(BackendServices services, Action<string, string>? afterCopy, Action<string>? afterSettings = null)
    {
        _services = services;
        _afterCopy = afterCopy;
        _afterSettings = afterSettings;
    }

    public async Task<StorageRelocationPreview> PreviewAsync(string newRoot, CancellationToken cancellationToken)
    {
        var destination = _services.PathTokens.Resolve(newRoot, "storageRoot");
        var games = await _services.Catalog.ListGamesAsync(cancellationToken).ConfigureAwait(false);
        return await _services.Storage.ReadAsync(
            async storage =>
            {
                ContentListing listing;
                try
                {
                    listing = await Task.Run(() => StorageContent.Scan(storage.Root, cancellationToken, strict: true), cancellationToken).ConfigureAwait(false);
                }
                catch (StorageSourceUnreadableException unreadable)
                {
                    // A move that cannot go ahead is an answer, not a failed call; nothing is sized because nothing could be listed.
                    return new StorageRelocationPreview(storage.Root, destination, [], 0, 0, _services.Disk.GetFreeBytes(destination) ?? 0, ErrorInfo.From(unreadable));
                }

                var sizes = new List<GameBytesUsed>();
                foreach (var game in games)
                {
                    sizes.Add(new GameBytesUsed(game.Id, await DirectorySize.GetBytesAsync(storage.GameDirectory(game.Id), cancellationToken).ConfigureAwait(false)));
                }

                var blocker = BlockerOf(storage, games, destination, listing);
                return new StorageRelocationPreview(
                    storage.Root,
                    destination,
                    sizes,
                    listing.Bytes,
                    listing.FileCount,
                    _services.Disk.GetFreeBytes(destination) ?? 0,
                    blocker is null ? null : ErrorInfo.From(blocker));
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> StartAsync(string newRoot)
    {
        var destination = _services.PathTokens.Resolve(newRoot, "storageRoot");
        var games = await _services.Catalog.ListGamesAsync().ConfigureAwait(false);
        var blocker = await _services.Storage.ReadAsync(
            async storage =>
            {
                var listing = await Task.Run(() => StorageContent.Scan(storage.Root, CancellationToken.None, strict: true)).ConfigureAwait(false);
                return BlockerOf(storage, games, destination, listing);
            }).ConfigureAwait(false);
        if (blocker is not null)
        {
            throw blocker;
        }

        return _services.Jobs.Start(JobKind.RelocateStorage, null, cancellable: true, context => RunAsync(context, destination));
    }

    // The first reason the move cannot go ahead, in the order the user would fix them; null when it can.
    private SavepointException? BlockerOf(StorageSnapshot storage, IReadOnlyList<GameDefinition> games, string destination, ContentListing listing)
    {
        // Games in the catalog and none of their folders here: the drive is not the one that holds the history, and copying nothing would look like success.
        if (games.Count > 0 && listing.Folders.Count == 0)
        {
            return new StorageUnreachableException(storage.Root, null);
        }

        if (_services.Sessions.RunningGameIds().FirstOrDefault() is { } running)
        {
            var name = games.FirstOrDefault(game => game.Id == running)?.Name ?? running;
            return new GameRunningException(running, name);
        }

        try
        {
            storage.PathSafety.Check(destination);
        }
        catch (PathSafetyException refused)
        {
            return refused;
        }

        if (InstallFolderOf(destination, storage) is { } installFolder)
        {
            return new StorageInsideInstallFolderException(destination, installFolder);
        }

        foreach (var game in games)
        {
            foreach (var folder in GameFolders.ResolveLoosely(_services.PathTokens, game).Where(folder => folder.Path is not null))
            {
                if (storage.PathSafety.PathsOverlap(destination, folder.Path!))
                {
                    return new GameOverlapException(game.Id, game.Name);
                }
            }
        }

        // Checked before "not empty": the files in such a folder are SavePoint's own half-finished copy, which the user can delete.
        if (File.Exists(Path.Combine(destination, MarkerFileName)))
        {
            return new StorageMoveUnfinishedException(destination);
        }

        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            return new StorageDestinationNotEmptyException(destination);
        }

        var required = listing.Bytes + _spareBytes;
        return _services.Disk.GetFreeBytes(destination) is { } free && free < required ? new DiskSpaceException(destination, required, free) : null;
    }

    // The installed app runs from "current" inside an install folder that also holds Update.exe, and an update or an uninstall deletes
    // that whole folder. Returns the install folder when the destination lies in it.
    private string? InstallFolderOf(string destination, StorageSnapshot storage)
    {
        if (Directory.GetParent(_services.InstallDirectory)?.FullName is not { } installFolder || !File.Exists(Path.Combine(installFolder, _updaterFileName)))
        {
            return null;
        }

        var relative = Path.GetRelativePath(storage.PathSafety.ResolveRealPath(installFolder), storage.PathSafety.ResolveRealPath(destination));
        var inside = relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        return inside ? installFolder : null;
    }

    private async Task<JobResult?> RunAsync(JobContext context, string destination)
    {
        try
        {
            // The library key first, then every game: nothing can be added, deleted or saved while the copy runs.
            return await _services.Locks.RunExclusiveAsync(
                BackendServices.LibraryLockKey,
                async () =>
                {
                    var games = await _services.Catalog.ListGamesAsync(context.CancellationToken).ConfigureAwait(false);
                    return await _services.RunWithLocksAsync(
                        games.Select(game => game.Id),
                        () => CopyAndSwitchAsync(context, games, destination),
                        context.CancellationToken).ConfigureAwait(false);
                },
                context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCancelledByUserException(CancelledOperation.StorageRelocation, null);
        }
    }

    private async Task<JobResult?> CopyAndSwitchAsync(JobContext context, IReadOnlyList<GameDefinition> games, string destination)
    {
        var storage = await _services.Storage.GetAsync(context.CancellationToken).ConfigureAwait(false);
        var listing = await Task.Run(() => StorageContent.Scan(storage.Root, context.CancellationToken, strict: true), context.CancellationToken).ConfigureAwait(false);

        // Re-judged now that the locks are held: a game may have started, or the destination may have filled, while the job waited.
        if (BlockerOf(storage, games, destination, listing) is { } blocker)
        {
            throw blocker;
        }

        _services.Log.Information($"Storage move: copying {listing.FileCount} files ({listing.Bytes} bytes) from {storage.Root} to {destination}.");
        var createdRoot = !Directory.Exists(destination);
        var progress = new Tally(context, listing);
        StorageRelocationResult result;
        try
        {
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, MarkerFileName), $"SavePoint is moving its data here from '{storage.Root}'. If this file is still here, the move did not finish and the other files in this folder are an incomplete copy.{Environment.NewLine}");
            foreach (var entry in listing.Entries)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                await CopyEntryAsync(storage.Root, destination, entry, progress, context.CancellationToken).ConfigureAwait(false);
            }

            _services.Log.Information("Storage move: every file is copied and checked.");

            // Everything is in place and checked; from here the switch is the only thing left that can fail.
            using var exclusive = await _services.Storage.Gate.EnterExclusiveAsync(context.CancellationToken).ConfigureAwait(false);
            var moved = _services.Storage.Build(destination);
            await CommitSettingsAsync(moved.Root).ConfigureAwait(false);
            _services.Storage.Replace(moved);
            _services.Log.Information($"Storage move: now using {moved.Root}.");
            result = new StorageRelocationResult(storage.Root, moved.Root, progress.BytesDone, []);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _services.Log.Warning($"Storage move: failed; taking back the copy at {destination}.", e);
            TakeBack(destination, listing, createdRoot);
            throw new StorageRelocationFailedException(destination, $"The storage could not be moved to '{destination}': {e.Message}", e);
        }
        catch (Exception e)
        {
            if (e is OperationCanceledException or OperationCancelledByUserException)
            {
                _services.Log.Information($"Storage move: stopped on request; taking back the copy at {destination}.");
            }
            else
            {
                _services.Log.Warning($"Storage move: failed; taking back the copy at {destination}.", e);
            }

            TakeBack(destination, listing, createdRoot);
            throw;
        }

        // The move has switched over; nothing past this point may undo it, so a cancel no longer reaches the removal either.
        TryDelete(Path.Combine(destination, MarkerFileName));
        var notRemoved = await RemoveOldCopiesAsync(context, storage, listing, result.NewRoot).ConfigureAwait(false);
        _services.Log.Information($"Storage move: the old copies are removed; {notRemoved.Count} could not be removed.");
        return result with { NotRemoved = notRemoved };
    }

    // The settings write is the commit point. A write that threw may still have taken effect (a clean-up after the rename failed,
    // or the rename applied and reported a transient error), and then the move has to carry on, not be undone.
    private async Task CommitSettingsAsync(string newRoot)
    {
        try
        {
            await _services.Catalog.UpdateSettingsAsync(
                current => (current ?? AppSettings.Defaults(_services.Platform.Directories.DefaultStorageRoot)) with { StorageRoot = newRoot },
                CancellationToken.None).ConfigureAwait(false);
            _afterSettings?.Invoke(newRoot);
        }
        catch (Exception)
        {
            // Stored already: the new folder is the root from here on.
            if (!await SettingsNameAsync(newRoot).ConfigureAwait(false))
            {
                throw;
            }
        }
    }

    private async Task<bool> SettingsNameAsync(string root)
    {
        try
        {
            var stored = await _services.Catalog.GetSettingsAsync(CancellationToken.None).ConfigureAwait(false);
            return string.Equals(stored?.StorageRoot, root, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SavepointException)
        {
            return false;
        }
    }

    // Nothing refers to the old root any more, so its copies are only duplicates. Only SavePoint's own git directories go, each
    // one checked again by GitDirRemoval, and the old root and anything else in it stay. A copy that cannot be removed is left,
    // named in the job's lines and returned; it never fails the move.
    private async Task<IReadOnlyList<ClearFailure>> RemoveOldCopiesAsync(JobContext context, StorageSnapshot old, ContentListing listing, string newRoot)
    {
        var left = new List<ClearFailure>();
        if (Overlaps(old, newRoot))
        {
            return left;
        }

        foreach (var folder in listing.Folders)
        {
            // Built from the folder as listed, since a folder in the storage need not be named like a game (a copy made in Explorer);
            // GitDirRemoval still refuses whatever is not a git directory SavePoint made.
            foreach (var gitDir in new[] { Path.Combine(folder, "install.git"), Path.Combine(folder, "external.git") })
            {
                try
                {
                    await GitDirRemoval.RemoveAsync(gitDir, old.Root, old.PathSafety.ResolveRealPath, _removeDelays, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
                {
                    // The move has switched over, so nothing here may fail it.
                    context.AddLine(gitDir);
                    _services.Log.Warning($"Storage move: could not remove the old copy {gitDir}: {ErrorInfo.From(e).MessageId}.", e);
                    left.Add(new ClearFailure(gitDir, ErrorInfo.From(e)));
                }
            }

            StorageFolders.RemoveIfEmpty(folder);
        }

        return left;
    }

    // A root that cannot be judged is treated as overlapping: deleting nothing is always safe.
    private static bool Overlaps(StorageSnapshot old, string newRoot)
    {
        try
        {
            return old.PathSafety.PathsOverlap(old.Root, newRoot);
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            return true;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A marker left in the folder now in use is only a note; the next move away from it never looks at it.
        }
    }

    private async Task CopyEntryAsync(string sourceRoot, string destinationRoot, ContentEntry entry, Tally progress, CancellationToken cancellationToken)
    {
        var source = Path.Combine(sourceRoot, entry.RelativePath);
        var target = Path.Combine(destinationRoot, entry.RelativePath);
        try
        {
            if (entry.LinkTarget is { } link)
            {
                CopyLink(target, link, entry.IsFolder);
            }
            else if (entry.IsFolder)
            {
                Directory.CreateDirectory(target);
                return;
            }
            else
            {
                await CopyFileAsync(source, target, entry, progress, cancellationToken).ConfigureAwait(false);
                return;
            }

            progress.FileDone(entry.RelativePath, 0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw new StorageRelocationFailedException(target, $"'{source}' could not be copied to '{target}': {e.Message}", e);
        }
    }

    // A link is made again as a link, so nothing outside the storage is ever read through it or copied by value.
    private static void CopyLink(string target, string linkTarget, bool toFolder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (toFolder)
        {
            Directory.CreateSymbolicLink(target, linkTarget);
        }
        else
        {
            File.CreateSymbolicLink(target, linkTarget);
        }

        if (new FileInfo(target).LinkTarget != linkTarget)
        {
            throw new IOException($"The link '{target}' does not point where its source does.");
        }
    }

    // The source is hashed as it is read, the copy is read back and hashed again; a copy that differs in a single byte fails the move.
    private async Task CopyFileAsync(string source, string target, ContentEntry entry, Tally progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        byte[] expected;
        long copied = 0;
        var sinceReport = 0L;
        progress.Starting(entry.RelativePath);
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            await using var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, _bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var to = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, _bufferSize, FileOptions.Asynchronous);
            var buffer = new byte[_bufferSize];
            int read;
            while ((read = await from.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;
                sinceReport += read;
                if (sinceReport >= _progressInterval)
                {
                    progress.Moved(entry.RelativePath, sinceReport);
                    sinceReport = 0;
                }
            }

            to.Flush(flushToDisk: true);
            expected = hash.GetHashAndReset();
        }

        File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        _afterCopy?.Invoke(source, target);

        var actual = await HashAsync(target, cancellationToken).ConfigureAwait(false);
        if (new FileInfo(target).Length != copied || !actual.AsSpan().SequenceEqual(expected))
        {
            throw new StorageRelocationFailedException(target, $"The copy '{target}' does not match '{source}'.", null);
        }

        progress.FileDone(entry.RelativePath, sinceReport);
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, _bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    // Removes what this move copied; the destination folder itself goes only if the move made it and nothing else is in it. The
    // marker goes last and only once every copied folder is gone: a take-back that is cut short, or cannot remove everything, leaves a
    // partial copy that still says an earlier move did not finish.
    private static void TakeBack(string destination, ContentListing listing, bool createdRoot)
    {
        var allGone = true;
        foreach (var folder in listing.Folders)
        {
            try
            {
                StorageFolders.DeleteTree(Path.Combine(destination, Path.GetFileName(folder)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left behind, the copy is only a folder the user can delete; the old storage is what stays in use.
                allGone = false;
            }
        }

        if (allGone)
        {
            TryDelete(Path.Combine(destination, MarkerFileName));
        }

        try
        {
            if (createdRoot && Directory.Exists(destination) && !Directory.EnumerateFileSystemEntries(destination).Any())
            {
                Directory.Delete(destination);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An empty folder left behind is harmless.
        }
    }

    // Files and bytes done so far, reported to the job as one bar.
    private sealed class Tally
    {
        private readonly JobContext _context;
        private readonly int _filesTotal;
        private readonly long _bytesTotal;
        private int _filesDone;

        public Tally(JobContext context, ContentListing listing)
        {
            _context = context;
            _filesTotal = listing.FileCount;
            _bytesTotal = listing.Bytes;
            Report(null);
        }

        public long BytesDone { get; private set; }

        public void Starting(string path) => Report(path);

        public void Moved(string path, long bytes)
        {
            BytesDone += bytes;
            Report(path);
        }

        public void FileDone(string path, long remainingBytes)
        {
            BytesDone += remainingBytes;
            _filesDone++;
            Report(path);
        }

        private void Report(string? path) =>
            _context.Progress.Report(new JobProgress(_filesDone, _filesTotal, BytesDone, _bytesTotal, path));
    }
}
