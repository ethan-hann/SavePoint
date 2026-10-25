using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// The reader answers "no entry is marked skip-worktree" from the index file alone, so it must never say so for a file it cannot fully
// account for: a wrong "none" would leave a hidden file out of every later save and comparison.
[Collection(EngineCollections.D)]
public sealed class IndexMarkScanTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    // Entries whose names share prefixes (version 4 stores a name as a cut of the previous one) and differ in length.
    private async Task<RepoRef> RepoAsync(string version)
    {
        var repo = await _ws.InitializedFreshAsync();
        foreach (var path in new[] { "Cache/a.bin", "Cache/b.bin", "Cache/deep/inner/c.bin", "Data/keep.txt", "Data/keep2.txt", "z.txt", "readme" })
        {
            _ws.Write(repo, path, path);
        }

        await _ws.CaptureAsync(repo, "profile-a", "A");
        await _ws.GitWriteAsync(repo, "update-index", "--index-version", version);
        return repo;
    }

    private static string IndexOf(RepoRef repo) => Path.Combine(repo.GitDir, "index");

    [Theory]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("4")]
    public async Task An_index_with_no_mark_is_proven_markless(string version)
    {
        var repo = await RepoAsync(version);

        Assert.False(IndexMarkScan.MayHoldMarks(IndexOf(repo)));
    }

    [Theory]
    [InlineData("2", "Cache/a.bin")]
    [InlineData("3", "Cache/a.bin")]
    [InlineData("4", "Cache/a.bin")]
    [InlineData("4", "Cache/deep/inner/c.bin")]
    [InlineData("4", "readme")]
    [InlineData("3", "z.txt")]
    public async Task A_marked_entry_is_found_in_every_version(string version, string marked)
    {
        var repo = await RepoAsync(version);
        await _ws.GitWriteAsync(repo, "update-index", "--skip-worktree", "--", marked);
        await _ws.GitWriteAsync(repo, "update-index", "--index-version", version);

        Assert.True(IndexMarkScan.MayHoldMarks(IndexOf(repo)));
    }

    [Fact]
    public async Task An_intent_to_add_entry_is_not_a_mark_and_does_not_hide_one()
    {
        var repo = await RepoAsync("4");
        _ws.Write(repo, "new.txt", "new");
        await _ws.GitWriteAsync(repo, "add", "--intent-to-add", "--", "new.txt");
        Assert.False(IndexMarkScan.MayHoldMarks(IndexOf(repo)));

        await _ws.GitWriteAsync(repo, "update-index", "--skip-worktree", "--", "readme");

        Assert.True(IndexMarkScan.MayHoldMarks(IndexOf(repo)));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("4")]
    public async Task A_cut_short_index_is_never_proven_markless(string version)
    {
        var repo = await RepoAsync(version);
        var bytes = File.ReadAllBytes(IndexOf(repo));
        var copy = Path.Combine(_ws.Root, "cut-index");

        for (var length = 0; length < bytes.Length; length++)
        {
            File.WriteAllBytes(copy, bytes[..length]);
            Assert.True(IndexMarkScan.MayHoldMarks(copy), $"cut to {length} of {bytes.Length} bytes");
        }
    }

    [Fact]
    public async Task A_damaged_header_or_a_missing_file_is_never_proven_markless()
    {
        var repo = await RepoAsync("4");
        var bytes = File.ReadAllBytes(IndexOf(repo));
        var copy = Path.Combine(_ws.Root, "damaged-index");

        foreach (var (offset, value) in new (int, byte)[] { (0, (byte)'X'), (7, (byte)5), (7, (byte)1), (11, (byte)200) })
        {
            var damaged = (byte[])bytes.Clone();
            damaged[offset] = value;
            File.WriteAllBytes(copy, damaged);
            Assert.True(IndexMarkScan.MayHoldMarks(copy), $"byte {offset} set to {value}");
        }

        Assert.True(IndexMarkScan.MayHoldMarks(Path.Combine(_ws.Root, "no-such-index")));
    }

    [Theory]
    [InlineData("link")]
    [InlineData("sdir")]
    public async Task A_split_or_sparse_index_is_never_proven_markless(string extension)
    {
        var repo = await RepoAsync("4");
        var copy = Path.Combine(_ws.Root, "extended-index");
        File.WriteAllBytes(copy, WithExtension(File.ReadAllBytes(IndexOf(repo)), extension, []));

        Assert.True(IndexMarkScan.MayHoldMarks(copy));
    }

    [Fact]
    public async Task An_unknown_optional_extension_is_skipped()
    {
        var repo = await RepoAsync("4");
        var copy = Path.Combine(_ws.Root, "extra-index");
        File.WriteAllBytes(copy, WithExtension(File.ReadAllBytes(IndexOf(repo)), "ZZZZ", [1, 2, 3]));

        Assert.False(IndexMarkScan.MayHoldMarks(copy));
    }

    [Fact]
    public async Task A_file_whose_checksum_does_not_match_is_never_proven_markless()
    {
        var repo = await RepoAsync("4");
        var bytes = File.ReadAllBytes(IndexOf(repo));
        bytes[40] ^= 0xFF;
        var copy = Path.Combine(_ws.Root, "changed-index");
        File.WriteAllBytes(copy, bytes);

        Assert.True(IndexMarkScan.MayHoldMarks(copy));
    }

    // The index with one more extension before the checksum, which is worked out again for the longer file.
    private static byte[] WithExtension(byte[] index, string name, byte[] body)
    {
        var size = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(size, (uint)body.Length);
        byte[] changed = [.. index[..^20], .. System.Text.Encoding.ASCII.GetBytes(name), .. size, .. body];
        return [.. changed, .. System.Security.Cryptography.SHA1.HashData(changed)];
    }
}
