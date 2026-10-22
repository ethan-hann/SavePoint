using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Common;

// Runs idle work only when a test says so, and optionally holds back ordinary posts too, so the order of events is the test's to pick.
internal sealed class ManualDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _posted = [];
    private readonly Queue<Action> _idle = [];

    public bool PostsImmediately { get; set; } = true;

    public int PostedPending => _posted.Count;

    public int IdlePending => _idle.Count;

    public void Post(Action action)
    {
        if (PostsImmediately)
        {
            action();
        }
        else
        {
            _posted.Enqueue(action);
        }
    }

    public void PostWhenIdle(Action action) => _idle.Enqueue(action);

    public void RunPosted()
    {
        while (_posted.TryDequeue(out var action))
        {
            action();
        }
    }

    public void RunIdle()
    {
        while (_idle.TryDequeue(out var action))
        {
            action();
        }
    }
}

// An artwork Api whose reads end when the test says, so reads can finish in any order.
internal sealed class HeldArtworkApi : IArtworkApi
{
    public event EventHandler<ArtworkChange>? ArtworkChanged;

    public List<TaskCompletionSource<OperationResult<GameArtwork>>> Reads { get; } = [];

    public Task<OperationResult<GameArtwork>> GetAsync(string gameId, CancellationToken cancellationToken = default)
    {
        var read = new TaskCompletionSource<OperationResult<GameArtwork>>();
        Reads.Add(read);
        return read.Task;
    }

    public void RaiseChanged(string gameId) => ArtworkChanged?.Invoke(this, new ArtworkChange(gameId));

    public Task<OperationResult<GameArtwork>> SetCustomImageAsync(string gameId, string imagePath) => throw new NotSupportedException();

    public Task<OperationResult<GameArtwork>> ClearCustomImageAsync(string gameId) => throw new NotSupportedException();

    public Task<OperationResult<GameArtwork>> LookAgainAsync(string gameId) => throw new NotSupportedException();
}

public class ArtworkProviderTests
{
    private readonly FakeBackend _backend = FakeBackend.Mockup();
    private readonly ManualDispatcher _dispatcher = new();

    private ArtworkProvider NewProvider() => new(_backend.Artwork, _dispatcher, new ArtImages());

    private string Game(int index = 0) => _backend.Peek(() => _backend.State.Games[index].Id);

    private int Reads(string? gameId = null) =>
        _backend.Calls.To<IArtworkApi>(nameof(IArtworkApi.GetAsync)).Count(call => gameId is null || (string?)call.Args[0] == gameId);

    [Fact]
    public void A_game_has_one_shared_artwork_and_a_preview_is_never_it()
    {
        using var provider = NewProvider();
        var id = Game();

        Assert.Same(provider.For(id), provider.For(id));
        Assert.NotSame(provider.For(id), provider.For(Game(1)));
        Assert.NotSame(provider.For(id), provider.Preview(id));
        Assert.NotSame(provider.Preview(id), provider.Preview(id));
    }

    [Fact]
    public void Nothing_is_read_from_the_api_until_a_tile_of_the_game_is_on_screen()
    {
        using var provider = NewProvider();

        provider.For(Game());
        provider.For(Game(1));
        _dispatcher.RunIdle();

        Assert.Equal(0, Reads());
    }

    [Fact]
    public void A_tile_on_screen_starts_one_read_and_only_after_everything_else_has_had_its_turn()
    {
        using var provider = NewProvider();
        var art = provider.For(Game());

        art.NotifyOnScreen();
        art.NotifyOnScreen();

        Assert.Equal(0, Reads());
        Assert.Equal(1, _dispatcher.IdlePending);
        _dispatcher.RunIdle();
        Assert.Equal(1, Reads(Game()));
        art.NotifyOnScreen();
        _dispatcher.RunIdle();
        Assert.Equal(1, Reads());
    }

    [Fact]
    public void What_the_api_reports_is_shown_on_the_games_artwork()
    {
        using var provider = NewProvider();
        var id = Game();
        _backend.Artwork.ScriptCustom(id, "/mine.png");
        var art = provider.For(id);

        art.NotifyOnScreen();
        _dispatcher.RunIdle();

        Assert.Equal(ArtworkSource.Custom, art.Source);
        Assert.Equal("/mine.png", art.WidePath);
        Assert.Equal("/mine.png", art.SquarePath);
    }

