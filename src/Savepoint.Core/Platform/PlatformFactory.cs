using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;

namespace Savepoint.Core.Platform;

public static class PlatformFactory
{
    public static OsKind CurrentOs { get; } = DetectCurrentOs();

    public static IPlatform ForCurrentOs()
    {
        var environment = PlatformEnvironment.FromCurrentMachine();
        return CurrentOs switch
        {
            OsKind.Windows => new WindowsPlatform(environment),
            OsKind.MacOS => new MacOSPlatform(environment),
            _ => new LinuxPlatform(environment),
        };
    }

    private static OsKind DetectCurrentOs()
    {
        if (OperatingSystem.IsWindows())
        {
            return OsKind.Windows;
        }

        return OperatingSystem.IsMacOS() ? OsKind.MacOS : OsKind.Linux;
    }
}
