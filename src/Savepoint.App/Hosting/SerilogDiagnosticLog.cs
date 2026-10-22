using System.Text;
using Savepoint.Core.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.File;

namespace Savepoint.App.Hosting;

/// <summary>
/// The app's <see cref="IDiagnosticLog"/>: it writes <c>savepoint-&lt;date&gt;.log</c> files into a folder with Serilog (D34). A file
/// rolls over each day and when it reaches 10 MB (the next one is <c>savepoint-&lt;date&gt;_001.log</c>), and the newest 14 are kept.
/// </summary>
/// <remarks>
/// Nothing is buffered: each line is handed to the operating system before the call returns, so an error written just before a crash
/// is in the file. The logger belongs to this object and is never the static <c>Log</c>. Messages are written as they are, never read
/// as templates, so a path with braces in it is safe. A failure to write is swallowed; the app never stops for its log.
/// </remarks>
public sealed class SerilogDiagnosticLog : IDiagnosticLog, IDisposable
{
    private const string _outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Text:l}{NewLine}{Exception}";
    private const long _fileSizeLimit = 10L * 1024 * 1024;
    private const int _filesKept = 14;

    private readonly Logger _logger;
    private readonly OpenFileTracker _tracker;

    private SerilogDiagnosticLog(string folder, Logger logger, OpenFileTracker tracker)
    {
        Folder = folder;
        _logger = logger;
        _tracker = tracker;
    }

    /// <summary>The folder the files are written to.</summary>
    public string Folder { get; }

    /// <inheritdoc />
    /// <remarks>Null until the first line is written, because the file is opened then.</remarks>
    public string? CurrentFile => _tracker.Current;

    /// <summary>Opens a log in <paramref name="folder"/>.</summary>
    /// <param name="folder">Where the files go; made when missing.</param>
    /// <returns>The log, or null when the folder cannot be made or written, so a log that cannot start never stops the app.</returns>
    public static SerilogDiagnosticLog? TryCreate(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var tracker = new OpenFileTracker();
            var logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(folder, "savepoint-.log"),
                    outputTemplate: _outputTemplate,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: _fileSizeLimit,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: _filesKept,
                    encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    hooks: tracker)
                .CreateLogger();
            return new SerilogDiagnosticLog(folder, logger, tracker);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Information(string message, Exception? exception = null) => Write(LogEventLevel.Information, message, exception);

    /// <inheritdoc />
    public void Warning(string message, Exception? exception = null) => Write(LogEventLevel.Warning, message, exception);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null) => Write(LogEventLevel.Error, message, exception);

    /// <summary>Flushes and closes the file.</summary>
    public void Dispose() => _logger.Dispose();

    // The message is a value of the template, not the template: braces in a path must not be read as holes.
    private void Write(LogEventLevel level, string message, Exception? exception) => _logger.Write(level, exception, "{Text:l}", message);

    // Serilog tells a hook where each file was opened, which is the only way to learn the name a rolled file was given.
    private sealed class OpenFileTracker : FileLifecycleHooks
    {
        private volatile string? _current;

        public string? Current => _current;

        public override Stream OnFileOpened(string path, Stream underlyingStream, Encoding encoding)
        {
            _current = path;
            return underlyingStream;
        }
    }
}
