using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace Savepoint.App.Tests.Theme;

// Checks that nothing inside a rounded container paints past its rounded corners. The container is rendered as it is and again with every
// descendant's fill removed; in the ring where its border stroke and the page behind it meet, the two renders must agree, so a child
// whose square fill covers the stroke or reaches the page shows up as a difference.
internal sealed class CornerProbe : IDisposable
{
    private readonly WriteableBitmap _frame;
    private readonly byte[] _pixels;
    private readonly int _stride;

    private CornerProbe(WriteableBitmap frame, byte[] pixels, int stride)
    {
        _frame = frame;
        _pixels = pixels;
        _stride = stride;
    }

    public static CornerProbe Capture(TopLevel window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was rendered.");
        var size = frame.PixelSize;
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        return new CornerProbe(frame, pixels, stride);
    }

    // Renders the container as it is, then with every descendant's fill removed, and returns what differs in the corner rings.
    // Empty means every corner is clean. The fills are not put back: use this last in a test.
    public static IReadOnlyList<string> RingFailures(TopLevel window, Control container, string savePath)
    {
        using var actual = Capture(window);
        actual.Save(savePath);
        RemoveFills(container);
        using var bare = Capture(window);
        return actual.Differences(bare, container, window);
    }

    public string Save(string path) => ThemeTestSupport.SaveFrame(_frame, path);

    public void Dispose() => _frame.Dispose();

    private static void RemoveFills(Control container)
    {
        foreach (var descendant in container.GetVisualDescendants())
        {
            switch (descendant)
            {
                case Border border:
                    border.Background = Brushes.Transparent;
                    break;
                case TemplatedControl templated:
                    templated.Background = Brushes.Transparent;
                    break;
            }
        }
    }

    private List<string> Differences(CornerProbe bare, Control container, TopLevel window)
    {
        var origin = container.TranslatePoint(default, window) ?? throw new InvalidOperationException("The container is not on screen.");
        var left = (int)Math.Round(origin.X);
        var top = (int)Math.Round(origin.Y);
        var width = (int)Math.Round(container.Bounds.Width);
        var height = (int)Math.Round(container.Bounds.Height);
        var radii = container switch
        {
            Border border => border.CornerRadius,
            TemplatedControl templated => templated.CornerRadius,
            _ => throw new InvalidOperationException("The container has no corner radius."),
        };
        var failures = new List<string>();
        Corner("top-left", radii.TopLeft, left, top, 1, 1);
        Corner("top-right", radii.TopRight, left + width - 1, top, -1, 1);
        Corner("bottom-left", radii.BottomLeft, left, top + height - 1, 1, -1);
        Corner("bottom-right", radii.BottomRight, left + width - 1, top + height - 1, -1, -1);
        return failures;

        void Corner(string name, double radius, int cornerX, int cornerY, int stepX, int stepY)
        {
            if (radius < 1)
            {
                return;
            }

            // The pixels from the stroke's outer half outwards: a child's fill is clipped inside the stroke, so only a fill past it reaches here.
            var reach = (int)Math.Ceiling(radius);
            for (var dy = 0; dy < reach; dy++)
            {
                for (var dx = 0; dx < reach; dx++)
                {
                    var fromCenter = Math.Sqrt(Math.Pow(radius - dx - 0.5, 2) + Math.Pow(radius - dy - 0.5, 2));
                    if (fromCenter < radius - 0.5)
                    {
                        continue;
                    }

                    var x = cornerX + (dx * stepX);
                    var y = cornerY + (dy * stepY);
                    var a = At(x, y);
                    var b = bare.At(x, y);
                    if (!Near(a, b))
                    {
                        failures.Add($"{name} corner pixel ({dx},{dy}) is {Hex(a)}, it is {Hex(b)} without the container's fills");
                    }
                }
            }
        }
    }

    private Color At(int x, int y)
    {
        var offset = (y * _stride) + (x * 4);
        return _frame.Format == PixelFormat.Bgra8888
            ? Color.FromArgb(_pixels[offset + 3], _pixels[offset + 2], _pixels[offset + 1], _pixels[offset])
            : Color.FromArgb(_pixels[offset + 3], _pixels[offset], _pixels[offset + 1], _pixels[offset + 2]);
    }

    private static bool Near(Color a, Color b) =>
        Math.Abs(a.R - b.R) <= 3 && Math.Abs(a.G - b.G) <= 3 && Math.Abs(a.B - b.B) <= 3;

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
