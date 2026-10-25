using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Launch;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Launch;

public class SwitchProgressDialogViewModelTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static readonly InstallState _target = new("1.6.640", 189);

    private readonly ManualTimeProvider _clock = new(ScenarioBuilder.DefaultNow);
    private readonly List<int> _cancelCalls = [];
    private bool _cancelAnswer = true;

    private SwitchProgressDialogViewModel Create(InstallState? target = null, string game = "Skyrim Special Edition") =>
        new(game, target ?? _target, _text, _clock, () =>
        {
            _cancelCalls.Add(1);
            return Task.FromResult(_cancelAnswer);
        });

    [Fact]
    public void The_title_names_the_game_and_the_explanation_names_the_version_and_mod_files_being_put_back()
    {
        var dialog = Create();

        Assert.Equal("Getting Skyrim Special Edition ready…", dialog.Title);
        Assert.Equal("Putting version 1.6.640 and its 189 mod files back in place.", dialog.Explanation);
    }

    [Fact]
    public void A_version_with_no_mod_files_leaves_the_mods_out_of_the_explanation()
    {
        var dialog = Create(new InstallState("1.6.1170", 0));

        Assert.Equal("Putting version 1.6.1170 back in place.", dialog.Explanation);
    }

    [Fact]
    public void A_single_mod_file_is_singular()
    {
        var dialog = Create(new InstallState("2.0", 1));

        Assert.Equal("Putting version 2.0 and its 1 mod file back in place.", dialog.Explanation);
    }

    [Fact]
    public void Without_a_known_version_the_explanation_is_the_general_hint()
    {
        var dialog = Create(new InstallState(null, null));

        Assert.Equal("This can take a while for a large install, especially if a lot changed.", dialog.Explanation);
    }

    [Fact]
    public void It_starts_indeterminate_says_it_is_checking_and_cannot_be_dismissed()
    {
        var dialog = Create();

        Assert.True(dialog.IsIndeterminate);
        Assert.Equal("Checking your files…", dialog.ProgressText);
        Assert.Equal(string.Empty, dialog.PercentText);
        Assert.Null(dialog.CurrentPath);
        Assert.False(dialog.CanDismiss);
        Assert.Equal(600, dialog.DialogWidth);
        Assert.False(dialog.IsCancelling);
    }

    [Fact]
    public void Dismissing_a_dialog_that_cannot_be_dismissed_leaves_it_open()
    {
        var dialog = Create();

        dialog.RequestDismiss();

        Assert.False(dialog.IsClosed);
    }

    [Fact]
    public void Byte_totals_make_the_bar_determinate_with_bytes_done_time_left_percent_and_the_current_path()
    {
        var dialog = Create();

        dialog.Update(SampleScenario.SwitchProgress);

        Assert.False(dialog.IsIndeterminate);
        Assert.Equal(62.3, dialog.PercentValue, 1);
        Assert.Equal("62%", dialog.PercentText);
        Assert.Equal("3.8 GB of 6.1 GB · about 50 seconds left", dialog.ProgressText);
        Assert.Equal(@"Data\textures\architecture\whiterun\wrbuilding05.dds", dialog.CurrentPath);
    }

    [Fact]
    public void A_long_time_left_is_shown_in_whole_minutes()
    {
        var dialog = Create();

        dialog.Update(new JobProgress(BytesDone: 100_000_000, BytesTotal: 6_100_000_000));

        Assert.Equal("100 MB of 6.1 GB · about 2 minutes left", dialog.ProgressText);
    }

    [Fact]
    public void File_totals_without_bytes_show_a_file_count_and_no_time()
    {
        var dialog = Create();

        dialog.Update(new JobProgress(FilesDone: 812, FilesTotal: 1_311));

        Assert.False(dialog.IsIndeterminate);
        Assert.Equal("812 of 1,311 files", dialog.ProgressText);
        Assert.Equal("61%", dialog.PercentText);
    }

    [Fact]
    public void A_switch_that_is_removing_files_moves_the_bar_while_no_bytes_have_been_written()
    {
        var dialog = Create();

        // 150 of 201 files are gone, and the one file git has to write has not been started: no byte is done yet.
        dialog.Update(new JobProgress(FilesDone: 150, FilesTotal: 201, BytesDone: 0, BytesTotal: 10));

        Assert.False(dialog.IsIndeterminate);
        Assert.Equal(74.6, dialog.PercentValue, 1);
        Assert.Equal("74%", dialog.PercentText);
        Assert.Equal("150 of 201 files", dialog.ProgressText);
    }

    [Fact]
    public void The_time_left_of_a_removal_follows_the_share_of_files_done_once_the_switch_has_run_for_a_few_seconds()
    {
        var dialog = Create();
        dialog.Update(new JobProgress(FilesDone: 0, FilesTotal: 200, BytesDone: 0, BytesTotal: 10));

        _clock.Advance(TimeSpan.FromSeconds(10));
        dialog.Update(new JobProgress(FilesDone: 100, FilesTotal: 200, BytesDone: 0, BytesTotal: 10));

        // Half the files in 10 seconds leaves 10 seconds.
        Assert.Equal("100 of 200 files · about 10 seconds left", dialog.ProgressText);
        Assert.Equal(50, dialog.PercentValue);
    }

    [Fact]
    public void When_the_bytes_are_further_along_than_the_files_the_bar_and_the_line_follow_the_bytes()
    {
        var dialog = Create();

        dialog.Update(new JobProgress(FilesDone: 1, FilesTotal: 201, BytesDone: 900_000_000, BytesTotal: 1_000_000_000));

        Assert.Equal(90, dialog.PercentValue);
        Assert.StartsWith("900 MB of 1 GB", dialog.ProgressText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bar_never_goes_back_when_the_files_overtake_the_bytes_and_the_bytes_catch_up()
    {
        var dialog = Create();
        var seen = new List<double>();

        foreach (var progress in new[]
        {
            new JobProgress(FilesDone: 100, FilesTotal: 200, BytesDone: 0, BytesTotal: 1_000),
            new JobProgress(FilesDone: 199, FilesTotal: 200, BytesDone: 0, BytesTotal: 1_000),
            new JobProgress(FilesDone: 200, FilesTotal: 200, BytesDone: 500, BytesTotal: 1_000),
            new JobProgress(FilesDone: 200, FilesTotal: 200, BytesDone: 1_000, BytesTotal: 1_000),
        })
        {
            dialog.Update(progress);
            seen.Add(dialog.PercentValue);
        }

        Assert.Equal(seen.Order(), seen);
        Assert.Equal(100, seen[^1]);
    }

    [Fact]
    public void A_count_without_a_total_stays_indeterminate_and_says_the_total_is_being_worked_out()
    {
        var dialog = Create();

        dialog.Update(new JobProgress(FilesDone: 812));

        Assert.True(dialog.IsIndeterminate);
        Assert.Equal("Working out how much there is to copy…", dialog.ProgressText);
        Assert.Equal(string.Empty, dialog.PercentText);
    }

    [Fact]
    public void One_file_in_all_is_not_called_files()
    {
        var dialog = Create();

        dialog.Update(new JobProgress(FilesDone: 1, FilesTotal: 1));

        Assert.Equal("1 of 1 file", dialog.ProgressText);
    }

    [Fact]
    public void A_report_with_nothing_in_it_says_the_files_are_being_checked_and_stays_indeterminate()
    {
        var dialog = Create();

        dialog.Update(JobProgress.None);

        Assert.True(dialog.IsIndeterminate);
        Assert.Equal("Checking your files…", dialog.ProgressText);
    }

    [Fact]
    public void Once_every_file_is_written_the_dialog_names_the_check_that_follows_instead_of_standing_at_full()
    {
        var dialog = Create();
        dialog.Update(new JobProgress(FilesDone: 2, FilesTotal: 2, BytesDone: 4_300_000_000, BytesTotal: 4_300_000_000));
        Assert.Equal("100%", dialog.PercentText);

        dialog.Update(new JobProgress(FilesDone: 2, FilesTotal: 2, BytesDone: 4_300_000_000, BytesTotal: 4_300_000_000, Finishing: true));

        Assert.True(dialog.IsIndeterminate);
        Assert.Equal(string.Empty, dialog.PercentText);
        Assert.Equal("Making sure every file is in place…", dialog.ProgressText);
        Assert.Equal("Making sure every file is in place.", dialog.Announcement);
    }

    [Fact]
    public void The_check_is_announced_once_however_many_reports_say_it_is_running()
    {
        var dialog = Create();
        dialog.Update(new JobProgress(FilesDone: 2, FilesTotal: 2, BytesDone: 10, BytesTotal: 10, Finishing: true));
        dialog.Announcement = string.Empty;

        dialog.Update(new JobProgress(FilesDone: 2, FilesTotal: 2, BytesDone: 10, BytesTotal: 10, Finishing: true));

        Assert.Equal(string.Empty, dialog.Announcement);
    }

    [Fact]
    public void The_time_left_follows_the_measured_rate_once_the_switch_has_run_for_a_few_seconds()
    {
        var dialog = Create();
        dialog.Update(new JobProgress(BytesDone: 0, BytesTotal: 6_000_000_000));

        _clock.Advance(TimeSpan.FromSeconds(10));
        dialog.Update(new JobProgress(BytesDone: 3_000_000_000, BytesTotal: 6_000_000_000));

        // 3 GB in 10 seconds leaves 3 GB at 300 MB/s.
        Assert.Equal("3 GB of 6 GB · about 10 seconds left", dialog.ProgressText);
    }

    [Fact]
    public void A_complete_report_fills_the_bar_and_shows_no_time_left()
    {
        var dialog = Create();

        dialog.Update(new JobProgress(BytesDone: 6_100_000_000, BytesTotal: 6_100_000_000));

        Assert.Equal("6.1 GB of 6.1 GB", dialog.ProgressText);
        Assert.Equal(100, dialog.PercentValue);
        Assert.Equal("100%", dialog.PercentText);
    }

    [Fact]
    public void Quarters_are_announced_once_each_and_only_the_highest_one_crossed()
    {
        var dialog = Create();
        Assert.Equal("Getting Skyrim Special Edition ready.", dialog.Announcement);
        var spoken = new List<string>();
        dialog.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SwitchProgressDialogViewModel.Announcement))
            {
                spoken.Add(dialog.Announcement);
            }
        };

        dialog.Update(new JobProgress(BytesDone: 100, BytesTotal: 1_000));
        dialog.Update(new JobProgress(BytesDone: 260, BytesTotal: 1_000));
        dialog.Update(new JobProgress(BytesDone: 300, BytesTotal: 1_000));
        dialog.Update(new JobProgress(BytesDone: 800, BytesTotal: 1_000));
        dialog.Update(new JobProgress(BytesDone: 900, BytesTotal: 1_000));

        Assert.Equal(["25% done.", "75% done."], spoken);
    }

    [Fact]
    public async Task Cancel_asks_the_service_and_shows_that_it_is_canceling()
    {
        var dialog = Create();

        await dialog.CancelCommand.ExecuteAsync(null);

        Assert.Single(_cancelCalls);
        Assert.True(dialog.IsCancelling);
        Assert.Equal("Canceling the switch.", dialog.Announcement);
        Assert.False(dialog.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_cancel_the_service_could_not_take_lets_the_user_try_again()
    {
        _cancelAnswer = false;
        var dialog = Create();

        await dialog.CancelCommand.ExecuteAsync(null);

        Assert.False(dialog.IsCancelling);
        Assert.True(dialog.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task Finishing_closes_the_dialog_and_ends_its_task()
    {
        var dialog = Create();

        dialog.Finish();

        Assert.True(dialog.IsClosed);
        Assert.True(await dialog.Result);
    }
}
