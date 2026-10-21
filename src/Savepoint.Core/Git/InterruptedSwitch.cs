namespace Savepoint.Core.Git;

/// <summary>
/// A checkout that started and has not been finished: the process was stopped part way, or a file stayed in use. The
/// folder holds a mixture of the two points until the checkout is repeated and completes, or the changes are discarded.
/// </summary>
/// <param name="FromCommit">The point the folder was on when the switch began; null when the repository had no point.</param>
/// <param name="ToCommit">The point being switched to.</param>
public sealed record InterruptedSwitch(string? FromCommit, string ToCommit);
