using Savepoint.App.Features.Help;

namespace Savepoint.App.Tests.Features.Help;

// An explanation starts collapsed, opens in place, closes the way Escape closes it, and says what the audit's six questions ask.
public class ContextualHelpViewModelTests
{
    private static readonly Savepoint.App.Features.Common.Formatting.UiFormatter _text = HelpText.Formatter();

    [Fact]
    public void An_explanation_starts_collapsed()
    {
        var help = ContextualHelp.NewChanges(_text);

        Assert.False(help.IsOpen);
    }

    [Fact]
    public void Closing_an_open_answer_closes_it_and_asks_for_focus_back_on_the_question()
    {
        var help = ContextualHelp.NewChanges(_text);
        var focusRequests = 0;
        help.FocusQuestionRequested += (_, _) => focusRequests++;
        help.IsOpen = true;

        help.Close();

        Assert.False(help.IsOpen);
        Assert.Equal(1, focusRequests);
    }

    [Fact]
    public void Closing_an_answer_that_is_already_closed_moves_nothing()
    {
        var help = ContextualHelp.NewChanges(_text);
        var focusRequests = 0;
        help.FocusQuestionRequested += (_, _) => focusRequests++;

        help.Close();

        Assert.Equal(0, focusRequests);
    }

    [Fact]
    public void The_two_answers_on_screens_end_with_a_link_to_the_page_that_closes_the_answer_first()
    {
        var opened = 0;
        foreach (var help in new[] { ContextualHelp.Snapshot(_text, () => opened++), ContextualHelp.Space(_text, () => opened++) })
        {
            help.IsOpen = true;

            Assert.True(help.HasLink);
            Assert.Equal("More in How SavePoint works", help.MoreLabel);
            help.OpenHowItWorksCommand.Execute(null);
            Assert.False(help.IsOpen);
        }

        Assert.Equal(2, opened);
    }

    [Fact]
    public void The_four_answers_in_dialogs_have_no_link_because_a_page_opened_behind_a_dialog_could_not_be_used()
    {
        ContextualHelpViewModel[] inDialogs =
        [
            ContextualHelp.NewChanges(_text),
            ContextualHelp.SaveSnapshot(_text),
            ContextualHelp.OlderChoice(_text, "Vanilla"),
            ContextualHelp.Replace(_text, "Before the final boss"),
        ];

        Assert.All(inDialogs, help => Assert.False(help.HasLink));
    }

    [Fact]
    public void What_is_a_snapshot_is_asked_beside_the_History_title()
    {
        var help = ContextualHelp.Snapshot(_text, () => { });

        Assert.Equal("What's a snapshot?", help.Question);
        Assert.Equal(
            "A snapshot is a copy of a profile's game version, mods and saves (or just some of them) at one moment. Choose Go back and play on a snapshot to play from it, or Go back without playing to load it without starting the game.",
            help.Answer);
    }

    [Fact]
    public void What_are_new_changes_names_the_ways_a_folder_changes_and_what_to_do_about_it()
    {
        var help = ContextualHelp.NewChanges(_text);

        Assert.Equal("What are new changes?", help.Question);
        Assert.Equal(
            "Files here changed since the last snapshot. Take a snapshot to keep them, or discard them to restore the snapshot's files.",
            help.Answer);
    }

    [Fact]
    public void What_is_a_save_snapshot_says_the_whole_saves_folder_is_put_in_place_and_the_game_still_loads_the_save()
    {
        var help = ContextualHelp.SaveSnapshot(_text);

        Assert.Equal("What's a save snapshot?", help.Question);
        Assert.Equal(
            "Each choice is your whole saves folder at one moment. You still load the save you want inside the game.",
            help.Answer);
    }

    [Fact]
    public void Which_should_I_pick_names_the_profile_and_says_continued_snapshots_wait_in_Recently_removed()
    {
        var help = ContextualHelp.OlderChoice(_text, "Modded 1.6");

        Assert.Equal("Which should I pick?", help.Question);
        Assert.Equal(
            "Keep this session as a new profile if you might want the newer snapshots of \"Modded 1.6\" again. Continue only if you're done with them. They go to Recently removed.",
            help.Answer);
    }

    [Fact]
    public void When_should_I_replace_names_the_snapshot_and_says_where_what_it_held_goes()
    {
        var help = ContextualHelp.Replace(_text, "Before the final boss");

        Assert.Equal("When should I replace?", help.Question);
        Assert.Equal(
            "Only when you won't want \"Before the final boss\" as it was. What it held goes to Recently removed. Otherwise keep a new snapshot.",
            help.Answer);
    }

    [Fact]
    public void How_much_space_do_snapshots_take_says_the_first_is_big_and_later_ones_are_small()
    {
        var help = ContextualHelp.Space(_text, () => { });

        Assert.Equal("How much space do snapshots take?", help.Question);
        Assert.Equal(
            "A game's first snapshot can take about as much space as the game, and the new-profile check shows the exact amount. Later ones only add what changed, so they're usually small.",
            help.Answer);
    }
}
