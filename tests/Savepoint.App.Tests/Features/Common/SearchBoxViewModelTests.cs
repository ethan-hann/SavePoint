using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Search;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;

namespace Savepoint.App.Tests.Features.Common;

public class SearchBoxViewModelTests
{
    private readonly ManualTimeProvider _clock = new();

    private SearchBoxViewModel Box() => new(
        new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), _clock, "windows", TimeZoneInfo.Utc),
        new ImmediateUiDispatcher(),
        _clock,
        "Search games");

    [Fact]
    public void An_empty_box_has_no_text_and_nothing_to_clear()
    {
        var box = Box();

        Assert.False(box.HasText);
        Assert.True(box.Query.IsEmpty);
        Assert.False(box.ClearCommand.CanExecute(null));
        Assert.Equal("Search games", box.Label);
        Assert.Equal("Clear search", box.ClearName);
    }

    [Fact]
    public void Typing_reads_the_words_at_once_and_tells_the_owner()
    {
        var box = Box();
        var told = 0;
        box.Changed += (_, _) => told++;

        box.Text = "sky";

        Assert.True(box.HasText);
        Assert.False(box.Query.IsEmpty);
        Assert.True(box.Query.Matches(SearchTarget.ForName("Skyrim")));
        Assert.Equal(1, told);
        Assert.True(box.ClearCommand.CanExecute(null));
    }

    [Fact]
    public void The_owner_sees_the_new_query_when_it_is_told()
    {
        var box = Box();
        var seen = false;
        box.Changed += (_, _) => seen = box.Query.Matches(SearchTarget.ForName("Skyrim"));

        box.Text = "sky";

        Assert.True(seen);
    }

    [Fact]
    public void Clearing_empties_the_box_and_tells_the_owner()
    {
        var box = Box();
        box.Text = "sky";
        var told = 0;
        box.Changed += (_, _) => told++;

        box.ClearCommand.Execute(null);

        Assert.Equal(string.Empty, box.Text);
        Assert.False(box.HasText);
        Assert.True(box.Query.IsEmpty);
        Assert.Equal(1, told);
    }

    [Fact]
    public void Clearing_says_so_for_the_view_after_the_text_is_gone_but_emptying_the_text_directly_does_not()
    {
        var box = Box();
        box.Text = "sky";
        var textWhenTold = "unset";
        var told = 0;
        box.Cleared += (_, _) =>
        {
            told++;
            textWhenTold = box.Text;
        };

        box.Text = string.Empty;
        Assert.Equal(0, told);

        box.Text = "sky";
        box.ClearCommand.Execute(null);

        Assert.Equal(1, told);
        Assert.Equal(string.Empty, textWhenTold);
    }

    [Fact]
    public void An_owner_whose_own_clear_ran_can_ask_the_view_to_take_focus_back()
    {
        var box = Box();
        var told = 0;
        box.Cleared += (_, _) => told++;

        box.ReturnFocus();

        Assert.Equal(1, told);
    }

    [Fact]
    public void A_count_is_not_announced_until_typing_pauses()
    {
        var box = Box();

        box.AnnounceAfterPause("3 of 120 games");
        _clock.Advance(SearchBoxViewModel.AnnouncePause - TimeSpan.FromMilliseconds(1));
        Assert.Equal(string.Empty, box.Announcement);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("3 of 120 games", box.Announcement);
    }

    [Fact]
    public void Typing_on_restarts_the_pause_and_only_the_last_count_is_said()
    {
        var box = Box();
        var said = new List<string>();
        box.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchBoxViewModel.Announcement) && box.Announcement.Length > 0)
            {
                said.Add(box.Announcement);
            }
        };

        box.AnnounceAfterPause("40 of 120 games");
        _clock.Advance(TimeSpan.FromMilliseconds(400));
        box.AnnounceAfterPause("12 of 120 games");
        _clock.Advance(TimeSpan.FromMilliseconds(400));
        box.AnnounceAfterPause("3 of 120 games");
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal(["3 of 120 games"], said);
    }

    [Fact]
    public void The_same_count_is_said_again_after_a_new_search()
    {
        var box = Box();
        var raised = 0;
        box.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchBoxViewModel.Announcement) && box.Announcement.Length > 0)
            {
                raised++;
            }
        };

        box.AnnounceAfterPause("3 of 120 games");
        _clock.Advance(SearchBoxViewModel.AnnouncePause);
        box.AnnounceAfterPause("3 of 120 games");
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal(2, raised);
    }

    [Fact]
    public void Disposing_drops_a_count_still_waiting()
    {
        var box = Box();

        box.AnnounceAfterPause("3 of 120 games");
        box.Dispose();
        _clock.Advance(SearchBoxViewModel.AnnouncePause);

        Assert.Equal(string.Empty, box.Announcement);
    }
}
