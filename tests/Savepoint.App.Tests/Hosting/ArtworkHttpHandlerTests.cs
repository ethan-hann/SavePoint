using Savepoint.App.Features.Settings;
using Savepoint.App.Hosting;

namespace Savepoint.App.Tests.Hosting;

public class ArtworkHttpHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task A_request_names_the_app_and_its_version_and_carries_nothing_else_about_the_user()
    {
        var inner = new CapturingHandler();
        using var handler = new ArtworkHttpHandler(inner, "1.2.3");
        using var client = new HttpClient(handler);

        await client.GetAsync("https://example.test/art.jpg", TestContext.Current.CancellationToken);

        var request = inner.Requests.Single();
        Assert.Equal("SavePoint/1.2.3", request.Headers.UserAgent.ToString());
        Assert.Equal(["User-Agent"], request.Headers.Select(header => header.Key));
        Assert.Equal("SavePoint/1.2.3", handler.UserAgent);
    }

    [Fact]
    public void The_apps_handler_sends_the_apps_own_version()
    {
        using var handler = ArtworkHttpHandler.CreateForApp();

        Assert.Equal($"SavePoint/{SettingsEnvironment.ForThisApp().AppVersion}", handler.UserAgent);
        Assert.Matches(@"^SavePoint/\d+\.\d+", handler.UserAgent);
    }

    [Fact]
    public void The_apps_handler_follows_no_redirects_and_keeps_no_cookies()
    {
        using var handler = ArtworkHttpHandler.CreateForApp();

        var sockets = Assert.IsType<SocketsHttpHandler>(handler.InnerHandler);
        Assert.False(sockets.AllowAutoRedirect);
        Assert.False(sockets.UseCookies);
        Assert.Null(sockets.Credentials);
    }

    [Fact]
    public void The_apps_handler_gives_up_connecting_after_ten_seconds()
    {
        using var handler = ArtworkHttpHandler.CreateForApp();

        Assert.Equal(TimeSpan.FromSeconds(10), Assert.IsType<SocketsHttpHandler>(handler.InnerHandler).ConnectTimeout);
    }
}
