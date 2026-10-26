using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

// A big file is read in pieces, and a cancelled call must not wait for the rest of it.
public sealed class BlobHasherCancellationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "savepoint-hasher-" + Guid.NewGuid().ToString("N"));

    public BlobHasherCancellationTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string BigFile()
    {
        var path = Path.Combine(_folder, "big.bin");
        File.WriteAllBytes(path, new byte[1024 * 1024]);
        return path;
    }

    [Fact]
    public void A_cancelled_token_stops_the_read_before_the_file_is_hashed()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => BlobHasher.TryHash(BigFile(), out _, cancel.Token));
    }

    [Fact]
    public void Without_a_cancelled_token_the_file_is_hashed_as_before()
    {
        var outcome = BlobHasher.TryHash(BigFile(), out var id, CancellationToken.None);

        Assert.Equal(HashOutcome.Hashed, outcome);
        Assert.Equal(40, id.Length);
    }
}
