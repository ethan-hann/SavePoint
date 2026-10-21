using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Savepoint.Testing;
using SkiaSharp;

namespace Savepoint.App.Tests.Features.Common;

// Image files for the artwork tests, written into a throwaway folder: real PNGs and JPEGs any decoder reads, and a JPEG that has the
// structure of one but no pixels, which a decoder refuses.
internal sealed class ArtFixtures : IDisposable
{
    public ArtFixtures()
    {
        Folder = Directory.CreateTempSubdirectory("savepoint-art-").FullName;
    }

    public string Folder { get; }

    public string Png(string name, int width, int height, uint argb = 0xFF336699) =>
        TestImages.Write(Folder, name, TestImages.Png(width, height, argb));

    public string Jpeg(string name, int width, int height, uint argb = 0xFF994433)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, 0xFF));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return TestImages.Write(Folder, name, data.ToArray());
    }

    // A PNG whose middle half is one color and whose sides are another, so a test can tell what part of the image a crop kept.
    public string PngBands(string name, int width, int height, uint sides, uint middle)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor((byte)(sides >> 16), (byte)(sides >> 8), (byte)sides, 0xFF));
            using var paint = new SKPaint { Color = new SKColor((byte)(middle >> 16), (byte)(middle >> 8), (byte)middle, 0xFF) };
            canvas.DrawRect(width / 4, 0, width / 2, height, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return TestImages.Write(Folder, name, data.ToArray());
    }

    // Whole by the structure check, undecodable by a real decoder.
    public string BrokenJpeg(string name, int width, int height) =>
        TestImages.Write(Folder, name, TestImages.Jpeg(width, height));

    public string NotAnImage(string name) =>
        TestImages.Write(Folder, name, TestImages.Html());

    public static WriteableBitmap TinyBitmap() =>
        new(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A bitmap still holding the file is not worth failing a test over.
        }
    }
}
