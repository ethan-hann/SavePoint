using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Savepoint.Core.Artwork;

internal enum CdnOutcome
{
    // The image arrived and passed the checks.
    Found,

    // Every server said the image does not exist; worth remembering.
    NotFound,

    // Anything else: no connection, a timeout, a server error, an answer that broke a rule. Nothing is learned about the image.
    Failed,
}

// An outcome, the bytes when it is Found, and for Failed what went wrong, in words for the log.
internal sealed record CdnResult(CdnOutcome Outcome, byte[]? Bytes = null, string? Reason = null);

// Fetches a game's artwork from Steam's servers under these network rules: HTTPS and GET only, two named hosts, no redirect
// followed, ten seconds per request, five megabytes at most, a JPEG content type, bytes that really are an image, and no more than
// two requests in flight. The handler it is given must not follow redirects or keep cookies; the app's handler is set up that way.
internal sealed class SteamCdn
{
    private const int _maxBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _client;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _inFlight = new(2, 2);

    public SteamCdn(HttpMessageHandler handler, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _time = time ?? TimeProvider.System;
        // The handler belongs to whoever supplied it, and each request has its own time limit below.
        _client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<CdnResult> FetchAsync(int appId, ArtworkKind kind, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(appId, 1);
        var file = kind.SteamFileName();
        var id = appId.ToString(CultureInfo.InvariantCulture);
        Uri[] addresses =
        [
            new($"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{id}/{file}"),
            new($"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/{file}"),
        ];

        // Only when both servers say the file is not there is it not there; any other answer leaves the question open.
        var allNotFound = true;
        var reasons = new List<string>();
        foreach (var address in addresses)
        {
            var result = await TryAsync(address, cancellationToken).ConfigureAwait(false);
            if (result.Outcome == CdnOutcome.Found)
            {
                return result;
            }

            allNotFound &= result.Outcome == CdnOutcome.NotFound;
            if (result.Reason is not null)
            {
                reasons.Add($"{address.Host} {result.Reason}");
            }
        }

        return allNotFound ? new CdnResult(CdnOutcome.NotFound) : new CdnResult(CdnOutcome.Failed, Reason: string.Join(", ", reasons));
    }

    private async Task<CdnResult> TryAsync(Uri address, CancellationToken cancellationToken)
    {
        await _inFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var limit = new CancellationTokenSource(_timeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
            try
            {
                return await SendAsync(address, linked.Token).ConfigureAwait(false);
            }
            catch (Exception e) when ((e is HttpRequestException or IOException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                // A time-out, a dropped connection, or a handler that gave up: the question stays open.
                return new CdnResult(CdnOutcome.Failed, Reason: e is OperationCanceledException ? "did not answer in time" : e.Message);
            }
        }
        finally
        {
            _inFlight.Release();
        }
    }

    private async Task<CdnResult> SendAsync(Uri address, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        // A redirect is not followed. If the handler followed one anyway, the answer came from a host that was not asked for.
        if (response.RequestMessage?.RequestUri is { } answeredBy && !SameHost(answeredBy, address))
        {
            return new CdnResult(CdnOutcome.Failed, Reason: "was answered from another host");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new CdnResult(CdnOutcome.NotFound);
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return new CdnResult(CdnOutcome.Failed, Reason: $"answered {(int)response.StatusCode}");
        }

        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return new CdnResult(CdnOutcome.Failed, Reason: "sent something that is not a JPEG image");
        }

        if (response.Content.Headers.ContentLength > _maxBytes)
        {
            return new CdnResult(CdnOutcome.Failed, Reason: "sent more than 5 MB");
        }

        var bytes = await ReadCappedAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return new CdnResult(CdnOutcome.Failed, Reason: "sent more than 5 MB");
        }

        using var stream = new MemoryStream(bytes, writable: false);
        return ImageProbe.Read(stream) is null ? new CdnResult(CdnOutcome.Failed, Reason: "sent bytes that are not an image") : new CdnResult(CdnOutcome.Found, bytes);
    }

    // Reads the body, giving up as soon as it passes the cap instead of waiting for the end of what could be an endless stream.
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var body = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var collected = new MemoryStream();
        var buffer = new byte[81_920];
        int read;
        while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (collected.Length + read > _maxBytes)
            {
                return null;
            }

            collected.Write(buffer, 0, read);
        }

        return collected.ToArray();
    }

    private static bool SameHost(Uri answeredBy, Uri asked) =>
        answeredBy.Scheme == Uri.UriSchemeHttps && string.Equals(answeredBy.Host, asked.Host, StringComparison.OrdinalIgnoreCase);
}
