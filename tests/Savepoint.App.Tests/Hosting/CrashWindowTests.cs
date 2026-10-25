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

    private static CrashWindow Open(string? folder, Func<string, bool>? openFolder = null, Exception? crash = null, Func<string, Task>? copyText = null)
    {
        var window = new CrashWindow(
            new UiFormatter(LocalizationCatalog.Current),
            new CrashReport(crash ?? Thrown("the library is gone"), "the UI thread", folder),
            openFolder ?? (_ => true),
            copyText,
            "9.9.9+abc1234");
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

    // Without it the desktop shows the generic window icon over the crash message.
    [AvaloniaFact]
    public void The_window_carries_the_app_icon()
    {
        var window = Open(_logFolder);

        Assert.NotNull(window.Icon);
    }

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

    // The raw text is for support only: the default view says what happened in plain words.
    [AvaloniaFact]
    public void Before_Show_details_nothing_on_screen_carries_exception_text()
    {
        var window = Open(_logFolder, crash: Thrown("the library is gone"));

        var visible = window.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible).ToList();
        var shown = string.Join('\n', visible.OfType<TextBlock>().Select(block => block.Text)
            .Concat(visible.OfType<TextBox>().Select(box => box.Text)));

        Assert.DoesNotContain("the library is gone", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", shown, StringComparison.Ordinal);
        Assert.DoesNotContain(visible.OfType<TextBox>(), box => !string.IsNullOrEmpty(box.Text));
    }

    [AvaloniaFact]
    public void Copy_details_for_support_puts_the_support_text_on_the_clipboard_and_says_so()
    {
        var copied = new List<string>();
        var window = Open(_logFolder, copyText: text =>
        {
            copied.Add(text);
            return Task.CompletedTask;
        });
        var button = Named<Button>(window, "CopyDetailsButton");
        var note = Named<TextBlock>(window, "CopyNoteText");

        Assert.Equal("Copy details for support", button.Content);
        Assert.False(note.IsEffectivelyVisible);

        Click(button);

        var text = Assert.Single(copied);
        Assert.Contains("9.9.9+abc1234", text, StringComparison.Ordinal);
        Assert.Contains(System.Runtime.InteropServices.RuntimeInformation.OSDescription, text, StringComparison.Ordinal);
        Assert.Contains("the UI thread", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: the library is gone", text, StringComparison.Ordinal);
        Assert.Contains("   at ", text, StringComparison.Ordinal);
        Assert.Contains(_logFolder, text, StringComparison.Ordinal);
        Assert.True(note.IsEffectivelyVisible);
        Assert.Equal("Copied. Paste it into your bug report.", note.Text);
    }

    // What the player sees under Show details is what they paste.
    [AvaloniaFact]
    public void The_details_box_holds_exactly_the_text_that_is_copied()
    {
        var copied = new List<string>();
        var window = Open(_logFolder, copyText: text =>
        {
            copied.Add(text);
            return Task.CompletedTask;
        });

        Click(Named<Button>(window, "CopyDetailsButton"));

        Assert.Equal(Assert.Single(copied), Named<TextBox>(window, "DetailsText").Text);
    }

    [AvaloniaFact]
    public void A_clipboard_that_fails_shows_a_short_message_and_does_not_crash()
    {
        var thrown = Open(_logFolder, copyText: _ => throw new InvalidOperationException("no clipboard"));
        var faulted = Open(_logFolder, copyText: _ => Task.FromException(new InvalidOperationException("no clipboard")));

        foreach (var window in new[] { thrown, faulted })
        {
            Click(Named<Button>(window, "CopyDetailsButton"));

            var note = Named<TextBlock>(window, "CopyNoteText");
            Assert.True(note.IsEffectivelyVisible);
            Assert.Equal("SavePoint couldn't reach the clipboard. Try again.", note.Text);
            Assert.True(window.IsVisible);
        }
    }

    [AvaloniaFact]
    public void Without_a_log_folder_the_support_text_has_no_log_folder_line()
    {
        var copied = new List<string>();
        var window = Open(null, copyText: text =>
        {
            copied.Add(text);
            return Task.CompletedTask;
        });

        Click(Named<Button>(window, "CopyDetailsButton"));

        Assert.DoesNotContain("Log folder", Assert.Single(copied), StringComparison.Ordinal);
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
