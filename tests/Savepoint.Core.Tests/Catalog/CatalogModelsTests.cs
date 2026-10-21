using System.Text.Json;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Catalog;

public class CatalogModelsTests
{
    private const string _v1Game = """
        {
          "schemaVersion": 1,
          "id": "skyrim-se",
          "name": "The Elder Scrolls V: Skyrim Special Edition",
          "install": { "path": "C:\\Games\\SkyrimSE" },
          "external": { "path": "{documents}\\My Games\\Skyrim Special Edition\\Saves", "role": "saves" },
          "launch": {
            "method": "exe",
            "exePath": "SkyrimSELauncher.exe",
            "args": ["-skipintro"],
            "processNameForExitDetection": "SkyrimSE.exe"
          },
          "ignorePaths": ["**/*.log", "SKSE/Plugins/*.log"],
          "modPathHints": ["Data", "plugins.txt"],
          "notes": "Direct-exe launch preferred."
        }
        """;

    private const string _v1Profile = """
        {
          "id": "modded-playthrough-7k2a",
          "gameId": "skyrim-se",
          "name": "Modded playthrough",
          "branchName": "profile/modded-playthrough-7k2a",
          "notes": "Survival mods",
          "lastLaunchedAt": "2026-07-02T19:11:03.123Z",
          "versionOverride": "1.6.1170"
        }
        """;

    private const string _v1Settings = """
        {
          "storageRoot": "C:\\Users\\Ethan\\Documents\\SavePoint",
          "theme": "dark",
          "accentColorId": "violet",
          "locale": "en",
          "layout": "library",
          "confirmSessionCapture": false,
          "skipConfirmWhenOnlySaveChanges": true
        }
        """;

    public static TheoryData<object, string> EnumWireValues => new()
    {
        { RepoRole.Install, "install" },
        { RepoRole.External, "external" },
        { LaunchMethod.Exe, "exe" },
        { LaunchMethod.Uri, "uri" },
        { ExternalRole.Saves, "saves" },
        { ThemePreference.System, "system" },
        { ThemePreference.Light, "light" },
        { ThemePreference.Dark, "dark" },
        { ShellLayout.Sidebar, "sidebar" },
        { ShellLayout.Library, "library" },
        { AccentColorId.Blue, "blue" },
        { AccentColorId.Violet, "violet" },
        { AccentColorId.Green, "green" },
        { AccentColorId.Amber, "amber" },
        { AccentColorId.Crimson, "crimson" },
    };

    [Fact]
    public void V1_game_json_deserializes()
    {
        var game = JsonSerializer.Deserialize(_v1Game, CatalogJsonContext.Default.GameDefinition)!;

        Assert.Equal(1, game.SchemaVersion);
        Assert.Equal("skyrim-se", game.Id);
        Assert.Equal("The Elder Scrolls V: Skyrim Special Edition", game.Name);
        Assert.Equal(@"C:\Games\SkyrimSE", game.Install.Path);
        Assert.Equal(@"{documents}\My Games\Skyrim Special Edition\Saves", game.External!.Path);
        Assert.Equal(ExternalRole.Saves, game.External.Role);
        Assert.Equal(LaunchMethod.Exe, game.Launch.Method);
        Assert.Equal("SkyrimSELauncher.exe", game.Launch.ExePath);
        Assert.Null(game.Launch.Uri);
        Assert.Equal(["-skipintro"], game.Launch.Args);
        Assert.Equal("SkyrimSE.exe", game.Launch.ProcessNameForExitDetection);
        Assert.Equal(["**/*.log", "SKSE/Plugins/*.log"], game.IgnorePaths);
        Assert.Equal(["Data", "plugins.txt"], game.ModPathHints);
        Assert.Equal("Direct-exe launch preferred.", game.Notes);
    }

