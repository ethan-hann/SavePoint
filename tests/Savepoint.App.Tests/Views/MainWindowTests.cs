using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Views;

namespace Savepoint.App.Tests.Views;

public class MainWindowTests
{
    [AvaloniaFact]
    public void Heading_shows_the_catalog_text()
    {
        var window = new MainWindow();
        window.Show();

        var heading = window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "HeadingText");

        Assert.Equal("Keep the setup that works", heading?.Text);
    }

    [AvaloniaFact]
    public void Title_comes_from_the_catalog()
    {
        var window = new MainWindow();

        Assert.Equal("SavePoint", window.Title);
    }

    [AvaloniaFact]
    public void Window_has_the_default_and_minimum_sizes()
    {
        var window = new MainWindow();

        Assert.Equal(1040, window.Width);
        Assert.Equal(660, window.Height);
        Assert.Equal(760, window.MinWidth);
        Assert.Equal(520, window.MinHeight);
    }
}
