using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Paths;

public enum ConsentRole
{
    Install,
    External,
}

public sealed record ConsentMarkerData(string GameId, ConsentRole Role, DateTimeOffset ConfirmedAt);

public abstract record ConsentMarkerState
{
    private ConsentMarkerState()
    {
    }

    public sealed record Missing : ConsentMarkerState;

    public sealed record Valid(ConsentMarkerData Marker) : ConsentMarkerState;

    /// <summary>The file exists but is not a marker SavePoint wrote: bad JSON, or fields missing or invalid.</summary>
    public sealed record Corrupt : ConsentMarkerState;
}

/// <summary>Callers keep <see cref="ExcludePattern"/> out of git so a checkout can never rewrite or delete a marker.</summary>
public static class ConsentMarker
{
    public const string FileName = ".savepoint-managed.json";

    public const string TempFileName = ".savepoint-managed.json.tmp";

    /// <summary>The pattern to keep out of git, covering the marker and its temp file.</summary>
    public const string ExcludePattern = ".savepoint-managed.json*";

    private const string _timeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static ConsentMarkerState Read(string workTree)
    {
        var file = Path.Combine(workTree, FileName);
        if (!File.Exists(file))
        {
            return new ConsentMarkerState.Missing();
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file, _utf8NoBom));
            return TryParse(document.RootElement) is { } marker
                ? new ConsentMarkerState.Valid(marker)
                : new ConsentMarkerState.Corrupt();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A file we cannot read is as unusable as one we cannot parse: never adopt what we cannot identify.
            return new ConsentMarkerState.Corrupt();
        }
    }

    /// <summary>Writes through a temp file in the same folder so a crash never leaves a half-written marker.</summary>
    public static void Write(string workTree, string gameId, ConsentRole role, DateTimeOffset confirmedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);

        Directory.CreateDirectory(workTree);
        var target = Path.Combine(workTree, FileName);
        var temp = Path.Combine(workTree, TempFileName);

        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(_utf8NoBom.GetBytes(Serialize(gameId, role, confirmedAt)));
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <exception cref="ConsentMarkerException">Missing: no marker. Conflict: another game or role holds it, or it is unreadable.</exception>
    public static void Verify(string workTree, string gameId, ConsentRole role)
    {
        switch (Read(workTree))
        {
            case ConsentMarkerState.Valid valid when Matches(valid.Marker, gameId, role):
                return;
            case ConsentMarkerState.Valid other:
                throw Conflict(workTree, role, other.Marker.GameId, RoleText(other.Marker.Role));
            case ConsentMarkerState.Corrupt:
                throw Conflict(workTree, role, null, null);
            default:
                throw new ConsentMarkerException(ConsentFailure.Missing, workTree, RoleText(role), null, null);
        }
    }

    /// <summary>Deletes only a matching marker: a missing one is a no-op, and a mismatch or unreadable one throws and stays.</summary>
    public static void Remove(string workTree, string gameId, ConsentRole role)
    {
        switch (Read(workTree))
        {
            case ConsentMarkerState.Missing:
                return;
            case ConsentMarkerState.Valid valid when Matches(valid.Marker, gameId, role):
                File.Delete(Path.Combine(workTree, FileName));
                return;
            case ConsentMarkerState.Valid other:
                throw Conflict(workTree, role, other.Marker.GameId, RoleText(other.Marker.Role));
            default:
                throw Conflict(workTree, role, null, null);
        }
    }

    private static bool Matches(ConsentMarkerData marker, string gameId, ConsentRole role) =>
        string.Equals(marker.GameId, gameId, StringComparison.Ordinal) && marker.Role == role;

    private static ConsentMarkerException Conflict(string workTree, ConsentRole role, string? existingGameId, string? existingRole) =>
        new(ConsentFailure.Conflict, workTree, RoleText(role), existingGameId, existingRole);

    private static string RoleText(ConsentRole role) => role == ConsentRole.Install ? "install" : "external";

    private static string Serialize(string gameId, ConsentRole role, DateTimeOffset confirmedAt)
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
            "install" => new ConsentMarkerData(gameId, ConsentRole.Install, confirmedAt),
            "external" => new ConsentMarkerData(gameId, ConsentRole.External, confirmedAt),
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
