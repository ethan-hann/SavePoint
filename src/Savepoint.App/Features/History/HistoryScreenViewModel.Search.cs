using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Search;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.History;

/// <summary>Which kinds of snapshot the History search shows.</summary>
public enum HistoryKindFilter
{
    /// <summary>Every snapshot.</summary>
    All,

    /// <summary>The snapshots that hold saves, which is what the Saves column lists.</summary>
    Saves,

    /// <summary>The snapshots that hold the game's version and mods, which is what the timeline lists.</summary>
    GameAndMods,
}

/// <summary>One choice of the kind group beside the search box.</summary>
/// <param name="Filter">The kinds it shows.</param>
/// <param name="Label">Its name, already localized.</param>
public sealed record KindOption(HistoryKindFilter Filter, string Label);

public sealed partial class HistoryScreenViewModel
{
    // Everything is built once per read; the search only picks from what is built, so typing never formats a message.
    private sealed record PointEntry(HistoryPoint Point, SearchTarget Target, TimelineEntryViewModel? Timeline, SaveRowViewModel? Save, PointCardViewModel Card);

    private sealed record RemovedEntry(RemovedSnapshot Snapshot, SearchTarget Target, RemovedSnapshotViewModel Row);

    /// <summary>How many snapshots and recently removed ones a history can hold before the search waits for a pause in the typing.</summary>
    public const int InstantRows = 250;

    /// <summary>How long typing has to pause before a long history is narrowed.</summary>
    public static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(250);

    private ITimer? _typingPause;
    private int _typingGeneration;
    private List<PointEntry> _entries = [];
    private List<RemovedEntry> _removedEntries = [];
    private Dictionary<string, string> _profileNotes = [];
    private DateTimeOffset _groupsNow;
    private int _visibleCount;
    private bool _searchSilent;
    private KindOption _currentKindOption = null!;
    private PointGroupViewModel? _thisWeekGroup;
    private PointGroupViewModel? _earlierGroup;

    /// <summary>The "Versions &amp; mods" column, newest first; holds the entries the search leaves.</summary>
    public ObservableCollection<TimelineEntryViewModel> Timeline { get; } = [];

    /// <summary>The "Saves" column, newest first; holds the rows the search leaves.</summary>
    public ObservableCollection<SaveRowViewModel> Saves { get; } = [];

    /// <summary>The card view's groups, "Last 7 days" first; a group with no cards is left out.</summary>
    public ObservableCollection<PointGroupViewModel> Groups { get; } = [];

    /// <summary>
    /// The card view as the one list it scrolls: each group's heading, then its cards in rows of <see cref="CardColumns"/>. The view draws only
    /// the lines near the screen, so thousands of cards cost no more than a screenful.
    /// </summary>
    public ObservableCollection<CardListEntry> CardEntries { get; } = [];

    /// <summary>How many cards fit side by side at the width the view has; the view sets it as the window is resized.</summary>
    [ObservableProperty]
    private int _cardColumns = 3;

    /// <summary>The kind chosen; the choice lasts for the session and goes back to All when another game opens.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearSearchCommand))]
    private KindOption? _selectedKindOption;

    /// <summary>The search box under the header.</summary>
    public SearchBoxViewModel Search { get; private set; } = null!;

    /// <summary>All, Saves and Game and mods, in that order.</summary>
    public IReadOnlyList<KindOption> KindOptions { get; private set; } = [];

    /// <summary>The kind group's accessible name.</summary>
    public string KindLabel { get; private set; } = string.Empty;

    /// <summary>True when the search row shows: there are snapshots to narrow down, or recently removed ones.</summary>
    public bool ShowSearch => State == HistoryState.Loaded || (State == HistoryState.Empty && RemovedTotal > 0);

    /// <summary>True when there are snapshots but the search and kind choice leave none of them.</summary>
    public bool IsSearchEmpty => State == HistoryState.Loaded && _visibleCount == 0;

    /// <summary>True when the points are drawn: there are some and the search leaves some.</summary>
    public bool ShowPoints => HasPoints && !IsSearchEmpty;

