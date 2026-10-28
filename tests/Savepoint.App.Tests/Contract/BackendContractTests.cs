using Savepoint.App.Tests.Fakes;

namespace Savepoint.App.Tests.Contract;

// The fake backend is what every screen test is written against, so it has to say what the real one says.
public class BackendContractTests(ContractRuns runs) : IClassFixture<ContractRuns>
{
    [Fact]
    public void The_real_backend_tells_the_same_first_run_story_as_the_fake()
    {
        var expected = runs.Fake.Shared;
        var actual = runs.Real.Shared;

        // Compared step by step, so a failure names the step where the two part ways.
        var differences = expected.Zip(actual, (fakeSaw, realSaw) => (fakeSaw, realSaw))
            .Where(pair => pair.fakeSaw != pair.realSaw)
            .Select(pair => $"fake: {pair.fakeSaw}{Environment.NewLine}real: {pair.realSaw}")
            .ToList();
        Assert.True(differences.Count == 0 && expected.Count == actual.Count, string.Join(Environment.NewLine + Environment.NewLine, differences));
    }

    // The screens learn about jobs and sessions only from these two event streams, so a subscriber must see the same states in the same order.
    [Fact]
    public void The_real_backend_sends_the_same_job_and_session_events_as_the_fake()
    {
        Assert.True(
            runs.Fake.Events.SequenceEqual(runs.Real.Events),
            $"fake:{Environment.NewLine}{string.Join(Environment.NewLine, runs.Fake.Events)}{Environment.NewLine}real:{Environment.NewLine}{string.Join(Environment.NewLine, runs.Real.Events)}");
    }

    // A story that raised no events in one backend would still match itself, so the streams that prove it ran are named here.
    [Fact]
    public void The_event_streams_of_the_story_follow_a_launch_from_start_to_a_saved_play()
    {
        foreach (var events in new[] { runs.Fake.Events, runs.Real.Events })
        {
            Assert.Contains(events, line => line.StartsWith("Launch of alpha-1: Running > Succeeded; revisions rise=True", StringComparison.Ordinal));
            Assert.Contains(events, line => line.StartsWith("Capture of alpha-1: Running > Succeeded", StringComparison.Ordinal));
            Assert.Contains(events, line => line.StartsWith("session of alpha-1: Preparing > Running > Exited > Completed; revisions rise=True", StringComparison.Ordinal));
        }
    }

    // A screen that cancels a read on navigation takes "cancelled" for "nothing to show", so every read answers it in both backends.
    [Fact]
    public void The_shared_story_answers_every_read_given_a_cancelled_token_with_the_cancelled_error()
    {
        foreach (var story in new[] { runs.Fake.Shared, runs.Real.Shared })
        {
            var cancelled = story.Where(line => line.Contains("with a cancelled token: ", StringComparison.Ordinal)).ToList();
            Assert.Equal(24, cancelled.Count);
            Assert.All(cancelled, line => Assert.EndsWith(": error error.operation.cancelled", line, StringComparison.Ordinal));
        }
    }

