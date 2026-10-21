using System.Text;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using static Savepoint.Core.Tests.Catalog.CatalogSamples;

namespace Savepoint.Core.Tests.Catalog;

public class CatalogStoreTests : CatalogStoreTestBase
{
    private const string _v1Games = """
        [
          {
            "schemaVersion": 1,
            "id": "skyrim-se",
            "name": "Skyrim",
            "install": { "path": "C:\\Games\\SkyrimSE" },
            "external": { "path": "{documents}\\My Games\\Skyrim Special Edition\\Saves", "role": "saves" },
            "launch": { "method": "exe", "exePath": "SkyrimSELauncher.exe", "args": [], "processNameForExitDetection": "SkyrimSE.exe" },
            "ignorePaths": ["**/*.log"],
            "modPathHints": ["Data"]
          },
          {
            "schemaVersion": 1,
            "id": "indie",
            "name": "Indie",
            "install": { "path": "C:\\Games\\Indie" },
            "launch": { "method": "uri", "uri": "steam://run/1" }
          }
        ]
        """;

    private const string _v1Profiles = """
        [
          { "id": "a", "gameId": "skyrim-se", "name": "A", "branchName": "profile/a", "lastLaunchedAt": "2026-07-02T19:11:03.123Z" },
          { "id": "b", "gameId": "indie", "name": "B", "branchName": "profile/b", "versionOverride": "2.0" }
        ]
        """;

    private const string _v1Settings = """
        {
          "storageRoot": "C:\\Users\\Ethan\\Documents\\SavePoint",
          "theme": "dark",
          "accentColorId": "green",
          "locale": "en",
          "layout": "library",
          "confirmSessionCapture": true,
          "skipConfirmWhenOnlySaveChanges": false
        }
        """;

    [Fact]
    public async Task Missing_files_read_as_empty_and_reading_creates_nothing()
    {
        Assert.Empty(await Store.ListGamesAsync(Ct));
        Assert.Null(await Store.GetGameAsync("nope", Ct));
        Assert.Empty(await Store.ListProfilesAsync(cancellationToken: Ct));
        Assert.Null(await Store.GetProfileAsync("nope", Ct));
        Assert.Null(await Store.GetSettingsAsync(Ct));
        Assert.Empty(FileNames());
    }

    [Fact]
    public async Task Reading_from_a_directory_that_does_not_exist_yet_is_not_an_error()
    {
        var store = new CatalogStore(Path.Combine(Dir, "not", "yet"));

        Assert.Empty(await store.ListGamesAsync(Ct));
        Assert.Null(await store.GetSettingsAsync(Ct));
        Assert.False(Directory.Exists(Path.Combine(Dir, "not")));
    }

    [Fact]
    public async Task First_write_creates_the_missing_directory()
    {
        var store = new CatalogStore(Path.Combine(Dir, "nested", "catalog"));

        await store.SaveGameAsync(ExeGame(), Ct);

        Assert.True(File.Exists(Path.Combine(Dir, "nested", "catalog", "games.json")));
    }

    [Fact]
    public async Task Saved_game_is_listed_and_can_be_fetched_by_id()
    {
        var game = ExeGame();

        await Store.SaveGameAsync(game, Ct);

        Assert.Equal([game], await Store.ListGamesAsync(Ct));
        Assert.Equal(game, await Store.GetGameAsync(game.Id, Ct));
        Assert.Null(await Store.GetGameAsync("other", Ct));
    }

    [Fact]
    public async Task Games_and_profiles_and_settings_survive_a_new_store_instance()
    {
        var game = ExeGame();
        var profile = Profile() with { LastLaunchedAt = new DateTimeOffset(2026, 7, 2, 19, 11, 3, TimeSpan.FromHours(-5)) };
        var settings = Settings() with { Theme = ThemePreference.Dark, LastOptimizedAt = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero) };
        await Store.SaveGameAsync(game, Ct);
        await Store.SaveProfileAsync(profile, Ct);
        await Store.SaveSettingsAsync(settings, Ct);