    /// <summary>"No snapshots match.", where the snapshots would be.</summary>
    public string SearchNoMatchesText { get; private set; } = string.Empty;

    /// <summary>How many recently removed snapshots the game has before the search.</summary>
    public int RemovedTotal => _removedEntries.Count;

    /// <summary>True when removed snapshots exist but the search leaves none of them.</summary>
    public bool ShowRemovedNoMatches => RemovedTotal > 0 && Removed.Count == 0 && !HasRemovedError;

    /// <summary>How many recently removed rows the section draws in full; a longer list draws only the rows in view.</summary>
    public const int RemovedRowsDrawnInFull = 30;

    /// <summary>
    /// True when the section lists more rows than it draws in full. A list that is drawn in full is as tall as its rows, so the section
    /// does not change height while the screen scrolls; a long one fills its fixed height, which any number of rows exceeds.
    /// </summary>
    public bool IsRemovedLong => Removed.Count > RemovedRowsDrawnInFull;

    /// <summary>"No removed snapshots match.", inside the Recently removed section.</summary>
    public string RemovedNoMatchesText { get; private set; } = string.Empty;

    /// <summary>True while the typed words or the kind choice narrow the list, which is when there is something to clear.</summary>
    public bool CanClearSearch => Search.HasText || SelectedKindOption is { Filter: not HistoryKindFilter.All };

    /// <summary>
    /// Empties the search box and puts the kind back on All, so every snapshot is listed again. The "no matches" buttons run it, not
    /// the box's own Clear, because the kind choice can leave nothing without any words typed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClearSearch))]
    private void ClearSearch()
    {
        // Both go back before the list is narrowed once, so the count is said once.
        ResetSearch();
        StopTypingPause();
        ApplySearch();
        Search.ReturnFocus();
    }

    private void InitializeSearch()
    {
        var text = _deps.Text;
        Search = new SearchBoxViewModel(text, _deps.Dispatcher, _deps.Clock, text.Message("search.history.label"), text.Message("search.shortcut.find"));
        Search.Changed += OnSearchChanged;
        KindLabel = text.Message("search.kind.label");
        KindOptions =
        [
            new KindOption(HistoryKindFilter.All, text.Message("search.kind.all")),
            new KindOption(HistoryKindFilter.Saves, text.Message("search.kind.saves")),
            new KindOption(HistoryKindFilter.GameAndMods, text.Message("search.kind.gameAndMods")),
        ];
        _currentKindOption = KindOptions[0];
        Restore(() => SelectedKindOption = _currentKindOption);
        SearchNoMatchesText = text.Message("search.history.noMatch");
        RemovedNoMatchesText = text.Message("search.history.removedNoMatch");
    }

    /// <summary>
    /// Raised just before the lists change under the view (a search, a refresh, or cards re-flowing to a new width), so the view can
    /// note what has keyboard focus and put it back on the same snapshot afterwards.
    /// </summary>
    public event EventHandler? EntriesChanging;

    partial void OnCardColumnsChanged(int value)
    {
        EntriesChanging?.Invoke(this, EventArgs.Empty);
        ShowCardEntries();
    }

    partial void OnSelectedKindOptionChanged(KindOption? value)
    {
        if (_restoring)
        {
            return;
        }

        // A list box clears its selection while it rebuilds; only a real choice counts.
        if (value is null)
        {
            Restore(() => SelectedKindOption = _currentKindOption);
            return;
        }

        _currentKindOption = value;
        ApplySearch();
    }

    // Typing narrows at once while the list is short. A long history matches and re-lists thousands of entries each time, so each
    // narrowing waits for a pause in the typing and a word is one pass, not one per letter.
    private void OnSearchChanged(object? sender, EventArgs e)
    {
        ClearSearchCommand.NotifyCanExecuteChanged();
        StopTypingPause();
        var generation = _typingGeneration;
        if (_searchSilent || _entries.Count + _removedEntries.Count <= InstantRows)
        {
            ApplySearch();
            return;
        }

        _typingPause = _deps.Clock.CreateTimer(
            _ => _deps.Dispatcher.Post(
                () =>
                {
                    if (generation == _typingGeneration)
                    {
                        ApplySearch();
                    }
                }),
            null,
            TypingPause,
            Timeout.InfiniteTimeSpan);
    }

