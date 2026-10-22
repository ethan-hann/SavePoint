using Savepoint.Core.Api;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

// With the setting off the app makes no request. A settings file that cannot be read cannot say it is on, so it counts as off.
public class ArtworkPrivacyTests
{
    private const int _app = 489830;

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> ServeImages() =>
        (request, _) => Task.FromResult(FakeHttpHandler.Jpeg(request.RequestUri!.AbsolutePath.EndsWith("header.jpg", StringComparison.Ordinal) ? TestImages.Jpeg(460, 215) : TestImages.Jpeg(600, 900)));

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("null")]
    [InlineData("")]
    public async Task A_settings_file_that_cannot_be_read_makes_no_request_to_steam(string content)
    {
        using var rig = new ArtworkRig(ServeImages(), online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        await File.WriteAllTextAsync(Path.Combine(rig.Workspace.CatalogDirectory, "settings.json"), content, rig.Token);

        var art = await rig.LookedUpAsync(game.Id);
        var again = await rig.Artwork.LookAgainAsync(game.Id);

        Assert.Empty(rig.Http!.Requests);
        Assert.Equal(ArtworkSource.None, art.Source);
        Assert.Equal(ArtworkSource.None, again.Value!.Source);
    }

    [Fact]
    public async Task A_settings_file_that_cannot_be_read_still_uses_what_the_steam_client_has_on_this_computer()
    {
        using var rig = new ArtworkRig(ServeImages(), online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        rig.PutSteamCache(_app);
        await File.WriteAllTextAsync(Path.Combine(rig.Workspace.CatalogDirectory, "settings.json"), "{ broken", rig.Token);

        var art = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, art.Source);
        Assert.Empty(rig.Http!.Requests);
    }
}
