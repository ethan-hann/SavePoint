using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.History;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.History;

// The "Recently removed" section under History's snapshots: closed until opened, hidden when nothing was removed, and each row can be
// brought back as a new profile.
public class HistoryRecentlyRemovedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HistoryRig> ShownAsync(FakeBackend? backend = null) => await HistoryRig.ShownAsync(backend ?? new FakeBackend(RemovedSnapshotSamples.Scenario()));

    [Fact]
    public async Task The_screen_reads_the_selected_games_recently_removed_snapshots_when_it_shows()
    {
        var rig = await HistoryRig.StartAsync(new FakeBackend(RemovedSnapshotSamples.Scenario()));

        rig.Show();

        var read = Assert.Single(rig.Backend.Calls.To<IHistoryApi>(nameof(IHistoryApi.GetRecentlyRemovedAsync)));
        Assert.Equal([SampleScenario.SkyrimId], read.Args);
        Assert.Equal(2, rig.Screen.Removed.Count);
    }

    [Fact]
    public async Task The_section_is_hidden_when_nothing_was_removed()
    {
        var rig = await HistoryRig.ShownAsync();

        Assert.False(rig.Screen.HasRemoved);
        Assert.False(rig.Screen.ShowRemoved);
        Assert.Empty(rig.Screen.Removed);
    }

    [Fact]
    public async Task The_section_is_closed_until_the_player_opens_it_and_its_heading_counts_the_snapshots()
    {
        var rig = await ShownAsync();

        Assert.True(rig.Screen.ShowRemoved);
        Assert.False(rig.Screen.IsRemovedOpen);
        Assert.Equal("Recently removed (2)", rig.Screen.RemovedHeading);
        Assert.Contains("deleted for good", rig.Screen.RemovedNote, StringComparison.Ordinal);
        Assert.Contains("free up space", rig.Screen.RemovedNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_row_names_the_snapshot_what_it_held_the_profile_it_came_from_and_when_it_was_removed_most_recent_first()
    {
        var rig = await ShownAsync();

        var rows = rig.Screen.Removed;

        Assert.Equal(["Before the final boss", "Cleared Blackreach"], rows.Select(row => row.Name));
        Assert.Equal(["Version, mods and saves · 1.6.640 with 212 mod files", "Saves only · 1.6.1170, no mods"], rows.Select(row => row.Detail));
        Assert.Equal(
            [
                "From \"Modded playthrough - 7-2-26\". Removed when you continued it from a session.",
                "From \"Vanilla - latest update\". Removed when you replaced its latest snapshot with a session.",
            ],
            rows.Select(row => row.Origin));
        Assert.Equal(["Removed Jul 8, 10:00 AM", "Removed Jul 7, 10:00 AM"], rows.Select(row => row.Removed));
        Assert.Equal("Bring back as a new profile: Before the final boss", rows[0].BringBackAutomationName);
        Assert.Equal(
            "Before the final boss, Version, mods and saves · 1.6.640 with 212 mod files, From \"Modded playthrough - 7-2-26\". Removed when you continued it from a session., Removed Jul 8, 10:00 AM",
            rows[0].ItemAutomationName);
    }

    [Fact]
    public async Task A_snapshot_from_a_deleted_profile_says_so()
    {
        var scenario = ScenarioBuilder.Sample().WithRemovedSnapshot(SampleScenario.SkyrimId, RemovedSnapshotSamples.FromDeletedProfile);
        var rig = await ShownAsync(new FakeBackend(scenario));

        var row = Assert.Single(rig.Screen.Removed);

        Assert.Equal("From a profile that has since been deleted. Removed when you continued it from a session.", row.Origin);
    }

    [Fact]
    public async Task Following_another_game_shows_that_games_removed_snapshots()
    {
        var rig = await ShownAsync();

        rig.Shell.SelectGame(SampleScenario.SatisfactoryId);
        await rig.Screen.ReloadAsync();

        Assert.Equal(["14 mod files updated"], rig.Screen.Removed.Select(row => row.Name));
        Assert.False(rig.Screen.IsRemovedOpen);
    }

    [Fact]
    public async Task The_section_stays_open_across_a_reload_of_the_same_game_and_closes_for_another_game()
    {
        var rig = await ShownAsync();
        rig.Screen.IsRemovedOpen = true;

        await rig.Screen.ReloadAsync();
        Assert.True(rig.Screen.IsRemovedOpen);

        rig.Shell.SelectGame(SampleScenario.SatisfactoryId);
        await rig.Screen.ReloadAsync();
        Assert.False(rig.Screen.IsRemovedOpen);
    }

    [Fact]
    public async Task A_game_with_no_snapshots_left_still_shows_what_was_removed_from_it()
    {
        var scenario = ScenarioBuilder.Empty()
            .WithGame(SampleScenario.Skyrim)
            .WithRemovedSnapshot(SampleScenario.SkyrimId, RemovedSnapshotSamples.FromDeletedProfile);
        var rig = await ShownAsync(new FakeBackend(scenario));

        Assert.Equal(HistoryState.Empty, rig.Screen.State);
        Assert.True(rig.Screen.ShowRemoved);
        Assert.Single(rig.Screen.Removed);
    }

    [Fact]
    public async Task A_removed_snapshot_list_that_cannot_be_read_says_why_and_leaves_the_history_alone()
    {
        var backend = new FakeBackend(RemovedSnapshotSamples.Scenario());
        backend.FailNext<IHistoryApi>(nameof(IHistoryApi.GetRecentlyRemovedAsync), SampleScenario.FolderGoneError);
        var rig = await HistoryRig.ShownAsync(backend);

        Assert.Equal(HistoryState.Loaded, rig.Screen.State);
        Assert.True(rig.Screen.ShowRemoved);
        Assert.True(rig.Screen.HasRemovedError);
        Assert.Equal(rig.Text.Message(SampleScenario.FolderGoneError), rig.Screen.RemovedError);
        Assert.Empty(rig.Screen.Removed);
        Assert.Equal("Recently removed", rig.Screen.RemovedHeading);
    }

    [Fact]
    public async Task Bringing_one_back_asks_for_a_name_then_makes_the_profile_and_the_snapshot_leaves_the_section()
    {
        var rig = await ShownAsync();
        var row = FirstRemoved(rig);

        var (dialog, finished) = await OpenDialogAsync(rig, row.BringBackCommand);

        Assert.Equal("Bring back as a new profile", dialog.Title);
        Assert.Equal("Before the final boss", dialog.Name);
        Assert.Contains("\"Before the final boss\"", dialog.Body, StringComparison.Ordinal);
        dialog.Name = "Boss fight again";
        await dialog.SaveCommand.ExecuteAsync(null);
        await finished;

        Assert.Equal([SampleScenario.SkyrimId, row.Snapshot.Id, "Boss fight again"], Assert.Single(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.BringBackAsync))).Args);
        await UntilAsync(() => rig.Screen.Removed.Count == 1);
        Assert.Equal(["Cleared Blackreach"], rig.Screen.Removed.Select(removed => removed.Name));
        Assert.Equal("Skyrim Special Edition · 9 snapshots", rig.Screen.Subtitle);
        Assert.Contains("Boss fight again", FakeAssert.Ok(await rig.Backend.Profiles.ListAsync(SampleScenario.SkyrimId, Ct)).Select(profile => profile.Name));
    }

    [Fact]
    public async Task Cancelling_the_name_brings_nothing_back()
    {
        var rig = await ShownAsync();
        var (dialog, finished) = await OpenDialogAsync(rig, FirstRemoved(rig).BringBackCommand);

        dialog.CancelCommand.Execute(null);
        await finished;

        Assert.Empty(rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.BringBackAsync)));
        Assert.Equal(2, rig.Screen.Removed.Count);
    }

    [Fact]
    public async Task A_refusal_stays_in_the_dialog_with_what_the_player_typed()
    {
        var rig = await ShownAsync();
        rig.Backend.FailNext<IProfilesApi>(nameof(IProfilesApi.BringBackAsync), ErrorInfo.From(new RemovedSnapshotNotRestorableException("pt")));
        var (dialog, finished) = await OpenDialogAsync(rig, FirstRemoved(rig).BringBackCommand);
        dialog.Name = "Kept name";

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(dialog.HasError);
        Assert.Contains("can't bring this snapshot back", dialog.Error, StringComparison.Ordinal);
        Assert.Equal("Kept name", dialog.Name);
        Assert.Equal(2, rig.Screen.Removed.Count);
        dialog.CancelCommand.Execute(null);
        await finished;
    }

    [Fact]
    public async Task Freeing_up_space_empties_the_section_once_the_job_has_run()
    {
        var rig = await ShownAsync();
        var preview = FakeAssert.Ok(await rig.Backend.Games.GetFreeUpSpacePreviewAsync(SampleScenario.SkyrimId, TestContext.Current.CancellationToken));
        var started = FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(SampleScenario.SkyrimId, preview.Token));

        rig.Backend.Jobs.Complete(started.JobId);
        await UntilAsync(() => rig.Screen.Removed.Count == 0);

        Assert.False(rig.Screen.ShowRemoved);
    }

    private static RemovedSnapshotViewModel FirstRemoved(HistoryRig rig)
    {
        Assert.NotEmpty(rig.Screen.Removed);
        return rig.Screen.Removed[0];
    }

    // The command waits for the dialog to be answered, so the test starts it and takes the dialog it shows.
    private static async Task<(BringBackDialogViewModel Dialog, Task Finished)> OpenDialogAsync(HistoryRig rig, System.Windows.Input.ICommand bringBack)
    {
        var before = rig.Dialogs.Shown.Count;
        var finished = Assert.IsAssignableFrom<IAsyncRelayCommand>(bringBack).ExecuteAsync(null);
        await UntilAsync(() => rig.Dialogs.Shown.Count > before);
        return (Assert.IsType<BringBackDialogViewModel>(rig.Dialogs.Shown[^1]), finished);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "the screen never reached the state the test waits for");
            await Task.Delay(10, Ct);
        }
    }
}
