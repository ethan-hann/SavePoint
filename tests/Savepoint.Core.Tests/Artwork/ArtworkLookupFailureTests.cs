using System.Net;
using Savepoint.Core.Api;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

// A lookup that fails for want of a connection leaves nothing behind but a log line: it is tried again, and art already kept stays.
public class ArtworkLookupFailureTests
{
    private const int _app = 489830;

    private static readonly byte[] _wide = TestImages.Jpeg(460, 215);
    private static readonly byte[] _square = TestImages.Jpeg(600, 900);

    private static Task<HttpResponseMessage> Healthy(HttpRequestMessage request, CancellationToken token) =>
        Task.FromResult(FakeHttpHandler.Jpeg(request.RequestUri!.AbsolutePath.EndsWith("header.jpg", StringComparison.Ordinal) ? _wide : _square));

    [Fact]
    public async Task A_lookup_that_failed_is_tried_again_in_the_same_run()
    {
        var up = false;
        using var rig = new ArtworkRig((request, token) => up ? Healthy(request, token) : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        var offline = await rig.LookedUpAsync(game.Id);
        up = true;
        var online = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.None, offline.Source);
        Assert.Equal(ArtworkSource.Steam, online.Source);
        Assert.Equal([game.Id], rig.Changes);
    }

    [Fact]
    public async Task A_lookup_that_could_not_connect_is_tried_again_in_the_same_run()
    {
        var up = false;
        using var rig = new ArtworkRig((request, token) => up ? Healthy(request, token) : throw new HttpRequestException("No such host is known."), online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        await rig.LookedUpAsync(game.Id);
        up = true;
        var online = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, online.Source);
    }

    [Fact]
    public async Task A_lookup_that_came_to_an_answer_is_not_repeated_by_the_next_read_in_the_run()
    {
        using var rig = new ArtworkRig(online: false);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        await rig.LookedUpAsync(game.Id);
        await rig.LookedUpAsync(game.Id);

        Assert.Equal(1, rig.Steam.Calls);
    }

    [Fact]
    public async Task A_lookup_with_no_connection_is_logged_with_the_game_the_app_and_the_reason()
    {
        var log = new RecordingDiagnosticLog();
        using var rig = new ArtworkRig((_, _) => throw new HttpRequestException("No such host is known."), online: true, log: log);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        await rig.LookedUpAsync(game.Id);

        var line = Assert.Single(log.Containing(game.Id, _app.ToString(System.Globalization.CultureInfo.InvariantCulture), "No such host is known."), entry => entry.Level == LogLevel.Warning);
        Assert.Contains("artwork", line.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_server_error_is_logged_with_the_status_it_sent()
    {
        var log = new RecordingDiagnosticLog();
        using var rig = new ArtworkRig((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)), online: true, log: log);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        await rig.LookedUpAsync(game.Id);

        Assert.Single(log.Containing(game.Id, "503"), entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task A_not_found_is_not_a_failure_and_writes_no_warning()
    {
        var log = new RecordingDiagnosticLog();
        using var rig = new ArtworkRig(
            (request, _) => Task.FromResult(FakeHttpHandler.IsStoreApi(request) ? FakeHttpHandler.Json(FakeHttpHandler.StoreItems(_app, null)) : FakeHttpHandler.NotFound()),
            online: true,
            log: log);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        await rig.LookedUpAsync(game.Id);

        Assert.DoesNotContain(log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains(game.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Art_that_was_kept_stays_when_no_app_id_can_be_found_for_the_game_any_more()
    {
        using var rig = new ArtworkRig(online: false);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        rig.PutSteamCache(_app);
        var kept = await rig.LookedUpAsync(game.Id);

        // The install drive is unplugged: nothing names an app id any more.
        await rig.UpdateGameAsync(game with { SteamAppId = null });
        var after = await rig.LookedUpAsync(game.Id);
        var again = await rig.Artwork.LookAgainAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, kept.Source);
        Assert.Equal(ArtworkSource.Steam, after.Source);
        Assert.Equal(kept.WidePath, after.WidePath);
        Assert.True(File.Exists(after.WidePath));
        Assert.Equal(ArtworkSource.Steam, again.Value!.Source);
    }

    [Fact]
    public async Task A_lookup_while_no_app_id_can_be_found_does_not_block_the_returning_id_from_finishing_its_art()
    {
        var squareUp = false;
        using var rig = new ArtworkRig(
            (request, token) => request.RequestUri!.AbsolutePath.EndsWith("header.jpg", StringComparison.Ordinal) || squareUp
                ? Healthy(request, token)
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            online: true);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });

        // Only the wide image arrives; the square one failed, which is not a miss.
        var partial = await rig.LookedUpAsync(game.Id);

        // The install drive is unplugged, so nothing names the app for a while, and then it is back with the square image available.
        await rig.UpdateGameAsync(game with { SteamAppId = null });
        await rig.LookedUpAsync(game.Id);
        await rig.UpdateGameAsync(game);
        squareUp = true;
        var complete = await rig.LookedUpAsync(game.Id);

        Assert.NotNull(partial.WidePath);
        Assert.Null(partial.SquarePath);
        Assert.NotNull(complete.SquarePath);
        Assert.True(File.Exists(complete.SquarePath));
    }
    [Fact]
    public async Task Art_kept_for_another_app_id_is_still_removed_when_the_game_gets_a_different_one()
    {
        using var rig = new ArtworkRig(online: false);
        var game = await rig.AddGameAsync(change: g => g with { SteamAppId = _app });
        rig.PutSteamCache(_app);
        var first = await rig.LookedUpAsync(game.Id);

        await rig.UpdateGameAsync(game with { SteamAppId = 777 });
        var other = await rig.LookedUpAsync(game.Id);

        Assert.Equal(ArtworkSource.Steam, first.Source);
        Assert.Equal(ArtworkSource.None, other.Source);
        Assert.False(File.Exists(first.WidePath));
    }
}
