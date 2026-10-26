using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;

namespace Savepoint.App.Tests.Features.Help;

// The real English catalog, which is what the help's copy is judged against.
internal static class HelpText
{
    public static UiFormatter Formatter(string os = "windows") =>
        new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: os, timeZone: TimeZoneInfo.Utc);
}
