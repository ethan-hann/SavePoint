using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Savepoint.Core.Api;
using Savepoint.Core.Diagnostics;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Services.Jobs;

// The finished jobs Activity lists, kept in a small file beside the settings so they outlive a run. Only what the list shows is written:
// what the job was, which game and profile, how it ended with the first reason it did not, and when. Its lines and result are not kept,
// and a job that is still running is never written, so nothing read back can look as if it were still running.
internal static class JobHistoryFile
{
    public const string FileName = "activity-history.json";

    // The newest hundred, and none that finished more than a month ago: the list is a reminder of recent work, not a log (the log is the log).
    public const int MaxJobs = 100;
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private const int _version = 1;
    private const int _detailKept = 4000;

    // The jobs to keep, in finish order: dropped when too old, then only the newest few of each game, then only the newest overall.
    public static List<JobSnapshot> Retain(IEnumerable<JobSnapshot> finished, DateTimeOffset now, int perGame)
    {
        var recent = finished.Where(job => job.FinishedAt is { } at && now - at <= MaxAge).ToList();
        var dropped = new HashSet<Guid>();
        foreach (var game in recent.GroupBy(job => job.GameId))
        {
            foreach (var old in game.SkipLast(perGame))
            {
                dropped.Add(old.JobId);
            }
        }

        return [.. recent.Where(job => !dropped.Contains(job.JobId)).TakeLast(MaxJobs)];
    }

    // Empty when there is no file. A file that cannot be understood is mentioned in the log and also reads as empty; a job in it that cannot be
    // understood is left out and the rest stay.
    public static List<JobSnapshot> Read(string path, IDiagnosticLog log)
    {
        if (AtomicText.ReadOrNull(path) is not { } text)
        {
            return [];
        }

        var jobs = new List<JobSnapshot>();
        try
        {
            using var document = JsonDocument.Parse(text);
            foreach (var entry in document.RootElement.GetProperty("jobs").EnumerateArray())
            {
                if (ReadJob(entry) is { } job && jobs.All(known => known.JobId != job.JobId))
                {
                    jobs.Add(job);
                }
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            log.Warning($"The Activity history at {path} could not be read, so Activity starts empty.", e);
            return [];
        }

        return [.. jobs.OrderBy(job => job.FinishedAt)];
    }

    public static void Write(string path, IReadOnlyList<JobSnapshot> jobs)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", _version);
            writer.WriteStartArray("jobs");
            foreach (var job in jobs)
            {
                WriteJob(writer, job);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        AtomicText.Write(path, Encoding.UTF8.GetString(buffer.ToArray()));
    }

    // A file that is already gone is the goal; one that cannot be removed throws, for the caller to say.
    public static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    // The first thing that went wrong: the job's own error, or for a job that finished only in part, the first folder or file that failed.
    private static ErrorInfo? Reason(JobSnapshot job) => job.Error ?? job.Result switch
    {
        CaptureResult capture => capture.Folders.FirstOrDefault(folder => !folder.Ok)?.Error,
        OptimizeResult optimize => optimize.Games.SelectMany(game => game.Folders).FirstOrDefault(folder => !folder.Ok)?.Error,
        ClearAllDataResult clear => clear.NotRemoved.FirstOrDefault()?.Error,
        StorageRelocationResult moved => moved.NotRemoved.FirstOrDefault()?.Error,
        _ => null,
    };

    private static void WriteJob(Utf8JsonWriter writer, JobSnapshot job)
    {
        writer.WriteStartObject();
        writer.WriteString("id", job.JobId);
        writer.WriteString("kind", job.Kind.ToString());
        writer.WriteString("gameId", job.GameId);
        if (job.Profile is { } profile)
        {
            writer.WriteStartObject("profile");
            writer.WriteString("id", profile.Id);
            writer.WriteString("name", profile.Name);
            writer.WriteEndObject();
        }

        writer.WriteString("status", job.Status.ToString());
        writer.WriteString("startedAt", job.StartedAt);
        if (job.FinishedAt is { } finishedAt)
        {
            writer.WriteString("finishedAt", finishedAt);
        }
        else
        {
            writer.WriteNull("finishedAt");
        }

        if (Reason(job) is { } reason)
        {
            writer.WriteStartObject("error");
            writer.WriteString("messageId", reason.MessageId);
            writer.WriteString("detail", reason.Detail.Length > _detailKept ? reason.Detail[.._detailKept] : reason.Detail);
            writer.WriteStartObject("args");
            foreach (var (name, value) in reason.Args)
            {
                writer.WritePropertyName(name);
                WriteValue(writer, value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case sbyte or byte or short or ushort or int or uint or long:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case float or double or decimal or ulong:
                writer.WriteNumberValue(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                break;
            case IEnumerable items:
                writer.WriteStartArray();
                foreach (var item in items)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    // Null for an entry that cannot be understood, or that names a job as still running.
    private static JobSnapshot? ReadJob(JsonElement entry)
    {
        try
        {
            if (!Enum.TryParse<JobKind>(entry.GetProperty("kind").GetString(), out var kind) || !Enum.IsDefined(kind)
                || !Enum.TryParse<JobStatus>(entry.GetProperty("status").GetString(), out var status) || !Enum.IsDefined(status)
                || status == JobStatus.Running
                || entry.GetProperty("finishedAt").ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var gameId = entry.TryGetProperty("gameId", out var game) && game.ValueKind == JsonValueKind.String ? game.GetString() : null;
            JobProfile? profile = entry.TryGetProperty("profile", out var named) && named.ValueKind == JsonValueKind.Object
                ? new JobProfile(named.GetProperty("id").GetString()!, named.GetProperty("name").GetString()!)
                : null;
            ErrorInfo? error = entry.TryGetProperty("error", out var failed) && failed.ValueKind == JsonValueKind.Object ? ReadError(failed) : null;
            return new JobSnapshot(
                entry.GetProperty("id").GetGuid(),
                kind,
                gameId,
                profile,
                status,
                Cancellable: false,
                CancelRequested: false,
                JobProgress.None,
                [],
                LinesDropped: 0,
                entry.GetProperty("startedAt").GetDateTimeOffset(),
                entry.GetProperty("finishedAt").GetDateTimeOffset(),
                Result: null,
                error,
                Revision: 1);
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static ErrorInfo ReadError(JsonElement error)
    {
        var args = new Dictionary<string, object?>();
        if (error.TryGetProperty("args", out var given) && given.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in given.EnumerateObject())
            {
                args[property.Name] = ReadValue(property.Value);
            }
        }

        var detail = error.TryGetProperty("detail", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : string.Empty;
        return new ErrorInfo(error.GetProperty("messageId").GetString()!, args, detail);
    }

    // The plain values a message formats: text, whole and fractional numbers, flags and lists of them; the catalog's plural and select rules do not take a JSON element.
    private static object? ReadValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var whole) ? (object)whole : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => value.EnumerateArray().Select(ReadValue).ToArray(),
        JsonValueKind.Null => null,
        _ => value.GetRawText(),
    };
}
