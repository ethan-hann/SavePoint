using System.Runtime.ExceptionServices;
using System.Text.Json;
using Savepoint.Core.Backend;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Services.Storage;

/// <summary>
/// Brings data an earlier build left in <see cref="Platform.AppDirectories.LegacyLocalDir"/> (the default storage and the logs) to the
/// current default folders. Nothing is copied: the folders sit on one drive, so a rename moves them whole or not at all. A failure
/// leaves the data where it is and the app keeps using it there; the next start tries again.
/// </summary>
internal static class LegacyLocalData
{
    private const string _storageFolder = "Storage";
    private const string _logsFolder = "Logs";
    private const int _readBackAttempts = 3;

    /// <summary>
    /// Moves the old default storage and the old logs when there is something to move; does nothing where the platform has no old
    /// folder. When history cannot be made the saved root, the folder holding it is used for this run only, so it is never hidden.
    /// </summary>
    /// <param name="services">The backend, for the platform's folders, the settings and the log.</param>
    /// <param name="afterSettings">Runs right after a settings write for a storage root; tests use it to make the write seem to fail.</param>
    /// <param name="cancellationToken">Cancels the wait for the settings file.</param>
    /// <exception cref="Exception">A failure of an unexpected kind in the settings write, after the storage was put back.</exception>
    public static async Task MoveAsync(BackendServices services, Action<string>? afterSettings, CancellationToken cancellationToken)
    {
        if (services.Platform.Directories.LegacyLocalDir is not { } legacy)
        {
            return;
        }

        try
        {
            await MoveStorageAsync(services, legacy, afterSettings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SavepointException or JsonException)
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

    // Only when the settings name the old folder, the new default or no folder, and the new default holds nothing to cover: any other
    // folder is the player's choice.
    private static async Task MoveStorageAsync(BackendServices services, string legacy, Action<string>? afterSettings, CancellationToken cancellationToken)
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
        var namesTarget = !unset && Same(settings!.StorageRoot, target, comparison);
        if (!unset && !namesTarget && !Same(settings!.StorageRoot, old, comparison))
        {
            return;
        }

        var oldHoldsData = HoldsData(old);
        var targetHoldsData = HoldsData(target);
        var oldHoldsHistory = oldHoldsData && StorageContent.Folders(old).Any(StorageContent.HoldsHistory);
        var targetHoldsHistory = targetHoldsData && StorageContent.Folders(target).Any(StorageContent.HoldsHistory);

        if (namesTarget)
        {
            // The settings already follow a move; only history still in the old folder and nothing in the new one is left to finish.
            if (targetHoldsData || !oldHoldsHistory)
            {
                return;
            }

            services.Log.Warning($"Startup check: the settings name {target}, which holds nothing, while the history is still in {old}; finishing the move.");
        }
        else if (!oldHoldsData)
        {
            // A rename that finished but whose settings write did not: the history is already in the new default.
            if (!unset && targetHoldsHistory)
            {
                services.Log.Information($"Startup check: the storage was already moved to {target}; the settings follow it.");
                if (!await TryCommitAsync(services, target, afterSettings).ConfigureAwait(false))
                {
                    // The settings still name the empty old folder, which new snapshots must not land in.
                    ShowForThisRun(services, target);
                }
            }

            return;
        }
        else if (targetHoldsData)
        {
            // Nothing to move onto. With no folder saved, the first run would take the new default and hide the old history, so the
            // old folder is saved instead and the warnings about a folder the installer clears apply.
            if (unset && oldHoldsHistory)
            {
                services.Log.Warning(targetHoldsHistory
                    ? $"Startup check: {target} and {old} both hold history, so the storage in {old} stays where it is."
                    : $"Startup check: {target} holds files of its own, so the storage in {old} stays where it is.");
                await KeepInUseAsync(services, old, afterSettings).ConfigureAwait(false);
            }
            else if (!unset)
            {
                services.Log.Warning($"Startup check: {target} holds files of its own, so the storage in {old} stays where it is.");
            }

            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(target))!);

            // A folder in the way would make the rename fail; it holds nothing but, at most, the empty-start note.
            if (Directory.Exists(target))
            {
                File.Delete(Path.Combine(target, StorageContent.EmptyStartMarkerFileName));
                Directory.Delete(target);
            }

            Directory.Move(old, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // With nothing saved the first run would make an empty default and hide the history, and settings naming an empty
            // default do the same; naming the old folder keeps it in use.
            services.Log.Warning($"Startup check: the storage in {old} could not be moved to {target}, so it stays where it is: {e.Message}", e);
            if (unset || namesTarget)
            {
                await KeepInUseAsync(services, old, afterSettings).ConfigureAwait(false);
            }

            return;
        }

        services.Log.Information($"Startup check: moved the storage from {old} to {target}.");
        if (unset || namesTarget)
        {
            return;
        }

        // Whatever goes wrong with the settings write, the folder is put back before the failure is passed on.
        Exception? unexpected = null;
        var committed = false;
        try
        {
            committed = await TryCommitAsync(services, target, afterSettings).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            unexpected = e;
        }

        if (!committed)
        {
            await MoveBackAsync(services, old, target, comparison, cancellationToken).ConfigureAwait(false);
        }

        if (unexpected is not null)
        {
            ExceptionDispatchInfo.Capture(unexpected).Throw();
        }
    }

