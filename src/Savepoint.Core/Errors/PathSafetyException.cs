namespace Savepoint.Core.Errors;

public sealed class PathSafetyException : SavepointException
{
    public PathSafetyException(string path, int tier)
        : base(
            tier == 1 ? "error.pathSafety.tier1Blocked" : "error.pathSafety.tier2Blocked",
            $"Path '{path}' is blocked by a tier {tier} safety rule.",
            new Dictionary<string, object?> { ["path"] = path, ["tier"] = tier })
    {
    }
}
