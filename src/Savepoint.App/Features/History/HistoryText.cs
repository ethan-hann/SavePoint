using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Theme;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.History;

// The words of the History screen: what a point's install state, kind and times read as.
internal sealed class HistoryText
{
    private readonly UiFormatter _text;

    public HistoryText(UiFormatter text)
    {
        _text = text;
    }

    // "1.6.640 with 212 mod files", "1.6.1170, no mods", "1.6.640"; empty when the install state is unknown.
    public string Install(InstallState state)
    {
        var version = HasVersion(state) ? state.Version : null;
        return (version, state.ModFileCount) switch
        {
            (not null, > 0) => _text.Message("history.install.versionAndMods", ("version", version), ("count", state.ModFileCount)),
            (not null, 0) => _text.Message("history.install.versionNoMods", ("version", version)),
            (not null, null) => _text.Message("history.install.version", ("version", version)),
            (null, > 0) => _text.Message("history.install.mods", ("count", state.ModFileCount)),
            (null, 0) => _text.Message("history.install.noMods"),
            _ => string.Empty,
        };
    }

    // "14 mod files updated" when mods changed, otherwise what was captured; the flag says the headline already names the version.
    public (string Text, bool NamesVersion) Headline(HistoryPoint point)
    {
        if (point.ModFilesChanged is > 0 and var changed)
        {
            return (_text.Message("history.timeline.modsUpdated", ("count", changed)), false);
        }

        var install = Install(point.Install);
        return install.Length == 0
            ? (_text.Message("history.timeline.capturedPlain"), false)
            : (_text.Message("history.timeline.captured", ("install", install)), HasVersion(point.Install));
    }

    // The profile, then the version when the headline leaves it out, then the size where it is known.
    public string TimelineDetail(HistoryPoint point, bool headlineNamesVersion)
    {
        var detail = point.ProfileName;
        if (!headlineNamesVersion && HasVersion(point.Install))
        {
            detail = Join(detail, _text.Message("history.timeline.version", ("version", point.Install.Version)));
        }

        return point.Bytes is { } bytes ? Join(detail, _text.FileSize(bytes)) : detail;
    }

    // "Jul 2, 2026 · 6:03 PM", with "in use now" after it for the point the install folder holds.
    public string Stamp(DateTimeOffset when, bool inUse)
    {
        var stamp = _text.Message("history.stamp", ("date", _text.Date(when)), ("time", _text.Time(when)));
        return inUse ? _text.Message("history.timeline.inUse", ("stamp", stamp)) : stamp;
    }

    public string SaveDetail(HistoryPoint point) => Join(_text.DateAndTime(point.CapturedAt), point.ProfileName);

    // "Save only · 1.6.640 with 212 mod files": the kind, then the install state at that point.
    public string Contains(HistoryPoint point)
    {
        var install = Install(point.Install);
        var kind = Kind(point.Kind);
        return install.Length == 0 ? kind : Join(kind, install);
    }

    public string Date(DateTimeOffset when) => _text.DateAndTime(when);

    public string Join(string first, string second) => _text.Message("history.join", ("first", first), ("second", second));

    public string Kind(HistoryPointKind kind) => kind switch
    {
        HistoryPointKind.SaveOnly => _text.Message("history.kind.saveOnly"),
        HistoryPointKind.VersionAndMods => _text.Message("history.kind.versionAndMods"),
        _ => _text.Message("history.kind.versionModsAndSave"),
    };

    public static string Icon(HistoryPointKind kind) => kind switch
    {
        HistoryPointKind.SaveOnly => Icons.Save,
        HistoryPointKind.VersionAndMods => Icons.Extension,
        _ => Icons.BookmarkAdd,
    };

    private static bool HasVersion(InstallState state) => !string.IsNullOrWhiteSpace(state.Version);
}
