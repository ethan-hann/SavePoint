namespace Savepoint.Core.Diagnostics;

// The backend's view of the log it was given. The Api promises that no exception crosses its boundary, and a log call sits in
// the catch blocks that keep that promise, so whatever the real log throws stops here.
internal sealed class GuardedDiagnosticLog : IDiagnosticLog
{
    private readonly IDiagnosticLog _inner;

    public GuardedDiagnosticLog(IDiagnosticLog inner)
    {
        _inner = inner;
    }

    public string? CurrentFile
    {
        get
        {
            try
            {
                return _inner.CurrentFile;
            }
            catch (Exception)
            {
                // A log that cannot say which file it has open is treated as having none.
                return null;
            }
        }
    }

    public void Information(string message, Exception? exception = null) => Write(() => _inner.Information(message, exception));

    public void Warning(string message, Exception? exception = null) => Write(() => _inner.Warning(message, exception));

    public void Error(string message, Exception? exception = null) => Write(() => _inner.Error(message, exception));

    private static void Write(Action write)
    {
        try
        {
            write();
        }
        catch (Exception)
        {
            // A log that cannot write has nowhere to report that; the work it was describing goes on.
        }
    }
}