    // Tests that need a launch preview take one the real backend gave instead of building it by hand, so the files in Fakes/Previews have
    // to be what the real backend still says. Run once with SAVEPOINT_CAPTURE_PREVIEWS=1 to write them again after a change on purpose.
    [Fact]
    public void The_launch_previews_the_fake_plays_back_are_the_ones_the_real_backend_gave()
    {
        Assert.Equal(["finished-switch", "profile-tip-clean", "stopped-switch"], runs.Real.Previews.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, real) in runs.Real.Previews)
        {
            var path = Path.Combine(LaunchPreviewFixtures.Folder, name + ".json");
            if (Environment.GetEnvironmentVariable(LaunchPreviewFixtures.CaptureVariable) == "1")
            {
                Directory.CreateDirectory(LaunchPreviewFixtures.Folder);
                File.WriteAllText(path, real);
            }

            Assert.True(File.Exists(path), $"The preview '{name}' has no file. Run once with {LaunchPreviewFixtures.CaptureVariable}=1 to write it.");
            Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), real.ReplaceLineEndings("\n"));
            Assert.Equal(real, runs.Fake.Previews[name]);
        }
    }

    // A story that suggested nothing, or listed no history, in both backends would still match, so the answers it has to reach are named here.
    [Fact]
    public void The_shared_story_suggests_programs_by_folder_name_and_size_and_lists_what_each_save_recorded()
    {
        foreach (var story in new[] { runs.Fake.Shared, runs.Real.Shared })
        {
            Assert.Contains("program suggested for a folder named after one of two equal programs: zebraquest.exe", story);
            Assert.Contains("program suggested for a big program beside a small one: zeta-big.exe", story);
            Assert.Contains("program suggested for a folder with no program: none", story);
            Assert.Contains(story, line => line.StartsWith("history after the first save: 1 points: Fresh install kind=VersionModsAndSave", StringComparison.Ordinal));
            Assert.Contains(story, line => line.StartsWith("row stats after the first save: profile#", StringComparison.Ordinal) && line.Contains("active=True snapshots=1", StringComparison.Ordinal));
            Assert.Contains("delete of the active profile: error error.gameService.profileActive", story);
        }
    }

    // A story whose launch failed to start in both backends would still match, so the steps that prove it ran are named here.
    [Fact]
    public void The_shared_story_plays_a_whole_launch()
    {
        foreach (var story in new[] { runs.Fake.Shared, runs.Real.Shared })
        {
            Assert.Contains("start launch: job started", story);
            Assert.Contains(story, line => line.StartsWith("session while the game runs: Running ", StringComparison.Ordinal));
            Assert.Contains(story, line => line.StartsWith("session after the game exits: Exited ", StringComparison.Ordinal));
            Assert.Contains("session after skip: Completed profile=profile#1 older=False completion=Skip result=True", story);
            Assert.Contains("second start while it runs: error error.launch.alreadyRunning", story);
            Assert.Contains("discard while it runs: error error.gameService.gameRunning", story);
            Assert.Contains("discard while it waits for the choice: error error.gameService.gameRunning", story);
        }
    }

    // The steps that prove the stopped switch and the save after play really ran in both backends, since a story that stopped early would still match.
    [Fact]
    public void The_shared_story_stops_a_switch_finishes_it_and_saves_the_play_after_it()
    {
        foreach (var story in new[] { runs.Fake.Shared, runs.Real.Shared })
        {
            Assert.Contains("preview before the switch: install unfinished=False dirty=False heading for nothing; saves unfinished=False dirty=False", story);
            Assert.Contains("session after the switch was stopped: Failed profile=profile#3 older=False completion=none result=True", story);
            Assert.Contains("preview after the switch was stopped: install unfinished=True dirty=True heading for the tip of profile#3; saves unfinished=False dirty=False", story);
            Assert.Contains("start the recorded target: job started", story);
            Assert.Contains("preview after the switch was finished: install unfinished=False dirty=False heading for nothing; saves unfinished=False dirty=False", story);
            Assert.Contains("complete with a new save: started", story);
            Assert.Contains("session after the new save: Completed profile=profile#3 older=False completion=CaptureNew result=False", story);
            Assert.Contains("what the new save recorded: Install:ok, External:ok point=named", story);
            Assert.Contains("session error after the new save: none", story);
        }
    }

    // A story that stopped before the refusal would still match, so the step that proves it ran is named here.
    [Fact]
    public void The_shared_story_refuses_a_snapshot_of_a_profile_the_folders_do_not_hold_before_any_job()
    {
        foreach (var story in new[] { runs.Fake.Shared, runs.Real.Shared })
        {
            Assert.Contains("snapshot of a profile the folders do not hold: error error.profile.notLoaded", story);
        }
    }

    // A game with a mods folder is a three-folder game in both backends, so the story that checks it cannot quietly drop the mods.
    [Fact]
    public void The_shared_story_adds_a_game_with_a_mods_folder_and_saves_all_three_folders()
    {
        foreach (var story in new[] { runs.Fake.Shared, runs.Real.Shared })
        {
            Assert.Contains("add a game with a mods folder: added Zeta mods=True", story);
            Assert.Contains("folder status of a game with a mods folder: Install exists=True confirmed=True; Mods exists=True confirmed=True; External exists=True confirmed=True", story);
            Assert.Contains(story, line => line.StartsWith("capture finished: ", StringComparison.Ordinal) && line.Contains("folders=Install:ok, Mods:ok, External:ok", StringComparison.Ordinal));
            Assert.Contains("a mods folder that is there: exists=True", story);
            Assert.Contains("a mods folder that is not there yet: exists=False", story);
        }
    }

    // Where the fake is simpler than the real backend. Each side is pinned, so closing a gap in the fake fails here and the step
    // moves into the shared story.
    [Fact]
    public void The_places_the_fake_is_simpler_than_the_real_backend_are_pinned()
    {
        Assert.Equal(
            [
                "storage used: False",
                "add a game whose mods folder is not there: Added",
                "jobs the backend started by itself: none",
                "a session reported as switching: False",
            ],
            runs.Fake.Gaps);
        Assert.Equal(
            [
                "storage used: True",
                "add a game whose mods folder is not there: error error.path.missing",
                "jobs the backend started by itself: Compact",
                "a session reported as switching: True",
            ],
            runs.Real.Gaps);
    }
}

// The scenario runs once against each backend and both tests read the transcripts, since the real one costs a few git processes.
public sealed class ContractRuns : IAsyncLifetime
{
    internal ScenarioTranscript Fake { get; private set; } = new([], []);

    internal ScenarioTranscript Real { get; private set; } = new([], []);

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fake = new FakeContractHarness();
        Fake = await ContractScenario.RunAsync(fake, token);
        await using var real = new RealContractHarness();
        Real = await ContractScenario.RunAsync(real, token);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
