using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using static Savepoint.Core.Tests.Catalog.CatalogSamples;

namespace Savepoint.Core.Tests.Catalog;

public class CatalogStoreWriteTests : CatalogStoreTestBase
{
    [Fact]
    public async Task First_save_has_nothing_to_back_up()
    {
        await Store.SaveGameAsync(ExeGame(), Ct);

        Assert.Equal(["games.json"], FileNames());
    }

    [Fact]
    public async Task Each_save_keeps_the_previous_good_file_as_a_bak_copy()
    {
        await Store.SaveGameAsync(ExeGame("a-1"), Ct);
        var afterFirst = await File.ReadAllBytesAsync(PathOf("games.json"), Ct);

        await Store.SaveGameAsync(ExeGame("b-2"), Ct);
        var afterSecond = await File.ReadAllBytesAsync(PathOf("games.json"), Ct);

        Assert.Equal(afterFirst, await File.ReadAllBytesAsync(PathOf("games.json.bak"), Ct));
        Assert.NotEqual(afterFirst, afterSecond);

        await Store.SaveGameAsync(ExeGame("c-3"), Ct);

        Assert.Equal(afterSecond, await File.ReadAllBytesAsync(PathOf("games.json.bak"), Ct));
    }

    [Fact]
    public async Task Profiles_and_settings_get_their_own_bak_copies()
    {
        await Store.SaveProfileAsync(Profile("p1"), Ct);
        await Store.SaveProfileAsync(Profile("p2"), Ct);
        await Store.SaveSettingsAsync(Settings("/a"), Ct);
        await Store.SaveSettingsAsync(Settings("/b"), Ct);

        Assert.Equal(["profiles.json", "profiles.json.bak", "settings.json", "settings.json.bak"], FileNames());
        Assert.Contains("/a", await File.ReadAllTextAsync(PathOf("settings.json.bak"), Ct));
        Assert.Contains("p1", await File.ReadAllTextAsync(PathOf("profiles.json.bak"), Ct));
        Assert.DoesNotContain("p2", await File.ReadAllTextAsync(PathOf("profiles.json.bak"), Ct));
    }

    [Fact]
    public async Task Writes_leave_no_temporary_file_behind()
    {
        for (var i = 0; i < 10; i++)
        {
            await Store.SaveGameAsync(ExeGame($"game-{i}"), Ct);
            await Store.SaveProfileAsync(Profile($"p{i}"), Ct);
        }

        await Store.DeleteGameAsync("game-3", Ct);

        Assert.Equal(["games.json", "games.json.bak", "profiles.json", "profiles.json.bak"], FileNames());
    }

    [Fact]
    public async Task Stale_temporary_files_from_a_crash_are_removed_by_the_next_successful_write()
    {
        await File.WriteAllTextAsync(PathOf("games.json.1f2e3d4c.tmp"), "half written", Ct);
        await File.WriteAllTextAsync(PathOf("games.json.5a6b7c8d.tmp"), "", Ct);
        await File.WriteAllTextAsync(PathOf("profiles.json.9e8d7c6b.tmp"), "other file's", Ct);
        await File.WriteAllTextAsync(PathOf("games.json.corrupt-20260101T000000000Z"), "kept", Ct);
        await File.WriteAllTextAsync(PathOf("notes.txt"), "kept", Ct);

        await Store.SaveGameAsync(ExeGame(), Ct);

        Assert.Equal(
            ["games.json", "games.json.corrupt-20260101T000000000Z", "notes.txt", "profiles.json.9e8d7c6b.tmp"],
            FileNames());
    }

    [Fact]
    public async Task A_failed_write_keeps_the_original_file_and_leaves_no_temporary_file()
    {
        await Store.SaveGameAsync(ExeGame("a-1"), Ct);
        var before = await File.ReadAllBytesAsync(PathOf("games.json"), Ct);
        Directory.CreateDirectory(PathOf("games.json.bak"));

        await Assert.ThrowsAnyAsync<Exception>(() => Store.SaveGameAsync(ExeGame("b-2"), Ct));

        Assert.Equal(before, await File.ReadAllBytesAsync(PathOf("games.json"), Ct));
        Assert.Equal(["games.json", "games.json.bak"], FileNames());
        Assert.Equal(["a-1"], (await Store.ListGamesAsync(Ct)).Select(g => g.Id));
    }

