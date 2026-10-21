using System.ComponentModel;
using System.Text.RegularExpressions;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

// The start-and-translate-failures step every launcher shares.
internal static partial class Spawn
{
    // Same shape the catalog accepts: a scheme of at least two characters, so a drive letter is never taken for one.
    [GeneratedRegex(@"^(?<scheme>[A-Za-z][A-Za-z0-9+.\-]+):\S", RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();

    /// <summary>Starts a program and returns its process; any failure of the start becomes a <see cref="LaunchSpawnException"/>.</summary>
    public static IGameProcess Program(IProcessStarter starter, ProcessStartRequest request)
    {
        var process = Start(starter, request, LaunchMethod.Exe);
        return process ?? throw new LaunchSpawnException(LaunchMethod.Exe, $"'{request.FileName}' started, but the OS gave no process to follow.", null);
    }

    /// <summary>Starts an opener or the shell for a link; the process, when there is one, is the caller's to dispose.</summary>
    public static IGameProcess? Opener(IProcessStarter starter, ProcessStartRequest request, string uri)
    {
        RequireOpenableLink(uri);
        return Start(starter, request, LaunchMethod.Uri);
    }

    private static IGameProcess? Start(IProcessStarter starter, ProcessStartRequest request, LaunchMethod method)
    {
        try
        {
            return starter.Start(request);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or PlatformNotSupportedException or ArgumentException)
        {
            throw new LaunchSpawnException(method, $"Could not start '{request.FileName}': {ex.Message}", ex);
        }
    }

    // A link goes to the OS opener, so anything that is not a plain link (a file path, a program name, an option) must not get that far.
    private static void RequireOpenableLink(string uri)
    {
        var match = LinkPattern().Match(uri);
        if (!match.Success || string.Equals(match.Groups["scheme"].Value, "file", StringComparison.OrdinalIgnoreCase))
        {
            throw new LaunchSpawnException(LaunchMethod.Uri, $"'{uri}' is not a link SavePoint will hand to the OS: it needs a scheme such as steam: and cannot be a file link.", null);
        }
    }
}
