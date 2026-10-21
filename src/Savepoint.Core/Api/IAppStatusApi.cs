namespace Savepoint.Core.Api;

/// <summary>The startup checks and the recovery of corrupt catalog files (D18).</summary>
/// <remarks>See <see cref="OperationResult{T}"/> for the rules every member follows.</remarks>
public interface IAppStatusApi
{
    /// <summary>
    /// Checks that the bundled git runs and that every catalog file can be read. A problem found is part of the answer, not a
    /// failure of the call.
    /// </summary>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<StartupStatus>> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fixes one corrupt catalog file by restoring its last good copy or starting it empty. Either way the corrupt file is moved
    /// aside, never deleted. Fails when the path is not one of the corrupt files, or when a backup is asked for and there is none.
    /// </summary>
    /// <param name="path">A path from <see cref="CatalogStatus.CorruptFiles"/>.</param>
    /// <param name="recovery">Which fix to apply.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    /// <returns>Where the corrupt file was moved, so the recovery screen can say so. Check again afterwards.</returns>
    Task<OperationResult<string>> RecoverCatalogFileAsync(string path, CatalogRecovery recovery, CancellationToken cancellationToken = default);
}
