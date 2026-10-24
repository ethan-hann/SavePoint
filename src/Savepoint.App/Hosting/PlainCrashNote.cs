using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;

namespace Savepoint.App.Hosting;

/// <summary>
/// The few plain lines a crash leaves on the terminal, for the case where no window can be shown: a desktop with no usable display
/// gives the app nowhere to put the crash window, and the log is the only other trace.
/// </summary>
internal static class PlainCrashNote
{
    /// <summary>The lines for a crash, in the app's own words.</summary>
    /// <param name="report">The crash.</param>
    /// <param name="text">Looks up the words.</param>
    public static string For(CrashReport report, UiFormatter text)
    {
        var lines = new List<string> { text.Message("app.crash.note", ("message", report.Exception.Message)) };
        // The X library reports a missing or refused display with this exact text; nothing more specific reaches us.
        if (report.Exception.Message.Contains("XOpenDisplay", StringComparison.Ordinal))
        {
            lines.Add(text.Message("app.crash.noDisplay"));
        }

        if (report.LogFolder is { } folder)
        {
            lines.Add(text.Message("app.crash.noteLog", ("path", folder)));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Writes the note to the standard error stream.</summary>
    /// <param name="report">The crash.</param>
    public static void WriteToStandardError(CrashReport report)
    {
        // The crash may predate the catalog, so the note loads its own copy when the app has none.
        UiFormatter text;
        try
        {
            text = new UiFormatter(LocalizationCatalog.Current);
        }
        catch (InvalidOperationException)
        {
            text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en"));
        }

        Console.Error.WriteLine(For(report, text));
    }
}
