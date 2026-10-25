using Savepoint.Core.Artwork;
using Savepoint.Core.Tests.Services;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

// The layout is Galaxy's own: webcache/<account id>/gog/<product id>/<64 hex digits>_glx_<name>.webp.
public class GogLocalCacheTests
{
    private const string _product = "1423049311";
    private const string _account = "53949204526928523";
    private const string _hash = "8b37ef624d6f8a4092c43daed92660cde499434f8c0a2078f342bc34f4e752f2";

    private static string Write(TempFolder cache, string account, string product, string suffix, byte[]? content = null, DateTime? written = null)
    {
        var path = cache.WriteBytesWith($"{account}/gog/{product}/{_hash}{suffix}", content ?? TestImages.WebPLossy(342, 482));
        if (written is { } stamp)
        {
            File.SetLastWriteTimeUtc(path, stamp);
        }

        return path;
    }

    [Fact]
    public void The_vertical_cover_is_the_tall_image_and_the_background_is_the_wide_one()
    {
        using var cache = new TempFolder();
        var cover = Write(cache, _account, _product, "_glx_vertical_cover.webp");
        var background = Write(cache, _account, _product, "_glx_bg_top_padding_7.webp", TestImages.WebPLossy(1600, 650));
        Write(cache, _account, _product, "_glx_square_icon_v2.webp", TestImages.WebPLossy(112, 112));

        Assert.Equal(cover, GogLocalCache.Find(cache.Path, _product, ArtworkKind.Square));
        Assert.Equal(background, GogLocalCache.Find(cache.Path, _product, ArtworkKind.Wide));
    }

    [Fact]
    public void The_small_icon_is_never_taken_as_either_image()
    {
        using var cache = new TempFolder();
        Write(cache, _account, _product, "_glx_square_icon_v2.webp", TestImages.WebPLossy(112, 112));

        Assert.Null(GogLocalCache.Find(cache.Path, _product, ArtworkKind.Square));
        Assert.Null(GogLocalCache.Find(cache.Path, _product, ArtworkKind.Wide));
    }

    [Fact]
    public void Another_products_images_are_not_used()
    {
        using var cache = new TempFolder();
        Write(cache, _account, "1256837418", "_glx_vertical_cover.webp");

        Assert.Null(GogLocalCache.Find(cache.Path, _product, ArtworkKind.Square));
    }

    [Fact]
    public void Folders_that_are_not_an_account_are_not_searched()
    {
        using var cache = new TempFolder();
        Write(cache, "1423049311-overlay", _product, "_glx_vertical_cover.webp");
        Write(cache, "common", _product, "_glx_vertical_cover.webp");

        Assert.Null(GogLocalCache.Find(cache.Path, _product, ArtworkKind.Square));
    }

    [Fact]
    public void The_newest_image_wins_across_accounts()
    {
        using var cache = new TempFolder();
        Write(cache, "111", _product, "_glx_vertical_cover.webp", written: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newest = Write(cache, "222", _product, "_glx_vertical_cover.webp", written: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(newest, GogLocalCache.Find(cache.Path, _product, ArtworkKind.Square));
    }

    [Fact]
    public void A_file_that_is_not_a_whole_image_is_passed_over_for_an_older_good_one()
    {
        using var cache = new TempFolder();
        var good = Write(cache, "111", _product, "_glx_vertical_cover.webp", written: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Write(cache, "222", _product, "_glx_vertical_cover.webp", [1, 2, 3, 4], new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(good, GogLocalCache.Find(cache.Path, _product, ArtworkKind.Square));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("12/34")]
    [InlineData("abc")]
    [InlineData("../1423049311")]
    public void A_product_id_that_is_not_digits_finds_nothing_and_never_leaves_the_cache(string product)
    {
        using var cache = new TempFolder();
        Write(cache, _account, _product, "_glx_vertical_cover.webp");

        // A file where a path-shaped id would land if it were joined into the folder path.
        cache.WriteBytesWith($"{_account}/{_product}/{_hash}_glx_vertical_cover.webp", TestImages.WebPLossy(342, 482));

        Assert.Null(GogLocalCache.Find(cache.Path, product, ArtworkKind.Square));
    }

    [Fact]
    public void A_cache_folder_that_does_not_exist_finds_nothing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "savepoint-gogcache-" + Guid.NewGuid().ToString("N"));

        Assert.Null(GogLocalCache.Find(missing, _product, ArtworkKind.Square));
    }
}