    [Fact]
    public void V1_game_json_without_optional_arrays_reads_them_as_empty()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "id": "g",
              "name": "G",
              "install": { "path": "C:\\G" },
              "launch": { "method": "uri", "uri": "steam://run/1" }
            }
            """;

        var game = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.GameDefinition)!;

        Assert.Empty(game.IgnorePaths);
        Assert.Empty(game.ModPathHints);
        Assert.Empty(game.Launch.Args);
        Assert.Null(game.External);
        Assert.Null(game.Notes);
    }

    [Fact]
    public void Explicit_json_null_arrays_read_as_empty()
    {
        const string json = """
            {
              "id": "g",
              "name": "G",
              "install": { "path": "C:\\G" },
              "launch": { "method": "exe", "exePath": "g.exe", "args": null },
              "ignorePaths": null,
              "modPathHints": null
            }
            """;

        var game = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.GameDefinition)!;

        Assert.Empty(game.IgnorePaths);
        Assert.Empty(game.ModPathHints);
        Assert.Empty(game.Launch.Args);
    }

    [Fact]
    public void Missing_settings_fields_keep_their_defaults()
    {
        var settings = JsonSerializer.Deserialize("""{ "storageRoot": "S" }""", CatalogJsonContext.Default.AppSettings)!;

        Assert.Equal("en", settings.Locale);
        Assert.True(settings.ConfirmSessionCapture);
        Assert.False(settings.SkipConfirmWhenOnlySaveChanges);
        Assert.Equal(ThemePreference.System, settings.Theme);
    }

    [Fact]
    public void Missing_game_and_profile_fields_read_as_empty_values_not_null()
    {
        var game = JsonSerializer.Deserialize("""{ "id": "g" }""", CatalogJsonContext.Default.GameDefinition)!;
        var profile = JsonSerializer.Deserialize("""{ "id": "p" }""", CatalogJsonContext.Default.Profile)!;

        Assert.Equal(GameDefinition.CurrentSchemaVersion, game.SchemaVersion);
        Assert.Equal("", game.Name);
        Assert.Equal("", game.Install.Path);
        Assert.Equal(LaunchMethod.Exe, game.Launch.Method);
        Assert.Equal("", profile.Name);
        Assert.Equal("", profile.GameId);
        Assert.Equal("", profile.BranchName);
    }

    [Fact]
    public void Unknown_keys_are_ignored()
    {
        const string json = """
            { "id": "g", "name": "G", "futureField": { "x": 1 }, "install": { "path": "C:\\G", "extra": true } }
            """;

        var game = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.GameDefinition)!;

        Assert.Equal("g", game.Id);
        Assert.Equal(@"C:\G", game.Install.Path);
    }

    [Fact]
    public void V1_profile_json_deserializes()
    {
        var profile = JsonSerializer.Deserialize(_v1Profile, CatalogJsonContext.Default.Profile)!;

        Assert.Equal("modded-playthrough-7k2a", profile.Id);
        Assert.Equal("skyrim-se", profile.GameId);
        Assert.Equal("Modded playthrough", profile.Name);
        Assert.Equal("profile/modded-playthrough-7k2a", profile.BranchName);
        Assert.Equal("Survival mods", profile.Notes);
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 19, 11, 3, 123, TimeSpan.Zero), profile.LastLaunchedAt);
        Assert.Equal("1.6.1170", profile.VersionOverride);
    }

    [Fact]
    public void V1_profile_list_without_optional_fields_deserializes()
    {
        const string json = """
            [
              { "id": "a", "gameId": "g", "name": "A", "branchName": "profile/a" },
              { "id": "b", "gameId": "g", "name": "B", "branchName": "profile/b" }
            ]
            """;

        var profiles = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.ListProfile)!;

        Assert.Equal(["a", "b"], profiles.Select(p => p.Id));
        Assert.All(profiles, p =>
        {
            Assert.Null(p.Notes);
            Assert.Null(p.LastLaunchedAt);
            Assert.Null(p.VersionOverride);
        });
    }

    [Fact]
    public void V1_settings_json_deserializes_with_no_last_optimized_time()
    {
        var settings = JsonSerializer.Deserialize(_v1Settings, CatalogJsonContext.Default.AppSettings)!;

        Assert.Equal(@"C:\Users\Ethan\Documents\SavePoint", settings.StorageRoot);
        Assert.Equal(ThemePreference.Dark, settings.Theme);
        Assert.Equal(AccentColorId.Violet, settings.AccentColorId);
        Assert.Equal("en", settings.Locale);
        Assert.Equal(ShellLayout.Library, settings.Layout);
        Assert.False(settings.ConfirmSessionCapture);
        Assert.True(settings.SkipConfirmWhenOnlySaveChanges);
        Assert.Null(settings.LastOptimizedAt);
    }

    [Theory]
    [MemberData(nameof(EnumWireValues))]
    public void Enums_serialize_as_their_exact_lowercase_v1_strings(object value, string expected)
    {
        Assert.Equal($"\"{expected}\"", JsonSerializer.Serialize(value, value.GetType()));
    }

    [Fact]
    public void Settings_enums_are_written_as_lowercase_strings()
    {
        var settings = new AppSettings
        {
            StorageRoot = "S",
            Theme = ThemePreference.Dark,
            AccentColorId = AccentColorId.Crimson,
            Layout = ShellLayout.Library,
        };

        var json = JsonSerializer.Serialize(settings, CatalogJsonContext.Default.AppSettings);

        Assert.Contains("\"theme\": \"dark\"", json);
        Assert.Contains("\"accentColorId\": \"crimson\"", json);
        Assert.Contains("\"layout\": \"library\"", json);
    }

    [Fact]
    public void Unknown_enum_string_fails_to_read()
    {
        const string json = """{ "theme": "sepia", "storageRoot": "S" }""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, CatalogJsonContext.Default.AppSettings));
    }

    [Fact]
    public void Game_round_trips()
    {
        var original = JsonSerializer.Deserialize(_v1Game, CatalogJsonContext.Default.GameDefinition)!;

        var json = JsonSerializer.Serialize(original, CatalogJsonContext.Default.GameDefinition);
        var parsed = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.GameDefinition)!;

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Profile_round_trips()
    {
        var original = JsonSerializer.Deserialize(_v1Profile, CatalogJsonContext.Default.Profile)!;

        var json = JsonSerializer.Serialize(original, CatalogJsonContext.Default.Profile);
        var parsed = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.Profile)!;

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Settings_round_trip_including_last_optimized_time()
    {
        var fromV1 = JsonSerializer.Deserialize(_v1Settings, CatalogJsonContext.Default.AppSettings)!;
        var original = fromV1 with { LastOptimizedAt = new DateTimeOffset(2026, 10, 20, 8, 30, 0, TimeSpan.FromHours(-5)) };

        var json = JsonSerializer.Serialize(original, CatalogJsonContext.Default.AppSettings);
        var parsed = JsonSerializer.Deserialize(json, CatalogJsonContext.Default.AppSettings)!;

        Assert.Equal(original, parsed);
        Assert.Equal(original.LastOptimizedAt, parsed.LastOptimizedAt);
    }

    [Fact]
    public void Optional_fields_that_are_null_are_not_written()
    {
        var game = new GameDefinition
        {
            Id = "g",
            Name = "G",
            Install = new InstallLocation { Path = "C:/G" },
            Launch = new LaunchConfig { Method = LaunchMethod.Uri, Uri = "steam://run/1" },
        };

        var json = JsonSerializer.Serialize(game, CatalogJsonContext.Default.GameDefinition);

        Assert.DoesNotContain("\"external\"", json);
        Assert.DoesNotContain("\"notes\"", json);
        Assert.DoesNotContain("\"exePath\"", json);
        Assert.DoesNotContain("\"processNameForExitDetection\"", json);
        Assert.Contains("\"ignorePaths\": []", json);
    }

    [Fact]
    public void Output_uses_camel_case_names_and_two_space_indent()
    {
        var json = JsonSerializer.Serialize(
            new Profile { Id = "a", GameId = "g", Name = "A", BranchName = "profile/a" },
            CatalogJsonContext.Default.Profile);

        Assert.Contains("\n  \"gameId\": \"g\"", json.Replace("\r\n", "\n"));
        Assert.Contains("\"branchName\"", json);
    }

    [Fact]
    public void Games_with_equal_contents_are_equal_even_when_their_lists_are_different_instances()
    {
        var a = JsonSerializer.Deserialize(_v1Game, CatalogJsonContext.Default.GameDefinition)!;
        var b = JsonSerializer.Deserialize(_v1Game, CatalogJsonContext.Default.GameDefinition)!;

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Games_differing_in_a_list_entry_are_not_equal()
    {
        var a = JsonSerializer.Deserialize(_v1Game, CatalogJsonContext.Default.GameDefinition)!;

        Assert.NotEqual(a, a with { IgnorePaths = ["other"] });
        Assert.NotEqual(a, a with { ModPathHints = [] });
        Assert.NotEqual(a, a with { Launch = a.Launch with { Args = ["-other"] } });
    }

    [Fact]
    public void Defaults_start_from_the_given_storage_root()
    {
        var settings = AppSettings.Defaults("/data/savepoint");

        Assert.Equal("/data/savepoint", settings.StorageRoot);
        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.Equal(AccentColorId.Blue, settings.AccentColorId);
        Assert.Equal("en", settings.Locale);
        Assert.Equal(ShellLayout.Sidebar, settings.Layout);
        Assert.True(settings.ConfirmSessionCapture);
        Assert.False(settings.SkipConfirmWhenOnlySaveChanges);
        Assert.Null(settings.LastOptimizedAt);
    }
}
