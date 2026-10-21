using Savepoint.Core.Services.Exe;

namespace Savepoint.Core.Tests.Services.Exe;

public class ExeVersionCacheTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_content_id_is_read_once_and_remembered()
    {
        var cache = new ExeVersionCache();
        var reads = 0;
        Task<string?> Read(CancellationToken _)
        {
            reads++;
            return Task.FromResult<string?>("1.6.640");
        }

        var first = await cache.GetOrReadAsync("blob-a", Read, Ct);
        var second = await cache.GetOrReadAsync("blob-a", Read, Ct);

        Assert.Equal("1.6.640", first);
        Assert.Equal("1.6.640", second);
        Assert.Equal(1, reads);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task Different_content_ids_are_read_separately()
    {
        var cache = new ExeVersionCache();

        var a = await cache.GetOrReadAsync("blob-a", _ => Task.FromResult<string?>("1.0"), Ct);
        var b = await cache.GetOrReadAsync("blob-b", _ => Task.FromResult<string?>("2.0"), Ct);

        Assert.Equal("1.0", a);
        Assert.Equal("2.0", b);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public async Task A_file_with_no_version_is_remembered_too()
    {
        var cache = new ExeVersionCache();
        var reads = 0;
        Task<string?> Read(CancellationToken _)
        {
            reads++;
            return Task.FromResult<string?>(null);
        }

        Assert.Null(await cache.GetOrReadAsync("blob-a", Read, Ct));
        Assert.Null(await cache.GetOrReadAsync("blob-a", Read, Ct));

        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task A_failed_read_is_reported_and_not_remembered()
    {
        var cache = new ExeVersionCache();

        await Assert.ThrowsAsync<IOException>(() => cache.GetOrReadAsync("blob-a", _ => throw new IOException("blob unreadable"), Ct));
        var retried = await cache.GetOrReadAsync("blob-a", _ => Task.FromResult<string?>("3.1"), Ct);

        Assert.Equal("3.1", retried);
    }

    [Fact]
    public async Task The_token_reaches_the_read_and_a_cancelled_one_reads_nothing()
    {
        var cache = new ExeVersionCache();
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        await cache.GetOrReadAsync("blob-a", token =>
        {
            seen = token;
            return Task.FromResult<string?>("1.0");
        }, cts.Token);
        await cts.CancelAsync();
        var reads = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrReadAsync("blob-b", _ =>
        {
            reads++;
            return Task.FromResult<string?>("2.0");
        }, cts.Token));

        Assert.Equal(cts.Token, seen);
        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task An_empty_content_id_is_refused()
    {
        var cache = new ExeVersionCache();

        await Assert.ThrowsAsync<ArgumentException>(() => cache.GetOrReadAsync(string.Empty, _ => Task.FromResult<string?>("1.0"), Ct));
    }
}
