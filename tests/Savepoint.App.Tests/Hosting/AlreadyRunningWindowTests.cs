using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Hosting;
using Savepoint.App.Localization;

namespace Savepoint.App.Tests.Hosting;

public class AlreadyRunningWindowTests
{
    [AvaloniaFact]
    public void The_notice_says_the_app_is_already_running_in_the_catalogs_words()
    {
        var window = new AlreadyRunningWindow(new UiFormatter(LocalizationCatalog.Current));
        window.Show();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToList();

        Assert.Equal("SavePoint", window.Title);
        Assert.Contains("SavePoint is already running", texts);
        Assert.Contains(texts, text => text!.Contains("only one copy", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void The_notice_wears_the_app_icon()
    {
        var window = new AlreadyRunningWindow(new UiFormatter(LocalizationCatalog.Current));

        Assert.NotNull(window.Icon);
    }

    [AvaloniaFact]
    public void The_ok_button_closes_the_notice()
    {
        var window = new AlreadyRunningWindow(new UiFormatter(LocalizationCatalog.Current));
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();

        var ok = window.GetVisualDescendants().OfType<Button>().Single();
        ok.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("OK", ok.Content);
        Assert.True(closed);
    }

    [Fact]
    public void The_notice_is_brief()
    {
        Assert.InRange(AlreadyRunningWindow.Lifetime, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(15));
    }
}
