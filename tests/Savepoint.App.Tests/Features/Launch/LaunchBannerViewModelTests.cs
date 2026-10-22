using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Launch;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Theme;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Launch;

public class LaunchBannerViewModelTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private readonly FakeBackend _backend = FakeBackend.Mockup();
    private readonly FakeDialogs _dialogs = new();

    private async Task<LaunchPreview> PreviewAsync(string gameId, LaunchTarget target) =>
        FakeAssert.Ok(await _backend.Launch.GetPreviewAsync(gameId, target));

    private LaunchBannerViewModel Banner(LaunchPreview preview, string game = "Skyrim Special Edition", string profile = "Modded playthrough - 7-2-26") =>
        new(preview, game, profile, _text, _dialogs);

    [Fact]
    public async Task The_banner_says_everything_was_in_place_and_names_the_profile_and_the_save_that_was_loaded()
    {
        var banner = Banner(await PreviewAsync(MockupScenario.SkyrimId, new LaunchTarget.ProfileTip(MockupScenario.ModdedId)));

        Assert.Equal("Everything was already in place — starting Skyrim Special Edition now", banner.Title);
        Assert.Equal("Modded playthrough - 7-2-26 · save \"Before the final boss\" loaded", banner.Detail);
    }

    [Fact]
    public async Task Without_a_save_to_name_the_detail_is_just_the_profile()
    {
        var preview = await PreviewAsync(MockupScenario.SatisfactoryId, new LaunchTarget.ProfileTip(MockupScenario.MainFactoryId));

        var banner = Banner(preview, "Satisfactory", "Main factory - 6-1-26");

        Assert.Equal("Main factory - 6-1-26", banner.Detail);
    }

    [Fact]
    public async Task The_checks_list_what_was_compared_and_where()
    {
        var banner = Banner(await PreviewAsync(MockupScenario.SkyrimId, new LaunchTarget.ProfileTip(MockupScenario.ModdedId)));

        Assert.Collection(
            banner.Checks,
            install =>
            {
                Assert.Equal("Game version and mods: already on 1.6.640 with 212 mod files", install.Text);
                Assert.Equal(MockupScenario.SkyrimInstall, install.Path);
                Assert.Equal(Icons.CheckCircle, install.Icon);
            },
            saves =>
            {
                Assert.Equal("Saves: already match \"Before the final boss\"", saves.Text);
                Assert.Equal(MockupScenario.SkyrimSaves, saves.Path);
            },
            clean =>
            {
                Assert.Equal("No new changes in your game files or saves", clean.Text);
                Assert.Null(clean.Path);
            });
    }

    [Fact]
    public async Task A_game_with_no_saves_folder_checks_only_the_game_files()
    {
        var preview = (await PreviewAsync(MockupScenario.SkyrimId, new LaunchTarget.ProfileTip(MockupScenario.ModdedId))) with { External = null, AvailableSaves = [], SelectedSaveId = null };

        var banner = Banner(preview);

        Assert.Collection(
            banner.Checks,
            install => Assert.StartsWith("Game version and mods", install.Text),
            clean => Assert.Equal("No new changes in your game files", clean.Text));
    }

    [Fact]
    public async Task An_install_with_no_known_version_checks_in_general_terms()
    {
        var preview = (await PreviewAsync(MockupScenario.SkyrimId, new LaunchTarget.ProfileTip(MockupScenario.ModdedId))) with { TargetInstall = new InstallState(null, null) };

        var banner = Banner(preview);

        Assert.Equal("Game version and mods: already match this profile", banner.Checks[0].Text);
    }

    [Fact]
    public async Task Details_opens_the_checks_in_a_dialog_with_a_close_button()
    {
        var banner = Banner(await PreviewAsync(MockupScenario.SkyrimId, new LaunchTarget.ProfileTip(MockupScenario.ModdedId)));

        var open = banner.ShowDetailsCommand.ExecuteAsync(null);

        var dialog = Assert.IsType<LaunchChecksDialogViewModel>(Assert.Single(_dialogs.Shown));
        Assert.Equal("What SavePoint checked", dialog.Title);
        Assert.Equal(banner.Checks, dialog.Checks);
        Assert.True(dialog.CanDismiss);
        Assert.False(banner.ShowDetailsCommand.CanExecute(null));
        dialog.CloseDialogCommand.Execute(null);
        await open;
        Assert.True(dialog.IsClosed);
        Assert.True(banner.ShowDetailsCommand.CanExecute(null));
    }
}
