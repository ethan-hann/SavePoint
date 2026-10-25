using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Savepoint.App.Platform;

namespace Savepoint.App.Tests.Platform;

public class AppIconTests
{
    [Fact]
    public void Windows_windows_use_the_multi_size_ico()
    {
        Assert.EndsWith("/Assets/Icon/savepoint.ico", AppIcon.ResourceUri(windows: true).AbsolutePath);
    }

    [Fact]
    public void Other_platforms_use_the_large_png()
    {
        Assert.EndsWith("/Assets/Icon/savepoint-256.png", AppIcon.ResourceUri(windows: false).AbsolutePath);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_icon_resource_ships_inside_the_app(bool windows)
    {
        Assert.True(AssetLoader.Exists(AppIcon.ResourceUri(windows)));
    }

    [AvaloniaFact]
    public void The_icon_loads_for_this_platform()
    {
        Assert.NotNull(AppIcon.Load());
    }
}
