using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>Picks the launcher for an OS.</summary>
public static class GameLaunchers
{
    /// <summary>The launcher that starts real processes on <paramref name="os"/>.</summary>
    /// <param name="os">The OS the game runs on.</param>
    public static IGameLauncher ForOs(OsKind os) => os switch
    {
        OsKind.Windows => new WindowsGameLauncher(),
        OsKind.MacOS => new MacOSGameLauncher(),
        _ => new LinuxGameLauncher(),
    };

    /// <summary>The launcher for the OS the process runs on.</summary>
    public static IGameLauncher ForCurrentOs() => ForOs(PlatformFactory.CurrentOs);
}
