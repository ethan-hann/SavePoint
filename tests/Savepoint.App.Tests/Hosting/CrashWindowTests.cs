using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Hosting;
using Savepoint.App.Localization;

namespace Savepoint.App.Tests.Hosting;

public class CrashWindowTests
{
    private const string _logFolder = @"C:\Users\Sam\AppData\Local\SavePoint\Logs";

    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }

    private static CrashWindow Open(string? folder, Func<string, bool>? openFolder = null, Exception? crash = null)
    {
        var window = new CrashWindow(
            new UiFormatter(LocalizationCatalog.Current),
            new CrashReport(crash ?? Thrown("the library is gone"), "the UI thread", folder),
            openFolder ?? (_ => true));
        window.Show();
        return window;
    }

    private static T Named<T>(Window window, string name)
        where T : Control
    {
        var found = window.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);
        Assert.True(found is not null, $"The window has no {typeof(T).Name} named {name}.");
        return found;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static List<string?> Texts(Window window) => [.. window.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text)];

    [AvaloniaFact]
    public void The_window_says_savepoint_has_to_close_and_where_the_details_went()
    {
        var window = Open(_logFolder);

        var texts = Texts(window);

        Assert.Equal("SavePoint", window.Title);
        Assert.Contains("SavePoint ran into a problem and has to close", texts);
        Assert.Contains(texts, text => text!.Contains("send the newest file from the log folder", StringComparison.Ordinal));
        Assert.Contains($"Log folder: {_logFolder}", texts);
        Assert.Equal("Close SavePoint", Named<Button>(window, "CloseButton").Content);
    }

    [AvaloniaFact]
    public void The_details_are_hidden_until_asked_for_and_then_show_the_exception()
    {
        var crash = Thrown("the library is gone");
        var window = Open(_logFolder, crash: crash);
        var toggle = Named<Button>(window, "DetailsButton");
        var details = Named<TextBox>(window, "DetailsText");

        Assert.False(details.IsEffectivelyVisible);
        Assert.Equal("Show details", toggle.Content);

        Click(toggle);

        Assert.True(details.IsEffectivelyVisible);
        Assert.Equal("Hide details", toggle.Content);
        Assert.Contains("System.InvalidOperationException: the library is gone", details.Text, StringComparison.Ordinal);
        Assert.True(details.IsReadOnly);

        Click(toggle);

        Assert.False(details.IsEffectivelyVisible);
        Assert.Equal("Show details", toggle.Content);
    }

    [AvaloniaFact]
    public void The_log_folder_button_opens_the_folder_and_says_so_when_it_cannot()
    {
        var opened = new List<string>();
        var works = true;
        var window = Open(_logFolder, folder =>
        {
            opened.Add(folder);
            return works;
        });
        var button = Named<Button>(window, "OpenLogFolderButton");
        var failure = Named<TextBlock>(window, "OpenFailedText");

        Click(button);

        Assert.Equal("Open the log folder", button.Content);
        Assert.Equal([_logFolder], opened);
        Assert.False(failure.IsEffectivelyVisible);

        works = false;
        Click(button);

        Assert.True(failure.IsEffectivelyVisible);
        Assert.Equal("SavePoint couldn't open the log folder.", failure.Text);
    }

    [AvaloniaFact]
    public void Without_a_log_folder_the_window_offers_no_folder_and_says_the_details_were_not_saved()
    {
        var window = Open(null);

        var texts = Texts(window);

        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.Name == "OpenLogFolderButton");
        Assert.DoesNotContain(texts, text => text!.StartsWith("Log folder:", StringComparison.Ordinal));
        Assert.Contains(texts, text => text!.Contains("couldn't save the details", StringComparison.Ordinal));
        Assert.Contains("SavePoint ran into a problem and has to close", texts);
    }

    [AvaloniaFact]
    public void The_close_button_closes_the_window()
    {
        var window = Open(_logFolder);
        var closed = false;
        window.Closed += (_, _) => closed = true;

        Click(Named<Button>(window, "CloseButton"));

        Assert.True(closed);
    }

    [AvaloniaFact]
    public void Every_button_is_named_for_assistive_technology()
    {
        var window = Open(_logFolder);

        var problems = window.GetVisualDescendants().OfType<Button>()
            .Where(button => string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(button)))
            .Select(button => button.Name)
            .ToList();

        Assert.Empty(problems);
    }
}
