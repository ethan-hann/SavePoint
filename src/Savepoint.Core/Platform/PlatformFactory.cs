using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;

namespace Savepoint.Core.Platform;

/// <summary>Builds the platform of the machine the process runs on.</summary>
public static class PlatformFactory
{
    /// <summary>The OS the process runs on; any Unix other than macOS counts as Linux.</summary>
    public static OsKind CurrentOs { get; } = DetectCurrentOs();

    /// <summary>The platform for <see cref="CurrentOs"/>, built from this machine's folders, variables, and drives.</summary>
    /// <exception cref="InvalidOperationException">The account has no home folder.</exception>
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
