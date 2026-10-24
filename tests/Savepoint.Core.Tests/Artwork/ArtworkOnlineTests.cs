using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Services;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

// Steam's servers as the second source: the setting, the missing-handler default, remembered misses, and the network limits
// at the level of the whole backend.
public class ArtworkOnlineTests
{
    private const int _app = 489830;
    private static readonly TimeSpan _week = TimeSpan.FromDays(7);

    private static readonly byte[] _wide = TestImages.Jpeg(460, 215);
    private static readonly byte[] _square = TestImages.Jpeg(600, 900);

    // Answers with the right image for each file name, or 404 for apps in the missing list.
    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Serve(params int[] missing) =>
        (request, _) =>
        {
            // The store API knows no hashed names for these apps.
            if (FakeHttpHandler.IsStoreApi(request))
            {
                var asked = int.Parse(Regex.Match(Uri.UnescapeDataString(request.RequestUri!.Query), "\"appid\":(\\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
                return Task.FromResult(FakeHttpHandler.Json(FakeHttpHandler.StoreItems(asked, null)));
            }

            var path = request.RequestUri!.AbsolutePath;
            if (missing.Any(app => path.Contains($"/apps/{app}/", StringComparison.Ordinal)))
            {
                return Task.FromResult(FakeHttpHandler.NotFound());
            }

            return Task.FromResult(FakeHttpHandler.Jpeg(path.EndsWith("header.jpg", StringComparison.Ordinal) ? _wide : _square));
        };

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Status(HttpStatusCode code) =>
        (_, _) => Task.FromResult(new HttpResponseMessage(code));

    [Fact]
    public async Task With_nothing_on_this_computer_both_images_are_fetched_and_kept()
    {
        using var rig = new ArtworkRig(Serve(), online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, art.Source);
        Assert.Equal(_wide, File.ReadAllBytes(art.WidePath!));
        Assert.Equal(_square, File.ReadAllBytes(art.SquarePath!));
        Assert.Equal(
            [
                $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{_app}/header.jpg",
                $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{_app}/library_600x900.jpg",
            ],
            rig.Http!.Urls.Order());
        Assert.Equal([game.Id], rig.Changes);
    }

    [Fact]
    public async Task A_game_served_only_under_hashed_paths_gets_both_images_from_one_store_request()
    {
        const string capsule = "7d4b4f430dc4d07e6562eb445456f9615cb52f77";
        const string header = "1e0c76e4253d347bb1304c3f213570251d924edf";
        var store = FakeHttpHandler.StoreItems(
            _app,
            $"{{\"asset_url_format\":\"steam/apps/{_app}/${{FILENAME}}?t=1\",\"header\":\"{header}/header.jpg\",\"library_capsule\":\"{capsule}/library_capsule.jpg\"}}");
        using var rig = new ArtworkRig(
            (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (FakeHttpHandler.IsStoreApi(request))
                {
                    return Task.FromResult(FakeHttpHandler.Json(store));
                }

                return Task.FromResult(
                    path.Contains(header, StringComparison.Ordinal) ? FakeHttpHandler.Jpeg(_wide)
                    : path.Contains(capsule, StringComparison.Ordinal) ? FakeHttpHandler.Jpeg(_square)
                    : FakeHttpHandler.NotFound());
            },
            online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, art.Source);
        Assert.Equal(_wide, File.ReadAllBytes(art.WidePath!));
        Assert.Equal(_square, File.ReadAllBytes(art.SquarePath!));
        Assert.Single(rig.Http!.Requests, FakeHttpHandler.IsStoreApi);
    }

    [Fact]
    public async Task Whatever_the_cache_has_is_used_and_only_the_rest_is_fetched()
    {
        using var rig = new ArtworkRig(Serve(), online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        rig.PutSteamCache(_app, wide: true, square: false);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, art.Source);
        Assert.Equal([$"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{_app}/library_600x900.jpg"], rig.Http!.Urls);
    }

    [Fact]
    public async Task A_cache_that_has_both_images_means_no_request_at_all()
    {
        using var rig = new ArtworkRig(Serve(), online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        rig.PutSteamCache(_app);

        await rig.LookedUpAsync(game.Id);

        Assert.Empty(rig.Http!.Requests);
    }

    [Fact]
    public async Task With_the_setting_off_nothing_is_fetched_but_the_cache_is_still_used()
    {
        using var rig = new ArtworkRig(Serve(), online: true);
        await rig.Workspace.Settings.SaveAsync(AppSettings.Defaults(rig.Workspace.DefaultStorageRoot) with { LookUpArtworkOnline = false });
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        var other = await rig.AddGameAsync("Beta", g => g with { SteamAppId = 777 });
        rig.PutSteamCache(_app, wide: true, square: false);

        var art = await rig.LookedUpAsync(game.Id);
        var none = await rig.LookedUpAsync(other.Id);
        var again = await rig.Artwork.LookAgainAsync(other.Id);

        Assert.Equal(ArtworkSource.Steam, art.Source);
        Assert.NotNull(art.WidePath);
        Assert.Null(art.SquarePath);
        Assert.Equal(ArtworkSource.None, none.Source);
        Assert.Equal(ArtworkSource.None, again.Value!.Source);
        Assert.Empty(rig.Http!.Requests);
    }

    [Fact]
    public async Task Turning_the_setting_on_again_fetches_what_was_missing()
    {
        using var rig = new ArtworkRig(Serve(), online: true);
        await rig.Workspace.Settings.SaveAsync(AppSettings.Defaults(rig.Workspace.DefaultStorageRoot) with { LookUpArtworkOnline = false });
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        var off = await rig.LookedUpAsync(game.Id);

        await rig.Workspace.Settings.SaveAsync(AppSettings.Defaults(rig.Workspace.DefaultStorageRoot));
        var on = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.None, off.Source);
        Assert.Equal(ArtworkSource.Steam, on.Source);
    }

    [Fact]
    public async Task A_not_found_is_remembered_for_seven_days_across_restarts_and_then_asked_again()
    {
        var time = new ManualTimeProvider();
        using var rig = new ArtworkRig(Serve(_app), online: true, time);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        await rig.LookedUpAsync(game.Id);
        var asked = rig.Http!.Requests.Count;
        await rig.LookedUpAsync(game.Id);
        rig.Restart();
        time.Advance(_week - TimeSpan.FromHours(1));
        await rig.LookedUpAsync(game.Id);
        var withinTheWeek = rig.Http.Requests.Count;
        time.Advance(TimeSpan.FromHours(2));
        await rig.LookedUpAsync(game.Id);

        // Two plain addresses for each image, then one store request shared by both.
        Assert.Equal(5, asked);
        Assert.Equal(asked, withinTheWeek);
        Assert.Equal(2 * asked, rig.Http.Requests.Count);
    }

    [Fact]
    public async Task A_failure_that_is_not_a_not_found_is_not_remembered()
    {
        using var rig = new ArtworkRig(Status(HttpStatusCode.ServiceUnavailable), online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        // Both reads ask: a failure is remembered neither in the run nor across a restart.
        await rig.LookedUpAsync(game.Id);
        var asked = rig.Http!.Requests.Count;
        rig.Restart();
        await rig.LookedUpAsync(game.Id);

        Assert.Equal(8, asked);
        Assert.Equal(2 * asked, rig.Http.Requests.Count);
    }

    [Fact]
    public async Task One_missing_image_is_remembered_while_the_other_is_kept_and_only_the_missing_one_is_asked_again()
    {
        var time = new ManualTimeProvider();
        var answers = Serve();
        using var rig = new ArtworkRig(
            (request, token) => request.RequestUri!.AbsolutePath.EndsWith("library_600x900.jpg", StringComparison.Ordinal)
                ? Task.FromResult(FakeHttpHandler.NotFound())
                : answers(request, token),
            online: true,
            time);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        var art = await rig.LookedUpAsync(game.Id);
        var first = rig.Http!.Requests.Count;
        time.Advance(_week + TimeSpan.FromMinutes(1));
        await rig.LookedUpAsync(game.Id);

        Assert.NotNull(art.WidePath);
        Assert.Null(art.SquarePath);
        // The wide image once, then the two plain tall addresses and the store request.
        Assert.Equal(4, first);
        Assert.Equal(7, rig.Http.Requests.Count);
        Assert.All(
            rig.Http.Requests.Skip(first).Where(request => !FakeHttpHandler.IsStoreApi(request)),
            request => Assert.EndsWith("library_600x900.jpg", request.RequestUri!.AbsolutePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Looking_again_asks_at_once_whatever_was_remembered()
    {
        var found = false;
        var answers = Serve();
        using var rig = new ArtworkRig((request, token) => found ? answers(request, token) : Task.FromResult(FakeHttpHandler.NotFound()), online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        var before = await rig.LookedUpAsync(game.Id);
        found = true;

        var again = await rig.Artwork.LookAgainAsync(game.Id);

        Assert.Equal(ArtworkSource.None, before.Source);
        Assert.Equal(ArtworkSource.Steam, again.Value!.Source);
        Assert.Equal([game.Id], rig.Changes);
    }

    [Fact]
    public async Task Changing_the_app_id_asks_at_once_for_the_new_one_despite_a_remembered_miss()
    {
        using var rig = new ArtworkRig(Serve(111), online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = 111 });
        await rig.LookedUpAsync(game.Id);
        await rig.UpdateGameAsync(game with { SteamAppId = 222 });

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, art.Source);
        Assert.Equal(222, art.SteamAppId);
    }

    [Fact]
    public async Task A_redirect_to_another_host_is_never_followed()
    {
        using var rig = new ArtworkRig((_, _) => Task.FromResult(FakeHttpHandler.Redirect("https://evil.example.com/x.jpg")), online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.None, art.Source);
        Assert.All(rig.Http!.Requests, request => Assert.EndsWith(".steamstatic.com", request.RequestUri!.Host, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Never_more_than_two_requests_are_in_flight_across_all_the_games()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answers = Serve();
        using var rig = new ArtworkRig(async (request, token) =>
        {
            await gate.Task.WaitAsync(token);
            return await answers(request, token);
        }, online: true);
        var games = new List<GameDefinition>();
        for (var i = 1; i <= 5; i++)
        {
            games.Add(await rig.AddGameAsync($"Game{i}", g => g with { SteamAppId = 1000 + i }));
        }

        foreach (var game in games)
        {
            await rig.Artwork.GetAsync(game.Id, rig.Token);
        }

        Assert.True(await rig.Http!.WaitForRequestsAsync(2, rig.Token), "the request never reached the server");
        await Task.Delay(150, rig.Token);
        Assert.Equal(2, rig.Http.Requests.Count);
        gate.SetResult();
        await rig.WhenIdleAsync();

        Assert.Equal(2, rig.Http.MaxInFlight);
        foreach (var game in games)
        {
            Assert.Equal(ArtworkSource.Steam, (await rig.Artwork.GetAsync(game.Id, rig.Token)).Value!.Source);
        }
    }

    [Fact]
    public async Task A_game_deleted_while_its_images_download_leaves_no_folder_behind()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answers = Serve();
        using var rig = new ArtworkRig(async (request, token) =>
        {
            await gate.Task.WaitAsync(token);
            return await answers(request, token);
        }, online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        await rig.Artwork.GetAsync(game.Id, rig.Token);
        Assert.True(await rig.Http!.WaitForRequestsAsync(1, rig.Token), "the request never reached the server");

        await rig.Workspace.Backend.Services.Catalog.DeleteGameAsync(game.Id, rig.Token);
        rig.Workspace.Backend.Services.Artwork.DeleteGame(game.Id);
        gate.SetResult();
        await rig.WhenLookupDoneAsync(game.Id);

        Assert.False(Directory.Exists(rig.ArtworkFolder(game.Id)));
        Assert.Empty(rig.Changes);
    }

    [Fact]
    public async Task A_slow_server_ends_the_lookup_with_nothing_found_after_ten_seconds_a_request()
    {
        var time = new ManualTimeProvider();
        using var rig = new ArtworkRig(async (request, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeHttpHandler.NotFound();
        }, online: true, time);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        await rig.Artwork.GetAsync(game.Id, rig.Token);
        for (var request = 1; request <= 4; request++)
        {
            Assert.True(await rig.Http!.WaitForRequestsAsync(request, rig.Token), "the request never reached the server");
            time.Advance(TimeSpan.FromSeconds(10.5));
        }

        await rig.WhenIdleAsync();
        var art = await rig.Artwork.GetAsync(game.Id, rig.Token);

        Assert.Equal(ArtworkSource.None, art.Value!.Source);
        Assert.Equal(4, rig.Http!.Requests.Count);
    }
}
