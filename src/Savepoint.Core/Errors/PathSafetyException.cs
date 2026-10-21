namespace Savepoint.Core.Errors;

/// <summary>
/// A folder SavePoint refuses to track because it is a protected system or user location. Tier 1 blocks the folder, anything
/// inside it, and any folder that would contain one; tier 2 blocks only that exact folder, a whole mounted drive, or a Steam
/// library folder. Ids: <c>error.pathSafety.tier1Blocked</c> and <c>error.pathSafety.tier2Blocked</c>. Args: <c>path</c>, <c>tier</c>.
/// </summary>
public sealed class PathSafetyException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="path">The folder as the caller gave it, not the resolved path.</param>
    /// <param name="tier">1 for a tier-1 block; any other value is reported as tier 2.</param>
    public PathSafetyException(string path, int tier)
        : base(
            tier == 1 ? "error.pathSafety.tier1Blocked" : "error.pathSafety.tier2Blocked",
            $"Path '{path}' is blocked by a tier {tier} safety rule.",
            new Dictionary<string, object?> { ["path"] = path, ["tier"] = tier })
    {
    }
}
