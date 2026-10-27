using System.Globalization;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Localization;
using Savepoint.Core.Errors;
using Savepoint.Core.Platform;

namespace Savepoint.App.Features.Common.Formatting;

/// <summary>
/// Turns catalog messages and raw values (bytes, instants, durations, errors) into the text views show. Numbers and
/// dates are formatted here with the catalog language's culture and passed to ICU as strings, so a message only
/// ever branches on plurals and selects. View models take one of these instead of reaching for static state.
/// </summary>
public sealed class UiFormatter
{
    private const double _siUnit = 1000;
    private const double _daysPerMonth = 30.4375;

    private static readonly string[] _sizeKeys =
    [
        "format.size.kb",
        "format.size.mb",
        "format.size.gb",
        "format.size.tb",
    ];

    private readonly LocalizationCatalog _catalog;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _timeZone;
    private readonly CultureInfo _culture;

    /// <param name="catalog">The messages to format; its locale picks the culture for numbers, dates and plurals.</param>
    /// <param name="clock">What "now" means for relative times; the system clock when null.</param>
    /// <param name="os">The <c>os</c> argument every message can select on (windows, macos or linux); the running system when null.</param>
    /// <param name="timeZone">The zone absolute dates and times are shown in; the machine's local zone when null.</param>
    public UiFormatter(LocalizationCatalog catalog, TimeProvider? clock = null, string? os = null, TimeZoneInfo? timeZone = null)
    {
        _catalog = catalog;
        _clock = clock ?? TimeProvider.System;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _culture = CultureInfo.GetCultureInfo(catalog.Locale);
        OsKind = os is null ? PlatformFactory.CurrentOs : KindOf(os);
        Os = os ?? OsName(OsKind);
    }

    /// <summary>The value messages receive as <c>os</c>: <c>windows</c>, <c>macos</c> or <c>linux</c>.</summary>
    public string Os { get; }

    /// <summary>The operating system the messages are for, which code decides on; <see cref="Os"/> is only the text the catalog selects on.</summary>
    public OsKind OsKind { get; }

    /// <summary>Whether the catalog has a message for the key, so a caller can tell a message it knows from one a newer Core wrote.</summary>
    /// <param name="key">The catalog key.</param>
    public bool Knows(string key) => _catalog.Contains(key);

    /// <summary>
    /// Formats a catalog message; <c>os</c> is supplied unless <paramref name="args"/> already has it. Each whole-number argument
    /// also arrives as <c>{name}Text</c> with the language's group separators, for a message that shows the number inside a plural.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The key is missing and the catalog is set to throw.</exception>
    public string Message(string key, IReadOnlyDictionary<string, object?>? args = null)
    {
        var all = args is null ? [] : new Dictionary<string, object?>(args);
        all.TryAdd("os", Os);
        if (args is not null)
        {
            foreach (var (name, value) in args)
            {
                if (value is int or long)
                {
                    all.TryAdd(name + "Text", Number(Convert.ToInt64(value, CultureInfo.InvariantCulture)));
                }
            }
        }

        return _catalog.Format(key, all);
    }

    /// <summary>Formats a catalog message with named arguments, for example <c>Message("shell.profileCount", ("count", 3))</c>.</summary>
    public string Message(string key, params (string Name, object? Value)[] args) =>
        Message(key, args.ToDictionary(arg => arg.Name, arg => arg.Value));

    /// <summary>
    /// The user-facing text for an error: its message id is the catalog key and its arguments fill the placeholders.
    /// An id the catalog does not know (a newer Core than this app) reads as the generic unknown-error message, never
    /// as a raw key. The developer detail is never part of it.
    /// </summary>
    public string Message(ErrorInfo error) => Message(error, ErrorOperation.Unspecified);

    /// <summary>
    /// The user-facing text for an error that happened during a known operation: the same as <see cref="Message(ErrorInfo)"/>, but an
    /// error that several operations can raise is said in the terms of this one. An operation with no wording of its own gets the
    /// neutral text.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <param name="operation">What SavePoint was doing.</param>
    public string Message(ErrorInfo error, ErrorOperation operation)
    {
        var id = _catalog.Contains(error.MessageId) ? error.MessageId : "error.unknown";
        var key = ErrorCopy.BodyKey(id, operation);
        return Message(_catalog.Contains(key) ? key : id, id switch
        {
            "error.diskSpace.low" => WithDiskSpaceArguments(error.Args),
            "error.launch.switchInterrupted" => WithTargetKnown(error.Args),
            _ => error.Args,
        });
    }

