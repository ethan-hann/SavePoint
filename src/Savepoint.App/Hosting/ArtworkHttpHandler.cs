using System.Net.Http.Headers;
using Savepoint.App.Features.Settings;

namespace Savepoint.App.Hosting;

/// <summary>
/// The only handler game artwork is fetched through (D30). It sends the app's own name and version as the user agent and nothing
/// else about the user, follows no redirects, and keeps no cookies; the artwork lookup refuses any answer that is not a plain 200.
/// </summary>
public sealed class ArtworkHttpHandler : DelegatingHandler
{
    private readonly ProductInfoHeaderValue _product;

    /// <summary>Wraps a handler so every request names the app.</summary>
    /// <param name="inner">The handler that sends the request.</param>
    /// <param name="version">The app version sent as <c>SavePoint/&lt;version&gt;</c>.</param>
    public ArtworkHttpHandler(HttpMessageHandler inner, string version)
        : base(inner)
    {
        _product = ProductInfoHeaderValue.TryParse($"SavePoint/{version}", out var parsed) ? parsed : new ProductInfoHeaderValue("SavePoint");
    }

    /// <summary>The user agent every request carries.</summary>
    public string UserAgent => _product.ToString();

    /// <summary>The handler the running app uses: a socket handler that follows no redirects and keeps no cookies, with the app's version.</summary>
    public static ArtworkHttpHandler CreateForApp() =>
        new(
            new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            },
            SettingsEnvironment.ThisAppVersion());

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.Add(_product);
        return base.SendAsync(request, cancellationToken);
    }
}
