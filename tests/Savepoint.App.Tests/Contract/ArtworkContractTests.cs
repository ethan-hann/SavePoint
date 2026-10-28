using Savepoint.Core.Api;
using Savepoint.Testing;

namespace Savepoint.App.Tests.Contract;

// Artwork is told the same way by the fake every screen test is written against and by the real backend, over a fake Steam folder.
public class ArtworkContractTests(ArtworkContractRuns runs) : IClassFixture<ArtworkContractRuns>
{
    [Fact]
    public void The_real_backend_tells_the_same_artwork_story_as_the_fake()
    {
        var fake = runs.Fake;
        var real = runs.Real;

        var differences = fake.Zip(real, (fakeSaw, realSaw) => (fakeSaw, realSaw))
            .Where(pair => pair.fakeSaw != pair.realSaw)
            .Select(pair => $"fake: {pair.fakeSaw}{Environment.NewLine}real: {pair.realSaw}")
            .ToList();
        Assert.True(differences.Count == 0 && fake.Count == real.Count, string.Join(Environment.NewLine + Environment.NewLine, differences));
    }

    // A story that failed the same way in both backends would still match, so the steps that prove it ran are named here.
    [Fact]
    public void The_story_plays_the_whole_artwork_life()
    {
        foreach (var story in new[] { runs.Fake, runs.Real })
        {
            Assert.Contains("get before anything: None wide=none square=none id=none", story);
            Assert.Contains("choose an image: Custom same=True exists=True id=none", story);
            Assert.Contains("choose a page: error error.artwork.notAnImage", story);
            Assert.Contains("choose a missing file: error error.artwork.notAnImage", story);
            Assert.Contains("choose a huge file: error error.artwork.tooLarge", story);
            Assert.Contains("remove the image: None wide=none square=none id=none", story);
            Assert.Contains("look again: Steam wide=True square=True distinct=True id=489830 detected=False", story);
            Assert.Contains("the image beats Steam: Custom same=True exists=True id=489830", story);
            Assert.Contains("remove it again: Steam wide=True square=True distinct=True id=489830 detected=False", story);
            Assert.Contains("an unknown game: get error.game.notFound, set error.game.notFound, clear error.game.notFound, look error.game.notFound", story);
            Assert.Contains("get after deleting the game: error error.game.notFound", story);
        }
    }
}

// The scenario runs once against each backend and both tests read the transcripts.
public sealed class ArtworkContractRuns : IAsyncLifetime
{
    internal IReadOnlyList<string> Fake { get; private set; } = [];

