using System.Security.Cryptography;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Services.Jobs;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.Settings;

/// <summary>
/// Moves SavePoint's storage to a new folder (D13.4): everything is copied and checked byte for byte first, only then do the
/// settings and the engine switch to the new root, and the old copies are left where they were. A move that fails or is
/// cancelled takes back what it copied and changes nothing else.
/// </summary>
internal sealed class StorageRelocator
{
    private const int _bufferSize = 1024 * 1024;
    private const long _progressInterval = 16L * 1024 * 1024;

    // Room the destination must keep free beyond the data itself, the same floor the engine keeps for its own writes.
    private const long _spareBytes = 100L * 1024 * 1024;

    private readonly BackendServices _services;
    private readonly Action<string, string>? _afterCopy;

    public StorageRelocator(BackendServices services, Action<string, string>? afterCopy)
    {
        _services = services;
        _afterCopy = afterCopy;
    }

    public async Task<StorageRelocationPreview> PreviewAsync(string newRoot, CancellationToken cancellationToken)
    {
        var destination = _services.PathTokens.Resolve(newRoot, "storageRoot");
        var games = await _services.Catalog.ListGamesAsync(cancellationToken).ConfigureAwait(false);
        return await _services.Storage.ReadAsync(
            async storage =>
            {
                var listing = await Task.Run(() => StorageContent.Scan(storage.Root, cancellationToken), cancellationToken).ConfigureAwait(false);
                var sizes = new List<GameBytesUsed>();
                foreach (var game in games)
                {
                    sizes.Add(new GameBytesUsed(game.Id, await DirectorySize.GetBytesAsync(storage.GameDirectory(game.Id), cancellationToken).ConfigureAwait(false)));
                }

                var blocker = BlockerOf(storage, games, destination, listing.Bytes);
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
                var listing = await Task.Run(() => StorageContent.Scan(storage.Root, CancellationToken.None)).ConfigureAwait(false);
                return BlockerOf(storage, games, destination, listing.Bytes);
            }).ConfigureAwait(false);
        if (blocker is not null)
        {
            throw blocker;
        }

        return _services.Jobs.Start(JobKind.RelocateStorage, null, cancellable: true, context => RunAsync(context, destination));
    }

    // The first reason the move cannot go ahead, in the order the user would fix them; null when it can.
    private SavepointException? BlockerOf(StorageSnapshot storage, IReadOnlyList<GameDefinition> games, string destination, long bytesNeeded)
    {
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

        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            return new StorageDestinationNotEmptyException(destination);
        }

        var required = bytesNeeded + _spareBytes;
        return _services.Disk.GetFreeBytes(destination) is { } free && free < required ? new DiskSpaceException(destination, required, free) : null;
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
        var listing = await Task.Run(() => StorageContent.Scan(storage.Root, context.CancellationToken), context.CancellationToken).ConfigureAwait(false);

        // Re-judged now that the locks are held: a game may have started, or the destination may have filled, while the job waited.
        if (BlockerOf(storage, games, destination, listing.Bytes) is { } blocker)
        {
            throw blocker;
        }

        var createdRoot = !Directory.Exists(destination);
        var progress = new Tally(context, listing);
        try
        {
            Directory.CreateDirectory(destination);
            foreach (var entry in listing.Entries)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                await CopyEntryAsync(storage.Root, destination, entry, progress, context.CancellationToken).ConfigureAwait(false);
            }

            // Everything is in place and checked; from here the switch is the only thing left that can fail.
            using var exclusive = await _services.Storage.Gate.EnterExclusiveAsync(context.CancellationToken).ConfigureAwait(false);
            var moved = _services.Storage.Build(destination);
            await _services.Catalog.UpdateSettingsAsync(
                current => (current ?? AppSettings.Defaults(_services.Platform.Directories.DefaultStorageRoot)) with { StorageRoot = moved.Root },
                CancellationToken.None).ConfigureAwait(false);
            _services.Storage.Replace(moved);
            return new StorageRelocationResult(storage.Root, moved.Root, progress.BytesDone);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TakeBack(destination, listing, createdRoot);
            throw new StorageRelocationFailedException(destination, $"The storage could not be moved to '{destination}': {e.Message}", e);
        }
        catch
        {
            TakeBack(destination, listing, createdRoot);
            throw;
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

    // Removes what this move copied; the destination folder itself goes only if the move made it and nothing else is in it.
    private static void TakeBack(string destination, ContentListing listing, bool createdRoot)
    {
        foreach (var folder in listing.Folders)
        {
            try
            {
                StorageFolders.DeleteTree(Path.Combine(destination, Path.GetFileName(folder)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left behind, the copy is only a folder the user can delete; the old storage is what stays in use.
            }
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
