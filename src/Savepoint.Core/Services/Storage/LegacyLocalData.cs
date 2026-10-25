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

    // Only when the settings name the old folder or no folder, and the new default is not there yet: any other folder is the player's choice.
    private static async Task MoveStorageAsync(BackendServices services, string legacy, CancellationToken cancellationToken)
    {
        var old = Path.Combine(legacy, _storageFolder);
        var target = services.Platform.Directories.DefaultStorageRoot;
        var comparison = services.Platform.PathComparison;
        if (Same(old, target, comparison) || !Directory.Exists(old) || !Directory.EnumerateFileSystemEntries(old).Any() || Directory.Exists(target))
        {
            return;
        }

        var settings = await services.Catalog.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        var unset = string.IsNullOrWhiteSpace(settings?.StorageRoot);
        if (!unset && !Same(settings!.StorageRoot, old, comparison))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(target))!);
            Directory.Move(old, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // With nothing saved, the first run would make an empty default and hide the history; naming the old folder keeps it in use.
            services.Log.Warning($"Startup check: the storage in {old} could not be moved to {target}, so it stays where it is: {e.Message}", e);
            if (unset)
            {
                await StorageRootSetting.CommitAsync(services, old, null).ConfigureAwait(false);
            }

            return;
        }

        services.Log.Information($"Startup check: moved the storage from {old} to {target}.");
        if (unset)
        {
            return;
        }

        try
        {
            await StorageRootSetting.CommitAsync(services, target, null).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SavepointException)
        {
            // The settings still name the old folder, so the folder goes back rather than leave them pointing at nothing.
            services.Log.Warning($"Startup check: the settings could not be changed to {target}; moving the storage back to {old}.", e);
            Directory.Move(target, old);
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
