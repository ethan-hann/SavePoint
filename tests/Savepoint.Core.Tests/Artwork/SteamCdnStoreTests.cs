using System.Net;
using Savepoint.Core.Artwork;
using Savepoint.Core.Tests.Services;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

// Newer games are only on Steam's servers under a hashed path, which the store API names. These tests cover the second step of a fetch.
public class SteamCdnStoreTests
{
    private const int _app = 3669870;
    private const string _capsuleHash = "7d4b4f430dc4d07e6562eb445456f9615cb52f77";
    private const string _headerHash = "1e0c76e4253d347bb1304c3f213570251d924edf";
    private const string _assetHost = "https://shared.akamai.steamstatic.com/store_item_assets/";
    private const string _format = "steam/apps/3669870/${FILENAME}?t=1791013569";
    private const string _apiUrl = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=";
    private const string _plainWide = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/3669870/header.jpg";
    private const string _plainWideFallback = "https://cdn.cloudflare.steamstatic.com/steam/apps/3669870/header.jpg";
    private const string _plainSquare = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/3669870/library_600x900.jpg";
    private const string _plainSquareFallback = "https://cdn.cloudflare.steamstatic.com/steam/apps/3669870/library_600x900.jpg";
    private const int _apiCap = 256 * 1024;

    private static readonly byte[] _wide = TestImages.Jpeg(460, 215);
    private static readonly byte[] _square = TestImages.Jpeg(300, 450);

    private static readonly string[] _allowedHosts = ["shared.akamai.steamstatic.com", "cdn.cloudflare.steamstatic.com", "api.steampowered.com"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Assets(string? format = _format, string? header = $"{_headerHash}/header.jpg", string? capsule = $"{_capsuleHash}/library_capsule.jpg", string? capsule2x = null)
    {
        var parts = new List<string>();
        void Add(string key, string? value)
        {
            if (value is not null)
            {
                parts.Add($"\"{key}\":{System.Text.Json.JsonSerializer.Serialize(value)}");
            }
        }

        Add("asset_url_format", format);
        Add("header", header);
        Add("library_capsule", capsule);
        Add("library_capsule_2x", capsule2x);
        return "{" + string.Join(",", parts) + "}";
    }

    // Plain addresses say not found, the API says what it is given, and any other address gets the image its file name implies.
    private static FakeHttpHandler Server(string apiBody, Func<HttpRequestMessage, HttpResponseMessage?>? other = null) =>
        new((request, _) =>
        {
            if (FakeHttpHandler.IsStoreApi(request))
            {
                return Task.FromResult(FakeHttpHandler.Json(apiBody));
            }

            if (other?.Invoke(request) is { } custom)
            {
                return Task.FromResult(custom);
            }

            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains($"/{_capsuleHash}/library_capsule", StringComparison.Ordinal))
            {
                return Task.FromResult(FakeHttpHandler.Jpeg(_square));
            }

            return Task.FromResult(path.Contains($"/{_headerHash}/header.jpg", StringComparison.Ordinal) ? FakeHttpHandler.Jpeg(_wide) : FakeHttpHandler.NotFound());
        });

    private static Task<CdnResult> Fetch(FakeHttpHandler handler, ArtworkKind kind, StoreAssetsMemo? memo = null, TimeProvider? time = null) =>
        new SteamCdn(handler, time).FetchAsync(_app, kind, Token, memo);

    private static string Body(int appId = _app, string? assets = null) => FakeHttpHandler.StoreItems(appId, assets ?? Assets());

    [Fact]
    public async Task When_both_plain_addresses_say_not_found_the_hashed_wide_image_is_fetched_through_the_store_api()
    {
        var handler = Server(Body());

        var result = await Fetch(handler, ArtworkKind.Wide);

        Assert.Equal(CdnOutcome.Found, result.Outcome);
        Assert.Equal(_wide, result.Bytes);
        Assert.Equal(
            [
                _plainWide,
                _plainWideFallback,
                _apiUrl + Uri.EscapeDataString("{\"ids\":[{\"appid\":3669870}],\"context\":{\"country_code\":\"US\"},\"data_request\":{\"include_assets\":true}}"),
                $"{_assetHost}steam/apps/3669870/{_headerHash}/header.jpg?t=1791013569",
            ],
            handler.Urls);
    }

