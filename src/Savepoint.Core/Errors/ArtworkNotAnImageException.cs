namespace Savepoint.Core.Errors;

/// <summary>
/// The file the user chose for a game's artwork is missing, unreadable, or not a whole PNG, JPEG, or WebP image
/// (<c>error.artwork.notAnImage</c>). Args: <c>path</c>.
/// </summary>
public sealed class ArtworkNotAnImageException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The file that was refused.</param>
    /// <param name="inner">What went wrong reading it, when reading failed.</param>
    public ArtworkNotAnImageException(string path, Exception? inner = null)
        : base(
            "error.artwork.notAnImage",
            $"'{path}' is not a PNG, JPEG, or WebP image.",
            new Dictionary<string, object?> { ["path"] = path },
            inner)
    {
    }
}
