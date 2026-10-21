using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Launch;

// Every launcher hands links to an OS opener, so every launcher refuses what is not a plain link.
public class LauncherLinkRuleTests
{
    private static readonly OsKind[] _systems = [OsKind.Windows, OsKind.Linux, OsKind.MacOS];

    private static readonly string[] _notLinks =
    [
        "-flag",
        "--version",
        "file:///C:/Windows/System32/calc.exe",
        "FILE:///etc/passwd",
        @"C:\Windows\System32\calc.exe",
        "calc.exe",
        "/usr/bin/xcalc",
        "",
        "   ",
        "steam: run",
    ];

    private static readonly string[] _links =
    [
        "steam://run/489830",
        "https://store.steampowered.com/app/489830",
        "com.epicgames.launcher://apps/fortnite?action=launch",
        "ms-windows-store://home",
    ];

    public static TheoryData<OsKind, string> NotLinks() => Cross(_notLinks);

    public static TheoryData<OsKind, string> Links() => Cross(_links);

    private static TheoryData<OsKind, string> Cross(string[] links)
    {
        var data = new TheoryData<OsKind, string>();
        foreach (var os in _systems)
        {
            foreach (var link in links)
            {
                data.Add(os, link);
            }
        }

        return data;
    }

    private static IGameLauncher Build(OsKind os, RecordingStarter starter)
    {
        var files = new FakeLaunchFiles();
        var clock = TimeProvider.System;
        return os switch
        {
            OsKind.Windows => new WindowsGameLauncher(starter, files),
            OsKind.Linux => new LinuxGameLauncher(starter, files, TimeSpan.FromSeconds(10), clock),
            _ => new MacOSGameLauncher(starter, files, TimeSpan.FromSeconds(10), clock),
        };
    }

    [Theory]
    [MemberData(nameof(NotLinks))]
    public async Task A_value_that_is_not_a_plain_link_is_refused_and_nothing_is_started(OsKind os, string value)
    {
        var starter = new RecordingStarter();

        var error = await Assert.ThrowsAsync<LaunchSpawnException>(() => Build(os, starter).OpenUriAsync(value, TestContext.Current.CancellationToken));

        Assert.Equal(LaunchMethod.Uri, error.Args["method"]);
        Assert.Empty(starter.Requests);
    }

    [Theory]
    [MemberData(nameof(Links))]
    public async Task A_link_with_a_scheme_is_handed_to_the_opener(OsKind os, string value)
    {
        var starter = new RecordingStarter();
        starter.Answer = _ =>
        {
            var opener = new ScriptedProcess();
            opener.Exit(0);
            return opener;
        };

        await Build(os, starter).OpenUriAsync(value, TestContext.Current.CancellationToken);

        Assert.Contains(value, starter.Only.Arguments.Append(starter.Only.FileName));
    }
}
