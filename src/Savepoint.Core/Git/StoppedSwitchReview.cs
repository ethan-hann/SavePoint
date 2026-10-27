namespace Savepoint.Core.Git;

/// <summary>
/// What finishing a stopped switch would run into, read without writing anything.
/// </summary>
/// <param name="BlockingPaths">
/// Files with changes that are not saved and that the finish would run over: files outside the ones the stopped switch was changing, and
/// files the switch removes that were edited after it stopped. While there are any, the finish is refused and nothing is written.
/// </param>
/// <param name="ReplacedPaths">
/// Files the stopped switch was changing that hold content found in neither snapshot, so finishing replaces them with the target's copy.
/// A file the stopped switch left half written looks the same as one the player edited since, so both are listed.
/// </param>
public sealed record StoppedSwitchReview(IReadOnlyList<string> BlockingPaths, IReadOnlyList<string> ReplacedPaths)
{
    /// <summary>The review of a folder with nothing to finish.</summary>
    public static StoppedSwitchReview None { get; } = new([], []);
}
