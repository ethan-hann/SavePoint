using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Shell;

public class JobStatusViewModelTests
{
    private const string _skyrim = "Skyrim Special Edition";

    [Fact]
    public void With_no_jobs_the_pill_is_hidden_and_nothing_is_announced()
    {
        var rig = new Rig();

        Assert.False(rig.Status.IsVisible);
        Assert.Equal(string.Empty, rig.Status.PillText);
        Assert.Equal(string.Empty, rig.Status.Announcement);
        Assert.Empty(rig.Toasts.Toasts);
    }

    [Fact]
    public async Task A_running_capture_shows_the_game_in_the_pill()
    {
        var rig = new Rig();

        await rig.StartCapture();

        Assert.True(rig.Status.IsVisible);
        Assert.Equal("Saving Skyrim Special Edition\u2026", rig.Status.PillText);
    }

    [Fact]
    public async Task Several_running_jobs_are_counted_in_one_pill()
    {
        var rig = new Rig();

        await rig.StartCapture();
        FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(MockupScenario.SatisfactoryId));

        Assert.Equal("Saving 2 things\u2026", rig.Status.PillText);
    }

    [Fact]
    public async Task A_storage_move_a_game_optimization_an_all_games_optimization_and_clearing_each_have_their_own_pill_text()
    {
        var rig = new Rig();

        var relocate = FakeAssert.Ok(await rig.Backend.Settings.RelocateStorageAsync(@"E:\SavePoint"));
        Assert.Equal("Moving your storage\u2026", rig.Status.PillText);
        rig.Backend.Jobs.Complete(relocate.JobId);

        var compact = FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(MockupScenario.SatisfactoryId));
        Assert.Equal("Optimizing storage for Satisfactory\u2026", rig.Status.PillText);
        rig.Backend.Jobs.Complete(compact.JobId);

        var all = FakeAssert.Ok(await rig.Backend.Settings.OptimizeAllStorageAsync());
        Assert.Equal("Optimizing storage\u2026", rig.Status.PillText);
        rig.Backend.Jobs.Complete(all.JobId);

        FakeAssert.Ok(await rig.Backend.Settings.ClearAllDataAsync());
        Assert.Equal("Clearing SavePoint's data\u2026", rig.Status.PillText);
    }

    [Fact]
    public async Task The_pill_goes_when_the_last_job_ends()
    {
        var rig = new Rig();
        var job = await rig.StartCapture();

        rig.Backend.Jobs.Complete(job.JobId);

        Assert.False(rig.Status.IsVisible);
        Assert.Equal(string.Empty, rig.Status.PillText);
    }

    [Fact]
    public async Task A_launch_job_never_shows_in_the_pill_or_as_a_toast()
    {
        var rig = new Rig();

        FakeAssert.Ok(await rig.Backend.Launch.StartAsync(MockupScenario.SkyrimId, new LaunchTarget.ProfileTip(MockupScenario.ModdedId)));
        Assert.False(rig.Status.IsVisible);

        rig.Backend.Launch.ReportRunning(MockupScenario.SkyrimId);

        Assert.False(rig.Status.IsVisible);
        Assert.Empty(rig.Toasts.Toasts);
    }

    [Fact]
    public async Task A_finished_capture_is_a_success_toast_that_names_the_game()
    {
        var rig = new Rig();
        var job = await rig.StartCapture();

        rig.Backend.Jobs.Complete(job.JobId);

        var toast = Assert.Single(rig.Toasts.Toasts);
        Assert.Equal("Saved Skyrim Special Edition.", toast.Text);
        Assert.Equal(ToastKind.Success, toast.Message.Kind);
        Assert.Null(toast.Details);
    }

    [Fact]
    public async Task A_failed_capture_is_an_error_toast_with_the_reason_underneath()
    {
        var rig = new Rig();
        var job = await rig.StartCapture();

        rig.Backend.Jobs.Fail(job.JobId, MockupScenario.SavesOutOfRoomError);

        var toast = Assert.Single(rig.Toasts.Toasts);
        Assert.Equal("Something went wrong saving Skyrim Special Edition.", toast.Text);
        Assert.Equal(ToastKind.Error, toast.Message.Kind);
        Assert.Equal(rig.Text.Message(MockupScenario.SavesOutOfRoomError), toast.Details);
    }

    [Fact]
    public async Task A_cancelled_capture_is_a_plain_info_toast()
    {
        var rig = new Rig();
        var job = await rig.StartCapture();

        FakeAssert.Ok(await rig.Backend.Jobs.CancelAsync(job.JobId));
        rig.Backend.Jobs.CompleteCancelled(job.JobId);

        var toast = Assert.Single(rig.Toasts.Toasts);
        Assert.Equal("Cancelled saving Skyrim Special Edition.", toast.Text);
        Assert.Equal(ToastKind.Info, toast.Message.Kind);
        Assert.Null(toast.Details);
    }

    [Fact]
    public async Task A_capture_that_saved_only_one_folder_is_a_warning_never_a_success()
    {
        var rig = new Rig();
        var job = await rig.StartCapture();

        rig.Backend.Jobs.Complete(job.JobId, MockupScenario.HalfSaved);

        var toast = Assert.Single(rig.Toasts.Toasts);
        Assert.Equal("Only part of Skyrim Special Edition was saved.", toast.Text);
        Assert.Equal(ToastKind.Warning, toast.Message.Kind);
        Assert.Equal(rig.Text.Message(MockupScenario.SavesOutOfRoomError), toast.Details);
    }

    [Fact]
    public async Task Optimization_of_one_game_has_its_own_toast_copy()
    {
        var rig = new Rig();
        var job = FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(MockupScenario.SatisfactoryId));

        rig.Backend.Jobs.Complete(job.JobId);

        Assert.Equal("Optimized storage for Satisfactory.", Assert.Single(rig.Toasts.Toasts).Text);
    }

    [Fact]
    public async Task Optimization_of_every_game_does_not_name_a_game()
    {
        var rig = new Rig();
        var job = FakeAssert.Ok(await rig.Backend.Settings.OptimizeAllStorageAsync());

        rig.Backend.Jobs.Complete(job.JobId);

        Assert.Equal("Optimized storage for all your games.", Assert.Single(rig.Toasts.Toasts).Text);
    }

    [Fact]
    public async Task A_failed_or_cancelled_optimization_of_every_game_reads_without_a_game()
    {
        var rig = new Rig();
        var failed = FakeAssert.Ok(await rig.Backend.Settings.OptimizeAllStorageAsync());
        rig.Backend.Jobs.Fail(failed.JobId, ErrorInfo.From(new InvalidOperationException("boom")));
        var cancelled = FakeAssert.Ok(await rig.Backend.Settings.OptimizeAllStorageAsync());
        rig.Backend.Jobs.CompleteCancelled(cancelled.JobId);

        Assert.Equal(["Something went wrong optimizing storage.", "Cancelled optimizing storage."], rig.Toasts.Toasts.Select(toast => toast.Text));
    }

    [Fact]
    public async Task A_storage_move_that_finishes_says_so()
    {
        var rig = new Rig();
        var job = FakeAssert.Ok(await rig.Backend.Settings.RelocateStorageAsync(@"E:\SavePoint"));

        rig.Backend.Jobs.Complete(job.JobId);

        Assert.Equal("Storage moved.", Assert.Single(rig.Toasts.Toasts).Text);
    }

    [Fact]
    public async Task A_storage_move_that_left_old_copies_behind_says_it_moved_and_warns()
    {
        var rig = new Rig();
        var job = FakeAssert.Ok(await rig.Backend.Settings.RelocateStorageAsync(@"E:\SavePoint"));
        var stayed = new ClearFailure(@"D:\Old\game-1\install.git", ErrorInfo.From(new GameRunningException("game-1", "Skyrim")));

        rig.Backend.Jobs.Complete(job.JobId, new StorageRelocationResult(MockupScenario.StorageRoot, @"E:\SavePoint", 10, [stayed]));

        var toast = Assert.Single(rig.Toasts.Toasts);
        Assert.StartsWith("Storage moved, but some old files are still in the old folder.", toast.Text, StringComparison.Ordinal);
        Assert.Equal(ToastKind.Warning, toast.Message.Kind);
    }

    [Fact]
    public async Task A_cancelled_storage_move_is_an_info_toast()
    {
        var rig = new Rig();
        var job = FakeAssert.Ok(await rig.Backend.Settings.RelocateStorageAsync(@"E:\SavePoint"));

        rig.Backend.Jobs.CompleteCancelled(job.JobId);

        var toast = Assert.Single(rig.Toasts.Toasts);
        Assert.Equal("Cancelled moving your storage.", toast.Text);
        Assert.Equal(ToastKind.Info, toast.Message.Kind);
    }

    [Fact]
    public async Task Clearing_all_data_reports_success_and_leftovers()
    {
        var rig = new Rig();
        var clean = FakeAssert.Ok(await rig.Backend.Settings.ClearAllDataAsync());
        rig.Backend.Jobs.Complete(clean.JobId);
        var leftovers = FakeAssert.Ok(await rig.Backend.Settings.ClearAllDataAsync());
        rig.Backend.Jobs.Complete(leftovers.JobId, new ClearAllDataResult(0, [new ClearFailure(@"D:\SavePoint\locked.pack", MockupScenario.FileInUseError)]));

        Assert.Equal(["SavePoint's data was cleared.", "Some of SavePoint's data couldn't be removed."], rig.Toasts.Toasts.Select(toast => toast.Text));
        Assert.Equal(ToastKind.Warning, rig.Toasts.Toasts[1].Message.Kind);
    }

    [Fact]
    public async Task A_game_that_is_gone_reads_as_this_game()
    {
        var rig = new Rig();
        var job = await rig.StartCapture();
        rig.KnownGames.Remove(MockupScenario.SkyrimId);

        rig.Backend.Jobs.Complete(job.JobId);

        Assert.Equal("Saved this game.", Assert.Single(rig.Toasts.Toasts).Text);
    }

    [Fact]
    public async Task Jobs_that_already_existed_are_shown_when_running_and_forgotten_when_finished()
    {
        var rig = new Rig(wireEvents: false);
        var running = await rig.StartCapture();
        var finished = FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(MockupScenario.SatisfactoryId));
        rig.Backend.Jobs.Complete(finished.JobId);

        rig.Status.Seed(rig.Backend.Jobs.List());

        Assert.True(rig.Status.IsVisible);
        Assert.Equal("Saving Skyrim Special Edition\u2026", rig.Status.PillText);
        Assert.Empty(rig.Toasts.Toasts);

        rig.Backend.Jobs.Complete(running.JobId);
        rig.Status.Handle(rig.Backend.Jobs.Get(running.JobId)!);

        Assert.Equal("Saved Skyrim Special Edition.", Assert.Single(rig.Toasts.Toasts).Text);
    }

    [Fact]
    public async Task A_snapshot_that_is_not_newer_is_dropped()
    {
        var rig = new Rig(wireEvents: false);
        var job = await rig.StartCapture();
        var first = rig.Backend.Jobs.Get(job.JobId)!;
        rig.Backend.Jobs.ReportProgress(job.JobId, new JobProgress(FilesDone: 1));
        var second = rig.Backend.Jobs.Get(job.JobId)!;
        rig.Backend.Jobs.Complete(job.JobId);
        var last = rig.Backend.Jobs.Get(job.JobId)!;

        rig.Status.Handle(second);
        rig.Status.Handle(first);

        Assert.True(rig.Status.IsVisible);

        rig.Status.Handle(last);
        rig.Status.Handle(second);

        Assert.False(rig.Status.IsVisible);
        Assert.Single(rig.Toasts.Toasts);
    }

    [Fact]
    public async Task Seeing_the_same_finished_job_twice_gives_one_toast()
    {
        var rig = new Rig(wireEvents: false);
        var job = await rig.StartCapture();
        rig.Backend.Jobs.Complete(job.JobId);
        var finished = rig.Backend.Jobs.Get(job.JobId)!;

        rig.Status.Handle(finished);
        rig.Status.Handle(finished with { Revision = finished.Revision + 1 });

        Assert.Single(rig.Toasts.Toasts);
    }

    [Fact]
    public async Task The_live_region_hears_the_pill_text_when_it_changes_but_not_when_it_clears()
    {
        var rig = new Rig();
        var job = await rig.StartCapture();

        Assert.Equal("Saving Skyrim Special Edition\u2026", rig.Status.Announcement);

        FakeAssert.Ok(await rig.Backend.Games.OptimizeStorageAsync(MockupScenario.SatisfactoryId));

        Assert.Equal("Saving 2 things\u2026", rig.Status.Announcement);

        rig.Backend.Jobs.Complete(job.JobId);

        Assert.Equal("Optimizing storage for Satisfactory\u2026", rig.Status.Announcement);
    }

    [Fact]
    public async Task Progress_reports_for_the_same_job_do_not_re_announce()
    {
        var rig = new Rig();
        var job = await rig.StartCapture();
        var announcements = new List<string>();
        rig.Status.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(JobStatusViewModel.Announcement))
            {
                announcements.Add(rig.Status.Announcement);
            }
        };

        rig.Backend.Jobs.ReportProgress(job.JobId, new JobProgress(FilesDone: 5, FilesTotal: 10));
        rig.Backend.Jobs.ReportProgress(job.JobId, new JobProgress(FilesDone: 6, FilesTotal: 10));

        Assert.Empty(announcements);
    }

    private sealed class Rig
    {
        public Rig(bool wireEvents = true)
        {
            Backend = FakeBackend.Mockup();
            Text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), Backend.Clock, "windows", TimeZoneInfo.Utc);
            Toasts = new ToastHostViewModel(new ManualTimeProvider(), new ImmediateUiDispatcher());
            KnownGames = new Dictionary<string, string>
            {
                [MockupScenario.SkyrimId] = _skyrim,
                [MockupScenario.SatisfactoryId] = "Satisfactory",
                [MockupScenario.CaretakerId] = "The Last Caretaker",
            };
            Status = new JobStatusViewModel(Text, Toasts, id => KnownGames.GetValueOrDefault(id));
            if (wireEvents)
            {
                Backend.Jobs.JobChanged += (_, job) => Status.Handle(job);
            }
        }

        public FakeBackend Backend { get; }

        public UiFormatter Text { get; }

        public ToastHostViewModel Toasts { get; }

        public Dictionary<string, string> KnownGames { get; }

        public JobStatusViewModel Status { get; }

        public async Task<JobStarted> StartCapture() =>
            FakeAssert.Ok(await Backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A point"));
    }
}
