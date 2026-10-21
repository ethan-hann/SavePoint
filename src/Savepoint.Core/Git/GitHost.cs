namespace Savepoint.Core.Git;

// The operating system facts the git environment depends on, injectable so tests can cover every layout on any host.
internal readonly record struct GitHost(bool IsWindows, bool IsLinux)
{
    public static GitHost Current => new(OperatingSystem.IsWindows(), OperatingSystem.IsLinux());
}
