using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Hosting;
using Savepoint.App.Localization;
using Savepoint.App.Platform;
using Savepoint.App.Tests.Fakes;

namespace Savepoint.App.Tests.Hosting;

// A snapshot that finishes while the player has the window minimized (the "minimize instead of closing" choice) still announces itself.
public class HostIntegrationNotificationTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en"), os: "windows");

    private sealed class RecordingNotifications : INotificationService
    {
        public List<(string Title, string Body)> Shown { get; } = [];

        public void Show(string title, string body) => Shown.Add((title, body));
    }

    // The headless platform never activates a window by itself, so this calls the callback a real platform would.
    private static void PlatformActivates(Window window)
    {
        var impl = window.PlatformImpl ?? throw new InvalidOperationException("The window has no platform implementation.");
        var property = impl.GetType().GetProperty("Activated", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        ((Action?)property?.GetValue(impl) ?? throw new InvalidOperationException("No Activated callback."))();
    }

    private static async Task<(Window Window, FakeBackend Backend, RecordingNotifications Notifications, HostIntegration Integration, Guid JobId)> StartAsync()
    {
        var backend = FakeBackend.Sample();
        var services = new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork);
        var window = new Window { Width = 200, Height = 100 };
        window.Show();
        PlatformActivates(window);
        var notifications = new RecordingNotifications();
        var integration = HostIntegration.Attach(window, ((App)App.Current!).Theme, services, _text, notifications);
        var job = FakeAssert.Ok(await backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point")).JobId;
        return (window, backend, notifications, integration, job);
    }

    private static async Task SettleAsync(Func<bool> done, TimeSpan? limit = null)
    {
        var until = DateTime.UtcNow + (limit ?? TimeSpan.FromSeconds(10));
        while (!done() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task A_snapshot_that_finishes_while_the_window_is_minimized_raises_a_notification()
    {
        var (window, backend, notifications, integration, job) = await StartAsync();
        using var scope = integration;
        window.WindowState = WindowState.Minimized;

        backend.Jobs.Complete(job, null);
        await SettleAsync(() => notifications.Shown.Count > 0);

        var shown = Assert.Single(notifications.Shown);
        Assert.Equal("Took a snapshot of Skyrim Special Edition.", shown.Body);
    }

    [AvaloniaFact]
    public async Task A_snapshot_that_finishes_in_front_of_the_player_raises_no_notification()
    {
        var (_, backend, notifications, integration, job) = await StartAsync();
        using var scope = integration;

        backend.Jobs.Complete(job, null);
        await SettleAsync(() => false, TimeSpan.FromMilliseconds(300));

        Assert.Empty(notifications.Shown);
    }
}
