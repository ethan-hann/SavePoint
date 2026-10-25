using Savepoint.App.Features.Activity;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Activity;

// How a job reads in the list: what it is, how it stands, how far it has got, and when it ran.
public class JobRowViewModelTests
{
    private static readonly DateTimeOffset _now = new(2026, 7, 8, 17, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string> _games = new() { ["skyrim"] = "Skyrim Special Edition" };

    private readonly FixedTimeProvider _clock = new(_now);

    private ActivityText Words() =>
        new(new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), _clock, "windows", TimeZoneInfo.Utc), id => _games.GetValueOrDefault(id), _clock);

    private JobRowViewModel Row(JobSnapshot job) => new(job, Words());

    private static JobSnapshot Job(
        JobKind kind = JobKind.Capture,
        string? gameId = "skyrim",
        JobStatus status = JobStatus.Running,
        JobProgress? progress = null,
        TimeSpan? startedAgo = null,
        bool cancellable = true,
        bool cancelRequested = false,
        JobProfile? profile = null,
        JobResult? result = null,
        ErrorInfo? error = null) =>
        new(
            Guid.NewGuid(),
            kind,
            gameId,
            profile,
            status,
            cancellable,
            cancelRequested,
            progress ?? JobProgress.None,
            [],
            0,
            _now - (startedAgo ?? TimeSpan.FromSeconds(30)),
            status == JobStatus.Running ? null : _now - (startedAgo ?? TimeSpan.FromSeconds(30)) + TimeSpan.FromSeconds(12),
            result,
            error,
            1);

    [Theory]
    [InlineData(JobKind.Capture, "skyrim", "Taking a snapshot of Skyrim Special Edition")]
    [InlineData(JobKind.Launch, "skyrim", "Starting Skyrim Special Edition")]
    [InlineData(JobKind.Switch, "skyrim", "Switching Skyrim Special Edition's files")]
    [InlineData(JobKind.Compact, "skyrim", "Freeing up space for Skyrim Special Edition")]
    [InlineData(JobKind.Compact, null, "Freeing up space for all games")]
    [InlineData(JobKind.RelocateStorage, null, "Moving the snapshot folder")]
    [InlineData(JobKind.ClearData, null, "Clearing SavePoint's data")]
    [InlineData(JobKind.Capture, "gone", "Taking a snapshot of a game that was removed")]
    public void A_job_is_named_in_plain_words_with_its_game(JobKind kind, string? gameId, string expected)
    {
        Assert.Equal(expected, Row(Job(kind, gameId)).Title);
    }

    [Theory]
    [InlineData(JobStatus.Running, false, "In progress", JobTone.Running)]
    [InlineData(JobStatus.Running, true, "Stopping…", JobTone.Running)]
    [InlineData(JobStatus.Succeeded, false, "Done", JobTone.Success)]
    [InlineData(JobStatus.Partial, false, "Partly done", JobTone.Warning)]
    [InlineData(JobStatus.Failed, false, "Failed", JobTone.Error)]
    [InlineData(JobStatus.Cancelled, false, "Canceled", JobTone.Neutral)]
    public void A_job_says_where_it_stands_and_how_that_is_drawn(JobStatus status, bool cancelRequested, string expected, JobTone tone)
    {
        var row = Row(Job(status: status, cancelRequested: cancelRequested));

        Assert.Equal(expected, row.StatusText);
        Assert.Equal(tone, row.Tone);
        Assert.Equal(status == JobStatus.Running, row.IsRunning);
    }

    [Theory]
    [InlineData(JobStatus.Running, nameof(Icons.Sync))]
    [InlineData(JobStatus.Succeeded, nameof(Icons.CheckCircle))]
    [InlineData(JobStatus.Partial, nameof(Icons.Warning))]
    [InlineData(JobStatus.Failed, nameof(Icons.Error))]
    [InlineData(JobStatus.Cancelled, nameof(Icons.Cancel))]
    public void Each_state_has_its_own_icon(JobStatus status, string icon)
    {
        var expected = (string)typeof(Icons).GetField(icon)!.GetRawConstantValue()!;

        Assert.Equal(expected, Row(Job(status: status)).Icon);
    }

    [Fact]
    public void A_job_that_works_on_a_profile_names_it_and_others_name_none()
    {
        var named = Row(Job(profile: new JobProfile("p1", "Modded 1.6")));
        var none = Row(Job(JobKind.Compact));

        Assert.True(named.HasProfile);
        Assert.Equal("Profile: Modded 1.6", named.ProfileText);
        Assert.False(none.HasProfile);
        Assert.Equal(string.Empty, none.ProfileText);
    }

    [Fact]
    public void Bytes_are_shown_as_a_size_and_set_the_bar_when_the_job_counts_them()
    {
        var row = Row(Job(progress: new JobProgress(FilesDone: 1, FilesTotal: 10, BytesDone: 3_100_000, BytesTotal: 41_200_000)));

        Assert.Equal("3.1 MB of 41.2 MB", row.ProgressText);
        Assert.True(row.HasProgressText);
        Assert.True(row.ShowProgress);
        Assert.False(row.IsIndeterminate);
        Assert.Equal(7.5, row.ProgressPercent, 1);
    }

    [Fact]
    public void Files_show_when_the_job_counts_no_bytes()
    {
        var row = Row(Job(progress: new JobProgress(FilesDone: 4, FilesTotal: 120)));

        Assert.Equal("4 of 120 files", row.ProgressText);
        Assert.Equal(3.3, row.ProgressPercent, 1);
        Assert.False(row.IsIndeterminate);
    }

    [Fact]
    public void A_count_with_no_total_says_so_and_leaves_the_bar_moving()
    {
        var row = Row(Job(progress: new JobProgress(FilesDone: 7)));

        Assert.Equal("Working out how much there is to copy…", row.ProgressText);
        Assert.True(row.IsIndeterminate);
        Assert.True(row.ShowProgress);
    }

    [Fact]
    public void One_file_in_all_is_not_called_files()
    {
        var row = Row(Job(progress: new JobProgress(FilesDone: 1, FilesTotal: 1)));

        Assert.Equal("1 of 1 file", row.ProgressText);
    }

    [Fact]
    public void A_job_that_counts_nothing_shows_a_moving_bar_and_no_numbers()
    {
        var row = Row(Job());

        Assert.False(row.HasProgressText);
        Assert.True(row.ShowProgress);
        Assert.True(row.IsIndeterminate);
    }

    [Fact]
    public void A_finished_job_shows_no_bar_but_keeps_what_it_counted()
    {
        var row = Row(Job(status: JobStatus.Succeeded, progress: new JobProgress(FilesDone: 120, FilesTotal: 120)));

        Assert.False(row.ShowProgress);
        Assert.Equal("120 of 120 files", row.ProgressText);
    }

    [Fact]
    public void The_time_left_is_worked_out_from_how_fast_the_job_has_gone()
    {
        // A quarter of the bytes in 100 seconds leaves 300 more.
        var row = Row(Job(startedAgo: TimeSpan.FromSeconds(100), progress: new JobProgress(BytesDone: 10_000_000, BytesTotal: 40_000_000)));

        Assert.Equal("About 5 minutes left", row.TimeLeftText);
        Assert.True(row.HasTimeLeft);
    }

    [Fact]
    public void No_time_is_guessed_while_the_job_is_wrapping_up()
    {
        // 99% after 20 minutes would otherwise give "about 12 seconds left" beside text that says a few minutes.
        var row = Row(Job(startedAgo: TimeSpan.FromMinutes(20), progress: new JobProgress(BytesDone: 39_600_000, BytesTotal: 40_000_000, Finishing: true)));

        Assert.False(row.HasTimeLeft);
        Assert.Equal(string.Empty, row.TimeLeftText);
    }

    [Theory]
    [InlineData(3, 50)]
    [InlineData(100, 2)]
    [InlineData(100, 100)]
    public void No_time_is_guessed_when_it_is_too_early_or_too_late_to_say(int startedSecondsAgo, int percentDone)
    {
        var row = Row(Job(startedAgo: TimeSpan.FromSeconds(startedSecondsAgo), progress: new JobProgress(BytesDone: percentDone * 400_000L, BytesTotal: 40_000_000)));

        Assert.False(row.HasTimeLeft);
        Assert.Equal(string.Empty, row.TimeLeftText);
    }

    [Fact]
    public void A_job_with_no_totals_and_a_finished_job_guess_no_time()
    {
        Assert.False(Row(Job(startedAgo: TimeSpan.FromSeconds(100), progress: new JobProgress(FilesDone: 5))).HasTimeLeft);
        Assert.False(Row(Job(status: JobStatus.Succeeded, startedAgo: TimeSpan.FromSeconds(100), progress: new JobProgress(BytesDone: 10_000_000, BytesTotal: 40_000_000))).HasTimeLeft);
    }

    [Fact]
    public void The_start_and_the_finish_are_told_in_the_readers_time()
    {
        var running = Row(Job(startedAgo: TimeSpan.FromHours(1)));
        var finished = Row(Job(status: JobStatus.Succeeded, startedAgo: TimeSpan.FromHours(1)));

        Assert.Equal("Started Jul 8, 4:00 PM", running.StartedText);
        Assert.False(running.HasFinished);
        Assert.Equal(string.Empty, running.FinishedText);
        Assert.True(finished.HasFinished);
        Assert.Equal("Finished Jul 8, 4:00 PM", finished.FinishedText);
    }

    [Theory]
    [InlineData(JobKind.Capture, false, true, false, "")]
    [InlineData(JobKind.Capture, true, true, true, "Stopping now.")]
    [InlineData(JobKind.Compact, true, true, true, "Stopping now.")]
    [InlineData(JobKind.Launch, true, true, true, "Stopping now.")]
    [InlineData(JobKind.ClearData, false, false, false, "Clearing SavePoint's data can't be stopped once it has started.")]
    [InlineData(JobKind.RelocateStorage, false, false, false, "The move can't be stopped any more, because SavePoint already uses the new folder.")]
    [InlineData(JobKind.Compact, false, false, false, "Freeing up space can't be stopped any more, because it has already started deleting the recently removed snapshots it was asked to delete.")]
    [InlineData(JobKind.Capture, false, false, false, "This job can't be stopped right now.")]
    public void A_running_job_that_is_stopping_or_cannot_stop_says_so_instead_of_looking_stuck(JobKind kind, bool requested, bool cancellable, bool _, string expected)
    {
        var row = Row(Job(kind, kind is JobKind.ClearData or JobKind.RelocateStorage ? null : "skyrim", cancellable: cancellable, cancelRequested: requested));

        Assert.Equal(expected, row.StopNote);
        Assert.Equal(expected.Length > 0, row.HasStopNote);
    }

    [Fact]
    public void A_launch_that_is_switching_and_is_stopping_says_the_folder_is_left_with_a_mix_of_two_snapshots()
    {
        var switching = Job(JobKind.Launch, cancelRequested: true) with
        {
            RecentLines = [JobLine.ForStep(JobSteps.LaunchSwitchFolder, ("folder", RepoRole.Install), ("profile", "Modded"), ("older", "no"), ("counted", "no"), ("written", 0), ("removed", 0))],
        };
        var starting = switching with { RecentLines = [.. switching.RecentLines, JobLine.ForStep(JobSteps.LaunchStarting, ("game", "Skyrim"))] };
        var longSwitch = Job(JobKind.Launch, cancelRequested: true) with { LinesDropped = 40 };

        Assert.Equal("Stopping leaves the game's folder with a mix of two snapshots. Press Play again and SavePoint finishes the job.", Row(switching).StopNote);
        Assert.Equal("Stopping leaves the game's folder with a mix of two snapshots. Press Play again and SavePoint finishes the job.", Row(longSwitch).StopNote);
        Assert.Equal("Stopping now.", Row(starting).StopNote);
    }

    [Fact]
    public void A_switch_that_is_stopping_says_the_folder_is_left_with_a_mix_and_how_to_finish_it()
    {
        var switching = Job(JobKind.Switch, cancelRequested: true) with
        {
            RecentLines = [JobLine.ForStep(JobSteps.LaunchSwitchFolder, ("folder", RepoRole.Install), ("profile", "Modded"), ("older", "no"), ("counted", "no"), ("written", 0), ("removed", 0))],
        };
        var notYet = Job(JobKind.Switch, cancelRequested: true);

        Assert.Equal("Stopping leaves the game's folder with a mix of two snapshots. Switch again and SavePoint finishes the job.", Row(switching).StopNote);
        Assert.Equal("Stopping now.", Row(notYet).StopNote);
    }

    [Theory]
    [InlineData(JobKind.Launch)]
    [InlineData(JobKind.Switch)]
    public void A_switch_that_is_reading_its_files_back_says_so_and_not_that_snapshots_are_being_stored(JobKind kind)
    {
        var row = Row(Job(kind, progress: new JobProgress(FilesDone: 2, FilesTotal: 2, BytesDone: 10, BytesTotal: 10, Finishing: true)));

        Assert.Equal("Making sure every file is in place…", row.ProgressText);
    }

    [Fact]
    public void A_snapshot_that_is_wrapping_up_keeps_the_storing_wording()
    {
        var row = Row(Job(JobKind.Capture, progress: new JobProgress(FilesDone: 2, FilesTotal: 2, BytesDone: 10, BytesTotal: 10, Finishing: true)));

        Assert.Equal("Making sure everything is stored safely. This can take a few minutes for a big game.", row.ProgressText);
    }

    [Fact]
    public void A_finished_job_has_nothing_to_say_about_stopping()
    {
        Assert.Equal(string.Empty, Row(Job(JobKind.ClearData, null, JobStatus.Succeeded, cancellable: false)).StopNote);
        Assert.Equal(string.Empty, Row(Job(status: JobStatus.Cancelled, cancelRequested: true)).StopNote);
    }

    [Fact]
    public void A_screen_reader_hears_the_job_its_state_its_profile_its_progress_and_when_it_started()
    {
        var row = Row(Job(
            status: JobStatus.Running,
            profile: new JobProfile("p1", "Vanilla"),
            progress: new JobProgress(FilesDone: 4, FilesTotal: 120),
            startedAgo: TimeSpan.FromHours(1)));

        Assert.Equal("Taking a snapshot of Skyrim Special Edition. In progress. Profile: Vanilla. 4 of 120 files. Started Jul 8, 4:00 PM.", row.AutomationName);
    }

    [Fact]
    public void A_screen_reader_hears_a_finished_job_without_progress_it_never_counted()
    {
        var row = Row(Job(JobKind.ClearData, null, JobStatus.Failed, startedAgo: TimeSpan.FromHours(1)));

        Assert.Equal("Clearing SavePoint's data. Failed. Started Jul 8, 4:00 PM.", row.AutomationName);
    }

    [Fact]
    public void A_newer_snapshot_changes_the_row_and_says_which_properties_changed()
    {
        var job = Job(progress: new JobProgress(FilesDone: 1, FilesTotal: 10));
        var row = Row(job);
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.Apply(job with { Status = JobStatus.Succeeded, FinishedAt = _now, Revision = 5 });

        Assert.Equal("Done", row.StatusText);
        Assert.Equal(JobTone.Success, row.Tone);
        Assert.False(row.IsRunning);
        Assert.True(row.HasFinished);
        Assert.Contains(nameof(JobRowViewModel.StatusText), changed);
        Assert.Contains(nameof(JobRowViewModel.HasFinished), changed);
        Assert.Equal(5, row.Job.Revision);
    }

    [Fact]
    public void Progress_that_changes_nothing_a_reader_sees_raises_no_change()
    {
        var job = Job(progress: new JobProgress(FilesDone: 1, FilesTotal: 10));
        var row = Row(job);
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.Apply(job with { Revision = 2 });

        Assert.Empty(changed);
    }
}
