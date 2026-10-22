namespace Savepoint.Core.Diagnostics;

/// <summary>The log that writes nothing, which is what the backend uses unless it is given another.</summary>
public static class DiagnosticLog
{
    /// <summary>A log that discards everything. It holds no state, so one instance serves everyone.</summary>
    public static IDiagnosticLog None { get; } = new NullDiagnosticLog();

    private sealed class NullDiagnosticLog : IDiagnosticLog
    {
        public string? CurrentFile => null;

        public void Information(string message, Exception? exception = null)
        {
        }

        public void Warning(string message, Exception? exception = null)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
