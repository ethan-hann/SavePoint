using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using static Savepoint.Core.Tests.Catalog.CatalogSamples;

namespace Savepoint.Core.Tests.Catalog;

public class CatalogStoreCorruptionTests : CatalogStoreTestBase
{
    private const string _garbage = "{ this is not json";

    public static TheoryData<string, string> CorruptContents => new()
    {
        { "games.json", _garbage },
        { "games.json", "" },
        { "games.json", "  \r\n " },
        { "games.json", "null" },
        { "games.json", "[null]" },
        { "games.json", "{}" },
        { "games.json", "[1, 2]" },
        { "games.json", "[{ \"id\": \"a\", \"name\": \"A" },
        { "games.json", """[{ "id": 5 }]""" },
        { "games.json", """[{ "id": "a", "launch": { "method": "sepia" } }]""" },
        { "games.json", "[] trailing" },
        { "profiles.json", _garbage },
        { "profiles.json", "" },
        { "profiles.json", "null" },
        { "profiles.json", "[null]" },
        { "profiles.json", """{ "id": "a" }""" },
        { "profiles.json", """[{ "id": "a", "lastLaunchedAt": "not a date" }]""" },
        { "settings.json", _garbage },
        { "settings.json", "" },
        { "settings.json", "null" },
        { "settings.json", "[]" },
        { "settings.json", """{ "theme": "sepia" }""" },
        { "settings.json", """{ "storageRoot": 5 }""" },
    };

    public static TheoryData<string> CatalogFiles => new() { "games.json", "profiles.json", "settings.json" };

    [Theory]
    [MemberData(nameof(CorruptContents))]
    public async Task Unreadable_or_invalid_content_is_reported_as_corrupt_with_the_file_path(string fileName, string content)
    {
        await File.WriteAllTextAsync(PathOf(fileName), content, Ct);

        var error = await Assert.ThrowsAsync<CatalogCorruptException>(() => ReadAsync(fileName));

        Assert.Equal("error.catalog.corrupt", error.MessageId);
        Assert.Equal(PathOf(fileName), error.Args["path"]);
    }

    [Theory]
    [MemberData(nameof(CatalogFiles))]
    public async Task Binary_content_that_is_not_utf8_json_is_corrupt(string fileName)
    {
        await File.WriteAllBytesAsync(PathOf(fileName), [0xFF, 0xFE, 0x00, 0x7B, 0x00], Ct);

        await Assert.ThrowsAsync<CatalogCorruptException>(() => ReadAsync(fileName));
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_is_reported_as_corrupt_not_missing()
    {
        var path = PathOf("games.json");
        await File.WriteAllTextAsync(path, "[]", Ct);

        FileStream? lockHandle = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                lockHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            else
            {
                File.SetUnixFileMode(path, UnixFileMode.None);
                Assert.SkipWhen(CanStillRead(path), "this user ignores file permissions");
            }

            await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.ListGamesAsync(Ct));
        }
        finally
        {
            lockHandle?.Dispose();
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
    }

