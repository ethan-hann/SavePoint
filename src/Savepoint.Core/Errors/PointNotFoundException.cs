namespace Savepoint.Core.Errors;

/// <summary>
/// A launch named a history point or save that the game does not hold, such as one that left its profile or belongs to another
/// game (<c>error.history.pointNotFound</c>). Args: <c>pointId</c>.
/// </summary>
public sealed class PointNotFoundException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="pointId">The point id that matched nothing in the game's history.</param>
    public PointNotFoundException(string pointId)
        : base(
            "error.history.pointNotFound",
            $"The game's history has no point '{pointId}'.",
            new Dictionary<string, object?> { ["pointId"] = pointId })
    {
    }
}
