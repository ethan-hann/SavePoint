using Savepoint.Core.Artwork;
using Savepoint.Core.Tests.Services;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

public class SteamLocalCacheTests
{
    private const int _app = 489830;

    private static string Cache(TempFolder steam, params string[] parts) =>
        steam.Combine(["appcache", "librarycache", .. parts]);

    private static string Write(TempFolder steam, string relative, byte[]? content = null, DateTime? written = null)
    {
        var path = steam.WriteBytesWith("appcache/librarycache/" + relative, content ?? TestImages.Jpeg(460, 215));
        if (written is { } stamp)
        {
            File.SetLastWriteTimeUtc(path, stamp);
        }

        return path;
    }

    [Fact]
    public void An_image_straight_in_the_apps_folder_is_found_for_each_kind()
    {
        using var steam = new TempFolder();
        var wide = Write(steam, $"{_app}/header.jpg");
        var square = Write(steam, $"{_app}/library_600x900.jpg", TestImages.Jpeg(600, 900));

        Assert.Equal(wide, SteamLocalCache.Find(steam.Path, _app, ArtworkKind.Wide));
        Assert.Equal(square, SteamLocalCache.Find(steam.Path, _app, ArtworkKind.Square));
    }

    [Fact]
    public void An_image_in_a_subfolder_of_the_apps_folder_is_found_at_any_depth()
    {
        using var steam = new TempFolder();
        var wide = Write(steam, $"{_app}/d41d8cd98f00b204e9800998ecf8427e/header.jpg");
        var square = Write(steam, $"{_app}/a/b/library_600x900.jpg", TestImages.Jpeg(600, 900));

        Assert.Equal(wide, SteamLocalCache.Find(steam.Path, _app, ArtworkKind.Wide));
        Assert.Equal(square, SteamLocalCache.Find(steam.Path, _app, ArtworkKind.Square));
    }

    [Fact]
    public void The_older_flat_names_are_used_when_the_apps_folder_has_nothing()
    {
        using var steam = new TempFolder();
        var wide = Write(steam, $"{_app}_header.jpg");
        var square = Write(steam, $"{_app}_library_600x900.jpg", TestImages.Jpeg(600, 900));

        Assert.Equal(wide, SteamLocalCache.Find(steam.Path, _app, ArtworkKind.Wide));
        Assert.Equal(square, SteamLocalCache.Find(steam.Path, _app, ArtworkKind.Square));
    }

    [Fact]
    public void The_apps_folder_wins_over_the_older_flat_name()
    {
        using var steam = new TempFolder();
        Write(steam, $"{_app}_header.jpg");
        var folderImage = Write(steam, $"{_app}/abc/header.jpg");

        Assert.Equal(folderImage, SteamLocalCache.Find(steam.Path, _app, ArtworkKind.Wide));
    }

    [Fact]
    public void Of_several_copies_the_most_recently_written_one_is_used()
    {
        using var steam = new TempFolder();
        Write(steam, $"{_app}/old/header.jpg", written: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newest = Write(steam, $"{_app}/new/header.jpg", written: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Write(steam, $"{_app}/middle/header.jpg", written: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(newest, SteamLocalCache.Find(steam.Path, _app, ArtworkKind.Wide));
    }

    [Fact]
    public void A_file_that_is_not_a_whole_image_is_skipped_for_the_next_best_one()
    {
        using var steam = new TempFolder();
        var good = Write(steam, $"{_app}/old/header.jpg", written: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Write(steam, $"{_app}/new/header.jpg", TestImages.Html(), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Write(steam, $"{_app}/newer/header.jpg", TestImages.Jpeg(460, 215)[..30], new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(good, SteamLocalCache.Find(steam.Path, _app, ArtworkKind.Wide));
    }

    [Fact]
    public void Nothing_is_found_when_the_app_has_no_such_image_or_Steam_has_no_cache()
    {
        using var steam = new TempFolder();
        Write(steam, $"{_app}/header.jpg");

        Assert.Null(SteamLocalCache.Find(steam.Path, _app, ArtworkKind.Square));
        Assert.Null(SteamLocalCache.Find(steam.Path, _app + 1, ArtworkKind.Wide));
        Assert.Null(SteamLocalCache.Find(steam.Combine("not-steam"), _app, ArtworkKind.Wide));
    }

    [Fact]
    public void Another_apps_images_are_never_used_even_when_the_ids_share_digits()
    {
        using var steam = new TempFolder();
        Write(steam, "4898300/header.jpg");
        Write(steam, "48983/header.jpg");
        Write(steam, "4898300_header.jpg");
        Write(steam, "x489830_header.jpg");
        Write(steam, $"{_app}x/header.jpg");

        Assert.Null(SteamLocalCache.Find(steam.Path, _app, ArtworkKind.Wide));
    }

    [Fact]
    public void A_folder_named_like_an_image_is_not_an_image()
    {
        using var steam = new TempFolder();
        Directory.CreateDirectory(Cache(steam, _app.ToString(), "header.jpg"));

        Assert.Null(SteamLocalCache.Find(steam.Path, _app, ArtworkKind.Wide));
    }

    [Fact]
    public void Looking_changes_nothing_in_the_steam_folder()
    {
        using var steam = new TempFolder();
        var wide = Write(steam, $"{_app}/header.jpg");
        var stamp = File.GetLastWriteTimeUtc(wide);

        SteamLocalCache.Find(steam.Path, _app, ArtworkKind.Wide);

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(wide));
        Assert.Single(Directory.EnumerateFiles(steam.Path, "*", SearchOption.AllDirectories));
    }
}
