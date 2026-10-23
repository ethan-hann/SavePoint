using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Windows raises its live-region event when a peer reports a name change on an element whose live setting is on, so the tests
// listen where Avalonia hands that change to the platform: the peer's PropertyChanged.
public class LiveRegionEventTests
{
    private static List<AutomationPropertyChangedEventArgs> Listen(Control control)
    {
        var events = new List<AutomationPropertyChangedEventArgs>();
        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        peer.PropertyChanged += (_, e) => events.Add(e);
        return events;
    }

    [AvaloniaFact]
    public void An_announcement_region_raises_a_name_change_for_each_message_even_a_repeat()
    {
        var region = new AnnouncementRegion();
        using var host = new ThemeHost(region);
        var events = Listen(region);

        region.Announce("Saving 25%");
        region.Announce("Saving 25%");

        var names = events.Where(e => e.Property == AutomationElementIdentifiers.NameProperty).Select(e => e.NewValue as string).ToList();
        Assert.Equal(AutomationLiveSetting.Polite, ControlAutomationPeer.CreatePeerForElement(region).GetLiveSetting());
        Assert.Equal(["Saving 25%", "Saving 25%"], names.Where(name => !string.IsNullOrEmpty(name)));
    }

    // The spoken text of every name change the page's own announcer reported while the arrangement ran.
    private static async Task<List<string>> SpokenAsync(ComposedApp app, Func<Task> arrange)
    {
        var announcer = app.All<AnnouncementRegion>().FirstOrDefault(region => region.Name == "PageAnnouncer");
        Assert.NotNull(announcer);
        var events = Listen(announcer);

        await arrange();
        app.Settle();

        return [.. events.Where(e => e.Property == AutomationElementIdentifiers.NameProperty).Select(e => e.NewValue).OfType<string>().Where(name => name.Length > 0)];
    }

    private static ProfileRowViewModel Row(ComposedApp app, string id) => app.Games.Profiles.Single(row => row.Id == id);

    [AvaloniaFact]
    public async Task An_error_card_that_appears_is_announced_through_the_pages_announcer()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), MockupScenario.FileInUseError);

        var spoken = await SpokenAsync(app, () => Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null));

        var card = Assert.IsType<ErrorCardViewModel>(app.Games.InlineContent);
        Assert.Contains(spoken, text => text.Contains(card.Title, StringComparison.Ordinal) && text.Contains(card.Body, StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task The_nothing_to_change_banner_that_appears_is_announced_through_the_pages_announcer()
    {
        using var app = await ComposedApp.StartWithViewAsync();

        var spoken = await SpokenAsync(app, () => Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null));

        var banner = Assert.IsType<LaunchBannerViewModel>(app.Games.InlineContent);
        Assert.Contains(spoken, text => text.Contains(banner.Title, StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task The_job_pill_is_spoken_through_its_own_announcer_when_a_job_starts()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var announcer = app.Find<JobStatusView>("JobPill").GetVisualDescendants().OfType<AnnouncementRegion>().Single();
        var events = Listen(announcer);

        FakeAssert.Ok(await app.Backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A snapshot"));
        app.Settle();

        var spoken = events.Where(e => e.Property == AutomationElementIdentifiers.NameProperty).Select(e => e.NewValue).OfType<string>();
        Assert.Contains("Taking a snapshot of Skyrim Special Edition…", spoken);
    }

    [AvaloniaFact]
    public async Task A_toast_is_spoken_through_the_toast_hosts_announcer()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var announcer = app.Find<ToastHostView>("Toasts").GetVisualDescendants().OfType<AnnouncementRegion>().Single();
        var events = Listen(announcer);

        app.Main.Toasts.Show(new ToastMessage("Saved Skyrim Special Edition.", ToastKind.Success));
        app.Settle();

        var spoken = events.Where(e => e.Property == AutomationElementIdentifiers.NameProperty).Select(e => e.NewValue).OfType<string>();
        Assert.Contains("Saved Skyrim Special Edition.", spoken);
    }
}
