using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;

namespace Savepoint.Core.Tests.Platform;

internal static class FakePlatforms
{
    public static IPlatform For(OsKind os) => os switch
    {
        OsKind.Windows => new WindowsPlatform(FakeEnvironment.Windows().Build()),
        OsKind.Linux => new LinuxPlatform(FakeEnvironment.Linux().Build()),
        _ => new MacOSPlatform(FakeEnvironment.MacOS().Build()),
    };

    /// <summary>The platform of the running OS, with a minimal environment rooted at <paramref name="home"/>.</summary>
    public static IPlatform ForHost(string home, string? steamFolder = null)
    {
        var env = new PlatformEnvironment { HomeDirectory = home, GetSteamFolder = () => steamFolder };
        return PlatformFactory.CurrentOs switch
        {
            OsKind.Windows => new WindowsPlatform(env),
            OsKind.Linux => new LinuxPlatform(env),
            _ => new MacOSPlatform(env),
        };
    }
}