    // Ends a pause that is still waiting, and makes one that already fired do nothing.
    private void StopTypingPause()
    {
        _typingPause?.Dispose();
        _typingPause = null;
        _typingGeneration++;
    }

    private void ApplySearch()
    {
        ShowEntries();
        ShowRemovedEntries();
        if (!_searchSilent)
        {
            Search.AnnounceAfterPause(CountMessage());
        }
    }

    // What is on screen is what is counted. Matches inside a closed Recently removed section are said apart, so a listener does not
    // take them for snapshots in the list, and still learns they are there. With no other snapshots, nothing is listed, so the count
    // names the closed section alone rather than saying "0 of 0 snapshots" first.
    private string CountMessage()
    {
        var text = _deps.Text;
        if (IsRemovedOpen || _removedEntries.Count == 0)
        {
            return text.Message("search.history.count", ("shown", _visibleCount + Removed.Count), ("total", _entries.Count + _removedEntries.Count));
        }

        if (_entries.Count == 0)
        {
            return text.Message("search.history.countOnlyRemoved", ("removedShown", Removed.Count), ("removedTotal", _removedEntries.Count));
        }

        return Removed.Count == 0
            ? text.Message("search.history.count", ("shown", _visibleCount), ("total", _entries.Count))
            : text.Message(
                "search.history.countWithRemoved",
                ("shown", _visibleCount),
                ("total", _entries.Count),
                ("removedShown", Removed.Count),
                ("removedTotal", _removedEntries.Count));
    }

    // A search belongs to the game it was typed for.
    private void ResetSearch()
    {
        _searchSilent = true;
        try
        {
            Search.Text = string.Empty;
            Restore(() => SelectedKindOption = KindOptions[0]);
            _currentKindOption = KindOptions[0];
        }
        finally
        {
            _searchSilent = false;
        }
    }

    private bool Shown(HistoryPointKind kind, SearchTarget target)
    {
        var kindShown = _currentKindOption.Filter switch
        {
            HistoryKindFilter.Saves => kind != HistoryPointKind.VersionAndMods,
            HistoryKindFilter.GameAndMods => kind != HistoryPointKind.SaveOnly,
            _ => true,
        };
        return kindShown && (Search.Query.IsEmpty || Search.Query.Matches(target));
    }

    private void ClearEntries()
    {
        _entries = [];
        _removedEntries = [];
        _visibleCount = 0;
        _thisWeekGroup = null;
        _earlierGroup = null;
        Timeline.Clear();
        Saves.Clear();
        Groups.Clear();
        CardEntries.Clear();
        ShowRemovedEntries();
    }

    // The two groups last as long as the points they were built for, so a keystroke changes their cards and nothing else.
    private void BuildGroups()
    {
        _thisWeekGroup = new PointGroupViewModel(_deps.Text.Message("history.group.thisWeek"));
        _earlierGroup = new PointGroupViewModel(_deps.Text.Message("history.group.earlier"));
    }

    // Lists the entries the search and the kind choice leave, in the three arrangements the screen draws. Entries that stay are the
    // same objects, so a card a person has focused keeps its focus while the ones around it come and go.
    private void ShowEntries()
    {
        EntriesChanging?.Invoke(this, EventArgs.Empty);
        var visible = _entries.Where(entry => Shown(entry.Point.Kind, entry.Target)).ToList();
        _visibleCount = visible.Count;

        var onTimeline = visible.Where(entry => entry.Timeline is not null).Select(entry => entry.Timeline!).ToList();
        for (var index = 0; index < onTimeline.Count; index++)
        {
            // The rail ends at the last entry that shows, whichever entry that is.
            onTimeline[index].IsLast = index == onTimeline.Count - 1;
        }

        ListSync.To(Timeline, onTimeline);
        ListSync.To(Saves, [.. visible.Where(entry => entry.Save is not null).Select(entry => entry.Save!)]);
        ListedItemViewModel.Number(Timeline);
        ListedItemViewModel.Number(Saves);
        ShowGroups(visible);
        OnPropertyChanged(nameof(IsSearchEmpty));
        OnPropertyChanged(nameof(ShowPoints));
    }

