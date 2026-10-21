namespace Savepoint.Core.Errors;

/// <summary>
/// A new game points at folders that already hold history from a removed game, which can be reconnected
/// (<c>error.gameService.reconnectable</c>). The Api reports this case as <c>AddGameOutcome.HistoryFound</c>, a question for the
/// user, rather than as an error. Args: <c>existingGameId</c>, <c>roles</c>, <c>branchNames</c>.
/// </summary>
public sealed class ReconnectableGameException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="existingGameId">The id of the removed game that left the history.</param>
    /// <param name="roles">The folder roles that hold history, as role names.</param>
    /// <param name="branchNames">The branch names of the profiles that can be restored.</param>
    public ReconnectableGameException(
        string existingGameId,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> branchNames)
        : base(
            "error.gameService.reconnectable",
            $"Game '{existingGameId}' already has data at these paths and can be reconnected.",
            new Dictionary<string, object?>
            {
                ["existingGameId"] = existingGameId,
                ["roles"] = roles,
                ["branchNames"] = branchNames,
            })
    {
    }
}
