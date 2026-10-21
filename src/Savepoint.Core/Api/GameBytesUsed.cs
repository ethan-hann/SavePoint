namespace Savepoint.Core.Api;

/// <summary>One game's share of SavePoint's storage.</summary>
/// <param name="GameId">The game.</param>
/// <param name="Bytes">Bytes SavePoint stores for this game across its folders and profiles.</param>
public sealed record GameBytesUsed(string GameId, long Bytes);
