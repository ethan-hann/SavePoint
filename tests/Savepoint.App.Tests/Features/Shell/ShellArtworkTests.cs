using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;

namespace Savepoint.App.Tests.Features.Shell;

public class ShellArtworkTests
{
    private sealed class DisposableProvider : IArtworkProvider, IDisposable
    {
        private readonly ArtworkProvider _inner = new(FakeBackend.Mockup().Artwork, new ImmediateUiDispatcher());

        public bool Disposed { get; private set; }

        public GameArt For(string gameId) => _inner.For(gameId);

        public GameArt Preview(string gameId) => _inner.Preview(gameId);

        public void Refresh()
        {
        }

        public void Dispose()
        {
            Disposed = true;
            _inner.Dispose();
        }
    }

    private static ArtworkProvider ProviderFor(FakeBackend backend) => new(backend.Artwork, new ImmediateUiDispatcher());

    [Fact]
    public async Task Every_game_row_carries_the_artwork_the_provider_has_for_its_game()
    {
        var backend = FakeBackend.Mockup();
        using var provider = ProviderFor(backend);
        var rig = await ShellRig.StartAsync(backend, artwork: provider);

        Assert.NotEmpty(rig.Shell.Games);
        Assert.All(rig.Shell.Games, row => Assert.Same(provider.For(row.Id), row.Art));
        Assert.Equal(rig.Shell.Games.Count, rig.Shell.Games.Select(row => row.Art).Distinct().Count());
    }

    [Fact]
    public async Task Refreshing_the_rows_leaves_every_game_its_artwork_so_no_tile_goes_back_to_the_tint()
    {
        var backend = FakeBackend.Mockup();
        using var provider = ProviderFor(backend);
        var rig = await ShellRig.StartAsync(backend, artwork: provider);
        var before = rig.Shell.Games.ToDictionary(row => row.Id, row => (Row: row, row.Art));

        await rig.Shell.RefreshLibraryAsync();

        Assert.All(rig.Shell.Games, row =>
        {
            Assert.Same(before[row.Id].Row, row);
            Assert.Same(before[row.Id].Art, row.Art);
        });
    }

    [Fact]
    public async Task The_shell_offers_screens_the_provider_through_its_context()
    {
        var backend = FakeBackend.Mockup();
        using var provider = ProviderFor(backend);
        var factory = new RecordingScreenFactory(NavDestination.Games);
        var rig = await ShellRig.StartAsync(backend, screens: [factory], artwork: provider);

        Assert.Same(provider, rig.Shell.Artwork);
        Assert.Same(provider, factory.Context!.Artwork);
    }

    [Fact]
    public async Task A_game_the_artwork_api_has_nothing_for_gets_artwork_with_no_image()
    {
        var rig = await ShellRig.StartAsync();

        Assert.All(rig.Shell.Games, row =>
        {
            Assert.NotNull(row.Art);
            Assert.False(row.Art.HasArtwork);
        });
    }

    [Fact]
    public async Task Disposing_the_shell_disposes_the_provider_it_was_given()
    {
        var provider = new DisposableProvider();
        var rig = await ShellRig.StartAsync(artwork: provider);

        rig.Shell.Dispose();

        Assert.True(provider.Disposed);
    }

    [Fact]
    public async Task A_game_row_made_without_artwork_has_none()
    {
        var rig = await ShellRig.StartAsync();
        var row = new GameRowViewModel(rig.Shell.Games[0].Game, 1, 1, rig.Text);

        Assert.Null(row.Art);
    }
}
