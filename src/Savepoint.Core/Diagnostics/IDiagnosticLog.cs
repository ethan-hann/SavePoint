namespace Savepoint.Core.Diagnostics;

/// <summary>
/// Where the backend writes what happened, so a failure a user reports leaves a trace on their machine. Core knows only this
/// interface: the app supplies one that writes files, and the default (<see cref="DiagnosticLog.None"/>) writes nothing.
/// </summary>
/// <remarks>
/// Messages are for developers and are in English. Paths appear as they are, and the log never leaves the machine (D34).
/// Implementations must be safe to call from any thread and must not throw; the backend still shields itself from one that does.
/// </remarks>
public interface IDiagnosticLog
{
    /// <summary>
    /// The file the log is writing to right now, or null when it writes no file. "Clear all data" empties the log folder but leaves
    /// this one, which is open and still being written.
    /// </summary>
    string? CurrentFile { get; }

    /// <summary>Records something that happened and needs no attention.</summary>
    /// <param name="message">What happened.</param>
    /// <param name="exception">The exception behind it, if any; its type and stack are kept.</param>
    void Information(string message, Exception? exception = null);

    /// <summary>Records something that went wrong and was handled or expected, or that the user may have to deal with.</summary>
    /// <param name="message">What happened.</param>
    /// <param name="exception">The exception behind it, if any; its type and stack are kept.</param>
    void Warning(string message, Exception? exception = null);

    /// <summary>Records a failure nothing anticipated. An implementation should have it on disk before this returns.</summary>
    /// <param name="message">What happened.</param>
    /// <param name="exception">The exception behind it, if any; its type and stack are kept.</param>
    void Error(string message, Exception? exception = null);
}
