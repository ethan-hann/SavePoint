using Microsoft.Win32.SafeHandles;

namespace Savepoint.Core.Paths;

// Keeps a folder from being renamed, moved or deleted while it is held. Only Windows can: elsewhere a hold does nothing, and the
// engine's recheck of the folder before each destructive step is the whole defense.
internal sealed class FolderHold : IDisposable
{
    private readonly SafeFileHandle? _handle;

    private FolderHold(SafeFileHandle? handle, FileIdentity? identity)
    {
        _handle = handle;
        Identity = identity;
    }

    // True when the OS is keeping the folder from being renamed or deleted.
    public bool IsHeld => _handle is not null;

    // The identity of the folder the handle actually opened, read from the handle itself: null when nothing is held or the OS gave none.
    public FileIdentity? Identity { get; }

    // A folder that cannot be opened is not held; the caller still has the recheck. The folder itself is opened first, so a path that has
    // become a link shows as another identity than the pinned one. Only a folder that is a link by its nature (a volume mounted in a
    // folder, which the path resolution leaves in place) is opened again through the link, and then it has the pinned identity.
    public static FolderHold Open(string folder, FileIdentity? pinned)
    {
        if (!OperatingSystem.IsWindows() || WindowsFiles.HoldFolder(folder, followLinks: false) is not { } held)
        {
            return new FolderHold(null, null);
        }

        if (held.IsLink && pinned is not null && held.Identity != pinned && WindowsFiles.HoldFolder(folder, followLinks: true) is { } followed)
        {
            held.Handle.Dispose();
            held = followed;
        }

        return new FolderHold(held.Handle, held.Identity);
    }

    public void Dispose() => _handle?.Dispose();
}
