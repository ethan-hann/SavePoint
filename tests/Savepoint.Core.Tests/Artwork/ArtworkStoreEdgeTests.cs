using Savepoint.Core.Artwork;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Artwork;

// The edges of the artwork store: which ids name a folder, what may be written into it, and what a change does to the image that was
// there before when it fails.
public class ArtworkStoreEdgeTests : IDisposable
{
    // A choice that queues behind a held lease, and a delete that waits on one, show their state within moments; only waiting shows they stay put.
    private static readonly TimeSpan _queuedWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan _blockedWindow = TimeSpan.FromMilliseconds(500);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "savepoint-artstore-" + Guid.NewGuid().ToString("N"));

    public ArtworkStoreEdgeTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    private ArtworkStore NewStore() => new(Path.Combine(_root, "config"));

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("-leading-dash")]
    [InlineData("_leading-underscore")]
    [InlineData("has space")]
    [InlineData("trailing-newline\n")]
    [InlineData("dot.in.name")]
    public void An_id_the_catalog_would_refuse_never_names_a_folder(string id)
    {
        var store = NewStore();

        Assert.Throws<ArgumentException>(() => store.FolderOf(id));
    }

    [Theory]
    [InlineData("alpha-1")]
    [InlineData("Mixed_Case-2")]
    [InlineData("9lives")]
    public void An_id_the_catalog_accepts_names_a_folder_under_the_artwork_root(string id)
    {
        var store = NewStore();

        Assert.Equal(Path.Combine(store.Root, id), store.FolderOf(id));
    }

    [Fact]
    public void Deleting_a_game_with_an_id_the_catalog_would_refuse_deletes_nothing()
    {
        var store = NewStore();
        var neighbor = Path.Combine(store.Root, "-neighbor");
        Directory.CreateDirectory(neighbor);

        Assert.Throws<ArgumentException>(() => store.DeleteGame("-neighbor"));

        Assert.True(Directory.Exists(neighbor));
    }

    [Fact]
    public void Bytes_that_are_not_an_image_are_never_kept_as_a_steam_image()
    {
        var store = NewStore();

        var kept = store.SaveSteamImage("alpha-1", ArtworkKind.Wide, [1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.False(kept);
        Assert.Null(store.SteamPath("alpha-1", ArtworkKind.Wide));
        Assert.False(Directory.Exists(store.FolderOf("alpha-1")));
    }

    [Fact]
    public void Bytes_of_a_whole_image_are_kept_as_a_steam_image()
    {
        var store = NewStore();
        var bytes = TestImages.Jpeg(460, 215);

        var kept = store.SaveSteamImage("alpha-1", ArtworkKind.Wide, bytes);

        Assert.True(kept);
        Assert.Equal(bytes, File.ReadAllBytes(store.SteamPath("alpha-1", ArtworkKind.Wide)!));
    }

    [Fact]
    public void A_new_image_of_another_format_replaces_the_old_one_and_leaves_one_custom_image()
    {
        var store = NewStore();
        store.SaveCustom("alpha-1", TestImages.Write(_root, "old.jpg", TestImages.Jpeg(64, 64)));

        var saved = store.SaveCustom("alpha-1", TestImages.Write(_root, "new.png", TestImages.Png(64, 64)));

        Assert.EndsWith("custom.png", saved, StringComparison.Ordinal);
        Assert.Equal(saved, store.CustomPath("alpha-1"));
        Assert.Equal([saved], Directory.EnumerateFiles(store.FolderOf("alpha-1")));
    }

    [Fact]
    public void An_image_that_cannot_be_moved_into_place_leaves_the_one_that_was_there()
    {
        var store = NewStore();
        var old = store.SaveCustom("alpha-1", TestImages.Write(_root, "old.jpg", TestImages.Jpeg(64, 64)));
        var oldBytes = File.ReadAllBytes(old);

        // A folder where the new file has to go makes the move fail after the copy was judged good.
        Directory.CreateDirectory(Path.Combine(store.FolderOf("alpha-1"), "custom.png"));
        Assert.ThrowsAny<Exception>(() => store.SaveCustom("alpha-1", TestImages.Write(_root, "new.png", TestImages.Png(64, 64))));

        Assert.True(File.Exists(old));
        Assert.Equal(oldBytes, File.ReadAllBytes(old));
        Assert.Equal(old, store.CustomPath("alpha-1"));
    }

    [Fact]
    public async Task Choosing_an_image_for_a_game_that_was_deleted_while_the_choice_waited_creates_no_folder()
    {
        using var rig = new ArtworkRig();
        var game = await rig.AddGameAsync();
        var store = rig.Workspace.Backend.Services.Artwork;
        var source = TestImages.Write(_root, "mine.png", TestImages.Png(64, 64));

        // Another change to the game holds its lease; the choice waits behind it, and the game is deleted meanwhile.
        var lease = await store.EnterAsync(game.Id);
        var choosing = rig.Artwork.SetCustomImageAsync(game.Id, source);
        await Task.Delay(_queuedWindow, rig.Token);
        await rig.Workspace.Backend.Services.Catalog.DeleteGameAsync(game.Id, rig.Token);
        lease.Dispose();
        var result = await choosing;

        Assert.False(result.Ok);
        Assert.Equal("error.game.notFound", result.Error!.MessageId);
        Assert.False(Directory.Exists(rig.ArtworkFolder(game.Id)));
    }

    [Fact]
    public async Task Removing_all_artwork_waits_for_a_change_that_is_under_way_to_finish()
    {
        var store = NewStore();
        store.SaveCustom("alpha-1", TestImages.Write(_root, "mine.png", TestImages.Png(64, 64)));
        var lease = await store.EnterAsync("alpha-1");

        // The removal is known to be running before it is given time to finish, so a slow start cannot make the wait look like it held.
        using var started = new ManualResetEventSlim();
        var removing = Task.Run(() =>
        {
            started.Set();
            store.DeleteAll();
        }, TestContext.Current.CancellationToken);
        Assert.True(started.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        var finishedEarly = await Task.WhenAny(removing, Task.Delay(_blockedWindow, TestContext.Current.CancellationToken)) == removing;
        lease.Dispose();
        await removing.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.False(finishedEarly);
        Assert.False(Directory.Exists(store.Root));
    }
}