    // Starting the target finishes an unfinished switch only when the error knows which switch it was.
    private static Dictionary<string, object?> WithTargetKnown(IReadOnlyDictionary<string, object?> args) =>
        new(args) { ["targetKnown"] = args.TryGetValue("targetPoint", out var target) && target is not null ? "yes" : "no" };

    /// <summary>The drive a path is on: "D:" for a Windows drive letter, the "\\server\share" of a network path, the path itself otherwise; null for an empty path.</summary>
    public static string? Drive(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            return $"{char.ToUpperInvariant(path[0])}:";
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : path;
        }

        return path;
    }

    // The message for a full disk names the drive and how much to free, which the error carries only as a path and two byte counts.
    private IReadOnlyDictionary<string, object?> WithDiskSpaceArguments(IReadOnlyDictionary<string, object?> args)
    {
        var shortfall = args.TryGetValue("requiredBytes", out var required) && args.TryGetValue("freeBytes", out var free) && required is long needed && free is long available
            ? needed - available
            : 0;
        return new Dictionary<string, object?>(args)
        {
            ["hasShortfall"] = shortfall > 0 ? "yes" : "no",
            ["shortfall"] = FileSize(Math.Max(shortfall, 0)),
            ["drive"] = args.TryGetValue("path", out var path) && path is string folder ? Drive(folder) ?? folder : string.Empty,
        };
    }

    /// <summary>A size in decimal units with at most one fraction digit and no trailing zero ("6.1 GB", "210 GB", "512 bytes").</summary>
    public string FileSize(long bytes)
    {
        if (bytes < _siUnit)
        {
            return Message("format.size.bytes", ("count", Math.Max(bytes, 0)));
        }

        var value = (double)bytes;
        var unit = -1;
        while (value >= _siUnit && unit < _sizeKeys.Length - 1)
        {
            value /= _siUnit;
            unit++;
        }

        // 999.95 MB rounds to 1000 at one digit, which reads better as 1 GB.
        if (Math.Round(value, 1, MidpointRounding.AwayFromZero) >= _siUnit && unit < _sizeKeys.Length - 1)
        {
            value /= _siUnit;
            unit++;
        }

        return Message(_sizeKeys[unit], ("value", value.ToString("0.#", _culture)));
    }

    /// <summary>A whole number with the language's group separators.</summary>
    public string Number(long value) => value.ToString("N0", _culture);

    /// <summary>How long ago an instant was ("just now", "3 days ago", "12 weeks ago"); an instant in the future reads as just now.</summary>
    public string RelativeTime(DateTimeOffset when)
    {
        var elapsed = _clock.GetUtcNow() - when;

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return Message("format.relative.justNow");
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return Message("format.relative.minutes", ("count", (long)elapsed.TotalMinutes));
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return Message("format.relative.hours", ("count", (long)elapsed.TotalHours));
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            return Message("format.relative.days", ("count", (long)elapsed.TotalDays));
        }

        if (elapsed < TimeSpan.FromDays(91))
        {
            return Message("format.relative.weeks", ("count", (long)(elapsed.TotalDays / 7)));
        }

        if (elapsed < TimeSpan.FromDays(365))
        {
            return Message("format.relative.months", ("count", (long)Math.Round(elapsed.TotalDays / _daysPerMonth)));
        }

        return Message("format.relative.years", ("count", (long)(elapsed.TotalDays / 365)));
    }

    /// <summary>The calendar date in the formatter's time zone, in the catalog's pattern ("Jul 2, 2026").</summary>
    public string Date(DateTimeOffset when) => Local(when).ToString(_catalog.Format("format.pattern.date"), _culture);

    /// <summary>The time of day in the formatter's time zone, in the catalog's pattern ("5:40 PM").</summary>
    public string Time(DateTimeOffset when) => Local(when).ToString(_catalog.Format("format.pattern.time"), _culture);

    /// <summary>Date and time together ("Jul 2, 2026, 5:40 PM").</summary>
    public string DateAndTime(DateTimeOffset when) =>
        Message("format.pattern.dateAndTime", ("date", Date(when)), ("time", Time(when)));

    /// <summary>
    /// Date and time for a list of recent things ("Jul 5, 11:42 PM"): the year is left out while the instant is in the
    /// current year, as the formatter's time zone sees it, and shown otherwise ("Jul 5, 2025, 11:42 PM").
    /// </summary>
    public string ShortDateTime(DateTimeOffset when) =>
        DateAndTime(_catalog.Format(InCurrentYear(when) ? "format.pattern.dateNoYear" : "format.pattern.date"), when);

    /// <summary>Like <see cref="ShortDateTime"/> with the day of the week first ("Sun Jul 5, 11:42 PM"), for cards that stand alone.</summary>
    public string WeekdayDateTime(DateTimeOffset when) =>
        DateAndTime(_catalog.Format(InCurrentYear(when) ? "format.pattern.weekdayDateNoYear" : "format.pattern.weekdayDate"), when);

    /// <summary>Date and time for a timeline, where entries span years ("Jul 2, 2026 · 6:03 PM"): the year is always shown and the separator comes from the catalog.</summary>
    public string TimelineDateTime(DateTimeOffset when) =>
        Message("format.pattern.timeline", ("date", Date(when)), ("time", Time(when)));

    /// <summary>The month and day when the instant is in the current year ("Jul 1"), with the year otherwise ("Jul 1, 2025").</summary>
    public string ShortDate(DateTimeOffset when) =>
        Local(when).ToString(_catalog.Format(InCurrentYear(when) ? "format.pattern.dateNoYear" : "format.pattern.date"), _culture);

    /// <summary>
    /// The words a search finds a date by, in the formatter's time zone and the catalog's language: the year, month and day with the time
    /// ("2026-09-05 23:42"), then the full and short names of the month and of the weekday ("September Sep Saturday Sat").
    /// </summary>
    /// <param name="when">The instant.</param>
    public string DateSearchText(DateTimeOffset when)
    {
        var local = Local(when);
        var names = _culture.DateTimeFormat;
        return string.Join(
            ' ',
            local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            names.GetMonthName(local.Month),
            names.GetAbbreviatedMonthName(local.Month),
            names.GetDayName(local.DayOfWeek),
            names.GetAbbreviatedDayName(local.DayOfWeek));
    }

    /// <summary>The date as month, day and two-digit year with no padding ("7-2-26"), for the default names of profiles and points.</summary>
    public string NameDate(DateTimeOffset when)
    {
        var local = Local(when);
        return string.Create(CultureInfo.InvariantCulture, $"{local.Month}-{local.Day}-{local.Year % 100:00}");
    }

    /// <summary>A length of time in whole units, rounded down ("2 hours 14 minutes", "41 minutes", "50 seconds").</summary>
    public string Duration(TimeSpan span)
    {
        var total = (long)Math.Max(span.TotalSeconds, 0);
        var hours = total / 3600;
        var minutes = total % 3600 / 60;

        if (hours > 0 && minutes > 0)
        {
            return Message("format.duration.hoursAndMinutes", ("hours", hours), ("minutes", minutes));
        }

        if (hours > 0)
        {
            return Message("format.duration.hours", ("count", hours));
        }

        return total >= 60
            ? Message("format.duration.minutes", ("count", minutes))
            : Message("format.duration.seconds", ("count", total));
    }

    // The only place the catalog's text names meet OsKind; every other branch on the system uses OsKind.
    private static OsKind KindOf(string os) => os switch
    {
        "windows" => OsKind.Windows,
        "macos" => OsKind.MacOS,
        _ => OsKind.Linux,
    };

    private static string OsName(OsKind os) => os switch
    {
        OsKind.Windows => "windows",
        OsKind.MacOS => "macos",
        _ => "linux",
    };

    private DateTimeOffset Local(DateTimeOffset when) => TimeZoneInfo.ConvertTime(when, _timeZone);

    private bool InCurrentYear(DateTimeOffset when) => Local(when).Year == Local(_clock.GetUtcNow()).Year;

    private string DateAndTime(string datePattern, DateTimeOffset when) =>
        Message("format.pattern.dateAndTime", ("date", Local(when).ToString(datePattern, _culture)), ("time", Time(when)));
}
