using System.Net;
using Savepoint.Core.Artwork;
using Savepoint.Core.Tests.Services;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

public class SteamCdnTests
{
    private const string _primaryHeader = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/489830/header.jpg";
    private const string _fallbackHeader = "https://cdn.cloudflare.steamstatic.com/steam/apps/489830/header.jpg";
    private const string _primarySquare = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/489830/library_600x900.jpg";
    private const int _cap = 5 * 1024 * 1024;

    private static readonly string[] _allowedHosts = ["shared.akamai.steamstatic.com", "cdn.cloudflare.steamstatic.com"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<CdnResult> Fetch(FakeHttpHandler handler, ArtworkKind kind = ArtworkKind.Wide, TimeProvider? time = null) =>
        new SteamCdn(handler, time).FetchAsync(489830, kind, Token);

    private static void AssertOnlyTheTwoHostsOverHttpsWithGet(FakeHttpHandler handler)
    {
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Contains(request.RequestUri.Host, _allowedHosts);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.False(request.Headers.Contains("Authorization"));
            Assert.Null(request.Headers.Referrer);
        });
    }

    [Fact]
    public async Task The_wide_image_comes_from_the_first_host_with_one_get_request()
    {
        var jpeg = TestImages.Jpeg(460, 215);
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(jpeg)));

        var result = await Fetch(handler);

        Assert.Equal(CdnOutcome.Found, result.Outcome);
        Assert.Equal(jpeg, result.Bytes);
        Assert.Equal([_primaryHeader], handler.Urls);
        AssertOnlyTheTwoHostsOverHttpsWithGet(handler);
    }

    [Fact]
    public async Task The_square_image_has_its_own_file_name()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(TestImages.Jpeg(600, 900))));

        await Fetch(handler, ArtworkKind.Square);

        Assert.Equal([_primarySquare], handler.Urls);
    }

    [Fact]
    public async Task When_the_first_host_does_not_have_it_the_second_is_tried()
    {
        var jpeg = TestImages.Jpeg(460, 215);
        var handler = new FakeHttpHandler((request, _) => Task.FromResult(
            request.RequestUri!.Host.StartsWith("shared", StringComparison.Ordinal) ? FakeHttpHandler.NotFound() : FakeHttpHandler.Jpeg(jpeg)));

        var result = await Fetch(handler);

        Assert.Equal(CdnOutcome.Found, result.Outcome);
        Assert.Equal([_primaryHeader, _fallbackHeader], handler.Urls);
        AssertOnlyTheTwoHostsOverHttpsWithGet(handler);
    }

    [Fact]
    public async Task When_both_hosts_and_the_store_say_it_is_not_there_the_answer_is_not_found()
    {
        var handler = new FakeHttpHandler((request, _) => Task.FromResult(
            FakeHttpHandler.IsStoreApi(request) ? FakeHttpHandler.Json(FakeHttpHandler.StoreItems(489830, null)) : FakeHttpHandler.NotFound()));

        var result = await Fetch(handler);

        Assert.Equal(CdnOutcome.NotFound, result.Outcome);
        Assert.Null(result.Bytes);
        Assert.Equal([_primaryHeader, _fallbackHeader], handler.Urls.Take(2));
        Assert.Equal(3, handler.Urls.Count);
    }

    [Fact]
    public async Task A_server_error_is_a_failure_not_a_miss()
    {
        var handler = new FakeHttpHandler((request, _) => Task.FromResult(
            request.RequestUri!.Host.StartsWith("shared", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : FakeHttpHandler.NotFound()));

        Assert.Equal(CdnOutcome.Failed, (await Fetch(handler)).Outcome);
    }

    [Fact]
    public async Task A_network_error_is_a_failure_and_the_other_host_is_still_tried()
    {
        var jpeg = TestImages.Jpeg(460, 215);
        var handler = new FakeHttpHandler((request, _) =>
            request.RequestUri!.Host.StartsWith("shared", StringComparison.Ordinal)
                ? throw new HttpRequestException("no route to host")
                : Task.FromResult(FakeHttpHandler.Jpeg(jpeg)));

        var result = await Fetch(handler);

        Assert.Equal(CdnOutcome.Found, result.Outcome);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/gif")]
    [InlineData("application/octet-stream")]
    public async Task Only_an_image_content_type_the_app_reads_is_accepted(string contentType)
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(TestImages.Jpeg(460, 215), contentType)));

        var result = await Fetch(handler);

        Assert.Equal(CdnOutcome.Failed, result.Outcome);
        Assert.Null(result.Bytes);
    }

    [Fact]
    public async Task A_png_answer_is_accepted_when_the_bytes_are_a_png()
    {
        var png = TestImages.Png(600, 900);
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(png, "image/png")));

        var result = await Fetch(handler, ArtworkKind.Square);

        Assert.Equal(CdnOutcome.Found, result.Outcome);
        Assert.Equal(png, result.Bytes);
    }

    [Fact]
    public async Task A_webp_answer_is_accepted_when_the_bytes_are_a_webp()
    {
        var webp = TestImages.WebPLossless(460, 215);
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(webp, "image/webp")));

        var result = await Fetch(handler);

        Assert.Equal(CdnOutcome.Found, result.Outcome);
        Assert.Equal(webp, result.Bytes);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public async Task A_png_or_webp_content_type_over_bytes_that_are_not_an_image_is_refused(string contentType)
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(TestImages.Html(), contentType)));

        Assert.Equal(CdnOutcome.Failed, (await Fetch(handler)).Outcome);
    }

    [Fact]
    public async Task The_request_says_which_image_types_it_will_take()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(TestImages.Jpeg(460, 215))));

        await Fetch(handler);

        var accepted = handler.Requests[0].Headers.Accept.Select(h => h.MediaType).ToArray();
        Assert.Contains("image/jpeg", accepted);
        Assert.Contains("image/png", accepted);
        Assert.Contains("image/webp", accepted);
    }

    [Fact]
    public async Task A_jpeg_content_type_with_a_parameter_or_other_case_is_accepted()
    {
        var jpeg = TestImages.Jpeg(460, 215);
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(jpeg, "Image/JPEG")));

        Assert.Equal(CdnOutcome.Found, (await Fetch(handler)).Outcome);
    }

    [Fact]
    public async Task Bytes_that_are_not_an_image_are_not_kept_whatever_the_content_type_says()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(TestImages.Html())));

        Assert.Equal(CdnOutcome.Failed, (await Fetch(handler)).Outcome);
    }

    [Fact]
    public async Task A_download_cut_short_is_not_kept()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(TestImages.Jpeg(460, 215)[..^5])));

        Assert.Equal(CdnOutcome.Failed, (await Fetch(handler)).Outcome);
    }

    [Fact]
    public async Task A_redirect_is_never_followed_so_no_other_host_is_ever_asked()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Redirect("https://evil.example.com/header.jpg")));

        var result = await Fetch(handler);

        Assert.Equal(CdnOutcome.Failed, result.Outcome);
        Assert.Equal([_primaryHeader, _fallbackHeader], handler.Urls);
        AssertOnlyTheTwoHostsOverHttpsWithGet(handler);
    }

    [Fact]
    public async Task An_answer_that_came_from_another_host_after_a_hidden_redirect_is_refused()
    {
        var jpeg = TestImages.Jpeg(460, 215);
        var handler = new FakeHttpHandler((request, _) =>
        {
            var response = FakeHttpHandler.Jpeg(jpeg);
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://elsewhere.example.com/header.jpg");
            return Task.FromResult(response);
        });

        Assert.Equal(CdnOutcome.Failed, (await Fetch(handler)).Outcome);
    }

    [Fact]
    public async Task A_body_that_says_it_is_bigger_than_five_megabytes_is_refused_without_reading_it()
    {
        var handler = new FakeHttpHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnreadableStream()) };
            response.Content.Headers.ContentType = new("image/jpeg");
            response.Content.Headers.ContentLength = _cap + 1;
            return Task.FromResult(response);
        });

        Assert.Equal(CdnOutcome.Failed, (await Fetch(handler)).Outcome);
    }

    [Fact]
    public async Task A_body_with_no_size_is_cut_off_at_the_cap_instead_of_being_read_to_its_end()
    {
        var endless = new EndlessStream(long.MaxValue / 2);
        var handler = new FakeHttpHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(endless) };
            response.Content.Headers.ContentType = new("image/jpeg");
            return Task.FromResult(response);
        });

        var cdn = new SteamCdn(handler);
        var result = await cdn.FetchAsync(489830, ArtworkKind.Wide, Token);

        Assert.Equal(CdnOutcome.Failed, result.Outcome);
        // Both hosts are tried; each reads at most the cap and one buffer more.
        Assert.InRange(endless.BytesRead, 1, 2L * (_cap + 100_000));
    }

    [Fact]
    public async Task An_image_of_exactly_five_megabytes_is_accepted_and_one_byte_more_is_not()
    {
        var atCap = TestImages.JpegOfSize(460, 215, _cap);
        var over = TestImages.JpegOfSize(460, 215, _cap + 1);

        Assert.Equal(CdnOutcome.Found, (await Fetch(new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(atCap))))).Outcome);
        Assert.Equal(CdnOutcome.Failed, (await Fetch(new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(over))))).Outcome);
    }

    [Fact]
    public async Task A_request_that_takes_ten_seconds_is_given_up_on_and_the_second_host_gets_its_own_ten()
    {
        var time = new ManualTimeProvider();
        var handler = new FakeHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeHttpHandler.NotFound();
        });

        var fetching = Fetch(handler, time: time);
        Assert.True(await handler.WaitForRequestsAsync(1, Token), "the request never reached the server");
        time.Advance(TimeSpan.FromSeconds(9.5));
        await Task.Delay(50, Token);
        Assert.False(fetching.IsCompleted);
        Assert.Single(handler.Requests);

        time.Advance(TimeSpan.FromSeconds(0.6));
        Assert.True(await handler.WaitForRequestsAsync(2, Token), "the request never reached the server");
        time.Advance(TimeSpan.FromSeconds(10.1));
        var result = await fetching;

        Assert.Equal(CdnOutcome.Failed, result.Outcome);
        Assert.Equal([_primaryHeader, _fallbackHeader], handler.Urls);
    }

    [Fact]
    public async Task At_most_two_requests_are_in_flight_however_many_are_asked_for()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jpeg = TestImages.Jpeg(460, 215);
        var handler = new FakeHttpHandler(async (_, token) =>
        {
            await gate.Task.WaitAsync(token);
            return FakeHttpHandler.Jpeg(jpeg);
        });
        var cdn = new SteamCdn(handler);

        var all = Enumerable.Range(1, 6).Select(id => cdn.FetchAsync(id, ArtworkKind.Wide, Token)).ToList();
        Assert.True(await handler.WaitForRequestsAsync(2, Token), "the request never reached the server");
        await Task.Delay(150, Token);

        Assert.Equal(2, handler.Requests.Count);
        gate.SetResult();
        var results = await Task.WhenAll(all);

        Assert.All(results, result => Assert.Equal(CdnOutcome.Found, result.Outcome));
        Assert.Equal(6, handler.Requests.Count);
        Assert.Equal(2, handler.MaxInFlight);
    }

    [Fact]
    public async Task Cancelling_stops_the_fetch_with_a_cancellation_not_a_miss()
    {
        var handler = new FakeHttpHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeHttpHandler.NotFound();
        });
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Token);

        var fetching = new SteamCdn(handler).FetchAsync(489830, ArtworkKind.Wide, source.Token);
        Assert.True(await handler.WaitForRequestsAsync(1, Token), "the request never reached the server");
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetching);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task The_only_thing_sent_about_the_user_is_the_app_id_in_the_address()
    {
        var handler = new FakeHttpHandler((_, _) => Task.FromResult(FakeHttpHandler.Jpeg(TestImages.Jpeg(460, 215))));

        await Fetch(handler);

        var request = handler.Requests.Single();
        Assert.Equal(["Accept"], request.Headers.Select(header => header.Key));
        Assert.Null(request.Content);
        Assert.Equal(string.Empty, request.RequestUri!.Query);
    }
}
