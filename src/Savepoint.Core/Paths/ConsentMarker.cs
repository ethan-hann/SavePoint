using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Paths;

/// <summary>What a consent marker file claims: the game and folder role the user confirmed, and when.</summary>
/// <param name="GameId">The game that owns the folder.</param>
/// <param name="Role">Which of the game's two folders this is.</param>
/// <param name="ConfirmedAt">When the user confirmed it.</param>
public sealed record ConsentMarkerData(string GameId, RepoRole Role, DateTimeOffset ConfirmedAt);

/// <summary>The result of reading a folder's consent marker.</summary>
public abstract record ConsentMarkerState
{
    private ConsentMarkerState()
    {
    }

    /// <summary>There is no marker file.</summary>
    public sealed record Missing : ConsentMarkerState;

    /// <summary>A marker SavePoint wrote.</summary>
    /// <param name="Marker">What the marker claims.</param>
    public sealed record Valid(ConsentMarkerData Marker) : ConsentMarkerState;

    /// <summary>The file exists but is not a marker SavePoint wrote: bad JSON, fields missing or invalid, or too large.</summary>
    public sealed record Corrupt : ConsentMarkerState;

    /// <summary>The file could not be read even after retrying (another program holds it, or the disk failed); its content is unknown.</summary>
    /// <param name="Error">What stopped the read.</param>
    public sealed record Unreadable(Exception Error) : ConsentMarkerState;
}

/// <summary>
/// The small file at a work tree's root recording which game and role the user confirmed for that folder. Callers keep
/// <see cref="ExcludePattern"/> out of git so a checkout can never rewrite or delete a marker. Every operation fails closed: a marker
/// that cannot be identified is never adopted, replaced (unless asked), or deleted.
/// </summary>
/// <remarks>
/// The operations are synchronous: each touches one file of about a hundred bytes, and a locked file is retried for about
/// a second and a half at most. Async callers run them on a background thread.
/// </remarks>
public static class ConsentMarker
{
    /// <summary>The marker's file name, at the root of the work tree.</summary>
    public const string FileName = ".savepoint-managed.json";

    /// <summary>The temp file a write goes through; it lives in the same folder so the final move is atomic.</summary>
    public const string TempFileName = ".savepoint-managed.json.tmp";

    /// <summary>The pattern to keep out of git, covering the marker and its temp file.</summary>
    public const string ExcludePattern = ".savepoint-managed.json*";

    // A marker SavePoint writes is about 100 bytes; anything this large is not one, and reading it whole could exhaust memory.
    private const long _maxBytes = 4096;

    // Doubling from 20 ms, so about 1.3 s in all before a locked file is given up on.
    private const int _ioRetries = 6;
    private const int _firstRetryDelayMs = 20;