    internal IReadOnlyList<string> Real { get; private set; } = [];

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fake = new FakeContractHarness();
        Fake = await RunAsync(fake, token);
        await using var real = new RealContractHarness();
        Real = await RunAsync(real, token);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<IReadOnlyList<string>> RunAsync(ContractHarness backend, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var changes = new List<string>();
        backend.Artwork.ArtworkChanged += (_, change) => changes.Add(change.GameId);
        void Add(string step, string seen) => lines.Add($"{step}: {seen}");

        // The fake delivers events in the background, so they are counted only once they have all arrived.
        async Task<string> EventCountAsync()
        {
            await backend.EventsDeliveredAsync();
            return changes.Count.ToString();
        }

        var game = backend.NewGame("Alpha");
        var added = await backend.Games.AddAsync(game);
        Add("add the game", added.Ok ? "added" : "error " + added.Error.MessageId);

        Add("get before anything", Describe(await backend.Artwork.GetAsync(game.Id, cancellationToken)));
        await backend.ArtworkLookupDoneAsync(game.Id, cancellationToken);
        var png = TestImages.Write(backend.Root, "mine.png", TestImages.Png(300, 200));
        Add("choose an image", Describe(await backend.Artwork.SetCustomImageAsync(game.Id, png)));
        Add("events after choosing", await EventCountAsync());

        var page = TestImages.Write(backend.Root, "page.png", TestImages.Html());
        Add("choose a page", Describe(await backend.Artwork.SetCustomImageAsync(game.Id, page)));
        Add("choose a missing file", Describe(await backend.Artwork.SetCustomImageAsync(game.Id, Path.Combine(backend.Root, "missing.png"))));
        var huge = Path.Combine(backend.Root, "huge.jpg");
        using (var stream = File.Create(huge))
        {
            stream.SetLength((25L * 1024 * 1024) + 1);
        }

        Add("choose a huge file", Describe(await backend.Artwork.SetCustomImageAsync(game.Id, huge)));
        Add("still the first image", Describe(await backend.Artwork.GetAsync(game.Id, cancellationToken)));
        Add("events after the refusals", await EventCountAsync());

        Add("remove the image", Describe(await backend.Artwork.ClearCustomImageAsync(game.Id)));
        Add("remove it when there is none", Describe(await backend.Artwork.ClearCustomImageAsync(game.Id)));
        Add("events after removing", await EventCountAsync());

        var withId = game with { SteamAppId = 489830 };
        var updated = await backend.Games.UpdateAsync(withId);
        Add("store the steam app id", updated.Ok ? "updated" : "error " + updated.Error.MessageId);
        backend.SteamHasArt(game, 489830);
        Add("look again", Describe(await backend.Artwork.LookAgainAsync(game.Id)));
        Add("events after looking", await EventCountAsync());
        Add("get after looking", Describe(await backend.Artwork.GetAsync(game.Id, cancellationToken)));

        Add("the image beats Steam", Describe(await backend.Artwork.SetCustomImageAsync(game.Id, png)));
        Add("remove it again", Describe(await backend.Artwork.ClearCustomImageAsync(game.Id)));
        Add("events at the end", await EventCountAsync());
        Add("every event named the game", changes.All(id => id == game.Id).ToString());

        Add("an unknown game", string.Join(
            ", ",
            Brief("get", await backend.Artwork.GetAsync("no-such-game", cancellationToken)),
            Brief("set", await backend.Artwork.SetCustomImageAsync("no-such-game", png)),
            Brief("clear", await backend.Artwork.ClearCustomImageAsync("no-such-game")),
            Brief("look", await backend.Artwork.LookAgainAsync("no-such-game"))));

        var deleted = await backend.Games.DeleteAsync(game.Id);
        Add("delete the game", deleted.Ok ? "deleted" : "error " + deleted.Error.MessageId);
        var after = await backend.Artwork.GetAsync(game.Id, cancellationToken);
        Add("get after deleting the game", after.Ok ? Describe(after) : "error " + after.Error.MessageId);
        return lines;
    }

    // Paths differ between backends (the real one copies the image in), so only what they say about the files is compared.
    private static string Describe(OperationResult<GameArtwork> result)
    {
        if (!result.Ok)
        {
            return "error " + result.Error.MessageId;
        }

        var art = result.Value;
        var shape = art.Source == ArtworkSource.Custom
            ? $"same={art.WidePath == art.SquarePath} exists={art.WidePath is not null && File.Exists(art.WidePath)}"
            : $"wide={(art.WidePath is null ? "none" : File.Exists(art.WidePath).ToString())} square={(art.SquarePath is null ? "none" : File.Exists(art.SquarePath).ToString())}";
        var distinct = art.Source == ArtworkSource.Steam ? $" distinct={art.WidePath != art.SquarePath}" : string.Empty;
        var detected = art.Source == ArtworkSource.Steam ? $" detected={art.SteamAppIdDetected}" : string.Empty;
        return $"{art.Source} {shape}{distinct} id={(art.SteamAppId is { } id ? id.ToString() : "none")}{detected}";
    }

    private static string Brief(string name, OperationResult<GameArtwork> result) =>
        $"{name} {(result.Ok ? "ok" : result.Error.MessageId)}";
}
