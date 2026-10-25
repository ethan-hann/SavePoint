using Savepoint.App.Features.Help;

namespace Savepoint.App.Tests.Features.Help;

// The page teaches the few ideas the app needs, in the order a player meets them, in the app's own words.
public class HowItWorksViewModelTests
{
    private static HowItWorksViewModel Page(Action? close = null) => new(HelpText.Formatter(), close ?? (() => { }));

    // The ten topics the page has; a test that reads one of them says so first, so a missing topic is a plain failure.
    private static IReadOnlyList<HowItWorksTopic> Topics()
    {
        var topics = Page().Topics;
        Assert.Equal(10, topics.Count);
        return topics;
    }

    [Fact]
    public void The_page_is_named_for_what_it_is_and_opens_with_a_sentence_about_why()
    {
        var page = Page();

        Assert.Equal("How SavePoint works", page.Title);
        Assert.Equal("Back", page.BackName);
        Assert.Equal("SavePoint keeps the game setups that work, so you can go back to them.", page.Intro);
    }

    [Fact]
    public void The_topics_run_from_snapshots_through_playing_to_what_cannot_be_undone()
    {
        Assert.Equal(
            [
                "Games, profiles and snapshots",
                "Saves and snapshots are different",
                "What Play does",
                "New changes",
                "After you play",
                "Going back to an older snapshot",
                "Recently removed",
                "Activity",
                "The snapshot folder and freeing up space",
                "What can't be undone",
            ],
            Page().Topics.Select(topic => topic.Title));
    }

    [Fact]
    public void Every_topic_has_a_text_and_all_but_the_last_have_a_picture()
    {
        var topics = Topics();

        Assert.All(topics, topic => Assert.False(string.IsNullOrWhiteSpace(topic.Body)));
        Assert.Equal(
            [
                HelpPicture.Basics,
                HelpPicture.Saves,
                HelpPicture.Play,
                HelpPicture.NewChanges,
                HelpPicture.AfterPlay,
                HelpPicture.Older,
                HelpPicture.Removed,
                HelpPicture.Activity,
                HelpPicture.Space,
                HelpPicture.None,
            ],
            topics.Select(topic => topic.Picture));
    }

    [Fact]
    public void The_basics_teach_game_profile_and_snapshot_and_keep_save_for_the_games_own_files()
    {
        var topics = Topics();

        Assert.Equal(
            "A game is its install folder plus its saves folder. A profile is one way of playing it, such as Vanilla or Modded 1.6, with its own game version, mods and saves. A snapshot is a copy of a profile at one moment, taken when you create a profile and each time you keep a session.",
            topics[0].Body);
        Assert.Equal(
            "A save is the game's own file, made when you save in the game. A save snapshot holds your whole saves folder at one moment. Going back to one puts those files in place, and you still load the save inside the game.",
            topics[1].Body);
    }

    [Fact]
    public void Play_and_the_check_before_it_say_nothing_changes_until_the_player_agrees()
    {
        var play = Topics()[2];

        Assert.Contains("shows what first", play.Body, StringComparison.Ordinal);
        Assert.Contains("nothing changes until you press Switch and play", play.Body, StringComparison.Ordinal);
        Assert.Contains("choose Switch to this profile from the profile's menu", play.Body, StringComparison.Ordinal);
        Assert.Contains("doesn't ask what to do with a session afterwards", play.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeping_or_replacing_a_session_and_going_back_say_where_removed_snapshots_go()
    {
        var topics = Topics();
        var afterPlay = topics.Single(topic => topic.Title == "After you play");
        var older = topics.Single(topic => topic.Title == "Going back to an older snapshot");
        var removed = topics.Single(topic => topic.Title == "Recently removed");

        Assert.Contains("Keep it as a new snapshot", afterPlay.Body, StringComparison.Ordinal);
        Assert.Contains("replace the latest snapshot", afterPlay.Body, StringComparison.Ordinal);
        Assert.Contains("Not now", afterPlay.Body, StringComparison.Ordinal);
        Assert.Contains("Keep this session as a new profile", older.Body, StringComparison.Ordinal);
        Assert.Contains("continue the profile from your session", older.Body, StringComparison.Ordinal);
        Assert.Contains("snapshots go to Recently removed", older.Body, StringComparison.Ordinal);
        Assert.Contains("until you next free up space", removed.Body, StringComparison.Ordinal);
        Assert.Contains("bring any back as a new profile", removed.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Activity_is_a_topic_of_its_own_that_names_what_it_lists_and_what_Details_and_Cancel_do()
    {
        var activity = Topics().Single(topic => topic.Title == "Activity");

        Assert.Contains("what SavePoint is doing and has done", activity.Body, StringComparison.Ordinal);
        Assert.Contains("Details", activity.Body, StringComparison.Ordinal);
        Assert.Contains("Cancel stops a job that can be stopped", activity.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void What_cannot_be_undone_is_a_plain_list_and_does_not_count_replace_or_continue_among_them()
    {
        var last = Topics()[^1];

        Assert.Equal(
            [
                "Discarding new changes.",
                "Freeing up space, which deletes what is in Recently removed for good.",
                "Deleting a profile and the snapshots only it holds, or deleting a game and all its snapshots.",
                "Removing all SavePoint data.",
                "Starting with an empty snapshot folder when SavePoint can't reach the old one.",
            ],
            last.Items);
        Assert.DoesNotContain(last.Items, item => item.Contains("Replac", StringComparison.Ordinal) || item.Contains("Continu", StringComparison.Ordinal));
        Assert.Equal(
            [
                "Replacing a snapshot and continuing a profile are not on this list, because Recently removed lets you undo them.",
                "SavePoint asks a second time before each of these.",
            ],
            last.Notes);
    }

    [Fact]
    public void Back_runs_the_close_action()
    {
        var closed = 0;
        var page = Page(() => closed++);

        page.BackCommand.Execute(null);

        Assert.Equal(1, closed);
    }
}
