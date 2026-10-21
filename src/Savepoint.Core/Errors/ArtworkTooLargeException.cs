namespace Savepoint.Core.Errors;

/// <summary>
/// The file the user chose for a game's artwork is bigger than the limit (<c>error.artwork.tooLarge</c>). Args: <c>path</c>,
/// <c>maxMegabytes</c>.
/// </summary>
public sealed class ArtworkTooLargeException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The file that was refused.</param>
    /// <param name="maxMegabytes">The most a file may hold, in megabytes.</param>
    public ArtworkTooLargeException(string path, int maxMegabytes)
        : base(
            "error.artwork.tooLarge",
            $"'{path}' is larger than {maxMegabytes} MB.",
            new Dictionary<string, object?> { ["path"] = path, ["maxMegabytes"] = maxMegabytes })
    {
    }
}
