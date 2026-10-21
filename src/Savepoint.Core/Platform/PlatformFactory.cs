namespace Savepoint.Core.Platform;

public static class PlatformFactory
{
    public static OsKind CurrentOs { get; } = DetectCurrentOs();

    private static OsKind DetectCurrentOs()
    {
        if (OperatingSystem.IsWindows())
        {
            return OsKind.Windows;
        }

        return OperatingSystem.IsMacOS() ? OsKind.MacOS : OsKind.Linux;
    }
}
