using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Settings;

// Settings > Appearance: "Look up game artwork online". It is saved like every other setting, and what the screens show follows it.
public class AppearanceArtworkTests
{
    private sealed class RecordingProvider : IArtworkProvider
    {
        public int Refreshes { get; private set; }

        public GameArt For(string gameId) => NoArtworkProvider.Instance.For(gameId);

        public GameArt Preview(string gameId) => NoArtworkProvider.Instance.Preview(gameId);

        public void Refresh() => Refreshes++;
    }

    private static async Task<AppSettings> StoredAsync(SettingsRig rig) =>
        (await rig.Backend.Settings.GetAsync(TestContext.Current.CancellationToken)).Value!;

    private static ErrorInfo SaveFailure() => ErrorInfo.From(new StorageUnreachableException("/storage", null));

    [Fact]
    public async Task The_switch_is_on_by_default_and_follows_what_is_stored()
    {
        var rig = await SettingsRig.ShownAsync();
        var off = FakeBackend.Mockup();
        await off.Settings.SaveAsync((await off.Settings.GetAsync(TestContext.Current.CancellationToken)).Value! with { LookUpArtworkOnline = false });
        var offRig = await SettingsRig.ShownAsync(off);

        Assert.True(rig.Screen.Appearance.LookUpArtworkOnline);
        Assert.False(offRig.Screen.Appearance.LookUpArtworkOnline);
    }

    [Fact]
    public async Task Turning_it_off_saves_only_that_setting_and_the_shell_hears_about_it()
    {
        var rig = await SettingsRig.ShownAsync();
        var before = await StoredAsync(rig);
        var told = rig.Theme.Applied.Count;

        rig.Screen.Appearance.LookUpArtworkOnline = false;

        var stored = await StoredAsync(rig);
        Assert.False(stored.LookUpArtworkOnline);
        Assert.Equal(before with { LookUpArtworkOnline = false }, stored);
        Assert.False(rig.Screen.Appearance.LookUpArtworkOnline);
        Assert.Equal(told + 1, rig.Theme.Applied.Count);
    }

    [Fact]
    public async Task Turning_it_back_on_saves_that_too()
    {
        var rig = await SettingsRig.ShownAsync();

        rig.Screen.Appearance.LookUpArtworkOnline = false;
        rig.Screen.Appearance.LookUpArtworkOnline = true;

        Assert.True((await StoredAsync(rig)).LookUpArtworkOnline);
        Assert.Equal(2, rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
    }

    [Fact]
    public async Task A_save_that_fails_flips_the_switch_back_and_says_so()
    {
        var rig = await SettingsRig.ShownAsync();
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.SaveAsync), SaveFailure());

        rig.Screen.Appearance.LookUpArtworkOnline = false;

        Assert.True(rig.Screen.Appearance.LookUpArtworkOnline);
        Assert.Single(rig.Toasts.Toasts);
        Assert.True((await StoredAsync(rig)).LookUpArtworkOnline);
    }

    [Fact]
    public async Task A_change_that_saved_has_the_artwork_read_again_and_one_that_failed_does_not()
    {
        var provider = new RecordingProvider();
        var rig = await SettingsRig.StartAsync(artwork: provider);
        rig.Show();

        rig.Screen.Appearance.LookUpArtworkOnline = false;
        Assert.Equal(1, provider.Refreshes);
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.SaveAsync), SaveFailure());
        rig.Screen.Appearance.LookUpArtworkOnline = true;

        Assert.Equal(1, provider.Refreshes);
    }

    [Fact]
    public async Task Showing_a_stored_off_is_not_a_choice_and_saves_nothing()
    {
        var backend = FakeBackend.Mockup();
        await backend.Settings.SaveAsync((await backend.Settings.GetAsync(TestContext.Current.CancellationToken)).Value! with { LookUpArtworkOnline = false });
        var provider = new RecordingProvider();
        var rig = await SettingsRig.StartAsync(backend, artwork: provider);
        var saves = rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync));

        rig.Show();

        Assert.False(rig.Screen.Appearance.LookUpArtworkOnline);
        Assert.Equal(saves, rig.Calls<ISettingsApi>(nameof(ISettingsApi.SaveAsync)));
        Assert.Equal(0, provider.Refreshes);
    }

    [AvaloniaFact]
    public async Task The_appearance_section_offers_the_switch_with_its_note_and_names_it_for_assistive_technology()
    {
        using var harness = await SettingsScreenViewTests.StartAsync();

        var texts = harness.VisibleTexts().ToList();
        var toggle = harness.Find<ToggleSwitch>("ArtworkSwitch");

        Assert.Contains("Look up game artwork online", texts);
        Assert.Contains(texts, text => text?.Contains("already on this computer is used either way", StringComparison.Ordinal) == true);
        Assert.Equal("Look up game artwork online", AutomationProperties.GetName(toggle));
        Assert.Contains("already on this computer", AutomationProperties.GetHelpText(toggle) ?? string.Empty, StringComparison.Ordinal);
        Assert.True(toggle.IsChecked);
    }

    [AvaloniaFact]
    public async Task Flipping_the_switch_in_the_view_saves_the_setting()
    {
        using var harness = await SettingsScreenViewTests.StartAsync();
        var toggle = harness.Find<ToggleSwitch>("ArtworkSwitch");

        toggle.IsChecked = false;
        harness.Settle();

        var stored = (await harness.Backend.Settings.GetAsync(TestContext.Current.CancellationToken)).Value!;
        Assert.False(stored.LookUpArtworkOnline);
    }
}