    [Fact]
    public async Task The_hashed_tall_image_is_the_capsule_when_there_is_no_double_size_one()
    {
        var handler = Server(Body());

        var result = await Fetch(handler, ArtworkKind.Square);

        Assert.Equal(CdnOutcome.Found, result.Outcome);
        Assert.Equal(_square, result.Bytes);
        Assert.Equal($"{_assetHost}steam/apps/3669870/{_capsuleHash}/library_capsule.jpg?t=1791013569", handler.Urls[^1]);
    }

    [Fact]
    public async Task The_double_size_capsule_is_preferred_for_the_tall_image()
    {
        var handler = Server(Body(assets: Assets(capsule2x: $"{_capsuleHash}/library_capsule_2x.jpg")), request =>
            request.RequestUri!.AbsolutePath.EndsWith("library_capsule_2x.jpg", StringComparison.Ordinal) ? FakeHttpHandler.Jpeg(TestImages.Jpeg(600, 900)) : null);

        var result = await Fetch(handler, ArtworkKind.Square);

        Assert.Equal(CdnOutcome.Found, result.Outcome);
        Assert.Equal(TestImages.Jpeg(600, 900), result.Bytes);
        Assert.Equal($"{_assetHost}steam/apps/3669870/{_capsuleHash}/library_capsule_2x.jpg?t=1791013569", handler.Urls[^1]);
    }

    [Fact]
    public async Task An_older_game_whose_store_names_are_the_plain_ones_is_fetched_from_the_plain_address_after_the_store_answers()
    {
        var handler = Server(Body(assets: Assets(header: "header.jpg", capsule: "library_600x900.jpg")), request =>
            request.RequestUri!.AbsolutePath.EndsWith("/header.jpg", StringComparison.Ordinal) && request.RequestUri.Query.Length > 0 ? FakeHttpHandler.Jpeg(_wide) : null);

        var result = await Fetch(handler, ArtworkKind.Wide);

        Assert.Equal(CdnOutcome.Found, result.Outcome);
        Assert.Equal($"{_assetHost}steam/apps/3669870/header.jpg?t=1791013569", handler.Urls[^1]);
    }

