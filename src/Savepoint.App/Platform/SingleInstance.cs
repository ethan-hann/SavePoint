using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Savepoint.Core.Platform;

namespace Savepoint.App.Platform;

/// <summary>
/// Lets one copy of the app run per user. The first copy holds a named mutex and listens on a named pipe; a second copy
/// finds the mutex taken, asks the first to bring its window forward through the pipe, and exits. Only the mutex decides who runs
/// (or, on a machine that will not create the mutex, an exclusive lock on a file in the user's private folder): two copies working
/// on the same repositories is worse than a copy that cannot be brought forward. So <see cref="TryAcquire"/> is true for the holder
/// alone, a copy that cannot be told to show itself (no pipe) still holds its place, and a second copy whose
/// <see cref="SignalFirstInstance"/> fails must still exit; it can show a brief "already running" notice first. A machine that
/// refuses both the mutex and the file leaves no way to tell, and the safe answer there is not to run.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>The id the app uses; it is part of the mutex and pipe names, so two apps with different ids do not meet.</summary>
    public const string DefaultAppId = "SavePoint";

    private const string _request = "activate";
    private const string _reply = "ok";

    private readonly object _gate = new();
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _readTimeout;
    private readonly Func<string, InstanceMutex> _acquireMutex;
    private InstanceMutex? _owner;
    private FileStream? _lockFile;
    private NamedPipeServerStream? _firstListener;
    private CancellationTokenSource? _stop;
    private Task? _server;
    private EventHandler? _activationRequested;
    private bool _activationPending;
    private bool _acquired;
    private bool _disposed;

    /// <summary>Uses <see cref="DefaultAppId"/> and the current user.</summary>
    public SingleInstance()
        : this(DefaultAppId)
    {
    }

    /// <param name="appId">Names the app; copies with the same id and user are the same app.</param>
    public SingleInstance(string appId)
        : this(appId, $"{Environment.UserDomainName}\\{Environment.UserName}", null, TimeSpan.FromSeconds(2))
    {
    }

    internal SingleInstance(string appId, string userKey, string? pipeDirectory, TimeSpan connectTimeout, Func<string, InstanceMutex>? acquireMutex = null)
    {
        _connectTimeout = connectTimeout;
        // The first copy waits for a request at least as long as a second copy waits to connect.
        _readTimeout = connectTimeout > TimeSpan.FromSeconds(2) ? connectTimeout : TimeSpan.FromSeconds(2);
        _acquireMutex = acquireMutex ?? InstanceMutex.Acquire;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(appId + "\0" + userKey)));
        MutexName = "Global\\sp-" + hash[..16];
        // Short on purpose: a Unix socket path holds about 100 bytes, and this lands inside a folder that may be long.
        var pipeName = "sp-" + hash[..12];
        var directory = pipeDirectory
            ?? DefaultPipeDirectory(PlatformFactory.CurrentOs, Environment.GetEnvironmentVariable, Directory.Exists, Path.GetTempPath())
            ?? Path.GetTempPath();
        PipeName = OperatingSystem.IsWindows() ? pipeName : Path.Combine(directory, pipeName);
        LockFilePath = Path.Combine(directory, pipeName + ".lock");
    }

    /// <summary>
    /// Raised when another copy asked this one to show itself. Raised on a thread-pool thread, so marshal to the UI thread
    /// before touching a window. A request that arrives while nobody listens is kept and delivered to the first listener.
    /// </summary>
    public event EventHandler? ActivationRequested
    {
        add
        {
            bool deliverPending;
            lock (_gate)
            {
                _activationRequested += value;
                deliverPending = _activationPending && value is not null;
                _activationPending = false;
            }

            if (deliverPending)
            {
                value!(this, EventArgs.Empty);
            }
        }

        remove
        {
            lock (_gate)
            {
                _activationRequested -= value;
            }
        }
    }

    internal string MutexName { get; }

    /// <summary>The file a copy locks when the machine will not create the mutex; in the same private folder as the socket.</summary>
    internal string LockFilePath { get; }

    /// <summary>The pipe name handed to .NET: a bare name on Windows, the absolute path of the socket elsewhere.</summary>
    internal string PipeName { get; }

    /// <summary>
    /// The folder the Unix socket goes in: the login's runtime folder when it has a usable one, which is private to the
    /// user and short, otherwise the temp folder. Null on Windows, where pipes are not files.
    /// </summary>
    internal static string? DefaultPipeDirectory(OsKind os, Func<string, string?> environment, Func<string, bool> directoryExists, string tempPath)
    {
        if (os == OsKind.Windows)
        {
            return null;
        }

        var runtime = os == OsKind.Linux ? environment("XDG_RUNTIME_DIR") : null;
        return !string.IsNullOrEmpty(runtime) && Path.IsPathRooted(runtime) && directoryExists(runtime) ? runtime : tempPath;
    }

    /// <summary>Takes the right to be the running copy.</summary>
    /// <returns>True only when this copy holds the mutex (or the lock file, where the machine refuses the mutex) and should start normally; false when another copy is running, or the machine allows neither.</returns>
    /// <exception cref="ObjectDisposedException">This object was disposed.</exception>
    public bool TryAcquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_acquired)
            {
                return true;
            }

            var owner = _acquireMutex(MutexName);
            switch (owner.Outcome)
            {
                case MutexOutcome.Taken:
                    owner.Dispose();
                    return false;

                // A mutex the machine would not create proves nothing about other copies; a lock on a file settles it the same way.
                case MutexOutcome.Unavailable:
                    owner.Dispose();
                    if (!TryLockFile())
                    {
                        return false;
                    }

                    break;

                default:
                    _owner = owner;
                    break;
            }

            _acquired = true;
            // Without a pipe the running copy cannot be told to show itself, but it still keeps every other copy out.
            StartServer();
            return true;
        }
    }

    /// <summary>Asks the running copy to show itself.</summary>
    /// <returns>True when the running copy acknowledged the request; false when nobody answered, so the caller may tell the user the app is already running before it exits.</returns>
    public bool SignalFirstInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            client.Connect((int)_connectTimeout.TotalMilliseconds);
            // A pipe held by something that never reads or answers would block a plain Write forever, so every step has a deadline.
            using var limit = new CancellationTokenSource(_connectTimeout);
            client.WriteAsync(Encoding.UTF8.GetBytes(_request + "\n"), limit.Token).AsTask().GetAwaiter().GetResult();
            var buffer = new byte[16];
            var read = client.ReadAsync(buffer, limit.Token).AsTask().GetAwaiter().GetResult();
            return Encoding.UTF8.GetString(buffer, 0, read).StartsWith(_reply, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            // Nobody answered: no first copy, or one that is hung or not ours.
            return false;
        }
    }

    /// <summary>Stops listening and gives up the right to be the running copy.</summary>
    public void Dispose()
    {
        CancellationTokenSource? stop;
        Task? server;
        InstanceMutex? owner;
        FileStream? lockFile;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            stop = _stop;
            server = _server;
            owner = _owner;
            lockFile = _lockFile;
        }

        stop?.Cancel();
        try
        {
            server?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The server loop ends by itself on any failure; there is nothing more to learn from it.
        }

        stop?.Dispose();
        owner?.Dispose();
        lockFile?.Dispose();
    }

    // The lock belongs to the open handle, so a copy that crashes loses it with the process and a file left behind blocks nobody.
    private bool TryLockFile()
    {
        try
        {
            _lockFile = new FileStream(LockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (Exception)
        {
            // Held by another copy, or the folder cannot be written: either way this copy must not run.
            return false;
        }
    }

    private NamedPipeServerStream CreateListener() =>
        new(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private void StartServer()
    {
        try
        {
            // Listening before TryAcquire returns means a second copy that sees the mutex taken can already connect.
            _firstListener = CreateListener();
        }
        catch (Exception)
        {
            // No pipe: nobody can be asked to show the window, which is only a convenience; the mutex still holds.
            return;
        }

        _stop = new CancellationTokenSource();
        var listener = _firstListener;
        var token = _stop.Token;
        _server = Task.Run(() => ServeAsync(listener, token), CancellationToken.None);
    }
    private async Task ServeAsync(NamedPipeServerStream listener, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await listener.WaitForConnectionAsync(stop).ConfigureAwait(false);
                var connected = listener;
                // The next client is served by a fresh listener, so a slow one never keeps the others waiting.
                listener = CreateListener();
                _ = HandleAsync(connected, stop);
            }
        }
        catch (Exception)
        {
            // Cancelled on dispose, or the pipe broke: either way the loop is over and the app keeps running.
        }
        finally
        {
            listener.Dispose();
        }
    }

    private async Task HandleAsync(NamedPipeServerStream connection, CancellationToken stop)
    {
        try
        {
            using (connection)
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(stop))
            {
                limit.CancelAfter(_readTimeout);
                var buffer = new byte[32];
                var length = 0;
                while (length < buffer.Length && !buffer.AsSpan(0, length).Contains((byte)'\n'))
                {
                    var read = await connection.ReadAsync(buffer.AsMemory(length), limit.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    length += read;
                }

                if (Encoding.UTF8.GetString(buffer, 0, length).Trim() != _request)
                {
                    return;
                }

                RaiseActivationRequested();
                await connection.WriteAsync(Encoding.UTF8.GetBytes(_reply + "\n"), limit.Token).ConfigureAwait(false);
                await connection.FlushAsync(limit.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // A client that vanished, stalled or sent nonsense costs nothing more than its own connection.
        }
    }

    private void RaiseActivationRequested()
    {
        EventHandler? handler;
        lock (_gate)
        {
            handler = _activationRequested;
            _activationPending = handler is null;
        }

        try
        {
            handler?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // A faulty handler must not stop later requests from being served.
        }
    }
}
