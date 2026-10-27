using System.Collections.ObjectModel;
using Savepoint.App.Features.Common.Search;

namespace Savepoint.App.Features.Games;

public sealed partial class GamesScreenViewModel
{
    private bool _searchSilent;

    /// <summary>The search box above the profile table.</summary>
    public SearchBoxViewModel ProfileSearch { get; private set; } = null!;

    /// <summary>The profiles the search leaves, in table order; the table lists these. All of <see cref="Profiles"/> while nothing is typed.</summary>
    public ObservableCollection<ProfileRowViewModel> VisibleProfiles { get; } = [];

    /// <summary>True when the search box shows: the game has profiles to narrow down.</summary>
    public bool ShowSearch => ShowTable;

    /// <summary>True when the table has rows to draw: there are profiles and the search leaves some.</summary>
    public bool ShowRows => ShowTable && VisibleProfiles.Count > 0;

    /// <summary>True when the game has profiles but the search matches none of them.</summary>
    public bool ShowNoProfileMatches => ShowTable && VisibleProfiles.Count == 0;

    /// <summary>True when the table is narrowed by a search and shows rows, so the table header says how many.</summary>
    public bool ShowProfileCount => ShowRows && ProfileSearch.HasText;

    /// <summary>How many profiles the table shows out of how many the game has, such as "3 of 12 profiles".</summary>
    public string ProfileCountText => _deps.Text.Message("search.profiles.count", ("shown", VisibleProfiles.Count), ("total", Profiles.Count));

    /// <summary>"No profiles match.", under the search box.</summary>
    public string NoProfileMatchesText { get; private set; } = string.Empty;

    private void InitializeProfileSearch()
    {
        NoProfileMatchesText = _deps.Text.Message("search.profiles.noMatch");
        ProfileSearch = new SearchBoxViewModel(_deps.Text, _deps.Dispatcher, _deps.Clock, _deps.Text.Message("search.profiles.label"));
        ProfileSearch.Changed += OnProfileSearchChanged;
    }

    private void OnProfileSearchChanged(object? sender, EventArgs e)
    {
        RefilterProfiles();
        if (!_searchSilent)
        {
            ProfileSearch.AnnounceAfterPause(_deps.Text.Message("search.profiles.count", ("shown", VisibleProfiles.Count), ("total", Profiles.Count)));
        }
    }

    // The name and the notes are what a profile is found by; both can change when the rows are read again.
    private void RefilterProfiles()
    {
        var query = ProfileSearch.Query;
        ListSync.To(VisibleProfiles, [.. Profiles.Where(row => query.IsEmpty || query.Matches(SearchTarget.Of(row.Name, row.Notes)))]);
        RaiseSearchState();
    }

    private void RaiseSearchState()
    {
        OnPropertyChanged(nameof(ShowSearch));
        OnPropertyChanged(nameof(ShowRows));
        OnPropertyChanged(nameof(ShowNoProfileMatches));
        OnPropertyChanged(nameof(ShowProfileCount));
        OnPropertyChanged(nameof(ProfileCountText));
    }

    // A search belongs to the game it was typed for.
    private void ClearProfileSearch()
    {
        _searchSilent = true;
        try
        {
            ProfileSearch.Text = string.Empty;
        }
        finally
        {
            _searchSilent = false;
        }
    }
}
