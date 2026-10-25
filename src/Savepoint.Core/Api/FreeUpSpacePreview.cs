using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>What "Free up space" would delete for good that no profile holds any more: the recently removed snapshots.</summary>
/// <param name="RecentlyRemovedSnapshots">
/// How many recently removed snapshots it would delete, counted as <see cref="IHistoryApi.GetRecentlyRemovedAsync"/> lists them: for one
/// game, or across every game. Zero means nothing the player could still bring back is lost.
/// </param>
/// <param name="Token">
/// The exact records behind that count. Pass it back to the call that frees up space, which deletes those records and no others: a
/// snapshot removed after this preview was read is not in it, so it survives the run the player agreed to.
/// </param>
public sealed record FreeUpSpacePreview(int RecentlyRemovedSnapshots, FreeUpSpaceToken Token);

/// <summary>
/// The records a Free up space run may delete, as a preview found them. Callers keep it as it is and hand it back; they do not look inside.
/// Only a preview can make one: it carries a seal over its records that is keyed per process, so a token built or edited by hand, or
/// issued by another process, is refused by the run.
/// </summary>
public sealed class FreeUpSpaceToken
{
    private static readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    private readonly byte[] _seal;

    /// <summary>Issues a token over the records, sealed for this process.</summary>
    internal FreeUpSpaceToken(IReadOnlyList<FreeUpSpaceRecord> records)
        : this(records, null)
    {
    }

    /// <summary>Makes a token with the given seal, or a valid one when the seal is null; a wrong seal is how tests forge a token.</summary>
    internal FreeUpSpaceToken(IReadOnlyList<FreeUpSpaceRecord> records, byte[]? seal)
    {
        Records = Array.AsReadOnly([.. records]);
        _seal = seal ?? SealOf(Records);
    }

    /// <summary>A token that allows no record to be deleted, which is what a run without a preview gets.</summary>
    public static FreeUpSpaceToken Nothing { get; } = new([]);

    /// <summary>One entry per record of every snapshot the preview counted, in both folders of its game.</summary>
    public IReadOnlyList<FreeUpSpaceRecord> Records { get; }

    /// <summary>True when a preview in this process issued the token and its records are still the ones it sealed.</summary>
    internal bool IsIssued => CryptographicOperations.FixedTimeEquals(_seal, SealOf(Records));

    private static byte[] SealOf(IReadOnlyList<FreeUpSpaceRecord> records)
    {
        using var hmac = new HMACSHA256(_key);
        var writer = new StringBuilder();
        foreach (var record in records)
        {
            // Length prefixes keep two different record lists from reading as the same text.
            foreach (var field in new[] { record.GameId, ((int)record.Folder).ToString(CultureInfo.InvariantCulture), record.RefName, record.Value })
            {
                writer.Append(field.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(field).Append('\n');
            }
        }

        return hmac.ComputeHash(Encoding.UTF8.GetBytes(writer.ToString()));
    }
}

/// <summary>One record that keeps a removed snapshot's commit in a folder's repository.</summary>
/// <param name="GameId">The game the record belongs to.</param>
/// <param name="Folder">The folder whose repository holds it.</param>
/// <param name="RefName">The record's own name in that repository, which also says which commit it keeps; or a branch no profile names.</param>
/// <param name="Value">The commit the ref pointed at when the preview read it. The run deletes the ref only while it still points there.</param>
public sealed record FreeUpSpaceRecord(string GameId, RepoRole Folder, string RefName, string Value);
