using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Games;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Games;

public class GamesScreenOpenFolderTests
{
    private static readonly ConsentStatus _confirmed = new(true, null);

    private static FakeBackend WithFolders(params FolderStatus[] folders) =>
        new(ScenarioBuilder.Sample().WithFolderStatus(SampleScenario.SkyrimId, folders));

    private static FolderStatus Install(bool exists = true) =>
        new(RepoRole.Install, SampleScenario.SkyrimInstall, SampleScenario.SkyrimInstall, exists, _confirmed);

    private static FolderStatus Saves(bool exists = true) =>
        new(RepoRole.External, SampleScenario.SkyrimSavesRaw, SampleScenario.SkyrimSaves, exists, _confirmed);

    private static ProfileMenuEntry Entry(ProfileRowViewModel row, string key) =>
        Assert.Single(row.MenuEntries, entry => entry.Key == key);

    private static Task Choose(ProfileMenuEntry entry) => ((IAsyncRelayCommand)entry.Command!).ExecuteAsync(entry.CommandParameter);

    [Fact]
    public async Task A_row_menu_offers_the_game_folder_and_the_saves_folder_after_duplicate_and_before_delete()
    {
        var rig = await GamesRig.StartAsync();

        var entries = rig.Screen.Profiles[0].MenuEntries;

        Assert.Equal(
            ["switch", "takeSnapshot", "takeSnapshotBlocked", "edit", "duplicate", "open:install", "open:external", "delete"],
            entries.Select(entry => entry.Key));
        Assert.Equal(["Open game folder", "Open saves folder"], entries.Where(entry => entry.Key.StartsWith("open:", StringComparison.Ordinal)).Select(entry => entry.Header));
    }

    [Fact]
    public async Task A_game_without_a_saves_folder_offers_only_the_game_folder()
    {
        var rig = await GamesRig.StartAsync(WithFolders(Install()));

        var opens = rig.Screen.Profiles[0].MenuEntries.Where(entry => entry.Key.StartsWith("open:", StringComparison.Ordinal));

        Assert.Equal(["open:install"], opens.Select(entry => entry.Key));
    }

    [Fact]
    public async Task Every_row_offers_the_same_folders()
    {
        var rig = await GamesRig.StartAsync();

        var keys = rig.Screen.Profiles.Select(row => string.Join(",", row.MenuEntries.Select(entry => entry.Key))).Distinct().ToList();

        Assert.Single(keys);
        Assert.Contains("open:external", keys[0]);
    }

    [Fact]
    public async Task Choosing_the_saves_folder_opens_that_folder_on_this_machine()
    {
        var rig = await GamesRig.StartAsync();

        await Choose(Entry(rig.Screen.Profiles[1], "open:external"));

        Assert.Equal([SampleScenario.SkyrimSaves], rig.Revealer.Revealed);
        Assert.Empty(rig.ToastTexts);
    }

    [Fact]
    public async Task Choosing_the_game_folder_opens_the_install_folder()
    {
        var rig = await GamesRig.StartAsync();

        await Choose(Entry(rig.Screen.Profiles[0], "open:install"));

        Assert.Equal([SampleScenario.SkyrimInstall], rig.Revealer.Revealed);
    }

    [Fact]
    public async Task A_folder_that_is_gone_says_so_instead_of_opening()
    {
        var rig = await GamesRig.StartAsync(WithFolders(Install(), Saves(exists: false)));

        await Choose(Entry(rig.Screen.Profiles[0], "open:external"));

        Assert.Empty(rig.Revealer.Revealed);
        Assert.Contains("can't find the saves folder", Assert.Single(rig.ToastTexts));
    }

    [Fact]
    public async Task A_folder_that_went_missing_after_the_screen_loaded_is_caught_when_it_is_chosen()
    {
        var rig = await GamesRig.StartAsync();
        rig.Backend.State.FolderStatuses[SampleScenario.SkyrimId] = [Install(), Saves(exists: false)];

        await Choose(Entry(rig.Screen.Profiles[0], "open:external"));

        Assert.Empty(rig.Revealer.Revealed);
        Assert.Contains("can't find the saves folder", Assert.Single(rig.ToastTexts));
    }

    [Fact]
    public async Task A_file_manager_that_cannot_start_is_reported()
    {
        var rig = await GamesRig.StartAsync();
        rig.Revealer.Succeeds = false;

        await Choose(Entry(rig.Screen.Profiles[0], "open:install"));

        Assert.Equal("SavePoint couldn't open the game folder.", Assert.Single(rig.ToastTexts));
    }

    [Fact]
    public async Task A_folder_kind_with_no_label_of_its_own_still_reads_as_a_folder()
    {
        var rig = await GamesRig.StartAsync();

        var label = rig.Text.Message("games.profileTable.openFolder", ("role", "mods"));

        Assert.Equal("Open folder", label);
    }
}