    [Fact]
    public void A_change_to_a_game_on_screen_is_read_again_and_counts_as_new_even_with_the_same_paths()
    {
        using var provider = NewProvider();
        var id = Game();
        _backend.Artwork.ScriptCustom(id, "/mine.png");
        var art = provider.For(id);
        art.NotifyOnScreen();
        _dispatcher.RunIdle();
        var revision = art.Revision;

        _backend.Artwork.ScriptChanged(id);

        Assert.Equal(2, Reads(id));
        Assert.Equal(revision + 1, art.Revision);
    }

    [Fact]
    public void Steam_artwork_that_arrives_later_appears_on_the_artwork_that_was_empty()
    {
        using var provider = NewProvider();
        var id = Game();
        var art = provider.For(id);
        art.NotifyOnScreen();
        _dispatcher.RunIdle();

        _backend.Artwork.ScriptSteamArrives(id, "/w.jpg", "/s.jpg");

        Assert.Equal(ArtworkSource.Steam, art.Source);
        Assert.Equal("/w.jpg", art.WidePath);
        Assert.Equal("/s.jpg", art.SquarePath);
    }

    [Fact]
    public void A_change_to_a_game_that_was_never_on_screen_is_not_read()
    {
        using var provider = NewProvider();
        provider.For(Game());

        _backend.Artwork.ScriptChanged(Game());

        Assert.Equal(0, Reads());
    }

    [Fact]
    public void A_change_is_handled_on_the_ui_thread_not_where_the_api_raised_it()
    {
        using var provider = NewProvider();
        var id = Game();
        var art = provider.For(id);
        art.NotifyOnScreen();
        _dispatcher.RunIdle();
        _dispatcher.PostsImmediately = false;

        _backend.Artwork.ScriptSteamArrives(id, "/w.jpg", null);

        Assert.False(art.HasArtwork);
        Assert.Equal(1, Reads());
        _dispatcher.RunPosted();
        Assert.True(art.HasArtwork);
    }

    [Fact]
    public void Refreshing_reads_every_game_on_screen_again_and_leaves_unchanged_artwork_alone()
    {
        using var provider = NewProvider();
        var first = provider.For(Game());
        var second = provider.For(Game(1));
        provider.For(Game(2));
        _backend.Artwork.ScriptCustom(Game(), "/mine.png");
        first.NotifyOnScreen();
        second.NotifyOnScreen();
        _dispatcher.RunIdle();
        var revision = first.Revision;

        provider.Refresh();

        Assert.Equal(2, Reads(Game()));
        Assert.Equal(2, Reads(Game(1)));
        Assert.Equal(0, Reads(Game(2)));
        Assert.Equal(revision, first.Revision);
    }

    [Fact]
    public void A_read_that_fails_leaves_the_artwork_as_it_was()
    {
        using var provider = NewProvider();
        var id = Game();
        _backend.Artwork.ScriptCustom(id, "/mine.png");
        var art = provider.For(id);
        art.NotifyOnScreen();
        _dispatcher.RunIdle();
        _backend.FailNext<IArtworkApi>(nameof(IArtworkApi.GetAsync), ErrorInfo.From(new GameNotFoundException(id)));

        _backend.Artwork.ScriptChanged(id);

        Assert.Equal(2, Reads(id));
        Assert.Equal(ArtworkSource.Custom, art.Source);
        Assert.Equal("/mine.png", art.WidePath);
    }

    [Fact]
    public void A_read_that_finishes_after_a_newer_one_does_not_replace_it()
    {
        var api = new HeldArtworkApi();
        using var provider = new ArtworkProvider(api, _dispatcher, new ArtImages());
        var art = provider.For("game");
        art.NotifyOnScreen();
        _dispatcher.RunIdle();
        api.RaiseChanged("game");
        Assert.Equal(2, api.Reads.Count);

        api.Reads[1].SetResult(OperationResult.Success(new GameArtwork("game", ArtworkSource.Custom, "/new.png", "/new.png")));
        api.Reads[0].SetResult(OperationResult.Success(new GameArtwork("game", ArtworkSource.Custom, "/old.png", "/old.png")));

        Assert.Equal("/new.png", art.WidePath);
    }