    // Saves the old folder as the storage root; when even that cannot be written, the folder is used for this run only, so the
    // history stays visible and the warnings about a folder the installer clears still apply. The next start tries the save again.
    private static async Task KeepInUseAsync(BackendServices services, string old, Action<string>? afterSettings)
    {
        if (!await TryCommitAsync(services, old, afterSettings).ConfigureAwait(false))
        {
            ShowForThisRun(services, old);
        }
    }

    private static void ShowForThisRun(BackendServices services, string root)
    {
        try
        {
            services.Storage.UseForThisRun(root);
            services.Log.Warning($"Startup check: the settings could not be made to name {root}, so it is used for this run only; the next start tries again.");
        }
        catch (SavepointException e)
        {
            services.Log.Warning($"Startup check: {root} could not be used for this run: {e.Message}", e);
        }
    }

    // The settings write failed to confirm, but it may have landed: the folder goes back only while the settings still name the old
    // one (or none). If they cannot be read, nothing moves and this run uses the new default; the next start finishes the move. Nothing
    // here may replace the failure that led to it.
    private static async Task MoveBackAsync(BackendServices services, string old, string target, StringComparison comparison, CancellationToken cancellationToken)
    {
        string? saved = null;
        var read = false;
        try
        {
            for (var attempt = 1; attempt <= _readBackAttempts && !read; attempt++)
            {
                try
                {
                    saved = (await services.Catalog.GetSettingsAsync(cancellationToken).ConfigureAwait(false))?.StorageRoot;
                    read = true;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or SavepointException or JsonException)
                {
                    services.Log.Warning($"Startup check: the settings could not be read back (attempt {attempt}): {e.Message}", e);
                    if (attempt < _readBackAttempts)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            services.Log.Warning($"Startup check: reading the settings back stopped: {e.Message}", e);
        }

        if (!read)
        {
            services.Log.Warning($"Startup check: the settings could not be read, so the storage stays in {target}; the next start finishes the move.");
            ShowForThisRun(services, target);
            return;
        }

        if (!string.IsNullOrWhiteSpace(saved) && !Same(saved, old, comparison))
        {
            services.Log.Information($"Startup check: the settings name {saved}, so the storage stays in {target}.");
            return;
        }

        // The settings still name the old folder, so the folder goes back rather than leave them pointing at nothing.
        try
        {
            Directory.Move(target, old);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            services.Log.Warning($"Startup check: the storage could not be moved back to {old}; it stays in {target}, and the next start finishes the move.", e);
            ShowForThisRun(services, target);
        }
    }

    // A failure of a known kind is a "no": the caller decides what that leaves. Anything else is passed on.
    private static async Task<bool> TryCommitAsync(BackendServices services, string root, Action<string>? afterSettings)
    {
        try
        {
            await StorageRootSetting.CommitAsync(services, root, afterSettings).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SavepointException or JsonException)
        {
            services.Log.Warning($"Startup check: the settings could not be changed to {root}: {e.Message}", e);
            return false;
        }
    }

    // The empty-start note SavePoint leaves in an otherwise empty folder is not data.
    private static bool HoldsData(string folder) =>
        Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any() && !StorageContent.HoldsOnlyEmptyStartMarker(folder);

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
