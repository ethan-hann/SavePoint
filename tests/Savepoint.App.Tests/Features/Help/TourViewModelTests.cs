using Savepoint.App.Features.Help;

namespace Savepoint.App.Tests.Features.Help;

// Four steps that each point at a real control, skippable at every one, with the words of the audit's design in D44's vocabulary.
public class TourViewModelTests
{
    private static TourViewModel Started(string game = "Skyrim Special Edition")
    {
        var tour = new TourViewModel(HelpText.Formatter());
        tour.Start(game);
        return tour;
    }

    private static string[] Walk(TourViewModel tour)
    {
        var titles = new List<string> { tour.Current.Title };
        for (var step = 1; step < 4; step++)
        {
            tour.NextCommand.Execute(null);
            titles.Add(tour.Current.Title);
        }

        return [.. titles];
    }

    [Fact]
    public void The_tour_is_off_until_it_is_started()
    {
        var tour = new TourViewModel(HelpText.Formatter());

        Assert.False(tour.IsActive);
    }

    [Fact]
    public void Starting_it_shows_the_first_step_which_names_the_game_and_says_where_it_is()
    {
        var tour = Started();

        Assert.True(tour.IsActive);
        Assert.Equal(TourStep.Profile, tour.Current.Step);
        Assert.Equal("This is a profile", tour.Current.Title);
        Assert.Equal(
            "A profile is one way of playing Skyrim Special Edition, with its own game version, mods and saves. SavePoint keeps snapshots of each profile: copies of all three as they were at one moment.",
            tour.Current.Body);
        Assert.Equal("Step 1 of 4", tour.Current.Position);
        Assert.Equal("Tour, step 1 of 4: This is a profile", tour.Current.AutomationName);
        Assert.False(tour.Current.CanGoBack);
        Assert.Equal("Next", tour.Current.NextLabel);
        Assert.False(tour.Current.HasFindAgain);
    }

    [Fact]
    public void The_four_steps_are_the_profile_Play_History_and_New_profile_in_that_order()
    {
        var tour = Started();
        var steps = new List<TourStep> { tour.Current.Step };
        for (var step = 1; step < 4; step++)
        {
            tour.NextCommand.Execute(null);
            steps.Add(tour.Current.Step);
        }

        Assert.Equal([TourStep.Profile, TourStep.Play, TourStep.History, TourStep.NewProfile], steps);
        Assert.Equal(["This is a profile", "Play puts the right files in place", "Go back any time", "Try something new safely"], Walk(Started()));
    }

    [Fact]
    public void Each_step_says_what_the_control_it_points_at_does()
    {
        var tour = Started();
        var bodies = new List<string> { tour.Current.Body };
        for (var step = 1; step < 4; step++)
        {
            tour.NextCommand.Execute(null);
            bodies.Add(tour.Current.Body);
        }

        Assert.Equal(
            "Press Play and SavePoint makes your game folders match this profile, then starts the game. When you close the game, it asks whether to keep the session as a new snapshot.",
            bodies[1]);
        Assert.Equal("History lists every snapshot. Choose Go back to this on one to play from that moment, even after the game updates.", bodies[2]);
        Assert.Equal("Want a different mod list or game version? Make another profile. Each keeps its own snapshots, and Play switches between them.", bodies[3]);
    }

    [Fact]
    public void The_position_and_the_card_name_follow_the_step_and_Back_is_offered_after_the_first()
    {
        var tour = Started();
        tour.NextCommand.Execute(null);

        Assert.Equal("Step 2 of 4", tour.Current.Position);
        Assert.Equal("Tour, step 2 of 4: Play puts the right files in place", tour.Current.AutomationName);
        Assert.True(tour.Current.CanGoBack);
        Assert.True(tour.BackCommand.CanExecute(null));
    }

    [Fact]
    public void Back_goes_to_the_step_before_and_is_off_on_the_first_step()
    {
        var tour = Started();
        Assert.False(tour.BackCommand.CanExecute(null));
        tour.NextCommand.Execute(null);
        tour.NextCommand.Execute(null);

        tour.BackCommand.Execute(null);

        Assert.Equal(TourStep.Play, tour.Current.Step);
        tour.BackCommand.Execute(null);
        Assert.Equal(TourStep.Profile, tour.Current.Step);
        Assert.False(tour.BackCommand.CanExecute(null));
    }

    [Fact]
    public void The_last_step_offers_Done_and_says_where_the_tour_can_be_seen_again()
    {
        var tour = Started();
        for (var step = 1; step < 4; step++)
        {
            tour.NextCommand.Execute(null);
        }

        Assert.Equal("Done", tour.Current.NextLabel);
        Assert.True(tour.Current.HasFindAgain);
        Assert.Equal("You can see this again in Settings > Help.", tour.Current.FindAgain);
    }

    [Fact]
    public void Done_on_the_last_step_ends_the_tour_once()
    {
        var tour = Started();
        var ended = 0;
        tour.Ended += (_, _) => ended++;
        for (var step = 1; step < 4; step++)
        {
            tour.NextCommand.Execute(null);
        }

        tour.NextCommand.Execute(null);

        Assert.False(tour.IsActive);
        Assert.Equal(1, ended);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Skip_tour_ends_it_from_every_step_once(int stepsIn)
    {
        var tour = Started();
        var ended = 0;
        tour.Ended += (_, _) => ended++;
        for (var step = 0; step < stepsIn; step++)
        {
            tour.NextCommand.Execute(null);
        }

        tour.SkipCommand.Execute(null);

        Assert.False(tour.IsActive);
        Assert.Equal(1, ended);
    }

    [Fact]
    public void Every_step_is_announced_as_it_is_shown_and_none_more()
    {
        var tour = new TourViewModel(HelpText.Formatter());
        var shown = new List<string>();
        tour.StepShown += (_, _) => shown.Add(tour.Current.Title);

        tour.Start("Skyrim Special Edition");
        tour.NextCommand.Execute(null);
        tour.BackCommand.Execute(null);

        Assert.Equal(["This is a profile", "Play puts the right files in place", "This is a profile"], shown);
    }

    [Fact]
    public void An_ended_tour_can_be_started_again_from_the_first_step()
    {
        var tour = Started();
        tour.NextCommand.Execute(null);
        tour.SkipCommand.Execute(null);

        tour.Start("Satisfactory");

        Assert.True(tour.IsActive);
        Assert.Equal(TourStep.Profile, tour.Current.Step);
        Assert.Contains("Satisfactory", tour.Current.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Starting_a_tour_that_is_running_begins_it_again_without_ending_it()
    {
        var tour = Started();
        var ended = 0;
        tour.Ended += (_, _) => ended++;
        tour.NextCommand.Execute(null);

        tour.Start("Skyrim Special Edition");

        Assert.Equal(TourStep.Profile, tour.Current.Step);
        Assert.Equal(0, ended);
    }

    [Fact]
    public void Buttons_pressed_while_the_tour_is_off_do_nothing()
    {
        var tour = new TourViewModel(HelpText.Formatter());
        var ended = 0;
        var shown = 0;
        tour.Ended += (_, _) => ended++;
        tour.StepShown += (_, _) => shown++;

        tour.NextCommand.Execute(null);
        tour.BackCommand.Execute(null);
        tour.SkipCommand.Execute(null);

        Assert.False(tour.IsActive);
        Assert.Equal(0, ended);
        Assert.Equal(0, shown);
        Assert.Equal(TourStep.Profile, tour.Current.Step);
    }
}
