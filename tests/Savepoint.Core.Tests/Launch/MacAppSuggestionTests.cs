using Savepoint.Core.Launch;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Launch;

// A bundle is a folder, so a fake one in a temp folder runs the same on every system.
public class MacAppSuggestionTests
{
    private const string _plist = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>CFBundleName</key>
            <string>Pretty Name</string>
            <key>CFBundleExecutable</key>
            <string>PrettyGame</string>
        </dict>
        </plist>
        """;

    private static InstalledProgram[] Bundle(string name) => [new InstalledProgram(name, 0)];

    [Fact]
    public void A_bundle_is_suggested_by_the_name_of_the_program_inside_it()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(Path.Combine(temp.Path, "Game.app", "Contents"));
        File.WriteAllText(Path.Combine(temp.Path, "Game.app", "Contents", "Info.plist"), _plist);

        var suggestion = ProcessNameSuggester.Suggest("Game", "Game", Bundle("Game.app"), temp.Path);

        Assert.Equal("PrettyGame", suggestion);
    }

    [Fact]
    public void A_bundle_without_a_readable_plist_is_suggested_by_its_own_name()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(Path.Combine(temp.Path, "Game.app", "Contents"));
        File.WriteAllBytes(Path.Combine(temp.Path, "Game.app", "Contents", "Info.plist"), "bplist00"u8.ToArray());

        Assert.Equal("Game", ProcessNameSuggester.Suggest("Game", "Game", Bundle("Game.app"), temp.Path));
    }

    [Fact]
    public void A_bundle_with_no_plist_at_all_is_suggested_by_its_own_name()
    {
        using var temp = new TempFolder();

        Assert.Equal("Game", ProcessNameSuggester.Suggest("Game", "Game", Bundle("Game.app"), temp.Path));
    }

    [Fact]
    public void A_program_that_is_not_a_bundle_keeps_its_file_name()
    {
        using var temp = new TempFolder();

        Assert.Equal("Game.exe", ProcessNameSuggester.Suggest("Game", "Game", [new InstalledProgram("Game.exe", 5_000_000)], temp.Path));
    }
}