    [Fact]
    public async Task A_file_held_for_a_moment_by_a_scanner_is_read_after_it_is_released()
    {
        var path = PathOf("games.json");
        await Store.SaveGameAsync(ExeGame(), Ct);

        FileStream? lockHandle = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                lockHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            else
            {
                File.SetUnixFileMode(path, UnixFileMode.None);
                Assert.SkipWhen(CanStillRead(path), "this user ignores file permissions");
            }

            var read = Store.ListGamesAsync(Ct);

            // The store retries on its own clock, so the file is held across a retry or two before it is let go, as a scanner holds it.
            await Task.Delay(100, Ct);
            lockHandle?.Dispose();
            lockHandle = null;
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            Assert.Single(await read);
        }
        finally
        {
            lockHandle?.Dispose();
        }
    }

    [Fact]
    public async Task Saving_a_game_never_overwrites_a_corrupt_games_file()
    {
        await File.WriteAllTextAsync(PathOf("games.json"), _garbage, Ct);

        await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.SaveGameAsync(ExeGame(), Ct));
        await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.SaveGameAsync(ExeGame("other-1"), Ct));

        Assert.Equal(_garbage, await File.ReadAllTextAsync(PathOf("games.json"), Ct));
        Assert.Equal(["games.json"], FileNames());
    }

    [Fact]
    public async Task Deleting_a_game_never_overwrites_a_corrupt_games_file()
    {
        await File.WriteAllTextAsync(PathOf("games.json"), _garbage, Ct);

        await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.DeleteGameAsync("a", Ct));

        Assert.Equal(_garbage, await File.ReadAllTextAsync(PathOf("games.json"), Ct));
    }

    [Fact]
    public async Task Profile_writes_never_overwrite_a_corrupt_profiles_file()
    {
        await File.WriteAllTextAsync(PathOf("profiles.json"), _garbage, Ct);

        await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.SaveProfileAsync(Profile(), Ct));
        await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.UpdateProfileAsync("a", p => p, Ct));
        await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.DeleteProfileAsync("a", Ct));

        Assert.Equal(_garbage, await File.ReadAllTextAsync(PathOf("profiles.json"), Ct));
        Assert.Equal(["profiles.json"], FileNames());
    }

    [Fact]
    public async Task Saving_settings_never_overwrites_a_corrupt_settings_file_even_though_it_does_not_need_to_read_first()
    {
        await File.WriteAllTextAsync(PathOf("settings.json"), _garbage, Ct);

        await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.SaveSettingsAsync(Settings(), Ct));
        await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.UpdateSettingsAsync(_ => Settings(), Ct));

        Assert.Equal(_garbage, await File.ReadAllTextAsync(PathOf("settings.json"), Ct));
        Assert.Equal(["settings.json"], FileNames());
    }

    [Fact]
    public async Task A_valid_settings_object_that_is_literally_null_is_not_treated_as_no_settings()
    {
        await File.WriteAllTextAsync(PathOf("settings.json"), "null", Ct);

        await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.GetSettingsAsync(Ct));
        await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.SaveSettingsAsync(Settings(), Ct));
    }

    [Fact]
    public async Task A_corrupt_file_does_not_block_the_other_catalog_files()
    {
        await File.WriteAllTextAsync(PathOf("games.json"), _garbage, Ct);

        await Store.SaveProfileAsync(Profile(), Ct);
        await Store.SaveSettingsAsync(Settings(), Ct);

        Assert.Single(await Store.ListProfilesAsync(cancellationToken: Ct));
        Assert.NotNull(await Store.GetSettingsAsync(Ct));
        await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.ListGamesAsync(Ct));
    }

    [Fact]
    public async Task Writes_work_again_as_soon_as_the_file_is_fixed_by_hand()
    {
        await File.WriteAllTextAsync(PathOf("games.json"), _garbage, Ct);
        await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.SaveGameAsync(ExeGame(), Ct));

        await File.WriteAllTextAsync(PathOf("games.json"), "[]", Ct);
        await Store.SaveGameAsync(ExeGame(), Ct);

        Assert.Single(await Store.ListGamesAsync(Ct));
    }

    [Fact]
    public async Task Replacing_a_corrupt_file_moves_it_aside_with_a_timestamp_and_starts_empty()
    {
        var now = new DateTimeOffset(2026, 10, 20, 13, 5, 9, 42, TimeSpan.FromHours(-5));
        var store = new CatalogStore(Dir, new FixedTimeProvider(now));
        await File.WriteAllTextAsync(PathOf("games.json"), _garbage, Ct);

        var movedTo = await store.ReplaceCorruptFileAsync(PathOf("games.json"), Ct);

        Assert.Equal(PathOf("games.json.corrupt-20261020T180509042Z"), movedTo);
        Assert.Equal(_garbage, await File.ReadAllTextAsync(movedTo!, Ct));
        Assert.False(File.Exists(PathOf("games.json")));
        Assert.Empty(await store.ListGamesAsync(Ct));

        await store.SaveGameAsync(ExeGame(), Ct);
        Assert.Single(await store.ListGamesAsync(Ct));
        Assert.Equal(_garbage, await File.ReadAllTextAsync(movedTo!, Ct));
    }

    [Theory]
    [MemberData(nameof(CatalogFiles))]
    public async Task Every_catalog_file_can_be_replaced(string fileName)
    {
        await File.WriteAllTextAsync(PathOf(fileName), _garbage, Ct);

        var movedTo = await Store.ReplaceCorruptFileAsync(PathOf(fileName), Ct);

        Assert.StartsWith(PathOf(fileName + ".corrupt-"), movedTo);
        Assert.False(File.Exists(PathOf(fileName)));
    }

    [Fact]
    public async Task Replacing_accepts_the_path_carried_by_the_corrupt_error()
    {
        await File.WriteAllTextAsync(PathOf("profiles.json"), _garbage, Ct);
        var error = await Assert.ThrowsAsync<CatalogCorruptException>(() => Store.ListProfilesAsync(cancellationToken: Ct));

        var movedTo = await Store.ReplaceCorruptFileAsync((string)error.Args["path"]!, Ct);

        Assert.NotNull(movedTo);
        Assert.Empty(await Store.ListProfilesAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task Replacing_a_file_that_is_fine_or_missing_does_nothing()
    {
        await Store.SaveGameAsync(ExeGame(), Ct);
        var before = await File.ReadAllBytesAsync(PathOf("games.json"), Ct);

        Assert.Null(await Store.ReplaceCorruptFileAsync(PathOf("games.json"), Ct));
        Assert.Null(await Store.ReplaceCorruptFileAsync(PathOf("profiles.json"), Ct));

        Assert.Equal(before, await File.ReadAllBytesAsync(PathOf("games.json"), Ct));
        Assert.Equal(["games.json"], FileNames());
    }

    [Fact]
    public async Task Replacing_twice_in_the_same_millisecond_never_overwrites_an_earlier_copy()
    {
        var store = new CatalogStore(Dir, new FixedTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)));
        await File.WriteAllTextAsync(PathOf("games.json"), "first garbage", Ct);
        var first = await store.ReplaceCorruptFileAsync(PathOf("games.json"), Ct);
        await File.WriteAllTextAsync(PathOf("games.json"), "second garbage", Ct);

        var second = await store.ReplaceCorruptFileAsync(PathOf("games.json"), Ct);

        Assert.NotEqual(first, second);
        Assert.Equal("first garbage", await File.ReadAllTextAsync(first!, Ct));
        Assert.Equal("second garbage", await File.ReadAllTextAsync(second!, Ct));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("games.json.bak")]
    [InlineData("games.json.corrupt-20260101T000000000Z")]
    [InlineData("..")]
    [InlineData("")]
    public async Task Only_the_three_catalog_files_can_be_replaced(string fileName)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Store.ReplaceCorruptFileAsync(Path.Combine(Dir, fileName), Ct));
    }

    [Fact]
    public async Task A_catalog_file_name_in_some_other_folder_cannot_be_replaced()
    {
        using var other = new TempFolder();
        await File.WriteAllTextAsync(Path.Combine(other.Path, "games.json"), _garbage, Ct);

        await Assert.ThrowsAsync<ArgumentException>(() => Store.ReplaceCorruptFileAsync(Path.Combine(other.Path, "games.json"), Ct));

        Assert.Equal(_garbage, await File.ReadAllTextAsync(Path.Combine(other.Path, "games.json"), Ct));
    }

    private static bool CanStillRead(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private Task ReadAsync(string fileName) => fileName switch
    {
        "games.json" => Store.ListGamesAsync(Ct),
        "profiles.json" => Store.ListProfilesAsync(cancellationToken: Ct),
        _ => Store.GetSettingsAsync(Ct),
    };

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = Directory.CreateTempSubdirectory("savepoint-other-").FullName;
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
