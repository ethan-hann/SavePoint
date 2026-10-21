namespace Savepoint.App.Platform.Motion;

/// <summary>Reads the operating system's "reduce motion" preference, behind a seam so tests never ask a real one.</summary>
internal interface IReducedMotionReader
{
    /// <summary>Reads the preference now.</summary>
    /// <param name="cancellationToken">Gives up on a slow read.</param>
    /// <returns>True when the user asks for less motion, false when animations are welcome, null when the system does not say.</returns>
    ValueTask<bool?> ReadAsync(CancellationToken cancellationToken);
}
