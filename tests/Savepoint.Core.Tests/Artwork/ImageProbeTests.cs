using Savepoint.Core.Artwork;
using Savepoint.Core.Tests.Services;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

public class ImageProbeTests
{
    private static ImageInfo? Probe(byte[] content)
    {
        using var stream = new MemoryStream(content);
        return ImageProbe.Read(stream);
    }

    [Fact]
    public void A_png_reports_its_format_and_size()
    {
        Assert.Equal(new ImageInfo(ImageFormat.Png, 460, 215), Probe(TestImages.Png(460, 215)));
    }

    [Fact]
    public void A_jpeg_reports_its_format_and_size()
    {
        Assert.Equal(new ImageInfo(ImageFormat.Jpeg, 600, 900), Probe(TestImages.Jpeg(600, 900)));
    }

    [Fact]
    public void A_progressive_jpeg_with_a_large_block_before_the_frame_still_reports_its_size()
    {
        Assert.Equal(new ImageInfo(ImageFormat.Jpeg, 1920, 1080), Probe(TestImages.Jpeg(1920, 1080, frameMarker: 0xC2, padding: 40_000)));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(460, 215)]
    [InlineData(8000, 6000)]
    public void A_lossless_webp_reports_its_size(int width, int height)
    {
        Assert.Equal(new ImageInfo(ImageFormat.WebP, width, height), Probe(TestImages.WebPLossless(width, height)));
    }

    [Fact]
    public void A_lossy_webp_reports_its_size()
    {
        Assert.Equal(new ImageInfo(ImageFormat.WebP, 640, 360), Probe(TestImages.WebPLossy(640, 360)));
    }

    [Fact]
    public void An_extended_webp_reports_the_size_of_its_canvas()
    {
        Assert.Equal(new ImageInfo(ImageFormat.WebP, 2000, 1000), Probe(TestImages.WebPExtended(2000, 1000)));
    }

    [Fact]
    public void A_png_cut_short_is_not_an_image()
    {
        var whole = TestImages.Png(460, 215);

        Assert.Null(Probe(whole[..^20]));
        Assert.Null(Probe(whole[..40]));
    }

    [Fact]
    public void A_jpeg_cut_short_is_not_an_image()
    {
        var whole = TestImages.Jpeg(600, 900);

        Assert.Null(Probe(whole[..^2]));
        Assert.Null(Probe(whole[..30]));
    }

    [Fact]
    public void A_webp_cut_short_is_not_an_image()
    {
        var whole = TestImages.WebPLossless(460, 215);

        Assert.Null(Probe(whole[..^3]));
        Assert.Null(Probe(whole[..14]));
    }

    [Fact]
    public void A_png_whose_header_was_damaged_is_not_an_image()
    {
        var damaged = TestImages.Png(460, 215);
        damaged[18] ^= 0x01;

        Assert.Null(Probe(damaged));
    }

    [Fact]
    public void A_png_with_extra_bytes_after_its_end_is_not_an_image()
    {
        Assert.Null(Probe([.. TestImages.Png(10, 10), .. new byte[] { 1, 2, 3, 4, 5 }]));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public void An_image_with_no_pixels_is_not_an_image(int width, int height)
    {
        Assert.Null(Probe(TestImages.PngWithHeaderOnly(width, height)));
    }

    [Theory]
    [InlineData(70_000, 100)]
    [InlineData(100, 70_000)]
    [InlineData(20_000, 20_000)]
    public void An_image_of_an_absurd_size_is_not_an_image(int width, int height)
    {
        Assert.Null(Probe(TestImages.PngWithHeaderOnly(width, height)));
    }

    [Fact]
    public void Other_files_are_not_images()
    {
        Assert.Null(Probe(TestImages.Html()));
        Assert.Null(Probe(TestImages.Gif()));
        Assert.Null(Probe([]));
        Assert.Null(Probe([0xFF]));
        Assert.Null(Probe(new byte[4096]));
    }

    [Fact]
    public void A_file_is_read_from_its_first_byte_even_when_the_stream_was_already_used()
    {
        using var stream = new MemoryStream(TestImages.Png(30, 20));
        stream.Position = stream.Length;

        Assert.Equal(new ImageInfo(ImageFormat.Png, 30, 20), ImageProbe.Read(stream));
    }

    [Fact]
    public void A_file_on_disk_is_read_and_a_missing_or_locked_one_is_not_an_image()
    {
        using var folder = new TempFolder();
        var path = TestImages.Write(folder.Path, "art.png", TestImages.Png(30, 20));

        Assert.Equal(new ImageInfo(ImageFormat.Png, 30, 20), ImageProbe.ReadFile(path));
        Assert.Null(ImageProbe.ReadFile(Path.Combine(folder.Path, "missing.png")));
        Assert.Null(ImageProbe.ReadFile(folder.Path));
    }

    [Fact]
    public void The_extension_does_not_decide_what_a_file_is()
    {
        using var folder = new TempFolder();
        var path = TestImages.Write(folder.Path, "photo.txt", TestImages.Jpeg(30, 20));
        var disguised = TestImages.Write(folder.Path, "page.png", TestImages.Html());

        Assert.Equal(new ImageInfo(ImageFormat.Jpeg, 30, 20), ImageProbe.ReadFile(path));
        Assert.Null(ImageProbe.ReadFile(disguised));
    }
}
