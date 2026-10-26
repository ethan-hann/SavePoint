namespace Savepoint.Core.Git;

/// <summary>
/// A checkout that started and has not been finished: the process was stopped part way, or a file stayed in use. The
/// folder holds a mixture of the two points until the checkout is repeated and completes, or the changes are discarded.
/// </summary>
/// <param name="FromCommit">The point the folder was on when the switch began; null when the repository had no point.</param>
/// <param name="ToCommit">
/// The point being switched to; null when the note that says a switch started cannot be read or understood (a damaged file, or one
/// another program holds open), so which switch it was is not known. Nothing can finish such a switch; discarding the changes clears it.
/// </param>
/// <param name="ToBranch">
/// The branch being switched to; null when the switch was to a point and not to a branch. Profiles can share a point (a duplicate
/// starts at its source's), so the point alone cannot always say which profile a switch was for.
/// </param>
public sealed record InterruptedSwitch(string? FromCommit, string? ToCommit, string? ToBranch = null);
