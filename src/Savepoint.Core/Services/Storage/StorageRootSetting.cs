using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Services.Storage;

/// <summary>Writes the storage root into the settings, which is the point at which a change of root is decided.</summary>
internal static class StorageRootSetting
{
    /// <summary>
    /// Saves <paramref name="newRoot"/> as the storage root. A write that threw may still have taken effect (a clean-up after the
    /// rename failed, or the rename applied and reported a transient error), and then the change has to carry on, not be undone:
    /// the settings are read back, and only a file that does not name the new root fails the call.
    /// </summary>
    /// <param name="services">The backend, for the catalog.</param>
    /// <param name="newRoot">The root to save.</param>
    /// <param name="afterWrite">Runs inside the guarded step once the write is done; a test uses it to fail right after the write.</param>
    public static async Task CommitAsync(BackendServices services, string newRoot, Action<string>? afterWrite)
    {
        try
        {
            await services.Catalog.UpdateSettingsAsync(
                current => (current ?? AppSettings.Defaults(newRoot)) with { StorageRoot = newRoot },
                CancellationToken.None).ConfigureAwait(false);
            afterWrite?.Invoke(newRoot);
        }
        catch (Exception)
        {
            // Stored already: the new folder is the root from here on.
            if (!await NamesAsync(services, newRoot).ConfigureAwait(false))
            {
                throw;
            }
        }
    }

    private static async Task<bool> NamesAsync(BackendServices services, string root)
    {
        try
        {
            var stored = await services.Catalog.GetSettingsAsync(CancellationToken.None).ConfigureAwait(false);
            return string.Equals(stored?.StorageRoot, root, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SavepointException)
        {
            return false;
        }
    }
}
