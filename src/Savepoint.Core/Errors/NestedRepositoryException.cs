namespace Savepoint.Core.Errors;

/// <summary>
/// A folder inside the game holds its own version-control data (a hidden <c>.git</c> folder or file), which a capture
/// cannot store inside another history. Nothing was recorded.
/// </summary>
public sealed class NestedRepositoryException : SavepointException
{
    /// <summary>Builds the error for the folders found.</summary>
    /// <param name="path">The first hidden <c>.git</c> entry found, absolute.</param>
    /// <param name="count">How many such entries the game folder holds.</param>
    public NestedRepositoryException(string path, int count)
        : base(
            "error.capture.nestedRepository",
            $"'{path}' is a repository inside the game folder ({count} found); a capture cannot store it.",
            new Dictionary<string, object?> { ["path"] = path, ["count"] = count })
    {
    }
}
