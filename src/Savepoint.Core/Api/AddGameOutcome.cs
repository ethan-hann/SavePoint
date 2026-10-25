using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>What adding a game came to. Finding old history is a question for the user, not an error, so it is an outcome of its own.</summary>
public abstract record AddGameOutcome
{
    private AddGameOutcome()
    {
    }

    /// <summary>The game was added.</summary>
    /// <param name="Game">The game as stored, after normalization.</param>
    /// <param name="LeftOff">Leave-out rules the caller asked for that were not saved because they could hurt the saves folder; null when every rule was kept.</param>
    public sealed record Added(GameDefinition Game, RulesLeftOff? LeftOff = null) : AddGameOutcome;

    /// <summary>The folders hold history from a removed game. Nothing was saved; answer with <see cref="IGamesApi.ResolveOrphanAsync"/>.</summary>
    /// <param name="History">What was found.</param>
    public sealed record HistoryFound(OrphanedHistory History) : AddGameOutcome;
}

/// <summary>Leave-out rules a new game was saved without, so the saves folder keeps everything in it.</summary>
/// <param name="Rules">The rules that were left off.</param>
/// <param name="SavesHoldFiles">
/// True when the saves folder was checked and holds files the rules would leave out. False when the check could not run, so the rules
/// were left off to be safe and nothing is known about the folder.
/// </param>
public sealed record RulesLeftOff(IReadOnlyList<string> Rules, bool SavesHoldFiles)
{
    /// <summary>Compares the rules by content, so two answers that name the same rules are equal.</summary>
    public bool Equals(RulesLeftOff? other) => other is not null && SavesHoldFiles == other.SavesHoldFiles && Rules.SequenceEqual(other.Rules);

    /// <summary>A hash that agrees with <see cref="Equals(RulesLeftOff)"/>.</summary>
    public override int GetHashCode() => Rules.Aggregate(SavesHoldFiles.GetHashCode(), (hash, rule) => HashCode.Combine(hash, rule));
}
