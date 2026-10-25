using Savepoint.Core.Backend;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Services.Storage;

/// <summary>
/// Brings data an earlier build left in <see cref="Platform.AppDirectories.LegacyLocalDir"/> (the default storage and the logs) to the
/// current default folders. Nothing is copied: the folders sit on one drive, so a rename moves them whole or not at all. A failure never
/// stops the start; it leaves the data where it is and the app keeps using it there.
/// </summary>
internal static class LegacyLocalData
{
    private const string _storageFolder = "Storage";
    private const string _logsFolder = "Logs";

    /// <summary>Moves the old default storage and the old logs when there is something to move; does nothing where the platform has no old folder.</summary>
    /// <param name="services">The backend, for the platform's folders, the settings and the log.</param>
    /// <param name="cancellationToken">Cancels the wait for the settings file.</param>
    public static async Task MoveAsync(BackendServices services, CancellationToken cancellationToken)
    {
        if (services.Platform.Directories.LegacyLocalDir is not { } legacy)
        {
            return;
        }

        try
        {
            await MoveStorageAsync(services, legacy, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SavepointException)
        {
            // An unreadable settings file is reported by the catalog check that follows.
            services.Log.Warning($"Startup check: the old storage folder in {legacy} was not looked at: {e.Message}", e);
        }

        try
        {
            MoveLogs(services, legacy);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            services.Log.Warning($"Startup check: the old log folder in {legacy} could not be moved: {e.Message}", e);
        }
    }

    // Only when the settings name the old folder or no folder, and the new default holds nothing to cover: any other folder is the player's choice.
    private static async Task MoveStorageAsync(BackendServices services, string legacy, CancellationToken cancellationToken)
    {
        var old = Path.Combine(legacy, _storageFolder);
        var target = services.Platform.Directories.DefaultStorageRoot;
        var comparison = services.Platform.PathComparison;
        if (Same(old, target, comparison))
        {
            return;
        }

        var settings = await services.Catalog.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        var unset = string.IsNullOrWhiteSpace(settings?.StorageRoot);
        if (!unset && !Same(settings!.StorageRoot, old, comparison))
        {
            return;
        }

        var oldHoldsData = Directory.Exists(old) && Directory.EnumerateFileSystemEntries(old).Any();
        var targetHoldsData = Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any();
        if (!oldHoldsData)
        {
            // A rename that finished but whose settings write did not: the history is already in the new default.
            if (!unset && targetHoldsData && StorageContent.Folders(target).Any(StorageContent.HoldsHistory))
            {
                services.Log.Information($"Startup check: the storage was already moved to {target}; the settings follow it.");
                await TryCommitAsync(services, target).ConfigureAwait(false);
            }

            return;
        }

        if (targetHoldsData)
        {
            // Nothing to move onto; with no folder saved, the first run would take the new default and hide the old history.
            if (unset && !StorageContent.Folders(target).Any(StorageContent.HoldsHistory) && StorageContent.Folders(old).Any(StorageContent.HoldsHistory))
            {
                services.Log.Warning($"Startup check: {target} holds files of its own, so the storage in {old} stays where it is.");
                await TryCommitAsync(services, old).ConfigureAwait(false);
            }

            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(target))!);

            // An empty folder in the way would make the rename fail; it holds nothing.
            if (Directory.Exists(target))
            {
                Directory.Delete(target);
            }

            Directory.Move(old, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // With nothing saved, the first run would make an empty default and hide the history; naming the old folder keeps it in use.
            services.Log.Warning($"Startup check: the storage in {old} could not be moved to {target}, so it stays where it is: {e.Message}", e);
            if (unset)
            {
                await TryCommitAsync(services, old).ConfigureAwait(false);
            }

            return;
        }

        services.Log.Information($"Startup check: moved the storage from {old} to {target}.");
        if (unset)
        {
            return;
        }

        if (!await TryCommitAsync(services, target).ConfigureAwait(false))
        {
            // The settings still name the old folder, so the folder goes back rather than leave them pointing at nothing.
            try
            {
                Directory.Move(target, old);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                services.Log.Warning($"Startup check: the storage could not be moved back to {old}; it stays in {target}.", e);
            }
        }
    }

    // Any failure is a "no": the caller decides what that leaves, and nothing here may stop the start.
    private static async Task<bool> TryCommitAsync(BackendServices services, string root)
    {
        try
        {
            await StorageRootSetting.CommitAsync(services, root, null).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            services.Log.Warning($"Startup check: the settings could not be changed to {root}: {e.Message}", e);
            return false;
        }
    }

    // A file already in the new folder is never replaced; one that cannot be moved (an older SavePoint still has it open) stays.
    private static void MoveLogs(BackendServices services, string legacy)
    {
        var old = Path.Combine(legacy, _logsFolder);
        var target = services.Platform.Directories.LogsDir;
        if (Same(old, target, services.Platform.PathComparison) || !Directory.Exists(old))
        {
            return;
        }

        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(old))
        {
            var destination = Path.Combine(target, Path.GetFileName(file));
            if (File.Exists(destination))
            {
                continue;
            }

            try
            {
                File.Move(file, destination);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                services.Log.Warning($"Startup check: the old log {file} could not be moved: {e.Message}", e);
            }
        }

        try
        {
            Directory.Delete(old);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not empty (a file stayed, or a folder was never ours): the folder is left as it is.
        }
    }

    private static bool Same(string first, string second, StringComparison comparison) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            comparison);
}
