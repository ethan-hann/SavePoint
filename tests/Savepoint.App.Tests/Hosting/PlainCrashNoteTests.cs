using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Hosting;
using Savepoint.App.Localization;

namespace Savepoint.App.Tests.Hosting;

public class PlainCrashNoteTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en"));

    private static string Note(string message, string? logFolder = "/home/sam/.local/state/savepoint/logs") =>
        PlainCrashNote.For(new CrashReport(new InvalidOperationException(message), "the UI thread", logFolder), _text);

    [Fact]
    public void The_note_says_what_went_wrong_and_where_the_log_is()
    {
        var note = Note("the library is gone");

        Assert.Contains("SavePoint ran into a problem and has to close", note, StringComparison.Ordinal);
        Assert.Contains("the library is gone", note, StringComparison.Ordinal);
        Assert.Contains("/home/sam/.local/state/savepoint/logs", note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_crash_without_a_log_folder_does_not_point_to_one()
    {
        var note = Note("the library is gone", logFolder: null);

        Assert.DoesNotContain("log folder", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_desktop_with_no_usable_display_gets_advice_in_plain_words()
    {
        var note = Note("XOpenDisplay failed");

        Assert.Contains("DISPLAY", note, StringComparison.Ordinal);
        Assert.Contains("XAUTHORITY", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Other_crashes_get_no_display_advice()
    {
        Assert.DoesNotContain("DISPLAY", Note("the library is gone"), StringComparison.Ordinal);
    }
}