        var restarted = new CatalogStore(Dir);

        Assert.Equal([game], await restarted.ListGamesAsync(Ct));
        Assert.Equal([profile], await restarted.ListProfilesAsync(cancellationToken: Ct));
        Assert.Equal(settings, await restarted.GetSettingsAsync(Ct));
        Assert.Equal(profile.LastLaunchedAt, (await restarted.GetProfileAsync(profile.Id, Ct))!.LastLaunchedAt);
    }

    [Fact]
    public async Task Saving_a_game_with_an_existing_id_replaces_it_in_place()
    {
        await Store.SaveGameAsync(ExeGame("a-1"), Ct);
        await Store.SaveGameAsync(ExeGame("b-2"), Ct);
        await Store.SaveGameAsync(ExeGame("c-3"), Ct);

        await Store.SaveGameAsync(ExeGame("b-2") with { Name = "Renamed" }, Ct);

        var games = await Store.ListGamesAsync(Ct);
        Assert.Equal(["a-1", "b-2", "c-3"], games.Select(g => g.Id));
        Assert.Equal("Renamed", games[1].Name);
    }

    [Fact]
    public async Task Deleting_a_game_removes_only_that_game_and_reports_whether_it_existed()
    {
        await Store.SaveGameAsync(ExeGame("a-1"), Ct);
        await Store.SaveGameAsync(ExeGame("b-2"), Ct);

        Assert.True(await Store.DeleteGameAsync("a-1", Ct));
        Assert.False(await Store.DeleteGameAsync("a-1", Ct));

        Assert.Equal(["b-2"], (await Store.ListGamesAsync(Ct)).Select(g => g.Id));
    }

    [Fact]
    public async Task Deleting_something_that_is_not_there_writes_nothing()
    {
        Assert.False(await Store.DeleteGameAsync("nope", Ct));
        Assert.False(await Store.DeleteProfileAsync("nope", Ct));

        Assert.Empty(FileNames());
    }

    [Fact]
    public async Task Save_stores_and_returns_the_normalized_game()
    {
        var saved = await Store.SaveGameAsync(ExeGame() with { Name = "  Skyrim  ", IgnorePaths = ["a", " a", ""] }, Ct);

        Assert.Equal("Skyrim", saved.Name);
        Assert.Equal(["a"], saved.IgnorePaths);
        Assert.Equal(saved, await Store.GetGameAsync(saved.Id, Ct));
    }

    [Fact]
    public async Task Profiles_list_in_creation_order_and_can_be_filtered_by_game()
    {
        await Store.SaveProfileAsync(Profile("p1", "g1"), Ct);
        await Store.SaveProfileAsync(Profile("p2", "g2"), Ct);
        await Store.SaveProfileAsync(Profile("p3", "g1"), Ct);

        Assert.Equal(["p1", "p2", "p3"], (await Store.ListProfilesAsync(cancellationToken: Ct)).Select(p => p.Id));
        Assert.Equal(["p1", "p3"], (await Store.ListProfilesAsync("g1", Ct)).Select(p => p.Id));
        Assert.Empty(await Store.ListProfilesAsync("g9", Ct));
    }

    [Fact]
    public async Task Saving_a_profile_with_an_existing_id_replaces_it_in_place()
    {
        await Store.SaveProfileAsync(Profile("p1", "g1"), Ct);
        await Store.SaveProfileAsync(Profile("p2", "g1"), Ct);

        await Store.SaveProfileAsync(Profile("p1", "g1") with { Name = "Renamed", Notes = "n" }, Ct);

        var profiles = await Store.ListProfilesAsync(cancellationToken: Ct);
        Assert.Equal(["p1", "p2"], profiles.Select(p => p.Id));
        Assert.Equal("Renamed", profiles[0].Name);
        Assert.Equal("Renamed", (await Store.GetProfileAsync("p1", Ct))!.Name);
    }

    [Fact]
    public async Task Profiles_are_stored_independently_of_games()
    {
        await Store.SaveProfileAsync(Profile("p1", "no-such-game"), Ct);

        Assert.Empty(await Store.ListGamesAsync(Ct));
        Assert.Single(await Store.ListProfilesAsync(cancellationToken: Ct));
        Assert.True(await Store.DeleteProfileAsync("p1", Ct));
        Assert.Empty(await Store.ListProfilesAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task Settings_save_replaces_the_previous_object()
    {
        await Store.SaveSettingsAsync(Settings("/a"), Ct);
        await Store.SaveSettingsAsync(Settings("/b") with { Layout = ShellLayout.Library }, Ct);

        var settings = await Store.GetSettingsAsync(Ct);

        Assert.Equal("/b", settings!.StorageRoot);
        Assert.Equal(ShellLayout.Library, settings.Layout);
    }

    [Fact]
    public async Task Invalid_game_throws_a_validation_error_and_writes_nothing()
    {
        var error = await Assert.ThrowsAsync<ValidationException>(
            () => Store.SaveGameAsync(ExeGame() with { Name = "" }, Ct));

        Assert.Equal([Issue("name", ValidationMessageIds.GameNameRequired)], (IReadOnlyList<ValidationIssue>)error.Args["issues"]!);
        Assert.Empty(FileNames());
    }

    [Fact]
    public async Task Invalid_game_leaves_the_existing_file_untouched()
    {
        await Store.SaveGameAsync(ExeGame(), Ct);
        var before = await File.ReadAllBytesAsync(PathOf("games.json"), Ct);

        await Assert.ThrowsAsync<ValidationException>(() => Store.SaveGameAsync(ExeGame("other-1") with { Name = "" }, Ct));

        Assert.Equal(before, await File.ReadAllBytesAsync(PathOf("games.json"), Ct));
        Assert.Equal(["games.json"], FileNames());
    }

    [Fact]
    public async Task Invalid_profile_and_settings_throw_and_write_nothing()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Store.SaveProfileAsync(Profile() with { BranchName = "bad branch" }, Ct));
        await Assert.ThrowsAsync<ValidationException>(() => Store.SaveSettingsAsync(Settings(""), Ct));
        await Assert.ThrowsAsync<ValidationException>(
            () => Store.UpdateSettingsAsync(_ => Settings() with { Theme = (ThemePreference)9 }, Ct));
        await Assert.ThrowsAsync<ValidationException>(() => Store.SaveProfileAsync(Profile() with { Id = "" }, Ct));

        Assert.Empty(FileNames());
    }

    [Fact]
    public async Task Reads_catalog_files_written_by_the_original_app()
    {
        await File.WriteAllTextAsync(PathOf("games.json"), _v1Games.Replace("\n", "\r\n"), Ct);
        await File.WriteAllTextAsync(PathOf("profiles.json"), _v1Profiles.Replace("\n", "\r\n"), Ct);
        await File.WriteAllTextAsync(PathOf("settings.json"), _v1Settings.Replace("\n", "\r\n"), Ct);

        var games = await Store.ListGamesAsync(Ct);
        var profiles = await Store.ListProfilesAsync(cancellationToken: Ct);
        var settings = await Store.GetSettingsAsync(Ct);

        Assert.Equal(["skyrim-se", "indie"], games.Select(g => g.Id));
        Assert.Equal(LaunchMethod.Uri, games[1].Launch.Method);
        Assert.Empty(games[1].IgnorePaths);
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 19, 11, 3, 123, TimeSpan.Zero), profiles[0].LastLaunchedAt);
        Assert.Equal("2.0", profiles[1].VersionOverride);
        Assert.Equal(AccentColorId.Green, settings!.AccentColorId);
        Assert.Null(settings.LastOptimizedAt);
    }

    [Fact]
    public async Task Reads_files_that_start_with_a_byte_order_mark()
    {
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(_v1Games)).ToArray();
        await File.WriteAllBytesAsync(PathOf("games.json"), bytes, Ct);

        Assert.Equal(2, (await Store.ListGamesAsync(Ct)).Count);
    }

    [Fact]
    public async Task Saving_over_a_v1_file_keeps_the_other_games()
    {
        await File.WriteAllTextAsync(PathOf("games.json"), _v1Games, Ct);

        await Store.SaveGameAsync(ExeGame("new-1"), Ct);

        Assert.Equal(["skyrim-se", "indie", "new-1"], (await Store.ListGamesAsync(Ct)).Select(g => g.Id));
    }

    [Fact]
    public async Task Files_are_utf8_without_a_byte_order_mark_indented_with_lf_and_a_trailing_newline()
    {
        await Store.SaveGameAsync(ExeGame() with { Name = "Caf\u00e9", Notes = "Don't mix & match <mods>" }, Ct);
        await Store.SaveProfileAsync(Profile(), Ct);
        await Store.SaveSettingsAsync(Settings() with { Theme = ThemePreference.Dark }, Ct);

        foreach (var name in new[] { "games.json", "profiles.json", "settings.json" })
        {
            var bytes = await File.ReadAllBytesAsync(PathOf(name), Ct);

            Assert.False(bytes.AsSpan().StartsWith(new UTF8Encoding(true).GetPreamble()), $"{name} has a BOM");
            Assert.DoesNotContain((byte)'\r', bytes);
            Assert.Equal((byte)'\n', bytes[^1]);
            Assert.NotEqual((byte)'\n', bytes[^2]);
            Assert.Contains("\n  ", Encoding.UTF8.GetString(bytes));
        }

        var settingsText = await File.ReadAllTextAsync(PathOf("settings.json"), Ct);
        Assert.Contains("\"theme\": \"dark\"", settingsText);
        var gamesText = await File.ReadAllTextAsync(PathOf("games.json"), Ct);
        Assert.Contains("Caf\u00e9", gamesText);
        Assert.Contains("Don't mix & match <mods>", gamesText);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_call_before_anything_is_written()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.SaveGameAsync(ExeGame(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.ListGamesAsync(cts.Token));

        Assert.Empty(FileNames());
    }

    [Fact]
    public async Task Update_profile_changes_the_stored_profile_and_returns_it()
    {
        await Store.SaveProfileAsync(Profile("p1", "g1"), Ct);
        var launched = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        var updated = await Store.UpdateProfileAsync("p1", p => p with { LastLaunchedAt = launched }, Ct);

        Assert.Equal(launched, updated!.LastLaunchedAt);
        Assert.Equal(launched, (await Store.GetProfileAsync("p1", Ct))!.LastLaunchedAt);
    }

    [Fact]
    public async Task Update_profile_for_a_missing_profile_returns_null_and_writes_nothing()
    {
        Assert.Null(await Store.UpdateProfileAsync("nope", p => p with { Name = "x" }, Ct));

        Assert.Empty(FileNames());
    }

    [Fact]
    public async Task Update_profile_validates_the_result_before_writing()
    {
        await Store.SaveProfileAsync(Profile("p1", "g1"), Ct);

        await Assert.ThrowsAsync<ValidationException>(() => Store.UpdateProfileAsync("p1", p => p with { Name = " " }, Ct));

        Assert.Equal("Modded", (await Store.GetProfileAsync("p1", Ct))!.Name);
    }

    [Fact]
    public async Task Update_profile_cannot_change_the_id()
    {
        await Store.SaveProfileAsync(Profile("p1", "g1"), Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Store.UpdateProfileAsync("p1", p => p with { Id = "p2" }, Ct));

        Assert.Equal(["p1"], (await Store.ListProfilesAsync(cancellationToken: Ct)).Select(p => p.Id));
    }

    [Fact]
    public async Task Update_settings_starts_from_null_when_nothing_is_stored()
    {
        var updated = await Store.UpdateSettingsAsync(current => (current ?? Settings("/start")) with { Theme = ThemePreference.Light }, Ct);

        Assert.Equal("/start", updated.StorageRoot);
        Assert.Equal(updated, await Store.GetSettingsAsync(Ct));
    }
}
