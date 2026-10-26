using Microsoft.Win32.SafeHandles;

namespace Savepoint.Core.Paths;

// Keeps a folder from being renamed, moved or deleted while it is held. Only Windows can: elsewhere a hold does nothing, and the
// engine's recheck of the folder before each destructive step is the whole defense.
internal sealed class FolderHold : IDisposable
{
    private readonly SafeFileHandle? _handle;

    private FolderHold(SafeFileHandle? handle) => _handle = handle;

    // A folder that cannot be opened is not held; the caller still has the recheck.
    public static FolderHold Open(string folder) =>
        new(OperatingSystem.IsWindows() ? WindowsFiles.HoldFolder(folder) : null);

    public void Dispose() => _handle?.Dispose();
}
