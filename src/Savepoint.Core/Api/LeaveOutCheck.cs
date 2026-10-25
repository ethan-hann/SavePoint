namespace Savepoint.Core.Api;

/// <summary>What <see cref="IGamesApi.CheckLeaveOutAsync"/> found: which rules can be added without leaving out any of the game's saves.</summary>
/// <param name="Safe">The asked rules that match nothing in the saves folder, in the order asked.</param>
/// <param name="Blocked">The asked rules that match files in the saves folder, in the order asked.</param>
public sealed record LeaveOutCheck(IReadOnlyList<string> Safe, IReadOnlyList<RuleBlocked> Blocked);

/// <summary>A rule that would leave out files of the game's saves folder.</summary>
/// <param name="Rule">The rule.</param>
/// <param name="Files">Some of the files it would leave out, relative to the saves folder, at most <see cref="Sample"/> of them.</param>
public sealed record RuleBlocked(string Rule, IReadOnlyList<string> Files)
{
    /// <summary>The most file names a blocked rule carries, enough to show the player what it would have hidden.</summary>
    public const int Sample = 5;
}
