using Avalonia.Automation;
using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Games;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.EndToEnd;

// A person who cannot see a progress bar still hears the milestones: the start and each quarter, through the live region beside the bar.
public class ProgressAnnouncementTests
{
    private const string _skyrim = MockupScenario.SkyrimId;

    // Several screens name their live region "Announcer", so what a person could hear is every region of that name.
    private static IEnumerable<string> Spoken(ComposedApp app, string regionName) =>
        app.All<AnnouncementRegion>().Where(region => region.Name == regionName).Select(region => AutomationProperties.GetName(region) ?? string.Empty);

    [AvaloniaFact]
    public async Task The_switch_dialog_says_it_has_started_and_then_each_quarter()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var play = app.Games.Profiles.Single(row => row.Id == MockupScenario.VanillaId).PlayCommand.ExecuteAsync(null);
        app.Dialog<LaunchDialogViewModel>().SwitchCommand.Execute(null);
        await play.Soon();
        app.Settle();

        Assert.Equal(["Getting Skyrim Special Edition ready."], Spoken(app, "ProgressAnnouncer"));

        app.Backend.Launch.ReportSwitching(_skyrim, MockupScenario.SwitchProgress);
        app.Settle();

        Assert.Equal(["50% done."], Spoken(app, "ProgressAnnouncer"));

        app.Backend.Launch.ReportSwitching(_skyrim, MockupScenario.SwitchProgress with { BytesDone = 5_000_000_000 });
        app.Settle();

        Assert.Equal(["75% done."], Spoken(app, "ProgressAnnouncer"));
    }

    [AvaloniaFact]
    public async Task The_save_after_a_session_says_each_quarter()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        await app.Games.Profiles.Single(row => row.Id == MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();
        app.Backend.Launch.ReportRunning(_skyrim, MockupScenario.RunningSince);
        app.Backend.Launch.ReportExited(_skyrim, MockupScenario.PlayDuration, MockupScenario.NewSaveFiles);
        await app.Dialog<PostPlayDialogViewModel>().SaveCommand.ExecuteAsync(null).Soon();
        app.Settle();
        var capture = app.Backend.Jobs.LastStarted(JobKind.Capture);

        app.Backend.Jobs.ReportProgress(capture.JobId, new JobProgress(FilesDone: 30, FilesTotal: 100));
        app.Settle();

        Assert.Contains("25% copied.", Spoken(app, "Announcer"));

        app.Backend.Jobs.ReportProgress(capture.JobId, new JobProgress(FilesDone: 80, FilesTotal: 100));
        app.Settle();

        Assert.Contains("75% copied.", Spoken(app, "Announcer"));
    }

    [AvaloniaFact]
    public async Task Moving_the_data_says_it_has_started_and_then_each_quarter()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(Savepoint.App.Features.Shell.NavDestination.Settings);
        app.Settle();

        var started = FakeAssert.Ok(await app.Backend.Settings.RelocateStorageAsync(@"E:\NewData"));
        app.Settle();

        Assert.Equal(["Moving your snapshots\u2026"], Spoken(app, "MoveAnnouncer"));

        app.Backend.Jobs.ReportProgress(started.JobId, new JobProgress(BytesDone: 600, BytesTotal: 1_000));
        app.Settle();

        Assert.Equal(["50% moved."], Spoken(app, "MoveAnnouncer"));
    }

    [AvaloniaFact]
    public async Task The_job_pill_says_what_the_job_is_when_it_starts()
    {
        using var app = await ComposedApp.StartWithViewAsync();

        FakeAssert.Ok(await app.Backend.Games.OptimizeStorageAsync(_skyrim));
        app.Settle();

        Assert.Contains("Freeing up space for Skyrim Special Edition\u2026", Spoken(app, "Announcer"));
    }
}
