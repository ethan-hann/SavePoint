using System.Collections.ObjectModel;
using Savepoint.App.Features.Common.Search;

namespace Savepoint.App.Features.Shell;

public sealed partial class ShellViewModel
{
    private bool _searchSilent;

    /// <summary>The search box above the games in the rail, and in the library shelf's header.</summary>
    public SearchBoxViewModel GameSearch { get; private set; } = null!;

    /// <summary>The games the search leaves, in list order; the rail and the shelf list these. All of <see cref="Games"/> while nothing is typed.</summary>
    public ObservableCollection<GameRowViewModel> VisibleGames { get; } = [];

    /// <summary>True when the rail has room for the search box: there are games, and the window is wide enough to keep the rail's names.</summary>
    public bool ShowRailSearch => Games.Count > 0 && !IsCompact;

    /// <summary>True when there are games but the search matches none of them.</summary>
    public bool ShowNoGameMatches => Games.Count > 0 && VisibleGames.Count == 0;

    /// <summary>"No games match.", under the search box.</summary>
    public string NoGameMatchesText { get; private set; } = string.Empty;

    private void InitializeGameSearch()
    {
        NoGameMatchesText = _deps.Text.Message("search.games.noMatch");
        GameSearch = new SearchBoxViewModel(_deps.Text, _deps.Dispatcher, _deps.Clock ?? TimeProvider.System, _deps.Text.Message("search.games.label"), _deps.Text.Message("search.shortcut.findGames"));
        GameSearch.Changed += OnGameSearchChanged;
    }

    private void OnGameSearchChanged(object? sender, EventArgs e)
    {
        RefilterGames();
        if (!_searchSilent)
        {
            GameSearch.AnnounceAfterPause(_deps.Text.Message("search.games.count", ("shown", VisibleGames.Count), ("total", Games.Count)));
        }
    }

    // A collapsed icon rail has no search box, so it lists every game rather than filter by text nobody can see.
    private List<GameRowViewModel> ListedBySearch(IEnumerable<GameRowViewModel> rows)
    {
        var query = IsSidebarLayout && IsCompact ? SearchQuery.Empty : GameSearch.Query;
        return [.. rows.Where(row => query.IsEmpty || query.Matches(row.SearchTarget))];
    }

    private void RefilterGames()
    {
        ListSync.To(VisibleGames, ListedBySearch(Games));
        OnPropertyChanged(nameof(ShowNoGameMatches));
        OnPropertyChanged(nameof(ShowRailSearch));

        // A list box drops its selection when the row leaves it, so it is told again to show the selected row once that row is back.
        OnPropertyChanged(nameof(SelectedGameRow));
    }

    // Opening a game the search hides would leave nothing marked in the list, so the search goes first.
    private void RevealGame(GameRowViewModel row)
    {
        if (GameSearch.HasText && !VisibleGames.Contains(row))
        {
            _searchSilent = true;
            try
            {
                GameSearch.Text = string.Empty;
            }
            finally
            {
                _searchSilent = false;
            }
        }
    }
}
