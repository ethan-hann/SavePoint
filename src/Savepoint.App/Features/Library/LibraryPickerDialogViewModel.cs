using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;
using Savepoint.Core.Platform;

namespace Savepoint.App.Features.Library;

/// <summary>How the list of installed games is ordered.</summary>
public enum LibrarySort
{
    /// <summary>By name, ignoring case.</summary>
    Name,

    /// <summary>Steam games first, then GOG Galaxy games, each by name.</summary>
    Store,

    /// <summary>Games whose saves folder SavePoint knows first, then the rest, each by name.</summary>
    SavesKnown,
}

/// <summary>One way to sort the list, with its label.</summary>
/// <param name="Label">The label the player sees.</param>
/// <param name="Value">The order.</param>
public sealed record LibrarySortChoice(string Label, LibrarySort Value);

/// <summary>
/// "Add games from your libraries": looks through Steam and GOG Galaxy, lists what is installed with a check box for each game, and
/// answers with the games the player checked, or null when the player backs out. Nothing is added here.
/// </summary>
public sealed partial class LibraryPickerDialogViewModel : DialogViewModel<IReadOnlyList<LibraryGame>?>
{
    private readonly IGamesApi _games;
    private readonly UiFormatter _text;
    private readonly CancellationTokenSource _scan = new();
    private List<LibraryRowViewModel> _rows = [];
    private bool _loaded;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hideAdded;

    [ObservableProperty]
    private LibrarySortChoice _sort;

    /// <param name="games">The games Api that scans the libraries.</param>
    /// <param name="text">Formats the copy.</param>
    public LibraryPickerDialogViewModel(IGamesApi games, UiFormatter text)
        : base(text.Message("library.picker.title"))
    {
        _games = games;
        _text = text;
        // Closing the dialog, by any means, is the player giving up on the scan.
        Closed += (_, _) => _scan.Cancel();
        SortChoices =
        [
            new LibrarySortChoice(text.Message("library.picker.sort.name"), LibrarySort.Name),
            new LibrarySortChoice(text.Message("library.picker.sort.store"), LibrarySort.Store),
            new LibrarySortChoice(text.Message("library.picker.sort.savesKnown"), LibrarySort.SavesKnown),
        ];
        _sort = SortChoices[0];
        LoadingText = text.Message("library.picker.loading");
        EmptyText = text.Message("library.picker.empty");
        Description = text.Message("library.picker.intro");
        AddLabel = text.Message("library.picker.add", ("count", 0));
        CountText = string.Empty;
        ErrorText = string.Empty;
    }

    /// <summary>The rows that match the search and filters, in the chosen order.</summary>
    public ObservableCollection<LibraryRowViewModel> VisibleRows { get; } = [];

    /// <summary>The ways the list can be sorted.</summary>
    public IReadOnlyList<LibrarySortChoice> SortChoices { get; }

    /// <summary>"Looking for your installed games…", shown while the scan runs.</summary>
    public string LoadingText { get; }

    /// <summary>"No installed games were found.".</summary>
    public string EmptyText { get; }

    /// <summary>Why the scan failed; empty when it did not.</summary>
    public string ErrorText { get; private set; }

    /// <summary>True when the scan failed.</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>True when the scan found no games at all.</summary>
    public bool ShowEmpty => _loaded && !HasError && _rows.Count == 0;

    /// <summary>True when there are games but none match the search and filters.</summary>
    public bool ShowNoMatches => _rows.Count > 0 && VisibleRows.Count == 0;

    /// <summary>
    /// "No games match your search."; when the search box is empty and hiding the added games is what leaves nothing, it says every
    /// game is added and where to turn the hiding off.
    /// </summary>
    public string NoMatchesText => _text.Message(HideAdded && SearchText.Trim().Length == 0 ? "library.picker.allAdded" : "library.picker.noMatches");

    /// <summary>True when there are no matches and the typed search is what hides the games, so clearing it helps.</summary>
    public bool ShowClearSearch => ShowNoMatches && SearchText.Trim().Length > 0;

    /// <summary>The name of the button that clears the search, "Clear search".</summary>
    public string ClearSearchName => _text.Message("search.clear");

    /// <summary>Empties the search.</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    /// <summary>One line for each store that was not found, so a missing store is not mistaken for no games.</summary>
    public IReadOnlyList<string> StoreNotes { get; private set; } = [];

    /// <summary>True when there is a note about a store that was not found.</summary>
    public bool HasStoreNotes => StoreNotes.Count > 0;

    /// <summary>True once the scan has ended and found games, which is when the list and its controls show.</summary>
    public bool ShowList => !IsLoading && _rows.Count > 0;

    /// <summary>How many games are checked, whether or not the search shows them.</summary>
    public int SelectedCount => _rows.Count(row => row.IsSelected);

