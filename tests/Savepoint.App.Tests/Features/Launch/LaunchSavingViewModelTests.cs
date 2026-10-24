using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Launch;
using Savepoint.App.Localization;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Launch;

public class LaunchSavingViewModelTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static LaunchSavingViewModel Create() => new(_text, () => Task.FromResult(true));

    [Fact]
    public void Quarters_of_the_save_are_announced_once_each_and_only_the_highest_one_crossed()
    {
        var saving = Create();
        var spoken = new List<string>();
        saving.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LaunchSavingViewModel.Announcement))
            {
                spoken.Add(saving.Announcement);
            }
        };

        saving.Update(new JobProgress(FilesDone: 10, FilesTotal: 100));
        saving.Update(new JobProgress(FilesDone: 26, FilesTotal: 100));
        saving.Update(new JobProgress(FilesDone: 30, FilesTotal: 100));
        saving.Update(new JobProgress(FilesDone: 80, FilesTotal: 100));
        saving.Update(new JobProgress(FilesDone: 90, FilesTotal: 100));

        Assert.Equal(["25% copied.", "75% copied."], spoken);
    }

    [Fact]
    public void Bytes_lead_when_the_job_reports_them_and_a_single_file_is_not_called_files()
    {
        var saving = Create();

        saving.Update(new JobProgress(0, 3, 35_000_000_000, 70_000_000_000));
        Assert.Equal("35 GB of 70 GB", saving.ProgressText);
        Assert.Equal(50, saving.PercentValue);

        saving.Update(new JobProgress(1, 1));
        Assert.Equal("1 of 1 file", saving.ProgressText);
    }

    [Fact]
    public void A_save_with_a_count_but_no_total_says_it_is_working_out_how_much_there_is()
    {
        var saving = Create();

        saving.Update(new JobProgress(FilesDone: 12));

        Assert.Equal("Working out how much there is to copy…", saving.ProgressText);
        Assert.True(saving.IsIndeterminate);
    }

    [Fact]
    public void A_save_with_no_total_announces_nothing_beyond_the_card_itself()
    {
        var saving = Create();

        saving.Update(new JobProgress(FilesDone: 500));

        Assert.Equal(string.Empty, saving.Announcement);
    }
}
