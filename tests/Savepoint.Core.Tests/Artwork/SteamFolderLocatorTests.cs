using Savepoint.Core.Artwork;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.Artwork;

public class SteamFolderLocatorTests
{
    private static Func<string, bool> Existing(params string[] folders) => path => folders.Contains(path);

    [Fact]
    public void Windows_reads_the_steam_path_from_the_registry_value_and_uses_backslashes()
    {
        var locator = new SteamFolderLocator(
            OsKind.Windows,
            @"C:\Users\me",
            () => "c:/program files (x86)/steam",
            Existing(@"c:\program files (x86)\steam"));

        Assert.Equal(@"c:\program files (x86)\steam", locator.FindSteamFolder());
    }

    [Fact]
    public void Windows_without_the_registry_value_finds_nothing()
    {
        Assert.Null(new SteamFolderLocator(OsKind.Windows, @"C:\Users\me", () => null, _ => true).FindSteamFolder());
        Assert.Null(new SteamFolderLocator(OsKind.Windows, @"C:\Users\me", null, _ => true).FindSteamFolder());
    }

    [Fact]
    public void Windows_with_a_registry_value_for_a_folder_that_is_gone_finds_nothing()
    {
        var locator = new SteamFolderLocator(OsKind.Windows, @"C:\Users\me", () => "d:/gone/steam", _ => false);

        Assert.Null(locator.FindSteamFolder());
    }

    [Fact]
    public void Windows_ignores_the_home_folder_places_other_systems_use()
    {
        var home = @"C:\Users\me";
        var locator = new SteamFolderLocator(OsKind.Windows, home, () => null, path => path.StartsWith(home, StringComparison.Ordinal));

        Assert.Null(locator.FindSteamFolder());
    }

    [Fact]
    public void A_registry_read_that_fails_finds_nothing_instead_of_throwing()
    {
        var locator = new SteamFolderLocator(OsKind.Windows, @"C:\Users\me", () => throw new IOException("registry unavailable"), _ => true);

        Assert.Null(locator.FindSteamFolder());
    }

    [Fact]
    public void Linux_looks_in_the_three_usual_places_in_order()
    {
        var home = Path.Combine("/home", "me");
        var dotSteam = Path.Combine(home, ".steam", "steam");
        var share = Path.Combine(home, ".local", "share", "Steam");
        var flatpak = Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");

        Assert.Equal(dotSteam, new SteamFolderLocator(OsKind.Linux, home, null, Existing(dotSteam, share, flatpak)).FindSteamFolder());
        Assert.Equal(share, new SteamFolderLocator(OsKind.Linux, home, null, Existing(share, flatpak)).FindSteamFolder());
        Assert.Equal(flatpak, new SteamFolderLocator(OsKind.Linux, home, null, Existing(flatpak)).FindSteamFolder());
        Assert.Null(new SteamFolderLocator(OsKind.Linux, home, null, Existing()).FindSteamFolder());
    }

    [Fact]
    public void Linux_finds_the_snap_package_of_steam_after_the_other_places()
    {
        var home = Path.Combine("/home", "me");
        var share = Path.Combine(home, ".local", "share", "Steam");
        var snap = Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam");

        Assert.Equal(snap, new SteamFolderLocator(OsKind.Linux, home, null, Existing(snap)).FindSteamFolder());
        Assert.Equal(share, new SteamFolderLocator(OsKind.Linux, home, null, Existing(share, snap)).FindSteamFolder());
    }

    [Fact]
    public void Linux_does_not_read_the_registry()
    {
        var read = false;
        var locator = new SteamFolderLocator(OsKind.Linux, "/home/me", () =>
        {
            read = true;
            return "/registry";
        }, _ => false);

        Assert.Null(locator.FindSteamFolder());
        Assert.False(read);
    }

    [Fact]
    public void MacOS_looks_in_application_support()
    {
        var home = Path.Combine("/Users", "me");
        var steam = Path.Combine(home, "Library", "Application Support", "Steam");

        Assert.Equal(steam, new SteamFolderLocator(OsKind.MacOS, home, null, Existing(steam)).FindSteamFolder());
        Assert.Null(new SteamFolderLocator(OsKind.MacOS, home, null, Existing(Path.Combine(home, ".steam", "steam"))).FindSteamFolder());
    }

    [Fact]
    public void Real_folders_are_found_when_no_existence_check_is_given()
    {
        using var folder = new TempFolder();
        var steam = Path.Combine(folder.Path, ".local", "share", "Steam");
        Directory.CreateDirectory(steam);

        Assert.Equal(steam, new SteamFolderLocator(OsKind.Linux, folder.Path).FindSteamFolder());
        Assert.Null(new SteamFolderLocator(OsKind.MacOS, folder.Path).FindSteamFolder());
    }

    [Fact]
    public void The_platform_locator_looks_in_the_platform_s_home_folder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows asks the registry, which belongs to the real machine.");
        using var folder = new TempFolder();
        var platform = Savepoint.Core.Tests.Platform.FakePlatforms.ForHost(folder.Path);
        var steam = platform.Os == OsKind.Linux
            ? Path.Combine(folder.Path, ".local", "share", "Steam")
            : Path.Combine(folder.Path, "Library", "Application Support", "Steam");
        Directory.CreateDirectory(steam);

        Assert.Equal(steam, SteamFolderLocator.ForPlatform(platform).FindSteamFolder());
    }
}