    /// <summary>The add button's text ("Add 3 games").</summary>
    public string AddLabel { get; private set; }

    /// <summary>"2 of 14 selected".</summary>
    public string CountText { get; private set; }

    /// <summary>True when the player left through "Pick from the game list" instead of choosing games.</summary>
    public bool TemplateListRequested { get; private set; }

    /// <summary>Says which games the scan cannot see, above the button that leads to the template list.</summary>
    public string TemplatePointerText => _text.Message("library.picker.templates.note");

    /// <inheritdoc />
    public override string Description { get; }

    /// <inheritdoc />
    public override double DialogWidth => 680;

    /// <inheritdoc />
    public override bool PinsFooter => true;

    /// <inheritdoc />
    protected override IReadOnlyList<LibraryGame>? DismissResult => null;

    /// <summary>Scans the libraries and fills the list. Call once, as the dialog is shown.</summary>
    public async Task LoadAsync()
    {
        var scanned = await _games.ScanLibrariesAsync(_scan.Token).ConfigureAwait(true);
        if (_scan.IsCancellationRequested)
        {
            // The player closed the dialog; there is nothing left to show it in.
            return;
        }

        if (scanned.Ok)
        {
            _rows = [.. scanned.Value.Games.Select(game => new LibraryRowViewModel(game, _text))];
            foreach (var row in _rows)
            {
                row.PropertyChanged += (_, _) => OnSelectionChanged();
            }

            StoreNotes = NotesFor(scanned.Value);
        }
        else
        {
            ErrorText = $"{_text.Message("library.picker.error")} {_text.Message(scanned.Error)}";
        }

        _loaded = true;
        Refresh();
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(StoreNotes));
        OnPropertyChanged(nameof(HasStoreNotes));

        // Last, so whoever sees loading end finds the list already built.
        IsLoading = false;
        OnPropertyChanged(nameof(ShowList));
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnHideAddedChanged(bool value) => Refresh();

    partial void OnSortChanged(LibrarySortChoice value) => Refresh();

    private IReadOnlyList<string> NotesFor(LibraryScan scan)
    {
        var notes = new List<string>();
        if (!scan.SteamFound)
        {
            notes.Add(_text.Message("library.picker.noSteam"));
        }

        // GOG Galaxy only exists on Windows here, so its absence elsewhere is not news.
        if (!scan.GogFound && _text.OsKind == OsKind.Windows)
        {
            notes.Add(_text.Message("library.picker.noGog"));
        }

        return notes;
    }

    // Rebuilds what the list shows; the checked state lives on the rows, so a search never loses it.
    private void Refresh()
    {
        var query = SearchText.Trim();
        var shown = _rows.Where(row => (!HideAdded || !row.IsAlreadyAdded)
            && (query.Length == 0
                || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Game.InstallPath.Contains(query, StringComparison.OrdinalIgnoreCase)));
        IEnumerable<LibraryRowViewModel> ordered = Sort.Value switch
        {
            LibrarySort.Store => shown.OrderBy(row => row.Game.Source).ThenBy(Name, StringComparer.OrdinalIgnoreCase),
            LibrarySort.SavesKnown => shown.OrderBy(row => row.Game.SavesFolder is null).ThenBy(Name, StringComparer.OrdinalIgnoreCase),
            _ => shown.OrderBy(Name, StringComparer.OrdinalIgnoreCase),
        };

        VisibleRows.Clear();
        foreach (var row in ordered)
        {
            VisibleRows.Add(row);
        }

        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowNoMatches));
        OnPropertyChanged(nameof(NoMatchesText));
        OnPropertyChanged(nameof(ShowClearSearch));
        OnPropertyChanged(nameof(ShowList));
        OnSelectionChanged();
    }

    private static string Name(LibraryRowViewModel row) => row.Name;

    private void OnSelectionChanged()
    {
        var selected = SelectedCount;
        AddLabel = _text.Message("library.picker.add", ("count", selected));
        CountText = _text.Message("library.picker.count", ("selected", selected), ("total", _rows.Count(row => row.CanSelect)));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(AddLabel));
        OnPropertyChanged(nameof(CountText));
        AddCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in VisibleRows.Where(row => row.CanSelect))
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var row in VisibleRows)
        {
            row.IsSelected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        // A command's Execute runs even when CanExecute is false, so the count is checked here too.
        if (CanAdd())
        {
            Close([.. _rows.Where(row => row.IsSelected).Select(row => row.Game)]);
        }
    }

    private bool CanAdd() => _rows.Any(row => row.IsSelected);

    [RelayCommand]
    private void OpenTemplateList()
    {
        TemplateListRequested = true;
        Close(null);
    }

    [RelayCommand]
    private void Cancel() => Close(null);
}
