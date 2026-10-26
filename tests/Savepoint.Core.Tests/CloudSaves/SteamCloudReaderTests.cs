using Savepoint.Core.Api;
using Savepoint.Core.CloudSaves;
using Savepoint.Core.Tests.Artwork;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.CloudSaves;

// The files are laid out as Steam writes them: config/loginusers.vdf, userdata/<account id>/7/remote/sharedconfig.vdf, and
// userdata/<account id>/<app id>/remotecache.vdf for a game whose cloud files Steam tracks.
public class SteamCloudReaderTests
{
    // 76561198000000001 is account id 39734273 (the low 32 bits); 76561198000000002 is 39734274.
    private const string _first = "76561198000000001";
    private const string _second = "76561198000000002";

    // Recent clients write no MostRecent; the keys are the ones below, with a lowercase timestamp.
    private sealed record User(string SteamId, bool? AutoLogin = null, bool? MostRecent = null, long? Timestamp = null);

    private static string LoginUsers(params User[] users) =>
        "\"users\"\n{\n" + string.Concat(users.Select(user =>
            $"\t\"{user.SteamId}\"\n\t{{\n\t\t\"AccountName\"\t\t\"name{user.SteamId[^1]}\"\n\t\t\"PersonaName\"\t\t\"Name\"\n\t\t\"RememberPassword\"\t\t\"1\"\n"
            + (user.AutoLogin is { } auto ? $"\t\t\"AutoLogin\"\t\t\"{(auto ? 1 : 0)}\"\n" : string.Empty)
            + "\t\t\"WantsOfflineMode\"\t\t\"0\"\n\t\t\"SkipOfflineModeWarning\"\t\t\"0\"\n"
            + (user.MostRecent is { } recent ? $"\t\t\"MostRecent\"\t\t\"{(recent ? 1 : 0)}\"\n" : string.Empty)
            + (user.Timestamp is { } stamp ? $"\t\t\"timestamp\"\t\t\"{stamp}\"\n" : string.Empty)
            + "\t}\n")) + "}\n";

    private static string SharedConfig(params (string AppId, string? Cloud)[] apps) => SharedConfigCased("UserRoamingConfigStore", "Software", "Valve", "Steam", "apps", "cloudenabled", apps);

    private static string SharedConfigCased(string root, string software, string valve, string steam, string appsKey, string cloudKey, params (string AppId, string? Cloud)[] apps) =>
        $"\"{root}\"\n{{\n\t\"{software}\"\n\t{{\n\t\t\"{valve}\"\n\t\t{{\n\t\t\t\"{steam}\"\n\t\t\t{{\n\t\t\t\t\"{appsKey}\"\n\t\t\t\t{{\n"
        + string.Concat(apps.Select(app =>
            $"\t\t\t\t\t\"{app.AppId}\"\n\t\t\t\t\t{{\n\t\t\t\t\t\t\"LastPlayed\"\t\t\"1700000000\"\n"
            + (app.Cloud is null ? string.Empty : $"\t\t\t\t\t\t\"{cloudKey}\"\t\t\"{app.Cloud}\"\n")
            + "\t\t\t\t\t}\n"))
        + "\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n";

    private static void WriteShared(TempFolder steam, string account, string text) =>
        steam.WriteText($"userdata/{account}/7/remote/sharedconfig.vdf", text);

    private static SteamCloudReader ReaderFor(TempFolder steam, uint? activeUser = null) =>
        new(new FixedSteamFolder(steam.Path), activeUser is { } id ? () => id : null);

