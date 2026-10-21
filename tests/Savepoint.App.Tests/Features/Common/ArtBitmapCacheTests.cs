using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common.Art;

namespace Savepoint.App.Tests.Features.Common;

public class ArtBitmapCacheTests
{
    private static ArtKey Key(string name, int revision = 0) => new(name, revision, 40, 40, 100);

    [AvaloniaFact]
    public void A_bitmap_added_is_found_again()
    {
        var cache = new ArtBitmapCache(4);
        var bitmap = ArtFixtures.TinyBitmap();

        cache.Add(Key("a"), bitmap);

        Assert.True(cache.TryGet(Key("a"), out var found));
        Assert.Same(bitmap, found);
        Assert.Equal(1, cache.Count);
    }

    [AvaloniaFact]
    public void A_key_that_differs_in_any_part_is_a_different_entry()
    {
        var cache = new ArtBitmapCache(8);
        cache.Add(Key("a"), ArtFixtures.TinyBitmap());

        Assert.False(cache.TryGet(Key("b"), out _));
        Assert.False(cache.TryGet(Key("a", revision: 1), out _));
        Assert.False(cache.TryGet(Key("a") with { Width = 41 }, out _));
        Assert.False(cache.TryGet(Key("a") with { Height = 41 }, out _));
        Assert.False(cache.TryGet(Key("a") with { ScalePercent = 150 }, out _));
    }

    [AvaloniaFact]
    public void Beyond_its_capacity_the_least_recently_used_bitmap_is_dropped()
    {
        var cache = new ArtBitmapCache(3);
        foreach (var name in new[] { "a", "b", "c" })
        {
            cache.Add(Key(name), ArtFixtures.TinyBitmap());
        }

        cache.Add(Key("d"), ArtFixtures.TinyBitmap());

        Assert.Equal(3, cache.Count);
        Assert.False(cache.TryGet(Key("a"), out _));
        Assert.True(cache.TryGet(Key("b"), out _));
        Assert.True(cache.TryGet(Key("c"), out _));
        Assert.True(cache.TryGet(Key("d"), out _));
    }

    [AvaloniaFact]
    public void Reading_a_bitmap_counts_as_using_it()
    {
        var cache = new ArtBitmapCache(3);
        foreach (var name in new[] { "a", "b", "c" })
        {
            cache.Add(Key(name), ArtFixtures.TinyBitmap());
        }

        Assert.True(cache.TryGet(Key("a"), out _));
        cache.Add(Key("d"), ArtFixtures.TinyBitmap());

        Assert.True(cache.TryGet(Key("a"), out _));
        Assert.False(cache.TryGet(Key("b"), out _));
    }

    [AvaloniaFact]
    public void Adding_the_same_key_again_replaces_the_bitmap_without_growing()
    {
        var cache = new ArtBitmapCache(3);
        var first = ArtFixtures.TinyBitmap();
        var second = ArtFixtures.TinyBitmap();
        cache.Add(Key("a"), first);

        cache.Add(Key("a"), second);

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet(Key("a"), out var found));
        Assert.Same(second, found);
    }

    [AvaloniaFact]
    public async Task Many_threads_adding_and_reading_never_pass_the_capacity()
    {
        var cache = new ArtBitmapCache(8);
        var bitmap = ArtFixtures.TinyBitmap();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                cache.Add(Key($"{worker}-{i % 20}"), bitmap);
                cache.TryGet(Key($"{worker}-{(i + 3) % 20}"), out _);
            }
        }, TestContext.Current.CancellationToken)));

        Assert.InRange(cache.Count, 1, 8);
    }

    [AvaloniaFact]
    public void A_capacity_below_one_still_keeps_one_bitmap()
    {
        var cache = new ArtBitmapCache(0);

        cache.Add(Key("a"), ArtFixtures.TinyBitmap());

        Assert.Equal(1, cache.Count);
    }
}