    private void ShowGroups(List<PointEntry> visible)
    {
        if (_thisWeekGroup is null || _earlierGroup is null)
        {
            return;
        }

        var thisWeek = visible.Where(entry => IsThisWeek(entry.Point)).Select(entry => entry.Card).ToList();
        var earlier = visible.Where(entry => !IsThisWeek(entry.Point)).Select(entry => entry.Card).ToList();
        ListSync.To(_thisWeekGroup.Cards, thisWeek);
        ListSync.To(_earlierGroup.Cards, earlier);

        // A group with no cards is left out.
        List<PointGroupViewModel> groups = [];
        if (thisWeek.Count > 0)
        {
            groups.Add(_thisWeekGroup);
        }

        if (earlier.Count > 0)
        {
            groups.Add(_earlierGroup);
        }

        ListSync.To(Groups, groups);
        // One list to a screen reader, so the count runs through both groups.
        ListedItemViewModel.Number<PointCardViewModel>([.. thisWeek, .. earlier]);
        ShowCardEntries();
    }

    private bool IsThisWeek(HistoryPoint point) => _groupsNow - point.CapturedAt < _thisWeek;

    // The groups' cards laid out in rows for the one list the view scrolls. A row the search leaves as it was is kept as it was.
    private void ShowCardEntries()
    {
        var columns = Math.Max(1, CardColumns);
        List<CardListEntry> wanted = [];
        foreach (var group in Groups)
        {
            wanted.Add(new CardGroupHeadingViewModel(group));
            for (var first = 0; first < group.Cards.Count; first += columns)
            {
                var row = new PointCardViewModel[Math.Min(columns, group.Cards.Count - first)];
                for (var at = 0; at < row.Length; at++)
                {
                    row[at] = group.Cards[first + at];
                }

                wanted.Add(new CardRowViewModel(row, columns));
            }
        }

        ListSync.To(CardEntries, wanted);
    }

    private void SetRemoved(List<RemovedEntry> entries)
    {
        _removedEntries = entries;
        ShowRemovedEntries();
    }

    private void ShowRemovedEntries()
    {
        Removed = [.. _removedEntries.Where(entry => Shown(entry.Snapshot.Kind, entry.Target)).Select(entry => entry.Row)];
        ListedItemViewModel.Number(Removed);

        // The list can stay the same empty list while the section's total changes.
        OnPropertyChanged(nameof(RemovedTotal));
        OnPropertyChanged(nameof(IsRemovedLong));
        OnPropertyChanged(nameof(HasRemoved));
        OnPropertyChanged(nameof(ShowRemoved));
        OnPropertyChanged(nameof(RemovedHeading));
        OnPropertyChanged(nameof(ShowRemovedNoMatches));
        OnPropertyChanged(nameof(ShowSearch));
    }

