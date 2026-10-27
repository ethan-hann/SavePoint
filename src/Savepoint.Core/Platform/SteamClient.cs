using Savepoint.Core.Launch;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Platform;

/// <summary>What Steam's registry key says about the running client on Windows: its process id and the account signed in.</summary>
/// <param name="Pid">The process id of the client (<c>ActiveProcess\pid</c>).</param>
/// <param name="User">The signed-in account id (<c>ActiveProcess\ActiveUser</c>); 0 while nobody is signed in.</param>
internal readonly record struct SteamSignIn(int Pid, uint User);

/// <summary>
/// The Steam client of one OS, read from the process list (and, on Windows, Steam's registry key) and started as a program. Ready means
/// running and signed in on Windows, and running on Linux and macOS.
/// </summary>
internal sealed class SteamClient : ISteamClient
{
    private readonly OsKind _os;
    private readonly IProcessInspector _processes;
    private readonly Func<SteamSignIn?> _readSignIn;
    private readonly Func<string?> _steamFolder;
    private readonly IProcessStarter _starter;

    /// <summary>Creates the client of one OS.</summary>
    /// <param name="os">Which OS the process list and the start command are for.</param>
    /// <param name="processes">The running processes.</param>
    /// <param name="readSignIn">Reads Steam's registry key on Windows; null elsewhere or when the key is missing.</param>
    /// <param name="steamFolder">The folder Steam is installed in, or null when it cannot be found.</param>
    /// <param name="starter">Starts the client.</param>
    public SteamClient(OsKind os, IProcessInspector processes, Func<SteamSignIn?> readSignIn, Func<string?> steamFolder, IProcessStarter starter)
    {
        _os = os;
        _processes = processes;
        _readSignIn = readSignIn;
        _steamFolder = steamFolder;
        _starter = starter;
    }

    /// <inheritdoc />
    public bool IsReady()
    {
        try
        {
            var running = _processes.List();
            // Outside Windows no file says whether an account is signed in (Linux's registry.vdf keeps only Steam's pid and the account
            // that signs in automatically), so a running client is all that can be told.
            if (_os != OsKind.Windows)
            {
                return running.Any(IsClientProcess);
            }

            // Steam leaves its key behind when it is killed, and a pid can be reused: the process must be there under Steam's name too.
            return _readSignIn() is { Pid: > 0, User: not 0 } signIn
                && running.Any(process => process.Pid == signIn.Pid && IsClientProcess(process));
        }
        catch (Exception e) when (e is not (OperationCanceledException or OutOfMemoryException))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool TryStart()
    {
        try
        {
            if (Command() is not { } request)
            {
                return false;
            }

            _starter.Start(request)?.Dispose();
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or PlatformNotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    // Only the client itself, never a link or an app id: a game started through Steam is updated first, over the version SavePoint switched to.
    private ProcessStartRequest? Command()
    {
        switch (_os)
        {
            case OsKind.Windows:
                return _steamFolder() is { } folder ? new ProcessStartRequest(PathSyntax.For(OsKind.Windows).Combine(folder, "steam.exe"), ["-silent"]) : null;
            case OsKind.MacOS:
                return new ProcessStartRequest("open", ["-a", "Steam", "--args", "-silent"]);
            default:
                return new ProcessStartRequest("steam", ["-silent"]);
        }
    }

    // The client's process name per OS: Linux and Windows list it as steam, macOS as steam_osx.
    private bool IsClientProcess(RunningProcess process) =>
        string.Equals(process.Name, _os == OsKind.MacOS ? "steam_osx" : "steam", _os == OsKind.Linux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
