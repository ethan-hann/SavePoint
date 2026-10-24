using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

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

internal enum Payload
{
    Image,
    Json,
}

// What the store API said about one app during one lookup, so the wide and tall images share a single call.
internal sealed class StoreAssetsMemo
{
    internal int AppId { get; set; }

    internal StoreAnswer? Answer { get; set; }
}

// The store API's answer for one app: the outcome, and for Found the URL format and the asset names it listed.
internal sealed record StoreAnswer(CdnOutcome Outcome, string? Format = null, IReadOnlyDictionary<string, string>? Assets = null, string? Reason = null);

// Fetches a game's artwork from Steam's servers under these network rules: HTTPS and GET only, three named hosts (two image
// servers and the store API), no redirect followed, ten seconds per request, five megabytes at most for an image and 256 KB for
// the API answer, a JPEG, PNG, or WebP content type, bytes that really are an image, and no more than two requests in flight. The handler it is
// given must not follow redirects or keep cookies; the app's handler is set up that way.
internal sealed class SteamCdn
{
    private const int _maxBytes = 5 * 1024 * 1024;
    private const int _maxApiBytes = 256 * 1024;
    private const string _apiHost = "api.steampowered.com";
    private const string _assetRoot = "https://shared.akamai.steamstatic.com/store_item_assets/";
    private const string _placeholder = "${FILENAME}";

    // An optional 40-hex-digit folder, a slash, and a plain JPEG file name; \z so a trailing newline does not slip through.
    private static readonly Regex _assetName = new(@"^(?:[0-9a-f]{40}/)?[A-Za-z0-9_]+\.jpg\z", RegexOptions.CultureInvariant);
    private static readonly string[] _imageTypes = ["image/jpeg", "image/png", "image/webp"];
    private static readonly string[] _jsonTypes = ["application/json"];
    private static readonly string[] _squareKeys = ["library_capsule_2x", "library_capsule"];
    private static readonly string[] _wideKeys = ["header"];

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

    public async Task<CdnResult> FetchAsync(int appId, ArtworkKind kind, CancellationToken cancellationToken, StoreAssetsMemo? memo = null)
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
            var result = await TryAsync(address, Payload.Image, cancellationToken).ConfigureAwait(false);
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

