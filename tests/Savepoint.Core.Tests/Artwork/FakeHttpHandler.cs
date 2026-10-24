using System.Net;
using System.Net.Http.Headers;

namespace Savepoint.Core.Tests.Artwork;

// Stands in for the network: every request is recorded and answered by the test's own function, so no test reaches a real server.
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<HttpRequestMessage> _requests = [];
    private int _inFlight;
    private TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null)
    {
        Respond = respond ?? ((_, _) => Task.FromResult(NotFound()));
    }

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; }

    public IReadOnlyList<HttpRequestMessage> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public IReadOnlyList<string> Urls => [.. Requests.Select(request => request.RequestUri!.AbsoluteUri)];

    public int MaxInFlight { get; private set; }

    public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);

    public static HttpResponseMessage Jpeg(byte[] content, string contentType = "image/jpeg")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return response;
    }

    public static HttpResponseMessage Json(string body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    public static bool IsStoreApi(HttpRequestMessage request) => request.RequestUri!.Host == "api.steampowered.com";

    // The store API's answer for one app: the assets object is written by the caller, or left out for an app with none.
    public static string StoreItems(int appId, string? assets)
    {
        var tail = assets is null ? string.Empty : ",\"assets\":" + assets;
        return "{\"response\":{\"store_items\":[{\"item_type\":0,\"id\":" + appId + ",\"success\":1,\"appid\":" + appId + tail + "}]}}";
    }

    public static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }

    // True once the given number of requests have reached the handler, false when they have not within the time (default five seconds).
    public async Task<bool> WaitForRequestsAsync(int count, CancellationToken cancellationToken, TimeSpan? within = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(within ?? TimeSpan.FromSeconds(5));
        while (true)
        {
            Task arrived;
            lock (_gate)
            {
                if (_requests.Count >= count)
                {
                    return true;
                }

                arrived = _arrived.Task;
            }

            try
            {
                await arrived.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _requests.Add(request);
            _inFlight++;
            MaxInFlight = Math.Max(MaxInFlight, _inFlight);
            _arrived.TrySetResult();
            _arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        try
        {
            return await Respond(request, cancellationToken);
        }
        finally
        {
            lock (_gate)
            {
                _inFlight--;
            }
        }
    }
}

// A body that is not there until it is read: a test that says it must never be read fails the moment anything touches it.
internal sealed class UnreadableStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new Xunit.Sdk.XunitException("The body was read.");

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

// Zeros without end, counting what was read, so a test can see that a reader gave up at its limit.
internal sealed class EndlessStream(long length) : Stream
{
    public long BytesRead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var take = (int)Math.Min(count, length - BytesRead);
        Array.Clear(buffer, offset, take);
        BytesRead += take;
        return take;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