    [Fact]
    public void A_game_with_cloudenabled_1_is_on_for_the_most_recent_account()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_second), new User(_first, MostRecent: true)));
        WriteShared(steam, "39734273", SharedConfig(("220", "0"), ("730", "1"), ("440", null)));

        Assert.Equal(CloudSaveState.On, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void A_game_with_cloudenabled_0_is_off()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, MostRecent: true)));
        WriteShared(steam, "39734273", SharedConfig(("220", "0"), ("730", "1")));

        Assert.Equal(CloudSaveState.Off, ReaderFor(steam).Read("220"));
    }

    [Fact]
    public void Keys_in_any_case_are_read()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, MostRecent: true)));
        WriteShared(steam, "39734273", SharedConfigCased("USERROAMINGCONFIGSTORE", "software", "VALVE", "steam", "Apps", "CloudEnabled", ("730", "1")));

        Assert.Equal(CloudSaveState.On, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void A_game_without_the_key_and_without_cloud_files_is_unknown()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, MostRecent: true)));
        WriteShared(steam, "39734273", SharedConfig(("440", null), ("730", "1")));

        Assert.Equal(CloudSaveState.Unknown, ReaderFor(steam).Read("440"));
        Assert.Equal(CloudSaveState.Unknown, ReaderFor(steam).Read("999"));
    }

    [Fact]
    public void A_game_without_the_key_is_on_when_steam_tracks_its_cloud_files()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, MostRecent: true)));
        WriteShared(steam, "39734273", SharedConfig(("440", null)));
        steam.WriteText("userdata/39734273/440/remotecache.vdf", "\"440\"\n{\n\t\"ChangeNumber\"\t\t\"1\"\n}\n");

        Assert.Equal(CloudSaveState.On, ReaderFor(steam).Read("440"));
    }

    [Fact]
    public void Cloud_files_do_not_override_a_cloud_switch_that_is_off()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, MostRecent: true)));
        WriteShared(steam, "39734273", SharedConfig(("440", "0")));
        steam.WriteText("userdata/39734273/440/remotecache.vdf", "\"440\"\n{\n}\n");

        Assert.Equal(CloudSaveState.Off, ReaderFor(steam).Read("440"));
    }

    [Fact]
    public void Cloud_files_of_another_account_do_not_count()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, MostRecent: true), new User(_second)));
        WriteShared(steam, "39734273", SharedConfig(("440", null)));
        steam.WriteText("userdata/39734274/440/remotecache.vdf", "\"440\"\n{\n}\n");

        Assert.Equal(CloudSaveState.Unknown, ReaderFor(steam).Read("440"));
    }

    [Fact]
    public void A_missing_sharedconfig_file_without_cloud_files_is_unknown()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, MostRecent: true)));

        Assert.Equal(CloudSaveState.Unknown, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void A_file_that_is_not_key_value_text_is_unknown()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, MostRecent: true)));
        WriteShared(steam, "39734273", "\"UserRoamingConfigStore\" {{{ not closed");

        Assert.Equal(CloudSaveState.Unknown, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void The_most_recent_account_decides_when_several_accounts_have_files()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, AutoLogin: true), new User(_second, MostRecent: true)));
        WriteShared(steam, "39734273", SharedConfig(("730", "1")));
        WriteShared(steam, "39734274", SharedConfig(("730", "0")));

        Assert.Equal(CloudSaveState.Off, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void Without_a_most_recent_flag_the_auto_login_account_decides()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, AutoLogin: false, Timestamp: 1800000000), new User(_second, AutoLogin: true, Timestamp: 1700000000)));
        WriteShared(steam, "39734273", SharedConfig(("730", "1")));
        WriteShared(steam, "39734274", SharedConfig(("730", "0")));

        Assert.Equal(CloudSaveState.Off, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void The_only_listed_user_decides_when_nothing_is_flagged()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_second)));
        WriteShared(steam, "39734273", SharedConfig(("730", "1")));
        WriteShared(steam, "39734274", SharedConfig(("730", "0")));

        Assert.Equal(CloudSaveState.Off, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void The_largest_timestamp_decides_when_several_users_are_unflagged()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, Timestamp: 1700000000), new User(_second, Timestamp: 1800000000)));
        WriteShared(steam, "39734273", SharedConfig(("730", "1")));
        WriteShared(steam, "39734274", SharedConfig(("730", "0")));

        Assert.Equal(CloudSaveState.Off, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void Users_that_cannot_be_told_apart_fall_back_to_the_single_account_folder()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first), new User(_second)));
        WriteShared(steam, "39734274", SharedConfig(("730", "1")));
        Directory.CreateDirectory(steam.Combine("userdata", "0"));

        Assert.Equal(CloudSaveState.On, ReaderFor(steam).Read("730"));
    }

    [Fact]
    public void Users_that_cannot_be_told_apart_with_several_account_folders_are_unknown()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first), new User(_second)));
        WriteShared(steam, "39734273", SharedConfig(("730", "1")));
        WriteShared(steam, "39734274", SharedConfig(("730", "1")));

        Assert.Equal(CloudSaveState.Unknown, ReaderFor(steam).Read("730"));
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
    public void The_account_steam_reports_as_active_comes_first()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, MostRecent: true), new User(_second)));
        WriteShared(steam, "39734273", SharedConfig(("730", "1")));
        WriteShared(steam, "39734274", SharedConfig(("730", "0")));

        Assert.Equal(CloudSaveState.Off, ReaderFor(steam, activeUser: 39734274).Read("730"));
    }

    [Fact]
    public void An_active_account_of_0_or_without_a_folder_is_ignored()
    {
        using var steam = new TempFolder();
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, MostRecent: true)));
        WriteShared(steam, "39734273", SharedConfig(("730", "1")));

        Assert.Equal(CloudSaveState.On, ReaderFor(steam, activeUser: 0).Read("730"));
        Assert.Equal(CloudSaveState.On, ReaderFor(steam, activeUser: 12345).Read("730"));
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
        steam.WriteText("config/loginusers.vdf", LoginUsers(new User(_first, MostRecent: true)));
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
