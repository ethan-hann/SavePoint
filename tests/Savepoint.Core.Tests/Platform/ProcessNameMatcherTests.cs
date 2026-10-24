using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Platform;

public class ProcessNameMatcherTests
{
    private const string _protonExePath = "/home/u/.steam/steam/compatibilitytools.d/Proton/files/bin/wine64-preloader";
    private const string _protonArgv0 = @"Z:\home\u\.steam\steam\steamapps\common\Skyrim Special Edition\SkyrimSE.exe";

    public static TheoryData<OsKind, string, string, string?, string?, bool> Cases => new()
    {
        // Windows and macOS: case-insensitive, .exe optional on either side, name only.
        { OsKind.Windows, "SkyrimSE.exe", "SkyrimSE", null, null, true },
        { OsKind.Windows, "SkyrimSE", "SkyrimSE.exe", null, null, true },
        { OsKind.Windows, "skyrimse.EXE", "SkyrimSE", null, null, true },
        { OsKind.Windows, "  SkyrimSE.exe ", "SkyrimSE", null, null, true },
        { OsKind.Windows, "Game", "Game2", null, null, false },
        { OsKind.Windows, "Game2", "Game", null, null, false },
        { OsKind.Windows, "Game.exe", "Launcher", @"C:\Games\Game.exe", null, false },
        { OsKind.Windows, "VeryLongGameName.exe", "VeryLongGameNam", null, null, false },
        { OsKind.Windows, ".exe", "", null, null, false },
        { OsKind.MacOS, "game", "Game", null, null, true },
        { OsKind.MacOS, "Game.exe", "Game", null, null, true },
        { OsKind.MacOS, "Game", "GameHelper", null, null, false },

        // A bundle's name stands for the program inside it.
        { OsKind.MacOS, "Game.app", "Game", null, null, true },
        { OsKind.MacOS, "game.APP", "Game", null, null, true },
        { OsKind.MacOS, "Game.app", "GameHelper", null, null, false },
        { OsKind.Windows, "Game.app", "Game", null, null, false },

        // Linux: case-sensitive, .exe optional, also the executable's and argv0's file names.
        { OsKind.Linux, "factorio", "factorio", null, null, true },
        { OsKind.Linux, "SkyrimSE.exe", "SkyrimSE.exe", null, null, true },
        { OsKind.Linux, "SkyrimSE", "SkyrimSE.exe", null, null, true },
        { OsKind.Linux, "SkyrimSE.exe", "SkyrimSE", null, null, true },
        { OsKind.Linux, "skyrimse.exe", "SkyrimSE.exe", null, null, true },
        { OsKind.Linux, "SKYRIMSE.EXE", "SkyrimSE.exe", null, null, true },
        { OsKind.Linux, "Skyrimse", "SkyrimSE.exe", null, null, false },
        { OsKind.Linux, "Factorio", "factorio", null, null, false },
        { OsKind.Linux, "factorio", "Factorio", null, null, false },
        { OsKind.Linux, "factorio", "factorio2", null, null, false },
        { OsKind.Linux, "SkyrimSE.exe", "wine64-preloader", _protonExePath, null, false },
        { OsKind.Linux, "SkyrimSE.exe", "wine64-preloader", "/games/Skyrim/SkyrimSE.exe", null, true },
        { OsKind.Linux, "game", "wrapper", "/opt/games/game/bin/game", null, true },
        { OsKind.Linux, "SkyrimSE.exe", "wine64-preloader", _protonExePath, _protonArgv0, true },
        { OsKind.Linux, "SkyrimSE", "wine64-preloader", _protonExePath, _protonArgv0, true },
        { OsKind.Linux, "SkyrimSE.exe", "wine64-preloader", _protonExePath, "Z:/home/u/Skyrim/SkyrimSE.exe", true },
        { OsKind.Linux, "SkyrimSE.exe", "wine64-preloader", _protonExePath, @"C:\windows\system32\services.exe", false },
        { OsKind.Linux, "SkyrimSE.exe", "wine64-preloader", null, "SkyrimSE.exe", true },
        { OsKind.Linux, "skyrimse.exe", "wine64-preloader", _protonExePath, _protonArgv0, true },
        { OsKind.Linux, "skyrimse", "wine64-preloader", _protonExePath, _protonArgv0, false },
        { OsKind.Linux, "game", "wrapper", "/opt/games/Game/bin/Game", null, false },

        // Linux: the kernel cuts comm to 15 characters, so a 15-character name matches by prefix.
        { OsKind.Linux, "VeryLongGameName.exe", "VeryLongGameNam", null, null, true },
        { OsKind.Linux, "VeryLongGameName", "VeryLongGameNam", null, null, true },
        { OsKind.Linux, "WitcherGame_x.exe", "WitcherGame_x.e", null, null, true },
        { OsKind.Linux, "WitcherGame_x", "WitcherGame_x.e", null, null, true },
        { OsKind.Linux, "VeryLongGameName.exe", "VeryLongGameNax", null, null, false },
        { OsKind.Linux, "VeryLongGameName.exe", "VeryLongGameNa", null, null, false },
        { OsKind.Linux, "veryLongGameName.exe", "VeryLongGameNam", null, null, true },
        { OsKind.Linux, "veryLongGameName", "VeryLongGameNam", null, null, false },

        // The 15-character rule is a last resort: when the executable path or argv0 names a file, that file decides.
        { OsKind.Linux, "MyVeryLongGameName.exe", "MyVeryLongGameN", null, @"C:\x\MyVeryLongGameNameLauncher.exe", false },
        { OsKind.Linux, "VeryLongGameName.exe", "VeryLongGameNam", "/usr/bin/wrapper", null, false },
        { OsKind.Linux, "MyVeryLongGameName.exe", "MyVeryLongGameN", "/usr/bin/wrapper", @"C:\x\MyVeryLongGameName.exe", true },

        // macOS reports the executable's full path, so a long name that the kernel shortened still matches by file name.
        { OsKind.MacOS, "Pillars of Eternity II", "Pillars of Eter", "/Applications/PoE2.app/Contents/MacOS/Pillars of Eternity II", null, true },
        { OsKind.MacOS, "pillars of eternity ii", "Pillars of Eter", "/Applications/PoE2.app/Contents/MacOS/Pillars of Eternity II", null, true },
        { OsKind.MacOS, "Pillars of Eternity II", "Pillars of Eter", "/Applications/PoE2.app/Contents/MacOS/Other", null, false },
        { OsKind.Windows, "Pillars of Eternity II", "Pillars of Eter", @"C:\Games\PoE2\Pillars of Eternity II.exe", null, false },
        { OsKind.Linux, "SkyrimSE.exe", "VeryLongGameNam", null, null, false },
        { OsKind.Linux, "", "", null, null, false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matching_follows_the_rules_of_each_os(OsKind os, string configured, string name, string? executablePath, string? commandName, bool expected)
    {
        var process = new RunningProcess(4242, name, executablePath, commandName);

        Assert.Equal(expected, ProcessNameMatcher.Matches(configured, process, os));
    }
}
