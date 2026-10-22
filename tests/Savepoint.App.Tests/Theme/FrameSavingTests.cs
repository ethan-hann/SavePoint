using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Savepoint.App.Tests.Theme;

// Avalonia encodes a saved frame from the frame's own pixels without holding the frame, and a collected frame frees them. A collection
// on another thread in the middle of a save then crashes the test host, so every save goes through ThemeTestSupport.SaveFrame.
public class FrameSavingTests
{
    private const BindingFlags _declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [AvaloniaFact]
    public void A_frame_is_written_whole_while_collections_run_on_another_thread()
    {
        using var host = new ThemeHost(new Border { Background = Brushes.Teal }, width: 1040, height: 660);
        var path = Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "frame-saving", "collected.png");
        using var stop = new CancellationTokenSource();
        var collector = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                GC.Collect(1);
                GC.WaitForPendingFinalizers();
            }
        })
        { IsBackground = true };
        collector.Start();
        try
        {
            // Enough saves for the save path to be recompiled with optimizations, which is when a frame stops being held by its caller.
            for (var i = 0; i < 150; i++)
            {
                ThemeTestSupport.SaveFrame(host.Window, path);
            }
        }
        finally
        {
            stop.Cancel();
            collector.Join();
        }

        Assert.True(new FileInfo(path).Length > 0);
    }

    [Fact]
    public void Only_the_shared_helper_saves_a_bitmap()
    {
        var helper = $"{typeof(ThemeTestSupport).FullName}.{nameof(ThemeTestSupport.SaveFrame)}";
        var savers = typeof(FrameSavingTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(_declared).Cast<MethodBase>().Concat(type.GetConstructors(_declared)))
            .Where(method => MethodCalls.In(method).Any(called => called.Name == nameof(Bitmap.Save) && typeof(Bitmap).IsAssignableFrom(called.DeclaringType)))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .Distinct()
            .ToArray();
        var others = savers.Where(saver => saver != helper).ToArray();

        Assert.Contains(helper, savers);
        Assert.True(others.Length == 0, $"Frames saved outside {helper}: {string.Join(", ", others)}");
    }
}