    [Fact]
    public void A_read_that_finishes_before_a_newer_one_is_shown_and_then_replaced()
    {
        var api = new HeldArtworkApi();
        using var provider = new ArtworkProvider(api, _dispatcher, new ArtImages());
        var art = provider.For("game");
        art.NotifyOnScreen();
        _dispatcher.RunIdle();
        api.RaiseChanged("game");

        api.Reads[0].SetResult(OperationResult.Success(new GameArtwork("game", ArtworkSource.Custom, "/old.png", "/old.png")));
        Assert.Equal("/old.png", art.WidePath);
        api.Reads[1].SetResult(OperationResult.Success(new GameArtwork("game", ArtworkSource.Custom, "/new.png", "/new.png")));

        Assert.Equal("/new.png", art.WidePath);
    }

    // The read a change asked for can be the one that is dropped; the newer read that is shown instead has to reload the images too.
    [Fact]
    public void A_dropped_change_read_hands_its_reload_to_the_newer_read_that_is_shown()
    {
        var api = new HeldArtworkApi();
        using var provider = new ArtworkProvider(api, _dispatcher, new ArtImages());
        var art = provider.For("game");
        art.NotifyOnScreen();
        _dispatcher.RunIdle();
        api.Reads[0].SetResult(OperationResult.Success(new GameArtwork("game", ArtworkSource.Custom, "/same.png", "/same.png")));
        var revision = art.Revision;
        api.RaiseChanged("game");
        provider.Refresh();
        Assert.Equal(3, api.Reads.Count);

        api.Reads[2].SetResult(OperationResult.Success(new GameArtwork("game", ArtworkSource.Custom, "/same.png", "/same.png")));
        api.Reads[1].SetResult(OperationResult.Success(new GameArtwork("game", ArtworkSource.Custom, "/same.png", "/same.png")));

        Assert.Equal(revision + 1, art.Revision);
    }

    [Fact]
    public void A_read_that_started_before_the_change_does_not_reload_the_images_and_the_change_read_does()
    {
        var api = new HeldArtworkApi();
        using var provider = new ArtworkProvider(api, _dispatcher, new ArtImages());
        var art = provider.For("game");
        art.NotifyOnScreen();
        _dispatcher.RunIdle();
        api.Reads[0].SetResult(OperationResult.Success(new GameArtwork("game", ArtworkSource.Custom, "/same.png", "/same.png")));
        var revision = art.Revision;
        provider.Refresh();
        api.RaiseChanged("game");
        Assert.Equal(3, api.Reads.Count);

        api.Reads[1].SetResult(OperationResult.Success(new GameArtwork("game", ArtworkSource.Custom, "/same.png", "/same.png")));
        Assert.Equal(revision, art.Revision);
        api.Reads[2].SetResult(OperationResult.Success(new GameArtwork("game", ArtworkSource.Custom, "/same.png", "/same.png")));

        Assert.Equal(revision + 1, art.Revision);
    }

    [Fact]
    public void An_older_read_still_counts_when_the_newer_one_failed()
    {
        var api = new HeldArtworkApi();
        using var provider = new ArtworkProvider(api, _dispatcher, new ArtImages());
        var art = provider.For("game");
        art.NotifyOnScreen();
        _dispatcher.RunIdle();
        api.RaiseChanged("game");

        api.Reads[1].SetResult(OperationResult.Failure<GameArtwork>(ErrorInfo.From(new GameNotFoundException("game"))));
        api.Reads[0].SetResult(OperationResult.Success(new GameArtwork("game", ArtworkSource.Custom, "/old.png", "/old.png")));

        Assert.Equal("/old.png", art.WidePath);
    }

    [Fact]
    public void A_preview_is_not_changed_by_the_api_and_shows_what_it_is_given()
    {
        using var provider = NewProvider();
        var id = Game();
        var preview = provider.Preview(id);
        preview.NotifyOnScreen();
        _dispatcher.RunIdle();

        preview.Show(new GameArtwork(id, ArtworkSource.Custom, "/chosen.png", "/chosen.png"));
        _backend.Artwork.ScriptSteamArrives(id, "/w.jpg", "/s.jpg");

        Assert.Equal("/chosen.png", preview.WidePath);
        Assert.Equal(0, Reads());
    }

    [Fact]
    public void After_it_is_disposed_the_provider_ignores_the_api()
    {
        var provider = NewProvider();
        var id = Game();
        var art = provider.For(id);
        art.NotifyOnScreen();
        _dispatcher.RunIdle();

        provider.Dispose();
        _backend.Artwork.ScriptSteamArrives(id, "/w.jpg", "/s.jpg");

        Assert.Equal(1, Reads());
        Assert.False(art.HasArtwork);
    }
}
