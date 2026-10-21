using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;

namespace Savepoint.Core.Tests.Platform;

public class PathTokenTableTests
{
    [Fact]
    public void Windows_has_six_tokens_and_follows_redirected_known_folders()
    {
        var tokens = new WindowsPlatform(FakeEnvironment.Windows().Build()).PathTokens;

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["userHome"] = @"C:\Users\Test",
                ["documents"] = @"C:\Users\Test\OneDrive\Documents",
                ["appData"] = @"C:\Users\Test\AppData\Roaming",
                ["localAppData"] = @"C:\Users\Test\AppData\Local",
                ["localLow"] = @"C:\Users\Test\AppData\LocalLow",
                ["savedGames"] = @"C:\Users\Test\Saved Games",
            }.OrderBy(p => p.Key),
            tokens.OrderBy(p => p.Key));
    }

    [Fact]
    public void Windows_local_low_follows_a_redirected_known_folder_and_defaults_to_the_profile()
    {
        var redirected = FakeEnvironment.Windows();
        redirected.KnownFolders[KnownFolder.LocalLow] = @"D:\Redirected\LocalLow";
        var plain = FakeEnvironment.Windows();
        plain.KnownFolders.Remove(KnownFolder.LocalLow);

        Assert.Equal(@"D:\Redirected\LocalLow", new WindowsPlatform(redirected.Build()).PathTokens.GetValueOrDefault("localLow"));
        Assert.Equal(@"C:\Users\Test\AppData\LocalLow", new WindowsPlatform(plain.Build()).PathTokens.GetValueOrDefault("localLow"));
    }

    [Fact]
    public void Windows_documents_and_saved_games_default_to_folders_under_the_profile()
    {
        var env = FakeEnvironment.Windows();
        env.KnownFolders.Clear();

        var tokens = new WindowsPlatform(env.Build()).PathTokens;

        Assert.Equal(@"C:\Users\Test\Documents", tokens.GetValueOrDefault("documents"));
        Assert.Equal(@"C:\Users\Test\Saved Games", tokens.GetValueOrDefault("savedGames"));
    }

    [Fact]
    public void Linux_has_four_tokens_with_xdg_defaults()
    {
        var tokens = new LinuxPlatform(FakeEnvironment.Linux().Build()).PathTokens;

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["userHome"] = "/home/test",
                ["documents"] = "/home/test/Documents",
                ["appData"] = "/home/test/.config",
                ["localAppData"] = "/home/test/.local/share",
            }.OrderBy(p => p.Key),
            tokens.OrderBy(p => p.Key));
    }

    [Fact]
    public void Linux_app_data_tokens_follow_the_xdg_variables()
    {
        var env = FakeEnvironment.Linux();
        env.Variables["XDG_CONFIG_HOME"] = "/custom/config";
        env.Variables["XDG_DATA_HOME"] = "/custom/data";

        var tokens = new LinuxPlatform(env.Build()).PathTokens;

        Assert.Equal("/custom/config", tokens.GetValueOrDefault("appData"));
        Assert.Equal("/custom/data", tokens.GetValueOrDefault("localAppData"));
    }

    [Theory]
    [InlineData("XDG_DOCUMENTS_DIR=\"$HOME/Papers\"", "/home/test/Papers")]
    [InlineData("# comment\n\nXDG_DESKTOP_DIR=\"$HOME/Desk\"\nXDG_DOCUMENTS_DIR=\"/data/docs\"\n", "/data/docs")]
    [InlineData("XDG_DOCUMENTS_DIR=\"$HOME/\"", "/home/test/Documents")]
    [InlineData("XDG_DOCUMENTS_DIR=\"relative/docs\"", "/home/test/Documents")]
    [InlineData("XDG_DESKTOP_DIR=\"$HOME/Desk\"", "/home/test/Documents")]
    public void Linux_documents_come_from_user_dirs_when_present(string fileContent, string expected)
    {
        var env = FakeEnvironment.Linux();
        env.Files["/home/test/.config/user-dirs.dirs"] = fileContent;

        Assert.Equal(expected, new LinuxPlatform(env.Build()).PathTokens.GetValueOrDefault("documents"));
    }

    [Fact]
    public void Linux_reads_user_dirs_from_the_xdg_config_home()
    {
        var env = FakeEnvironment.Linux();
        env.Variables["XDG_CONFIG_HOME"] = "/custom/config";
        env.Files["/custom/config/user-dirs.dirs"] = "XDG_DOCUMENTS_DIR=\"$HOME/Papers\"";

        Assert.Equal("/home/test/Papers", new LinuxPlatform(env.Build()).PathTokens.GetValueOrDefault("documents"));
    }

    [Fact]
    public void MacOS_has_four_tokens_and_both_app_data_tokens_share_application_support()
    {
        var tokens = new MacOSPlatform(FakeEnvironment.MacOS().Build()).PathTokens;

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["userHome"] = "/Users/test",
                ["documents"] = "/Users/test/Documents",
                ["appData"] = "/Users/test/Library/Application Support",
                ["localAppData"] = "/Users/test/Library/Application Support",
            }.OrderBy(p => p.Key),
            tokens.OrderBy(p => p.Key));
    }

    [Theory]
    [InlineData("savedGames")]
    [InlineData("localLow")]
    public void A_token_the_os_lacks_is_absent_on_linux_and_macos(string token)
    {
        Assert.DoesNotContain(token, new LinuxPlatform(FakeEnvironment.Linux().Build()).PathTokens.Keys);
        Assert.DoesNotContain(token, new MacOSPlatform(FakeEnvironment.MacOS().Build()).PathTokens.Keys);
    }
}
