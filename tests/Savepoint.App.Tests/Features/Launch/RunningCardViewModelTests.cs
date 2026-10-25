using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Launch;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;

namespace Savepoint.App.Tests.Features.Launch;

public class RunningCardViewModelTests
{
    private readonly ManualTimeProvider _clock = new(ScenarioBuilder.DefaultNow);
    private readonly UiFormatter _text;
    private readonly List<int> _finished = [];
    private bool _finishAnswer = true;

    public RunningCardViewModelTests()
    {
        _text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), _clock, "windows", TimeZoneInfo.Utc);
    }

    private RunningCardViewModel Create(DateTimeOffset? startedAt = null) =>
        new(
            "Skyrim Special Edition",
            "Modded playthrough - 7-2-26",
            startedAt ?? SampleScenario.RunningSince,
            _text,
            _clock,
            new ImmediateUiDispatcher(),
            () =>
            {
                _finished.Add(1);
                return Task.FromResult(_finishAnswer);
            });

    [Fact]
    public void The_card_names_the_game_the_profile_and_how_long_ago_it_started()
    {
        using var card = Create();

        Assert.Equal("Skyrim Special Edition is running", card.Title);
        Assert.Equal("Modded playthrough - 7-2-26 · started 41 minutes ago", card.Subtitle);
    }

    [Fact]
    public void Under_a_minute_it_says_just_now()
    {
        using var card = Create(ScenarioBuilder.DefaultNow - TimeSpan.FromSeconds(20));

        Assert.Equal("Modded playthrough - 7-2-26 · started just now", card.Subtitle);
    }

    [Fact]
    public void A_single_minute_is_singular_and_hours_show_with_the_minutes()
    {
        using var minute = Create(ScenarioBuilder.DefaultNow - TimeSpan.FromMinutes(1));
        using var lengthy = Create(ScenarioBuilder.DefaultNow - new TimeSpan(2, 14, 0));

        Assert.EndsWith("started 1 minute ago", minute.Subtitle);
        Assert.EndsWith("started 2 hours 14 minutes ago", lengthy.Subtitle);
    }

    [Fact]
    public void The_subtitle_updates_each_minute_as_time_passes()
    {
        using var card = Create();

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.EndsWith("started 42 minutes ago", card.Subtitle);

        _clock.Advance(TimeSpan.FromMinutes(3));
        Assert.EndsWith("started 45 minutes ago", card.Subtitle);
    }

    [Fact]
    public void The_refresh_lines_up_with_the_minute_the_game_started_not_with_when_the_card_appeared()
    {
        using var card = Create(ScenarioBuilder.DefaultNow - TimeSpan.FromSeconds(30));
        Assert.EndsWith("started just now", card.Subtitle);

        _clock.Advance(TimeSpan.FromSeconds(30));

        Assert.EndsWith("started 1 minute ago", card.Subtitle);
    }

    [Fact]
    public void Disposing_the_card_stops_the_refresh()
    {
        var card = Create();
        Assert.Equal(1, _clock.ActiveTimers);

        card.Dispose();

        Assert.Equal(0, _clock.ActiveTimers);
    }

    [Fact]
    public async Task Im_done_playing_tells_the_service_and_cannot_be_pressed_twice()
    {
        using var card = Create();

        await card.DonePlayingCommand.ExecuteAsync(null);

        Assert.Single(_finished);
        Assert.True(card.IsFinishing);
        Assert.False(card.DonePlayingCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_finish_the_service_could_not_take_lets_the_user_press_the_button_again()
    {
        _finishAnswer = false;
        using var card = Create();

        await card.DonePlayingCommand.ExecuteAsync(null);

        Assert.False(card.IsFinishing);
        Assert.True(card.DonePlayingCommand.CanExecute(null));
    }
}
