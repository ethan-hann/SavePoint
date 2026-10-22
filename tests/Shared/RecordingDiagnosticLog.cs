using Savepoint.Core.Diagnostics;

namespace Savepoint.Testing;

// A log that keeps what it was told, so a test can assert on the lines without a file on disk. Linked into both test assemblies.
internal sealed class RecordingDiagnosticLog : IDiagnosticLog
{
    private readonly Lock _gate = new();
    private readonly List<LogEntry> _entries = [];

    public string? CurrentFile { get; set; }

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public void Information(string message, Exception? exception = null) => Add(LogLevel.Information, message, exception);

    public void Warning(string message, Exception? exception = null) => Add(LogLevel.Warning, message, exception);

    public void Error(string message, Exception? exception = null) => Add(LogLevel.Error, message, exception);

    // Entries whose text contains every fragment, in the order they were written.
    public IReadOnlyList<LogEntry> Containing(params string[] fragments) =>
        [.. Entries.Where(entry => fragments.All(fragment => entry.Message.Contains(fragment, StringComparison.Ordinal)))];

    private void Add(LogLevel level, string message, Exception? exception)
    {
        lock (_gate)
        {
            _entries.Add(new LogEntry(level, message, exception));
        }
    }
}

internal enum LogLevel
{
    Information,
    Warning,
    Error,
}

internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
