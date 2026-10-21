using Savepoint.Core.Launch;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Launch;

public class GameLaunchersTests
{
    [Fact]
    public void Each_os_gets_the_launcher_written_for_it()
    {
        Assert.IsType<WindowsGameLauncher>(GameLaunchers.ForOs(OsKind.Windows));
        Assert.IsType<LinuxGameLauncher>(GameLaunchers.ForOs(OsKind.Linux));
        Assert.IsType<MacOSGameLauncher>(GameLaunchers.ForOs(OsKind.MacOS));
    }

    [Fact]
    public void The_current_os_gets_the_launcher_for_the_os_the_process_runs_on()
    {
        var expected = PlatformFactory.CurrentOs switch
        {
            OsKind.Windows => typeof(WindowsGameLauncher),
            OsKind.MacOS => typeof(MacOSGameLauncher),
            _ => typeof(LinuxGameLauncher),
        };

        Assert.IsType(expected, GameLaunchers.ForCurrentOs());
    }
}
