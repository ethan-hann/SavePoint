using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Savepoint.App.Theme;
using Savepoint.App.Theme.Material;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme;

internal sealed class FakeSystemThemeSource(bool isDark) : ISystemThemeSource
{
    public bool IsDark { get; private set; } = isDark;

    public event EventHandler? Changed;

    public void Set(bool isDark)
    {
        IsDark = isDark;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

// Avalonia seals its platform settings against user subclasses, so the real default implementation is driven by reflection.
internal sealed class FakePlatformSettings
{
    private static readonly Type _type = typeof(DefaultPlatformSettings);

    public FakePlatformSettings() =>
        Settings = (IPlatformSettings)Activator.CreateInstance(_type, nonPublic: true)!;

    public IPlatformSettings Settings { get; }

    public void SetThemeVariant(PlatformThemeVariant variant)
    {
        var raise = _type.GetMethod("OnColorValuesChanged", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("DefaultPlatformSettings no longer has OnColorValuesChanged.");
        raise.Invoke(Settings, [new PlatformColorValues { ThemeVariant = variant }]);
    }
}
internal sealed class ThemeHarness
{
    public ThemeHarness(bool systemIsDark = false)
    {
        System = new FakeSystemThemeSource(systemIsDark);
        Resources = new ResourceDictionary();
        Service = new ThemeService(Resources, System, variant => Variants.Add(variant));
    }

    public FakeSystemThemeSource System { get; }

    public ResourceDictionary Resources { get; }

    public ThemeService Service { get; }

    public List<ThemeVariant> Variants { get; } = [];

    public Color ColorOf(string role) => Assert.IsType<Color>(Value(ThemeKeys.Color(role)));

    public SolidColorBrush BrushOf(string role) => Assert.IsType<SolidColorBrush>(Value(ThemeKeys.Brush(role)));

    public object Value(string key)
    {
        Assert.True(Resources.TryGetResource(key, null, out var value), $"No resource '{key}'.");
        return value!;
    }
}

internal static class ThemeTestSupport
{
    public static Color ToColor(uint argb) => Color.FromUInt32(argb);

    // Every palette role under the names the service publishes, so tests iterate the palette and miss nothing.
    public static IReadOnlyDictionary<string, uint> PaletteRoles(AccentColorId accent, bool dark) =>
        Md3Palette.Build(AccentSeeds.For(accent), dark).ToRoleMap()
            .ToDictionary(pair => ThemeKeys.RoleName(pair.Key), pair => pair.Value);

    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Savepoint.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Savepoint.slnx was not found above the test output folder.");
    }

    public static Color PixelAt(WriteableBitmap frame, int x, int y)
    {
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

        var offset = (y * stride) + (x * 4);
        // Opaque pixels only: premultiplied alpha is then a no-op.
        return frame.Format == PixelFormat.Bgra8888
            ? Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset])
            : Color.FromArgb(pixels[offset + 3], pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }

    public static WriteableBitmap Render(Window window)
    {
        window.Show();
        return window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window produced no frame.");
    }
}