    private const string _timeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Reads the marker. A folder that does not exist has no marker. A file another program holds briefly is retried; if it stays
    /// unreadable the state is <see cref="ConsentMarkerState.Unreadable"/>, which is not the same as corrupt.
    /// </summary>
    /// <param name="workTree">The work tree's root folder.</param>
    public static ConsentMarkerState Read(string workTree)
    {
        var file = Path.Combine(workTree, FileName);
        string text;
        try
        {
            if (!TryReadText(file, out text))
            {
                return new ConsentMarkerState.Missing();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ConsentMarkerState.Unreadable(ex);
        }

        if (text.Length > _maxBytes)
        {
            return new ConsentMarkerState.Corrupt();
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return TryParse(document.RootElement) is { } marker
                ? new ConsentMarkerState.Valid(marker)
                : new ConsentMarkerState.Corrupt();
        }
        catch (JsonException)
        {
            return new ConsentMarkerState.Corrupt();
        }
    }

    /// <summary>
    /// Writes the marker through a temp file in the same folder, so a crash never leaves a half-written marker and a failed write
    /// leaves any earlier marker as it was. Replaces a marker only when it already names this game and role.
    /// </summary>
    /// <param name="workTree">The work tree's root folder; created when missing.</param>
    /// <param name="gameId">The game that owns the folder.</param>
    /// <param name="role">Which of the game's folders this is.</param>
    /// <param name="confirmedAt">When the user confirmed it.</param>
    /// <param name="replaceCorrupt">
    /// True to replace a marker that is not a marker SavePoint wrote (the user chose to start over); it never overrides another
    /// game's valid marker.
    /// </param>
    /// <exception cref="ConsentMarkerException">Conflict: another game or role holds the folder, or the marker is corrupt and <paramref name="replaceCorrupt"/> is false.</exception>
    /// <exception cref="IOException">The existing marker could not be read, or the write failed.</exception>
    public static void Write(string workTree, string gameId, RepoRole role, DateTimeOffset confirmedAt, bool replaceCorrupt = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        switch (Read(workTree))
        {
            case ConsentMarkerState.Valid other when !Matches(other.Marker, gameId, role):
                throw Conflict(workTree, role, other.Marker.GameId, other.Marker.Role);
            case ConsentMarkerState.Corrupt when !replaceCorrupt:
                throw Conflict(workTree, role, null, null);
            case ConsentMarkerState.Unreadable unreadable:
                throw Unreadable(workTree, unreadable);
        }

        Directory.CreateDirectory(workTree);
        var target = Path.Combine(workTree, FileName);
        var temp = Path.Combine(workTree, TempFileName);
        if (Directory.Exists(target))
        {
            throw new IOException($"A folder named '{FileName}' is in the way of the consent marker in '{workTree}'.");
        }

        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(_utf8NoBom.GetBytes(Serialize(gameId, role, confirmedAt)));
                stream.Flush(flushToDisk: true);
            }

            RetryTransient(() => File.Move(temp, target, overwrite: true));
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// Returns when the folder's marker names this game and role. Throws <see cref="ConsentMarkerException"/>:
    /// Missing when there is no marker, Conflict when another game or role holds it or it is corrupt.
    /// </summary>
    /// <param name="workTree">The work tree's root folder.</param>
    /// <param name="gameId">The game that must own the folder.</param>
    /// <param name="role">Which of the game's folders it must be.</param>
    /// <exception cref="IOException">The marker could not be read; nothing is known about it.</exception>
    public static void Verify(string workTree, string gameId, RepoRole role)
    {
        switch (Read(workTree))
        {
            case ConsentMarkerState.Valid valid when Matches(valid.Marker, gameId, role):
                return;
            case ConsentMarkerState.Valid other:
                throw Conflict(workTree, role, other.Marker.GameId, other.Marker.Role);
            case ConsentMarkerState.Corrupt:
                throw Conflict(workTree, role, null, null);
            case ConsentMarkerState.Unreadable unreadable:
                throw Unreadable(workTree, unreadable);
            default:
                throw new ConsentMarkerException(ConsentFailure.Missing, workTree, role, null, null);
        }
    }

    /// <summary>Deletes only a matching marker: a missing one is a no-op, and a mismatched, corrupt, or unreadable one throws and stays.</summary>
    /// <param name="workTree">The work tree's root folder.</param>
    /// <param name="gameId">The game that must own the marker.</param>
    /// <param name="role">The folder role the marker must name.</param>
    /// <exception cref="ConsentMarkerException">Conflict: another game or role holds the marker, or it is corrupt.</exception>
    /// <exception cref="IOException">The marker could not be read, or could not be deleted.</exception>
    public static void Remove(string workTree, string gameId, RepoRole role)
    {
        switch (Read(workTree))
        {
            case ConsentMarkerState.Missing:
                return;
            case ConsentMarkerState.Valid valid when Matches(valid.Marker, gameId, role):
                RetryTransient(() => File.Delete(Path.Combine(workTree, FileName)));
                return;
            case ConsentMarkerState.Valid other:
                throw Conflict(workTree, role, other.Marker.GameId, other.Marker.Role);
            case ConsentMarkerState.Unreadable unreadable:
                throw Unreadable(workTree, unreadable);
            default:
                throw Conflict(workTree, role, null, null);
        }
    }

    private static bool Matches(ConsentMarkerData marker, string gameId, RepoRole role) =>
        string.Equals(marker.GameId, gameId, StringComparison.Ordinal) && marker.Role == role;

    private static ConsentMarkerException Conflict(string workTree, RepoRole role, string? existingGameId, RepoRole? existingRole) =>
        new(ConsentFailure.Conflict, workTree, role, existingGameId, existingRole);

    private static IOException Unreadable(string workTree, ConsentMarkerState.Unreadable state) =>
        new($"The consent marker in '{workTree}' could not be read.", state.Error);

    private static string RoleText(RepoRole role) => role == RepoRole.Install ? "install" : "external";

    // Absent when the file or its folder is missing; a locked file is retried before the error is passed on.
    private static bool TryReadText(string file, out string text)
    {
        text = string.Empty;
        string? read = null;
        try
        {
            RetryTransient(() =>
            {
                var info = new FileInfo(file);
                if (!info.Exists)
                {
                    return;
                }

                // Too large to be a marker: do not load it.
                read = info.Length > _maxBytes ? new string(' ', (int)_maxBytes + 1) : File.ReadAllText(file, _utf8NoBom);
            });
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }

        text = read ?? string.Empty;
        return read is not null;
    }

    // A virus scanner or backup tool can hold a file for a moment; that must not look like a conflict or fail a write.
    private static void RetryTransient(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < _ioRetries && IsTransient(ex))
            {
                Thread.Sleep(_firstRetryDelayMs << attempt);
            }
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is UnauthorizedAccessException || (ex is IOException and not FileNotFoundException and not DirectoryNotFoundException);

    private static string Serialize(string gameId, RepoRole role, DateTimeOffset confirmedAt)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("gameId", gameId);
            writer.WriteString("role", RoleText(role));
            writer.WriteString("confirmedAt", confirmedAt.UtcDateTime.ToString(_timeFormat, CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        return _utf8NoBom.GetString(buffer.ToArray());
    }

    private static ConsentMarkerData? TryParse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !TryGetString(root, "gameId", out var gameId) || string.IsNullOrWhiteSpace(gameId)
            || !TryGetString(root, "role", out var roleText)
            || !TryGetString(root, "confirmedAt", out var time)
            || !DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var confirmedAt))
        {
            return null;
        }

        return roleText switch
        {
            "install" => new ConsentMarkerData(gameId, RepoRole.Install, confirmedAt),
            "external" => new ConsentMarkerData(gameId, RepoRole.External, confirmedAt),
            _ => null,
        };
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        if (root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The original failure is the one worth reporting.
        }
    }
}
