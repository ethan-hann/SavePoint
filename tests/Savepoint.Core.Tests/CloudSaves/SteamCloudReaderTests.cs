using Savepoint.Core.Api;
using Savepoint.Core.CloudSaves;
using Savepoint.Core.Tests.Artwork;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.CloudSaves;

// The files are laid out as Steam writes them: config/loginusers.vdf, and userdata/<account id>/7/remote/sharedconfig.vdf.
public class SteamCloudReaderTests
{
    // 76561198000000001 is account id 39734273 (the low 32 bits); 76561198000000002 is 39734274.
    private const string _first = "76561198000000001";
    private const string _second = "76561198000000002";

    private static string LoginUsers(string recent, string other) =>
        "\"users\"\n{\n"
        + $"\t\"{other}\"\n\t{{\n\t\t\"AccountName\"\t\t\"older\"\n\t\t\"PersonaName\"\t\t\"Older\"\n\t\t\"RememberPassword\"\t\t\"1\"\n\t\t\"MostRecent\"\t\t\"0\"\n\t\t\"Timestamp\"\t\t\"1700000000\"\n\t}}\n"
        + $"\t\"{recent}\"\n\t{{\n\t\t\"AccountName\"\t\t\"newer\"\n\t\t\"PersonaName\"\t\t\"Newer\"\n\t\t\"RememberPassword\"\t\t\"1\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t\t\"Timestamp\"\t\t\"1800000000\"\n\t}}\n"
        + "}\n";

    private static string SharedConfig(params (string AppId, string? Cloud)[] apps) =>
        "\"UserRoamingConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"apps\"\n\t\t\t\t{\n"
        + string.Concat(apps.Select(app =>
            $"\t\t\t\t\t\"{app.AppId}\"\n\t\t\t\t\t{{\n\t\t\t\t\t\t\"LastPlayed\"\t\t\"1700000000\"\n"
            + (app.Cloud is null ? string.Empty : $"\t\t\t\t\t\t\"cloudenabled\"\t\t\"{app.Cloud}\"\n")
            + "\t\t\t\t\t}\n"))
        + "\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n";

    private static void WriteShared(TempFolder steam, string account, string text) =>
        steam.WriteText($"userdata/{account}/7/remote/sharedconfig.vdf", text);

    private static SteamCloudReader ReaderFor(TempFolder steam) => new(new FixedSteamFolder(steam.Path));

    [Fact]
    public void A_game_with_cloudenabled_1_is_on_for_the_most_recent_account()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(_first, _second));
        WriteShared(steam, "39734273", SharedConfig(("220", "0"), ("730", "1"), ("440", null)));

        Assert.Equal(CloudSaveState.On, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void A_game_with_cloudenabled_0_is_off()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(_first, _second));
        WriteShared(steam, "39734273", SharedConfig(("220", "0"), ("730", "1")));

        Assert.Equal(CloudSaveState.Off, ReaderFor(steam).Read("220"));
    }

    [Fact]
    public void A_game_without_the_key_is_unknown()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(_first, _second));
        WriteShared(steam, "39734273", SharedConfig(("440", null), ("730", "1")));

        Assert.Equal(CloudSaveState.Unknown, ReaderFor(steam).Read("440"));
        Assert.Equal(CloudSaveState.Unknown, ReaderFor(steam).Read("999"));
    }

    [Fact]
    public void A_missing_sharedconfig_file_is_unknown()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(_first, _second));

        Assert.Equal(CloudSaveState.Unknown, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void A_file_that_is_not_key_value_text_is_unknown()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(_first, _second));
        WriteShared(steam, "39734273", "\"UserRoamingConfigStore\" {{{ not closed");

        Assert.Equal(CloudSaveState.Unknown, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void The_most_recent_account_decides_when_several_accounts_have_files()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(_second, _first));
        WriteShared(steam, "39734273", SharedConfig(("730", "1")));
        WriteShared(steam, "39734274", SharedConfig(("730", "0")));

        Assert.Equal(CloudSaveState.Off, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void Without_loginusers_a_single_account_folder_is_used()
    {
        using var steam = new TempFolder();
        WriteShared(steam, "39734273", SharedConfig(("730", "1")));

        Assert.Equal(CloudSaveState.On, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void Without_a_most_recent_account_several_account_folders_are_unknown()
    {
        using var steam = new TempFolder();
        WriteShared(steam, "39734273", SharedConfig(("730", "1")));
        WriteShared(steam, "39734274", SharedConfig(("730", "1")));

        Assert.Equal(CloudSaveState.Unknown, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void Steam_not_installed_is_unknown()
    {
        Assert.Equal(CloudSaveState.Unknown, new SteamCloudReader(new FixedSteamFolder(null)).Read("730"));
    }

    [Fact]
    public void An_id_that_is_not_digits_is_unknown()
    {
        using var steam = new TempFolder();
        WriteShared(steam, "39734273", SharedConfig(("730", "1")));

        Assert.Equal(CloudSaveState.Unknown, ReaderFor(steam).Read("../730"));
    }

    [Fact]
    public void Reading_changes_nothing_in_the_steam_folder()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(_first, _second));
        WriteShared(steam, "39734273", SharedConfig(("730", "1")));
        var before = Snapshot(steam.Path);

        ReaderFor(steam).Read("730");

        Assert.Equal(before, Snapshot(steam.Path));
    }

    private static string[] Snapshot(string root) =>
        [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => $"{path}|{(File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks + "|" + new FileInfo(path).Length : "dir")}")];
}