    private List<PointEntry> BuildEntries(GameDefinition game, List<HistoryPoint> points)
    {
        var text = _deps.Text;
        List<PointEntry> entries = new(points.Count);
        foreach (var point in points)
        {
            TimelineEntryViewModel? timeline = null;
            if (point.Kind != HistoryPointKind.SaveOnly)
            {
                var (headline, namesVersion) = _words.Headline(point);
                var detail = _words.TimelineDetail(point, namesVersion);
                var stamp = _words.Stamp(point.CapturedAt, point.InUse);
                timeline = new TimelineEntryViewModel(
                    headline,
                    detail,
                    stamp,
                    point.InUse,
                    _words.ItemName(headline, detail, stamp),
                    text.Message("history.card.goBackAutomation", ("name", point.Name)),
                    BeginCommand(game.Id, new LaunchTarget.Point(point.Id)),
                    text.Message("history.card.switchAutomation", ("name", point.Name)),
                    BeginCommand(game.Id, new LaunchTarget.Point(point.Id), switchOnly: true))
                {
                    FocusKey = point.Id.Value,
                };
            }

            SaveRowViewModel? save = null;
            if (point.Kind != HistoryPointKind.VersionAndMods)
            {
                var saveDetail = _words.SaveDetail(point);
                save = new SaveRowViewModel(
                    point,
                    saveDetail,
                    text.Message("history.save.loadAutomation", ("name", point.Name)),
                    _words.ItemName(point.Name, saveDetail, point.LoadedNow ? text.Message("history.save.loaded") : string.Empty),
                    BeginCommand(game.Id, new LaunchTarget.Save(point.ProfileId, point.Id)),
                    text.Message("history.save.switchAutomation", ("name", point.Name)),
                    BeginCommand(game.Id, new LaunchTarget.Save(point.ProfileId, point.Id), switchOnly: true))
                {
                    FocusKey = point.Id.Value,
                };
            }

            var contains = _words.Contains(point);
            var date = _words.Date(point.CapturedAt);

            // The card sits in one list under its group's heading, which a screen reader does not pair with it, so the name says the group.
            var group = text.Message(IsThisWeek(point) ? "history.group.thisWeek" : "history.group.earlier");
            var card = new PointCardViewModel(
                point,
                HistoryText.Icon(point.Kind),
                contains,
                date,
                text.Message("history.card.goBackAutomation", ("name", point.Name)),
                text.Message("history.card.inGroup", ("group", group), ("item", _words.ItemName(point.Name, contains, date, point.LoadedNow ? text.Message("history.card.loadedNow") : string.Empty))),
                BeginCommand(game.Id, new LaunchTarget.Point(point.Id)),
                text.Message("history.card.switchAutomation", ("name", point.Name)),
                BeginCommand(game.Id, new LaunchTarget.Point(point.Id), switchOnly: true))
            {
                FocusKey = point.Id.Value,
            };

            // A snapshot is found by its name, its profile and that profile's notes, what it holds, and its date in the forms the screen shows and in ISO order.
            entries.Add(new PointEntry(point, SearchTarget.Of(point.Name, point.ProfileName, NotesOf(point.ProfileId), contains, date, text.DateSearchText(point.CapturedAt)), timeline, save, card));
        }

        return entries;
    }

    private List<RemovedEntry> BuildRemoved(GameDefinition game, IReadOnlyList<RemovedSnapshot> removed) =>
        [
            .. removed.OrderByDescending(snapshot => snapshot.RemovedAt).Select(snapshot =>
            {
                var detail = _words.Contains(snapshot.Kind, snapshot.Install);
                var origin = _words.RemovedOrigin(snapshot);
                var when = _deps.Text.Message("history.removed.when", ("when", _deps.Text.ShortDateTime(snapshot.RemovedAt)));
                var row = new RemovedSnapshotViewModel(
                    snapshot,
                    detail,
                    origin,
                    when,
                    _deps.Text.Message("history.removed.bringBackAutomation", ("name", snapshot.Name)),
                    _words.ItemName(snapshot.Name, detail, origin, when),
                    new AsyncRelayCommand(() => BringBackAsync(game.Id, snapshot)));
                var target = SearchTarget.Of(snapshot.Name, snapshot.FromProfileName, NotesOf(snapshot.FromProfileId), detail, origin, when, _words.Date(snapshot.CapturedAt), _deps.Text.DateSearchText(snapshot.CapturedAt));
                return new RemovedEntry(snapshot, target, row);
            }),
        ];

    // A deleted profile (null id) has no notes left to find its snapshots by.
    private string? NotesOf(string? profileId) => profileId is not null && _profileNotes.TryGetValue(profileId, out var notes) ? notes : null;
}