        // Newer games are only served under a hashed path that the store API names.
        return allNotFound
            ? await FetchHashedAsync(appId, kind, memo, cancellationToken).ConfigureAwait(false)
            : new CdnResult(CdnOutcome.Failed, Reason: string.Join(", ", reasons));
    }

    private async Task<CdnResult> FetchHashedAsync(int appId, ArtworkKind kind, StoreAssetsMemo? memo, CancellationToken cancellationToken)
    {
        var answer = memo is { Answer: not null } && memo.AppId == appId
            ? memo.Answer
            : await AskStoreAsync(appId, cancellationToken).ConfigureAwait(false);
        if (memo is not null)
        {
            memo.AppId = appId;
            memo.Answer = answer;
        }

        if (answer.Outcome != CdnOutcome.Found)
        {
            return new CdnResult(answer.Outcome, Reason: answer.Reason);
        }

        var name = (kind == ArtworkKind.Wide ? _wideKeys : _squareKeys)
            .Select(key => answer.Assets!.TryGetValue(key, out var value) ? value : null)
            .FirstOrDefault(value => value is not null && _assetName.IsMatch(value));
        if (name is null)
        {
            return new CdnResult(CdnOutcome.NotFound);
        }

        var id = appId.ToString(CultureInfo.InvariantCulture);
        if (answer.Format is not { } format || !FormatIsSafe(format, id))
        {
            return new CdnResult(CdnOutcome.Failed, Reason: $"{_apiHost} sent an address pattern that broke the rules");
        }

        var address = new Uri(_assetRoot + format.Replace(_placeholder, name, StringComparison.Ordinal));
        var result = await TryAsync(address, Payload.Image, cancellationToken).ConfigureAwait(false);
        return result.Reason is null ? result : result with { Reason = $"{address.Host} {result.Reason}" };
    }

    // The format must stay under this app's folder, hold the placeholder once, and carry at most a plain query.
    private static bool FormatIsSafe(string format, string id) =>
        Regex.IsMatch(format, $@"^steam/apps/{id}/\$\{{FILENAME\}}(\?[A-Za-z0-9_=&.\-]*)?\z", RegexOptions.CultureInvariant);

    private async Task<StoreAnswer> AskStoreAsync(int appId, CancellationToken cancellationToken)
    {
        var id = appId.ToString(CultureInfo.InvariantCulture);
        var input = "{\"ids\":[{\"appid\":" + id + "}],\"context\":{\"country_code\":\"US\"},\"data_request\":{\"include_assets\":true}}";
        var address = new Uri($"https://{_apiHost}/IStoreBrowseService/GetItems/v1?input_json={Uri.EscapeDataString(input)}");
        var result = await TryAsync(address, Payload.Json, cancellationToken).ConfigureAwait(false);
        if (result.Outcome != CdnOutcome.Found)
        {
            // The API has no "not there" answer worth remembering: a 404 from it means the question itself was wrong.
            return new StoreAnswer(CdnOutcome.Failed, Reason: $"{_apiHost} {result.Reason ?? "did not know the question"}");
        }

        try
        {
            return ParseStoreAnswer(result.Bytes!, appId);
        }
        catch (JsonException)
        {
            return Broken("sent an answer that is not the expected JSON");
        }
    }

    private static StoreAnswer Broken(string why) => new(CdnOutcome.Failed, Reason: $"{_apiHost} {why}");

    private static StoreAnswer ParseStoreAnswer(byte[] bytes, int appId)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Object)
        {
            return Broken("sent an answer that is not the expected JSON");
        }

        if (!response.TryGetProperty("store_items", out var items))
        {
            return new StoreAnswer(CdnOutcome.NotFound);
        }

        if (items.ValueKind != JsonValueKind.Array)
        {
            return Broken("sent an answer that is not the expected JSON");
        }

        if (items.GetArrayLength() == 0)
        {
            return new StoreAnswer(CdnOutcome.NotFound);
        }

        var item = items[0];
        if (item.ValueKind != JsonValueKind.Object)
        {
            return Broken("sent an answer that is not the expected JSON");
        }

        if (item.TryGetProperty("appid", out var itemApp) && !(itemApp.ValueKind == JsonValueKind.Number && itemApp.TryGetInt32(out var number) && number == appId))
        {
            return Broken("answered about another app");
        }

        if (!item.TryGetProperty("assets", out var assets))
        {
            return new StoreAnswer(CdnOutcome.NotFound);
        }

        if (assets.ValueKind != JsonValueKind.Object)
        {
            return Broken("sent an answer that is not the expected JSON");
        }

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in assets.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                names[property.Name] = property.Value.GetString()!;
            }
            else if (property.Name == "asset_url_format")
            {
                return Broken("sent an answer that is not the expected JSON");
            }
        }

        names.Remove("asset_url_format", out var format);
        return new StoreAnswer(CdnOutcome.Found, format, names);
    }

    private async Task<CdnResult> TryAsync(Uri address, Payload payload, CancellationToken cancellationToken)
    {
        await _inFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var limit = new CancellationTokenSource(_timeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);
            try
            {
                return await SendAsync(address, payload, linked.Token).ConfigureAwait(false);
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

    private async Task<CdnResult> SendAsync(Uri address, Payload payload, CancellationToken cancellationToken)
    {
        var isImage = payload == Payload.Image;
        var cap = isImage ? _maxBytes : _maxApiBytes;
        var capWords = isImage ? "5 MB" : "256 KB";
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        foreach (var type in isImage ? _imageTypes : _jsonTypes)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(type));
        }
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

        // The servers may answer in any of these; the bytes are still checked below.
        if (isImage && !_imageTypes.Contains(response.Content.Headers.ContentType?.MediaType ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            return new CdnResult(CdnOutcome.Failed, Reason: "sent something that is not a JPEG, PNG, or WebP image");
        }

        if (response.Content.Headers.ContentLength > cap)
        {
            return new CdnResult(CdnOutcome.Failed, Reason: $"sent more than {capWords}");
        }

        var bytes = await ReadCappedAsync(response.Content, cap, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return new CdnResult(CdnOutcome.Failed, Reason: $"sent more than {capWords}");
        }

        if (!isImage)
        {
            return new CdnResult(CdnOutcome.Found, bytes);
        }

        using var stream = new MemoryStream(bytes, writable: false);
        return ImageProbe.Read(stream) is null ? new CdnResult(CdnOutcome.Failed, Reason: "sent bytes that are not an image") : new CdnResult(CdnOutcome.Found, bytes);
    }

    // Reads the body, giving up as soon as it passes the cap instead of waiting for the end of what could be an endless stream.
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, int cap, CancellationToken cancellationToken)
    {
        await using var body = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var collected = new MemoryStream();
        var buffer = new byte[81_920];
        int read;
        while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (collected.Length + read > cap)
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
