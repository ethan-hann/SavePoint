using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Shell;

// Closing the window while a job runs: ask when the job must not be cut short, stop what can be stopped, and wait a moment for it (D31).
public class ExitGuardTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static (ExitGuard Guard, FakeBackend Backend, FakeDialogs Dialogs) Build(TimeSpan? wait = null)
    {
        var backend = FakeBackend.Mockup();
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
        dialogs.WhileConfirming = async _ => late = FakeAssert.Ok(await backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A point"));

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
        var save = FakeAssert.Ok(await backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A point"));
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
    public async Task A_save_is_cancelled_on_the_way_out_without_a_question()
    {
        var (guard, backend, dialogs) = Build();
        var save = FakeAssert.Ok(await backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A point"));

        var allowed = await guard.PrepareToCloseAsync();

        Assert.True(allowed);
        Assert.Empty(dialogs.Confirmations);
        Assert.Equal([save.JobId], Assert.Single(backend.Calls.To<IJobsApi>(nameof(IJobsApi.CancelAsync))).Args);
    }

    [Fact]
    public async Task The_close_waits_for_a_cancelled_job_to_end()
    {
        var (guard, backend, _) = Build(TimeSpan.FromSeconds(20));
        var save = FakeAssert.Ok(await backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A point"));

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
        FakeAssert.Ok(await backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A point"));

        var allowed = await guard.PrepareToCloseAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(allowed);
    }
}
