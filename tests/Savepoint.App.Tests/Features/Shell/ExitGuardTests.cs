using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Shell;

// Closing the window while a job runs: ask when the job must not be cut short, stop what can be stopped, and wait a moment for it (D31).
public class ExitGuardTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static (ExitGuard Guard, FakeBackend Backend, FakeDialogs Dialogs) Build(TimeSpan? wait = null)
    {
        var backend = FakeBackend.Sample();
        var dialogs = new FakeDialogs();
        return (new ExitGuard(backend.Jobs, dialogs, _text, wait ?? TimeSpan.FromMilliseconds(100)), backend, dialogs);
    }

    [Fact]
    public async Task With_nothing_running_the_window_closes_without_a_question()
    {
        var (guard, backend, dialogs) = Build();

        var allowed = await guard.PrepareToCloseAsync();

        Assert.True(allowed);
        Assert.Empty(dialogs.Confirmations);
        Assert.Empty(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [Fact]
    public async Task Clearing_all_data_asks_first_and_staying_leaves_it_alone()
    {
        var (guard, backend, dialogs) = Build();
        dialogs.AnswerConfirm(false);
        FakeAssert.Ok(await backend.Settings.ClearAllDataAsync());

        var allowed = await guard.PrepareToCloseAsync();

        Assert.False(allowed);
        var asked = Assert.Single(dialogs.Confirmations);
        Assert.Equal("SavePoint is still working", asked.Title);
        Assert.Contains("removing its data", asked.Message, StringComparison.Ordinal);
        Assert.Empty(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [Fact]
    public async Task Quitting_anyway_during_a_clear_does_not_try_to_cancel_what_cannot_be_cancelled()
    {
        var (guard, backend, dialogs) = Build();
        dialogs.AnswerConfirm(true);
        FakeAssert.Ok(await backend.Settings.ClearAllDataAsync());

        var allowed = await guard.PrepareToCloseAsync();

        Assert.True(allowed);
        Assert.Empty(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [Fact]
    public async Task A_storage_move_asks_first_and_quitting_anyway_cancels_it()
    {
        var (guard, backend, dialogs) = Build();
        dialogs.AnswerConfirm(true);
        var move = FakeAssert.Ok(await backend.Settings.RelocateStorageAsync(@"E:\SavePoint"));

        var allowed = await guard.PrepareToCloseAsync();

        Assert.True(allowed);
        Assert.Contains("moving your snapshots", Assert.Single(dialogs.Confirmations).Message, StringComparison.Ordinal);
        Assert.Equal([move.JobId], Assert.Single(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync))).Args);
    }

    [Fact]
    public async Task A_job_that_starts_while_the_question_is_open_is_cancelled_with_the_rest()
    {
        var (guard, backend, dialogs) = Build();
        dialogs.AnswerConfirm(true);
        var move = FakeAssert.Ok(await backend.Settings.RelocateStorageAsync(@"E:\SavePoint"));
        JobStarted? late = null;
        dialogs.WhileConfirming = async _ => late = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));

        var allowed = await guard.PrepareToCloseAsync();

        Assert.True(allowed);
        var cancelled = backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)).Select(call => call.Args[0]).ToList();
        Assert.Contains(move.JobId, cancelled);
        Assert.Contains(late!.JobId, cancelled);
    }

    [Fact]
    public async Task A_job_that_ended_while_the_question_was_open_is_not_cancelled()
    {
        var (guard, backend, dialogs) = Build();
        dialogs.AnswerConfirm(true);
        var save = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));
        var move = FakeAssert.Ok(await backend.Settings.RelocateStorageAsync(@"E:\SavePoint"));
        dialogs.WhileConfirming = _ =>
        {
            backend.Jobs.Complete(save.JobId, null);
            return Task.CompletedTask;
        };

        var allowed = await guard.PrepareToCloseAsync();

        Assert.True(allowed);
        Assert.Equal([move.JobId], Assert.Single(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync))).Args);
    }

    [Fact]
    public async Task A_free_up_that_has_deleted_snapshots_asks_first_offers_no_quit_anyway_and_staying_leaves_it_alone()
    {
        var (guard, backend, dialogs) = Build();
        dialogs.AnswerConfirm(false);
        var run = FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(SampleScenario.SkyrimId));
        backend.Jobs.ReportNoLongerCancellable(run.JobId);

        var allowed = await guard.PrepareToCloseAsync();

        Assert.False(allowed);
        var asked = Assert.Single(dialogs.Confirmations);
        Assert.Equal("SavePoint is still working", asked.Title);
        Assert.Contains("freeing up space", asked.Message, StringComparison.Ordinal);
        Assert.Contains("because it has started deleting the snapshots you chose to delete", asked.Message, StringComparison.Ordinal);
        Assert.Contains("quit as soon as it is done", asked.Message, StringComparison.Ordinal);
        Assert.Equal("Quit when it's done", asked.ConfirmLabel);
        Assert.Equal("Stay open", asked.CancelLabel);
        Assert.Empty(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [Fact]
    public async Task Quitting_while_a_free_up_is_past_its_clear_waits_for_it_to_finish_however_long_that_takes()
    {
        var (guard, backend, dialogs) = Build(TimeSpan.FromMilliseconds(100));
        dialogs.AnswerConfirm(true);
        var run = FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(SampleScenario.SkyrimId));
        backend.Jobs.ReportNoLongerCancellable(run.JobId);

        var closing = guard.PrepareToCloseAsync();

        // Five times the bound a cancelled job gets: only a wait for the run itself can hold the close this long.
        var early = await Task.WhenAny(closing, Task.Delay(500, TestContext.Current.CancellationToken));
        backend.Jobs.Complete(run.JobId);
        var allowed = await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.NotSame(closing, early);
        Assert.True(allowed);
        Assert.Empty(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [Fact]
    public async Task A_free_up_that_passes_its_clear_while_another_question_is_open_is_asked_about_and_waited_for_too()
    {
        var (guard, backend, dialogs) = Build(TimeSpan.FromMilliseconds(100));
        dialogs.AnswerConfirm(true, true);
        var move = FakeAssert.Ok(await backend.Settings.RelocateStorageAsync(@"E:\SavePoint"));
        var run = FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(SampleScenario.SkyrimId));
        var passed = false;
        dialogs.WhileConfirming = _ =>
        {
            if (!passed)
            {
                passed = true;
                backend.Jobs.ReportNoLongerCancellable(run.JobId);
            }

            return Task.CompletedTask;
        };

        var closing = guard.PrepareToCloseAsync();
        var early = await Task.WhenAny(closing, Task.Delay(500, TestContext.Current.CancellationToken));
        backend.Jobs.Complete(run.JobId);
        var allowed = await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.NotSame(closing, early);
        Assert.True(allowed);
        Assert.Equal(2, dialogs.Confirmations.Count);
        Assert.Contains("freeing up space", dialogs.Confirmations[1].Message, StringComparison.Ordinal);
        Assert.Equal([move.JobId], Assert.Single(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync))).Args);
    }

    [Fact]
    public async Task A_launch_that_is_switching_asks_first_and_says_the_folder_is_left_with_a_mix_of_two_snapshots()
    {
        var (guard, backend, dialogs) = Build();
        dialogs.AnswerConfirm(false);
        await StartSwitchingAsync(backend);

        var allowed = await guard.PrepareToCloseAsync();

        Assert.False(allowed);
        var asked = Assert.Single(dialogs.Confirmations);
        Assert.Equal("SavePoint is still working", asked.Title);
        Assert.Contains("mix of two snapshots until the switch is finished or the changes are discarded", asked.Message, StringComparison.Ordinal);
        Assert.Equal("Quit anyway", asked.ConfirmLabel);
        Assert.Equal("Stay open", asked.CancelLabel);
        Assert.Empty(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [Fact]
    public async Task A_switch_without_playing_asks_first_in_the_same_words()
    {
        var (guard, backend, dialogs) = Build();
        dialogs.AnswerConfirm(false);
        var job = FakeAssert.Ok(await backend.Launch.SwitchAsync(SampleScenario.SkyrimId, new LaunchTarget.ProfileTip(SampleScenario.ModdedId))).JobId;
        backend.Jobs.ReportStep(job, JobSteps.LaunchSwitchFolder, ("folder", RepoRole.Install), ("profile", "Modded playthrough"), ("older", "no"), ("counted", "yes"), ("written", 12), ("removed", 2));

        var allowed = await guard.PrepareToCloseAsync();

        Assert.False(allowed);
        var asked = Assert.Single(dialogs.Confirmations);
        Assert.Contains("mix of two snapshots until the switch is finished or the changes are discarded", asked.Message, StringComparison.Ordinal);
        Assert.Empty(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync)));
    }

    [Fact]
    public async Task Quitting_anyway_during_a_switch_stops_it_on_the_way_out_and_asks_only_once()
    {
        var (guard, backend, dialogs) = Build();
        dialogs.AnswerConfirm(true);
        var launch = await StartSwitchingAsync(backend);

        var allowed = await guard.PrepareToCloseAsync();

        Assert.True(allowed);
        Assert.Single(dialogs.Confirmations);
        Assert.Equal([launch], Assert.Single(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync))).Args);
    }

    [Fact]
    public async Task A_launch_that_has_not_begun_to_switch_is_stopped_on_the_way_out_without_a_question()
    {
        var (guard, backend, dialogs) = Build();
        var launch = FakeAssert.Ok(await backend.Launch.StartAsync(SampleScenario.SkyrimId, new LaunchTarget.ProfileTip(SampleScenario.ModdedId))).JobId;

        var allowed = await guard.PrepareToCloseAsync();

        Assert.True(allowed);
        Assert.Empty(dialogs.Confirmations);
        Assert.Equal([launch], Assert.Single(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync))).Args);
    }

    private static async Task<Guid> StartSwitchingAsync(FakeBackend backend)
    {
        var launch = FakeAssert.Ok(await backend.Launch.StartAsync(SampleScenario.SkyrimId, new LaunchTarget.ProfileTip(SampleScenario.ModdedId))).JobId;
        backend.Jobs.ReportStep(launch, JobSteps.LaunchSwitchFolder, ("folder", RepoRole.Install), ("profile", "Modded playthrough"), ("older", "no"), ("counted", "yes"), ("written", 12), ("removed", 2));
        return launch;
    }

    [Fact]
    public async Task A_free_up_that_can_still_be_stopped_is_cancelled_on_the_way_out_without_a_question()
    {
        var (guard, backend, dialogs) = Build();
        var run = FakeAssert.Ok(await backend.Games.OptimizeStorageAsync(SampleScenario.SkyrimId));

        var allowed = await guard.PrepareToCloseAsync();

        Assert.True(allowed);
        Assert.Empty(dialogs.Confirmations);
        Assert.Equal([run.JobId], Assert.Single(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync))).Args);
    }

    [Fact]
    public async Task A_save_is_cancelled_on_the_way_out_without_a_question()
    {
        var (guard, backend, dialogs) = Build();
        var save = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));

        var allowed = await guard.PrepareToCloseAsync();

        Assert.True(allowed);
        Assert.Empty(dialogs.Confirmations);
        Assert.Equal([save.JobId], Assert.Single(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync))).Args);
    }

    [Fact]
    public async Task The_close_waits_for_a_cancelled_job_to_end()
    {
        var (guard, backend, _) = Build(TimeSpan.FromSeconds(20));
        var save = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));

        var closing = guard.PrepareToCloseAsync();
        var early = await Task.WhenAny(closing, Task.Delay(300, TestContext.Current.CancellationToken));
        backend.Jobs.CompleteCancelled(save.JobId);
        var allowed = await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.NotSame(closing, early);
        Assert.True(allowed);
    }

    [Fact]
    public async Task A_job_that_will_not_end_does_not_hold_the_close_forever()
    {
        var (guard, backend, _) = Build(TimeSpan.FromMilliseconds(200));
        FakeAssert.Ok(await backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));

        var allowed = await guard.PrepareToCloseAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(allowed);
    }
}