    [Fact]
    public async Task A_found_plain_image_asks_the_store_nothing()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(_wide)));

        await Fetch(handler, ArtworkKind.Wide);

        Assert.Equal([_plainWide], handler.Urls);
    }

    [Fact]
    public async Task A_plain_address_that_failed_rather_than_said_not_found_asks_the_store_nothing()
    {
        var handler = new FakeHttpHandler((request, _) => Task.FromResult(
            request.RequestUri!.Host.StartsWith("shared", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : FakeHttpHandler.NotFound()));

        var result = await Fetch(handler, ArtworkKind.Wide);

        Assert.Equal(CdnOutcome.Failed, result.Outcome);
        Assert.DoesNotContain(handler.Requests, FakeHttpHandler.IsStoreApi);
    }

    [Fact]
    public async Task The_two_kinds_of_one_lookup_share_one_store_request()
    {
        var handler = Server(Body());
        var cdn = new SteamCdn(handler);
        var memo = new StoreAssetsMemo();

        var wide = await cdn.FetchAsync(_app, ArtworkKind.Wide, Token, memo);
        var square = await cdn.FetchAsync(_app, ArtworkKind.Square, Token, memo);

        Assert.Equal(CdnOutcome.Found, wide.Outcome);
        Assert.Equal(CdnOutcome.Found, square.Outcome);
        Assert.Single(handler.Requests, FakeHttpHandler.IsStoreApi);
    }

    [Fact]
    public async Task Without_a_shared_memo_each_fetch_asks_the_store_itself()
    {
        var handler = Server(Body());
        var cdn = new SteamCdn(handler);

        await cdn.FetchAsync(_app, ArtworkKind.Wide, Token);
        await cdn.FetchAsync(_app, ArtworkKind.Square, Token);

        Assert.Equal(2, handler.Requests.Count(FakeHttpHandler.IsStoreApi));
    }

    [Fact]
    public async Task A_store_answer_with_no_such_asset_is_not_found()
    {
        var handler = Server(Body(assets: Assets(capsule: null)));

        var result = await Fetch(handler, ArtworkKind.Square);

        Assert.Equal(CdnOutcome.NotFound, result.Outcome);
        Assert.Null(result.Bytes);
        Assert.DoesNotContain(handler.Urls, url => url.Contains("library_capsule", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_store_answer_with_no_assets_at_all_is_not_found()
    {
        var handler = Server(FakeHttpHandler.StoreItems(_app, null));

        Assert.Equal(CdnOutcome.NotFound, (await Fetch(handler, ArtworkKind.Wide)).Outcome);
    }

    [Fact]
    public async Task A_store_answer_with_no_items_is_not_found()
    {
        var handler = Server("{\"response\":{}}");

        Assert.Equal(CdnOutcome.NotFound, (await Fetch(handler, ArtworkKind.Wide)).Outcome);
    }

    [Fact]
    public async Task A_hashed_image_that_is_not_there_is_not_found()
    {
        var handler = Server(Body(), request => request.RequestUri!.AbsolutePath.Contains(_headerHash, StringComparison.Ordinal) ? FakeHttpHandler.NotFound() : null);

        Assert.Equal(CdnOutcome.NotFound, (await Fetch(handler, ArtworkKind.Wide)).Outcome);
    }

    [Fact]
    public async Task A_hashed_image_that_is_not_a_jpeg_image_is_a_failure()
    {
        var handler = Server(Body(), request => request.RequestUri!.AbsolutePath.Contains(_headerHash, StringComparison.Ordinal) ? FakeHttpHandler.Jpeg(TestImages.Html()) : null);

        Assert.Equal(CdnOutcome.Failed, (await Fetch(handler, ArtworkKind.Wide)).Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Found)]
    public async Task A_store_answer_that_is_not_a_plain_200_is_a_failure_not_a_miss(HttpStatusCode status)
    {
        var handler = new FakeHttpHandler((request, _) => Task.FromResult(FakeHttpHandler.IsStoreApi(request) ? new HttpResponseMessage(status) : FakeHttpHandler.NotFound()));

        var result = await Fetch(handler, ArtworkKind.Wide);

        Assert.Equal(CdnOutcome.Failed, result.Outcome);
        Assert.Null(result.Bytes);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task A_store_redirect_is_not_followed()
    {
        var handler = new FakeHttpHandler((request, _) => Task.FromResult(
            FakeHttpHandler.IsStoreApi(request) ? FakeHttpHandler.Redirect("https://evil.example.com/x") : FakeHttpHandler.NotFound()));

        Assert.Equal(CdnOutcome.Failed, (await Fetch(handler, ArtworkKind.Wide)).Outcome);
        Assert.All(handler.Requests, request => Assert.Contains(request.RequestUri!.Host, _allowedHosts));
    }

    [Fact]
    public async Task A_store_answer_from_another_host_after_a_hidden_redirect_is_refused()
    {
        var handler = new FakeHttpHandler((request, _) =>
        {
            if (!FakeHttpHandler.IsStoreApi(request))
            {
                return Task.FromResult(FakeHttpHandler.NotFound());
            }

            var response = FakeHttpHandler.Json(Body());
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://elsewhere.example.com/x");
            return Task.FromResult(response);
        });

        Assert.Equal(CdnOutcome.Failed, (await Fetch(handler, ArtworkKind.Wide)).Outcome);
    }

    [Fact]
    public async Task A_network_error_asking_the_store_is_a_failure()
    {
        var handler = new FakeHttpHandler((request, _) =>
            FakeHttpHandler.IsStoreApi(request) ? throw new HttpRequestException("no route to host") : Task.FromResult(FakeHttpHandler.NotFound()));

        var result = await Fetch(handler, ArtworkKind.Wide);

        Assert.Equal(CdnOutcome.Failed, result.Outcome);
        Assert.Contains("api.steampowered.com", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"response\":{\"store_items\":\"x\"}}")]
    [InlineData("{\"response\":{\"store_items\":[{\"appid\":3669870,\"assets\":[]}]}}")]
    [InlineData("{\"response\":{\"store_items\":[{\"appid\":3669870,\"assets\":{\"asset_url_format\":5}}]}}")]
    public async Task A_store_answer_that_is_not_the_expected_json_is_a_failure(string body)
    {
        var handler = Server(body);

        var result = await Fetch(handler, ArtworkKind.Wide);

        Assert.Equal(CdnOutcome.Failed, result.Outcome);
        Assert.DoesNotContain(handler.Urls, url => url.Contains("store_item_assets/steam/apps/3669870/", StringComparison.Ordinal) && url.Contains('?', StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_store_answer_about_another_app_is_a_failure()
    {
        var handler = Server(Body(appId: _app + 1));

        Assert.Equal(CdnOutcome.Failed, (await Fetch(handler, ArtworkKind.Wide)).Outcome);
    }

    [Theory]
    [InlineData("steam/apps/1234/${FILENAME}")]
    [InlineData("steam/apps/36698700/${FILENAME}")]
    [InlineData("steam/apps/3669870/")]
    [InlineData("steam/apps/3669870/header.jpg")]
    [InlineData("steam/apps/3669870/${FILENAME}${FILENAME}")]
    [InlineData("steam/apps/3669870/${FILENAME}/${FILENAME}")]
    [InlineData("https://evil.example.com/steam/apps/3669870/${FILENAME}")]
    [InlineData("/steam/apps/3669870/${FILENAME}")]
    [InlineData("../steam/apps/3669870/${FILENAME}")]
    [InlineData("steam/apps/3669870/../../x/${FILENAME}")]
    [InlineData("steam/apps/3669870/${FILENAME}#frag")]
    [InlineData("steam/apps/3669870/${FILENAME}?t=1\\..\\x")]
    public async Task A_url_format_that_breaks_the_rules_is_a_failure_and_nothing_is_fetched_from_it(string format)
    {
        var handler = Server(Body(assets: Assets(format: format)));

        var result = await Fetch(handler, ArtworkKind.Wide);

        Assert.Equal(CdnOutcome.Failed, result.Outcome);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task A_store_answer_with_no_url_format_is_a_failure()
    {
        var handler = Server(Body(assets: Assets(format: null)));

        Assert.Equal(CdnOutcome.Failed, (await Fetch(handler, ArtworkKind.Wide)).Outcome);
    }

    [Theory]
    [InlineData("../header.jpg")]
    [InlineData("/header.jpg")]
    [InlineData("a/b/header.jpg")]
    [InlineData("zzzz4b4f430dc4d07e6562eb445456f9615cb52f77/header.jpg")]
    [InlineData("7D4B4F430DC4D07E6562EB445456F9615CB52F77/header.jpg")]
    [InlineData("7d4b4f430dc4d07e6562eb445456f9615cb52f/header.jpg")]
    [InlineData("7d4b4f430dc4d07e6562eb445456f9615cb52f77/../header.jpg")]
    [InlineData("header.png")]
    [InlineData("header.jpg?x=1")]
    [InlineData("header.jpg#x")]
    [InlineData("hea der.jpg")]
    [InlineData("https://evil.example.com/header.jpg")]
    [InlineData("//evil.example.com/header.jpg")]
    [InlineData("..\\header.jpg")]
    [InlineData("header.jpg\n")]
    [InlineData("")]
    [InlineData(".jpg")]
    public async Task An_asset_name_that_breaks_the_pattern_counts_as_not_found_and_is_never_followed(string name)
    {
        var handler = Server(Body(assets: Assets(header: name)));

        var result = await Fetch(handler, ArtworkKind.Wide);

        Assert.Equal(CdnOutcome.NotFound, result.Outcome);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Contains(request.RequestUri!.Host, _allowedHosts));
    }

    [Fact]
    public async Task A_bad_double_size_name_is_skipped_for_a_good_plain_capsule()
    {
        var handler = Server(Body(assets: Assets(capsule2x: "../x/library_capsule_2x.jpg")));

        var result = await Fetch(handler, ArtworkKind.Square);

        Assert.Equal(CdnOutcome.Found, result.Outcome);
        Assert.Equal(_square, result.Bytes);
    }

    [Fact]
    public async Task Only_the_allowed_hosts_are_asked_over_https_with_get_and_nothing_but_the_app_id_is_sent()
    {
        var handler = Server(Body());

        await Fetch(handler, ArtworkKind.Wide);

        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Contains(request.RequestUri.Host, _allowedHosts);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.False(request.Headers.Contains("Authorization"));
            Assert.Null(request.Headers.Referrer);
            Assert.Null(request.Content);
            Assert.Equal(["Accept"], request.Headers.Select(header => header.Key));
        });
        Assert.Equal("api.steampowered.com", handler.Requests[2].RequestUri!.Host);
        Assert.Equal("shared.akamai.steamstatic.com", handler.Requests[3].RequestUri!.Host);
    }

    [Fact]
    public async Task A_store_answer_that_says_it_is_bigger_than_the_cap_is_refused_without_reading_it()
    {
        var handler = new FakeHttpHandler((request, _) =>
        {
            if (!FakeHttpHandler.IsStoreApi(request))
            {
                return Task.FromResult(FakeHttpHandler.NotFound());
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnreadableStream()) };
            response.Content.Headers.ContentLength = _apiCap + 1;
            return Task.FromResult(response);
        });

        Assert.Equal(CdnOutcome.Failed, (await Fetch(handler, ArtworkKind.Wide)).Outcome);
    }

    [Fact]
    public async Task A_store_answer_with_no_size_is_cut_off_at_the_cap()
    {
        var endless = new EndlessStream(long.MaxValue / 2);
        var handler = new FakeHttpHandler((request, _) =>
            Task.FromResult(FakeHttpHandler.IsStoreApi(request) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(endless) } : FakeHttpHandler.NotFound()));

        var result = await Fetch(handler, ArtworkKind.Wide);

        Assert.Equal(CdnOutcome.Failed, result.Outcome);
        Assert.InRange(endless.BytesRead, 1, _apiCap + 100_000);
    }

    [Fact]
    public async Task A_store_answer_just_under_the_cap_is_read()
    {
        var padding = new string(' ', _apiCap - Body().Length);
        var handler = Server(Body() + padding);

        Assert.Equal(CdnOutcome.Found, (await Fetch(handler, ArtworkKind.Wide)).Outcome);
    }

    [Fact]
    public async Task A_store_request_that_takes_ten_seconds_is_given_up_on()
    {
        var time = new ManualTimeProvider();
        var handler = new FakeHttpHandler(async (request, token) =>
        {
            if (FakeHttpHandler.IsStoreApi(request))
            {
                await Task.Delay(Timeout.Infinite, token);
            }

            return FakeHttpHandler.NotFound();
        });

        var fetching = Fetch(handler, ArtworkKind.Wide, time: time);
        Assert.True(await handler.WaitForRequestsAsync(3, Token), "the store request never reached the server");
        time.Advance(TimeSpan.FromSeconds(9.5));
        await Task.Delay(50, Token);
        Assert.False(fetching.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(0.6));

        Assert.Equal(CdnOutcome.Failed, (await fetching).Outcome);
    }

    [Fact]
    public async Task At_most_two_requests_are_in_flight_across_plain_store_and_hashed_fetches()
    {
        var handler = new FakeHttpHandler(async (request, token) =>
        {
            await Task.Delay(20, token);
            if (FakeHttpHandler.IsStoreApi(request))
            {
                return FakeHttpHandler.Json(Body());
            }

            var path = request.RequestUri!.AbsolutePath;
            return path.Contains(_headerHash, StringComparison.Ordinal) ? FakeHttpHandler.Jpeg(_wide) : FakeHttpHandler.NotFound();
        });
        var cdn = new SteamCdn(handler);

        var all = Enumerable.Range(0, 5).Select(_ => cdn.FetchAsync(_app, ArtworkKind.Wide, Token)).ToList();
        var results = await Task.WhenAll(all);

        Assert.All(results, result => Assert.Equal(CdnOutcome.Found, result.Outcome));
        Assert.Equal(2, handler.MaxInFlight);
    }

    [Fact]
    public async Task Cancelling_during_the_store_request_stops_the_fetch_with_a_cancellation()
    {
        var handler = new FakeHttpHandler(async (request, token) =>
        {
            if (FakeHttpHandler.IsStoreApi(request))
            {
                await Task.Delay(Timeout.Infinite, token);
            }

            return FakeHttpHandler.NotFound();
        });
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var fetching = new SteamCdn(handler).FetchAsync(_app, ArtworkKind.Wide, source.Token);
        Assert.True(await handler.WaitForRequestsAsync(3, Token), "the store request never reached the server");
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetching);
    }

    [Fact]
    public async Task The_wide_fetch_of_a_tall_only_listing_is_not_found()
    {
        var handler = Server(Body(assets: Assets(header: null)));

        var result = await Fetch(handler, ArtworkKind.Wide);

        Assert.Equal(CdnOutcome.NotFound, result.Outcome);
        Assert.Equal([_plainWide, _plainWideFallback], handler.Urls.Where(url => !url.StartsWith(_apiUrl, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_plain_tall_addresses_are_tried_first_for_the_tall_kind_too()
    {
        var handler = Server(Body());

        await Fetch(handler, ArtworkKind.Square);

        Assert.Equal([_plainSquare, _plainSquareFallback], handler.Urls.Take(2));
    }
}
