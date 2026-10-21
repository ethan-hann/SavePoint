using Savepoint.Core.Errors;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Paths;

public class PathTokenResolverTests
{
    private static readonly PathTokenResolver _windows = new(new WindowsPlatform(FakeEnvironment.Windows().Build()));
    private static readonly PathTokenResolver _linux = new(new LinuxPlatform(FakeEnvironment.Linux().Build()));
    private static readonly PathTokenResolver _mac = new(new MacOSPlatform(FakeEnvironment.MacOS().Build()));

    public static TheoryData<OsKind, string, string> Resolutions => new()
    {
        { OsKind.Windows, @"{documents}\My Games\Skyrim", @"C:\Users\Test\OneDrive\Documents\My Games\Skyrim" },
        { OsKind.Windows, "{userHome}/a/b", @"C:\Users\Test\a\b" },
        { OsKind.Windows, "{userHome}", @"C:\Users\Test" },
        { OsKind.Windows, @"{userHome}\", @"C:\Users\Test" },
        { OsKind.Windows, @"{userHome}\\//Games", @"C:\Users\Test\Games" },
        { OsKind.Windows, @"{userHome}Games", @"C:\Users\Test\Games" },
        { OsKind.Windows, @"{appData}\Game", @"C:\Users\Test\AppData\Roaming\Game" },
        { OsKind.Windows, @"{localAppData}\Game", @"C:\Users\Test\AppData\Local\Game" },
        { OsKind.Windows, @"{localLow}\Studio\Game", @"C:\Users\Test\AppData\LocalLow\Studio\Game" },
        { OsKind.Windows, @"{savedGames}\Game", @"C:\Users\Test\Saved Games\Game" },
        { OsKind.Windows, @"{documents}\..\Elsewhere", @"C:\Users\Test\OneDrive\Elsewhere" },
        { OsKind.Windows, "C:/Games/Sky", @"C:\Games\Sky" },
        { OsKind.Windows, @"d:\a\..\b\", @"D:\b" },
        { OsKind.Windows, @"\\server\share\Games", @"\\server\share\Games" },
        { OsKind.Linux, "{documents}/Games", "/home/test/Documents/Games" },
        { OsKind.Linux, @"{appData}\Game\saves", "/home/test/.config/Game/saves" },
        { OsKind.Linux, "{localAppData}/Game", "/home/test/.local/share/Game" },
        { OsKind.Linux, "{userHome}", "/home/test" },
        { OsKind.Linux, @"/opt/games\sky/", @"/opt/games\sky" },
        { OsKind.MacOS, @"/Applications/Game\Name.app", @"/Applications/Game\Name.app" },
        { OsKind.Linux, @"{userHome}/a\b/c", "/home/test/a/b/c" },
        { OsKind.Linux, "/a/./b/../c", "/a/c" },
        { OsKind.MacOS, "{appData}/Game", "/Users/test/Library/Application Support/Game" },
        { OsKind.MacOS, @"{localAppData}\Game", "/Users/test/Library/Application Support/Game" },
        { OsKind.MacOS, "/Applications/Game.app", "/Applications/Game.app" },
    };

    public static TheoryData<OsKind, string> UnknownTokens => new()
    {
        { OsKind.Windows, @"{nope}\x" },
        { OsKind.Windows, @"{Documents}\x" },
        { OsKind.Linux, "{savedGames}/x" },
        { OsKind.Linux, "{localLow}/x" },
        { OsKind.MacOS, "{savedGames}/x" },
        { OsKind.MacOS, "{localLow}/x" },
    };

    public static TheoryData<OsKind, string> NotAbsolute => new()
    {
        { OsKind.Windows, @"Games\Sky" },
        { OsKind.Windows, @"\Games" },
        { OsKind.Windows, "C:Games" },
        { OsKind.Windows, "/etc" },
        { OsKind.Windows, "" },
        { OsKind.Windows, @"{foo1}\x" },
        { OsKind.Linux, "games/sky" },
        { OsKind.Linux, @"C:\Games" },
        { OsKind.Linux, "" },
        { OsKind.MacOS, "Applications/Game.app" },
    };

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void A_path_resolves_to_the_normalized_absolute_path_for_the_os(OsKind os, string raw, string expected)
    {
        Assert.Equal(expected, For(os).Resolve(raw));
    }

    [Theory]
    [MemberData(nameof(UnknownTokens))]
    public void An_unknown_token_is_a_validation_error_naming_the_field(OsKind os, string raw)
    {
        var error = Assert.Throws<ValidationException>(() => For(os).Resolve(raw));

        Assert.Equal([new ValidationIssue("path", "validation.path.unknownToken")], Issues(error));
    }

    [Theory]
    [MemberData(nameof(NotAbsolute))]
    public void A_relative_path_is_a_validation_error(OsKind os, string raw)
    {
        var error = Assert.Throws<ValidationException>(() => For(os).Resolve(raw));

        Assert.Equal([new ValidationIssue("path", "validation.path.notAbsolute")], Issues(error));
    }

    [Theory]
    [InlineData(@"C:\Windows::$INDEX_ALLOCATION\System32")]
    [InlineData(@"D:\Games\Sky:stream")]
    [InlineData(@"{documents}\Game::$DATA")]
    [InlineData(@"{userHome}\a:b\c")]
    [InlineData(@"\\server\share\x:y")]
    public void A_stream_spelling_on_windows_is_a_validation_error_of_its_own(string raw)
    {
        var error = Assert.Throws<ValidationException>(() => _windows.Resolve(raw));

        Assert.Equal([new ValidationIssue("path", PathValidationIds.InvalidCharacter)], Issues(error));
    }

    [Fact]
    public void A_colon_is_fine_in_a_posix_folder_name()
    {
        Assert.Equal("/games/a:b", _linux.Resolve("/games/a:b"));
        Assert.Equal("/home/test/Documents/a:b", _linux.Resolve("{documents}/a:b"));
    }

    [Fact]
    public void The_caller_can_name_the_field_in_the_issue()
    {
        var error = Assert.Throws<ValidationException>(() => _windows.Resolve("{nope}", "install.path"));

        Assert.Equal([new ValidationIssue("install.path", "validation.path.unknownToken")], Issues(error));
    }

    private static PathTokenResolver For(OsKind os) => os switch
    {
        OsKind.Windows => _windows,
        OsKind.Linux => _linux,
        _ => _mac,
    };

    private static IReadOnlyList<ValidationIssue> Issues(ValidationException error) =>
        Assert.IsAssignableFrom<IReadOnlyList<ValidationIssue>>(error.Args["issues"]);
}
