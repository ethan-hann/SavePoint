using Savepoint.App.Features.Help;

namespace Savepoint.App.Tests.Features.Help;

// The page teaches the few ideas the app needs, in the order a player meets them, in the app's own words.
public class HowItWorksViewModelTests
{
    private static HowItWorksViewModel Page(Action? close = null) => new(HelpText.Formatter(), close ?? (() => { }));

    // The thirteen topics the page has; a test that reads one of them says so first, so a missing topic is a plain failure.
    private static IReadOnlyList<HowItWorksTopic> Topics()
    {
        var topics = Page().Topics;
        Assert.Equal(14, topics.Count);
        return topics;
    }

    private static HowItWorksTopic TopicFor(HowItWorksSection section) =>
        Assert.Single(Page().Topics, topic => topic.Section == section);

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
                "Mods inside and outside the game folder",
                "How the game starts",
                "Files to leave out",
                "Cloud saves",
                "What can't be undone",
            ],
            Page().Topics.Select(topic => topic.Title));
    }

    [Fact]
    public void Every_topic_has_a_text_and_the_first_nine_have_a_picture()
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
                HelpPicture.None,
                HelpPicture.None,
                HelpPicture.None,
                HelpPicture.None,
            ],
            topics.Select(topic => topic.Picture));
    }

    [Fact]
    public void Cloud_saves_says_changed_saves_are_kept_removed_saves_come_back_and_a_second_PC_asks_which_to_keep()
    {
        var cloud = TopicFor(HowItWorksSection.CloudSaves);

        Assert.Equal("Cloud saves", cloud.Title);
        Assert.Contains("Steam and GOG Galaxy", cloud.Body, StringComparison.Ordinal);
        Assert.Contains("Saves that SavePoint changes are kept and uploaded", cloud.Body, StringComparison.Ordinal);
        Assert.Contains("Saves that SavePoint removes come back at the next launch", cloud.Body, StringComparison.Ordinal);
        Assert.Contains("Cloud Conflict", cloud.Body, StringComparison.Ordinal);
        Assert.Contains("choose Local Save", cloud.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Cloud_saves_says_when_to_turn_them_off_in_the_store()
    {
        var cloud = TopicFor(HowItWorksSection.CloudSaves);

        var advice = Assert.Single(cloud.Notes);
        Assert.Contains("Turn off cloud saves for the game in Steam or GOG Galaxy", advice, StringComparison.Ordinal);
        Assert.Contains("go back to a snapshot from before a save, or switch to a profile with fewer saves", advice, StringComparison.Ordinal);
        Assert.Contains("before you next start the game", advice, StringComparison.Ordinal);
        Assert.Contains("a save the store brings back becomes part of that profile's next snapshot", advice, StringComparison.Ordinal);
    }

    [Fact]
    public void On_Linux_cloud_saves_name_only_Steam_because_GOG_Galaxy_does_not_run_there()
    {
        var cloud = Assert.Single(new HowItWorksViewModel(HelpText.Formatter("linux"), () => { }).Topics, topic => topic.Section == HowItWorksSection.CloudSaves);

        Assert.DoesNotContain("GOG", cloud.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("GOG", Assert.Single(cloud.Notes), StringComparison.Ordinal);
        Assert.StartsWith("Steam can keep your saves in the cloud.", cloud.Body, StringComparison.Ordinal);
        Assert.StartsWith("Turn off cloud saves for the game in Steam if", Assert.Single(cloud.Notes), StringComparison.Ordinal);
    }

    [Fact]
    public void The_mods_topic_tells_a_mods_folder_outside_the_game_folder_from_mod_files_inside_it()
    {
        var mods = TopicFor(HowItWorksSection.ModsFolders);

        Assert.Equal("Mods inside and outside the game folder", mods.Title);
        Assert.StartsWith("Most games load mods from inside their own folder", mods.Body, StringComparison.Ordinal);
        Assert.Contains("7 Days to Die", mods.Body, StringComparison.Ordinal);
        var note = Assert.Single(mods.Notes);
        Assert.StartsWith("Mod files inside the game folder:", note, StringComparison.Ordinal);
        Assert.Contains("never changes what SavePoint saves", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Files_to_leave_out_names_both_lists_what_star_and_double_star_mean_and_warns_against_matching_saves()
    {
        var files = TopicFor(HowItWorksSection.FilesToLeaveOut);

        Assert.Equal("Files to leave out", files.Title);
        Assert.Equal(
            [
                "Files to leave out of every folder: rules that cover the game's own folder, your saves folder and your mods folder.",
                "Files to leave out of the game's own folder: rules that never cover your saves folder. New games start with rules for logs and crash reports, which a game rewrites while it runs and SavePoint would take for a game update.",
            ],
            files.Items);
        Assert.DoesNotContain("logs", files.Body, StringComparison.Ordinal);
        Assert.Contains("A rule tells SavePoint to skip the files that match it", files.Body, StringComparison.Ordinal);
        Assert.Contains("* stands for any name", files.Body, StringComparison.Ordinal);
        Assert.Contains("** stands for any folder", files.Body, StringComparison.Ordinal);
        Assert.Equal(["Don't add a rule that matches your saves, or profiles stop switching them."], files.Notes);
    }

    [Fact]
    public void How_the_game_starts_says_running_the_program_is_safer_than_Through_Steam_because_Steam_may_update_the_game()
    {
        var starts = TopicFor(HowItWorksSection.HowTheGameStarts);

        Assert.Equal("How the game starts", starts.Title);
        Assert.Contains("Run the .exe", starts.Body, StringComparison.Ordinal);
        Assert.Contains("Through Steam", starts.Body, StringComparison.Ordinal);
        Assert.Contains("Steam may update the game right after SavePoint puts your version back", starts.Body, StringComparison.Ordinal);
        Assert.Contains("Running the .exe avoids that.", starts.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Linux", starts.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void On_Linux_how_the_game_starts_says_to_start_Steam_first_and_that_Through_Steam_can_update_the_game()
    {
        var starts = Assert.Single(new HowItWorksViewModel(HelpText.Formatter("linux"), () => { }).Topics, topic => topic.Section == HowItWorksSection.HowTheGameStarts);

        Assert.Contains("\"Run the program\" starts the game's own program.", starts.Body, StringComparison.Ordinal);
        Assert.Contains("Games installed by Steam on Linux usually need Steam running, so start Steam first, then run the program. Through Steam can update the game first and replace the version you switched to.", starts.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("safer choice", starts.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("avoids that", starts.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void How_the_game_starts_explains_what_knowing_when_you_have_stopped_playing_is_for()
    {
        var starts = TopicFor(HowItWorksSection.HowTheGameStarts);

        var note = Assert.Single(starts.Notes);
        Assert.StartsWith("Knowing when you've stopped playing", note, StringComparison.Ordinal);
        Assert.Contains("asks what to do with your session", note, StringComparison.Ordinal);
        Assert.Contains("More settings", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_four_linked_topics_have_a_section_name()
    {
        Assert.Equal(
            [HowItWorksSection.ModsFolders, HowItWorksSection.HowTheGameStarts, HowItWorksSection.FilesToLeaveOut, HowItWorksSection.CloudSaves],
            Page().Topics.Where(topic => topic.Section is not null).Select(topic => topic.Section!.Value));
    }

    [Fact]
    public void A_page_opened_at_a_section_remembers_it_and_a_plain_page_does_not()
    {
        var text = HelpText.Formatter();

        Assert.Null(Page().Section);
        Assert.Equal(HowItWorksSection.FilesToLeaveOut, new HowItWorksViewModel(text, () => { }, section: HowItWorksSection.FilesToLeaveOut).Section);
    }

    [Fact]
    public void Asking_an_open_page_for_a_section_moves_it_there_and_says_so()
    {
        var page = Page();
        var requests = 0;
        page.SectionRequested += (_, _) => requests++;

        page.ShowSection(HowItWorksSection.CloudSaves);

        Assert.Equal(HowItWorksSection.CloudSaves, page.Section);
        Assert.Equal(1, requests);
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
        Assert.Contains("Cancel stops a task that can be stopped", activity.Body, StringComparison.Ordinal);
        Assert.Contains("Finished tasks stay listed after SavePoint closes", activity.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("job", activity.Body, StringComparison.OrdinalIgnoreCase);
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
                "SavePoint asks a second time before each of these. It never sends your files anywhere.",
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
