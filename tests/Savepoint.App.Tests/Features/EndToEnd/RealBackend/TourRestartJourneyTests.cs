using Avalonia;
using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Features.Settings;

namespace Savepoint.App.Tests.Features.EndToEnd.RealBackend;

// The tour starts once for a person: the settings file remembers that it did, and the next start of the app, which reads that file again over the
// same data, does not show it.
public class TourRestartJourneyTests
{
    [AvaloniaFact(Timeout = 180_000)]
    public async Task A_shell_started_after_the_tour_ran_reads_that_it_ran_and_does_not_show_it_again()
    {
        await using var rig = await JourneyRig.StartAsync();
        var game = rig.NewGame("Journey Game");
        await rig.AddGameAsync(game, ["--saves", game.Saves, "--save-every", "200"]);
        await rig.CreateProfileAsync("Vanilla");
        Assert.True(rig.Shell.Tour.IsActive);
        var settingsFile = Path.Combine(rig.CatalogFolder, "settings.json");
        await rig.WaitAsync(() => File.Exists(settingsFile) && File.ReadAllText(settingsFile).Contains("\"tourSeen\": true", StringComparison.Ordinal), "the settings file to record that the tour started");

        var application = (App)Application.Current!;
        var restarted = AppComposition.Create(
            rig.Services,
            rig.Text,
            new ThemeServiceApplier(application.Theme),
            () => null,
            new AvaloniaUiDispatcher(),
            dialogs: rig.Dialogs,
            settingsEnvironment: new SettingsEnvironment(new RecordingFileRevealer(), () => null, "1.0.0"));
        await restarted.InitializeAsync();
        await rig.WaitAsync(() => restarted.Shell is { IsLoaded: true }, "the restarted shell to appear");

        Assert.False(restarted.Shell!.Tour.IsActive);
        Assert.False(restarted.Shell.ShowTour);
    }
}
