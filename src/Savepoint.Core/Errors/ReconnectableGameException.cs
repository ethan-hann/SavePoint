namespace Savepoint.Core.Errors;

public sealed class ReconnectableGameException : SavepointException
{
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
