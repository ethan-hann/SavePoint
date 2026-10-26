namespace Savepoint.Core.Errors;

/// <summary>
/// The saved history's own bookkeeping for a game folder is damaged (<c>error.gitOperation.recordDamaged</c>), usually by a power
/// loss or a crash right after a snapshot or switch, and SavePoint could not rebuild it from the latest snapshot because that
/// snapshot cannot be read either. The game's files were not touched. No args.
/// </summary>
public sealed class RepositoryDamagedException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="devMessage">English text for the log: what was damaged and what git said.</param>
    /// <param name="inner">The git failure behind it, when there was one.</param>
    public RepositoryDamagedException(string devMessage, Exception? inner = null)
        : base("error.gitOperation.recordDamaged", devMessage, inner: inner)
    {
    }
}