    [Fact]
    public async Task A_target_held_open_for_a_moment_by_a_scanner_does_not_fail_the_save()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to replace a file that is open without delete sharing");
        await Store.SaveGameAsync(ExeGame("a-1"), Ct);

        var holder = new FileStream(PathOf("games.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var save = Store.SaveGameAsync(ExeGame("b-2"), Ct);

            // The store retries on its own clock, so the file is held across a retry or two before it is let go, as a scanner holds it.
            await Task.Delay(100, Ct);
            holder.Dispose();

            await save;
        }
        finally
        {
            holder.Dispose();
        }

        Assert.Equal(["a-1", "b-2"], (await Store.ListGamesAsync(Ct)).Select(g => g.Id));
        Assert.Equal(["games.json", "games.json.bak"], FileNames());
    }

    [Fact]
    public async Task A_rejected_save_does_not_block_later_saves()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Store.SaveGameAsync(ExeGame() with { Name = "" }, Ct));

        await Store.SaveGameAsync(ExeGame(), Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Single(await Store.ListGamesAsync(Ct));
    }

    [Fact]
    public async Task Fifty_concurrent_profile_saves_lose_no_update()
    {
        var saves = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() => Store.SaveProfileAsync(Profile($"p{i:00}", "g1"), Ct), Ct));

        await Task.WhenAll(saves);

        var profiles = await Store.ListProfilesAsync(cancellationToken: Ct);
        Assert.Equal(50, profiles.Count);
        Assert.Equal(50, profiles.Select(p => p.Id).Distinct().Count());
        Assert.Equal(50, (await new CatalogStore(Dir).ListProfilesAsync(cancellationToken: Ct)).Count);
    }

    [Fact]
    public async Task Fifty_concurrent_game_saves_lose_no_update()
    {
        var saves = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() => Store.SaveGameAsync(ExeGame($"game-{i:00}"), Ct), Ct));

        await Task.WhenAll(saves);

        Assert.Equal(50, (await Store.ListGamesAsync(Ct)).Count);
    }

    [Fact]
    public async Task Concurrent_saves_deletes_and_updates_across_all_three_files_stay_consistent()
    {
        for (var i = 0; i < 20; i++)
        {
            await Store.SaveProfileAsync(Profile($"old{i:00}", "g1"), Ct);
        }

        var work = new List<Task>();
        for (var i = 0; i < 30; i++)
        {
            var n = i;
            work.Add(Task.Run(() => Store.SaveGameAsync(ExeGame($"game-{n:00}"), Ct), Ct));
            work.Add(Task.Run(() => Store.SaveProfileAsync(Profile($"new{n:00}", "g2"), Ct), Ct));
            work.Add(Task.Run(() => Store.UpdateSettingsAsync(c => (c ?? Settings()) with { Theme = ThemePreference.Dark }, Ct), Ct));
            if (n < 20)
            {
                work.Add(Task.Run(() => Store.DeleteProfileAsync($"old{n:00}", Ct), Ct));
            }
        }

        await Task.WhenAll(work);

        Assert.Equal(30, (await Store.ListGamesAsync(Ct)).Count);
        Assert.Equal(30, (await Store.ListProfilesAsync(cancellationToken: Ct)).Count);
        Assert.Empty(await Store.ListProfilesAsync("g1", Ct));
        Assert.Equal(ThemePreference.Dark, (await Store.GetSettingsAsync(Ct))!.Theme);
        Assert.DoesNotContain(FileNames(), n => n.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Concurrent_profile_updates_are_applied_one_at_a_time()
    {
        await Store.SaveProfileAsync(Profile("p1", "g1") with { Notes = "" }, Ct);

        var updates = Enumerable.Range(0, 40)
            .Select(_ => Task.Run(() => Store.UpdateProfileAsync("p1", p => p with { Notes = p.Notes + "x" }, Ct), Ct));

        await Task.WhenAll(updates);

        Assert.Equal(new string('x', 40), (await Store.GetProfileAsync("p1", Ct))!.Notes);
    }

    [Fact]
    public async Task Concurrent_settings_updates_are_applied_one_at_a_time()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var updates = Enumerable.Range(0, 25).Select(_ => Task.Run(
            () => Store.UpdateSettingsAsync(
                c => (c ?? Settings()) with { LastOptimizedAt = (c?.LastOptimizedAt ?? start).AddDays(1) },
                Ct),
            Ct));

        await Task.WhenAll(updates);

        Assert.Equal(start.AddDays(25), (await Store.GetSettingsAsync(Ct))!.LastOptimizedAt);
    }
}
