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

    [Fact]
    public async Task A_split_index_made_by_git_is_never_proven_markless()
    {
        var repo = await RepoAsync("4");
        await _ws.GitWriteAsync(repo, "update-index", "--split-index");
        Assert.True(Has(File.ReadAllBytes(IndexOf(repo)), "link"u8));

        Assert.True(IndexMarkScan.MayHoldMarks(IndexOf(repo)));
    }

    [Fact]
    public async Task A_sparse_index_made_by_git_is_never_proven_markless()
    {
        var repo = await RepoAsync("4");
        await _ws.GitWriteAsync(repo, "sparse-checkout", "set", "--cone", "--sparse-index", "Data");
        Assert.True(Has(File.ReadAllBytes(IndexOf(repo)), "sdir"u8));

        Assert.True(IndexMarkScan.MayHoldMarks(IndexOf(repo)));
    }

    [Fact]
    public async Task An_extension_git_requires_and_this_reader_does_not_know_is_never_skipped()
    {
        var repo = await RepoAsync("4");
        var copy = Path.Combine(_ws.Root, "required-index");
        File.WriteAllBytes(copy, WithExtension(File.ReadAllBytes(IndexOf(repo)), "abcd", [1, 2, 3]));

        Assert.True(IndexMarkScan.MayHoldMarks(copy));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_repository_with_32_byte_object_ids_is_never_proven_markless(bool skipHash)
    {
        var folder = Path.Combine(_ws.Root, "sha256-repo");
        Directory.CreateDirectory(Path.Combine(folder, "Cache"));
        File.WriteAllText(Path.Combine(folder, "Cache", "a.bin"), "a");
        File.WriteAllText(Path.Combine(folder, "b.txt"), "b");
        Task<GitResult> Git(params string[] arguments) => _ws.Runner.RunAsync(new GitInvocation(arguments) { WorkingDirectory = folder }, Ct);
        await Git("init", "--object-format=sha256", ".");
        if (skipHash)
        {
            await Git("config", "index.skipHash", "true");
        }

        await Git("add", "-A");
        Assert.Equal("sha256", (await Git("rev-parse", "--show-object-format")).StandardOutput.Trim());

        Assert.True(IndexMarkScan.MayHoldMarks(Path.Combine(folder, ".git", "index")));
    }

    [Fact]
    public async Task An_index_written_without_a_checksum_is_never_proven_markless()
    {
        var repo = await RepoAsync("4");
        await _ws.GitWriteAsync(repo, "config", "index.skipHash", "true");
        await _ws.GitWriteAsync(repo, "update-index", "--index-version", "3");
        await _ws.GitWriteAsync(repo, "update-index", "--index-version", "4");
        Assert.All(File.ReadAllBytes(IndexOf(repo))[^20..], b => Assert.Equal(0, b));

        Assert.True(IndexMarkScan.MayHoldMarks(IndexOf(repo)));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("4")]
    public async Task A_zero_filled_tail_is_never_proven_markless(string version)
    {
        var repo = await RepoAsync(version);
        var bytes = File.ReadAllBytes(IndexOf(repo));
        var copy = Path.Combine(_ws.Root, "zeroed-index");

        for (var from = 12; from < bytes.Length; from += 7)
        {
            var zeroed = (byte[])bytes.Clone();
            Array.Clear(zeroed, from, zeroed.Length - from);
            File.WriteAllBytes(copy, zeroed);
            Assert.True(IndexMarkScan.MayHoldMarks(copy), $"zeroed from {from} of {bytes.Length}");
        }
    }

    [Theory]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("4")]
    public async Task A_name_of_4095_bytes_or_more_is_walked_to_the_end(string version)
    {
        var repo = await RepoAsync(version);
        var blob = (await _ws.GitAsync(repo, "rev-parse", "profile-a:readme")).StandardOutput.Trim();
        var entries = new System.Text.StringBuilder();
        foreach (var name in new[] { "Long/" + new string('a', 5000), "Long/" + new string('a', 5000) + "b", "Long/" + new string('b', 4090) })
        {
            entries.Append("100644 ").Append(blob).Append('\t').Append(name).Append('\0');
        }

        await _ws.Runner.RunAsync(new GitInvocation(["update-index", "-z", "--add", "--index-info"]) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, Stdin = entries.ToString() }, Ct);
        await _ws.GitWriteAsync(repo, "update-index", "--index-version", version);
        Assert.Contains((await _ws.GitAsync(repo, "ls-files")).StandardOutput.Split('\n'), line => line.Length > 4095);

        Assert.False(IndexMarkScan.MayHoldMarks(IndexOf(repo)));
    }

    // The checksum is worked out again after each change, so only the entry itself can give the damage away.
    [Theory]
    [InlineData("2", 72, 1)]
    [InlineData("4", 72, 1)]
    [InlineData("4", 74, 5)]
    public async Task An_entry_whose_name_disagrees_with_its_recorded_length_or_cut_is_never_proven_markless(string version, int offset, int add)
    {
        var repo = await RepoAsync(version);
        var folder = Path.Combine(_ws.Root, "renamed");
        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(repo.GitDir, "config"), Path.Combine(folder, "config"));
        var bytes = File.ReadAllBytes(IndexOf(repo));
        var copy = Path.Combine(folder, "index");
        File.WriteAllBytes(copy, bytes);
        Assert.False(IndexMarkScan.MayHoldMarks(copy));

        bytes[offset] += (byte)add;
        File.WriteAllBytes(copy, [.. bytes[..^20], .. System.Security.Cryptography.SHA1.HashData(bytes[..^20])]);

        Assert.True(IndexMarkScan.MayHoldMarks(copy));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool Has(byte[] bytes, ReadOnlySpan<byte> name) => bytes.AsSpan().IndexOf(name) >= 0;

    [Fact]
    public async Task An_unknown_optional_extension_is_skipped()
    {
        var repo = await RepoAsync("4");
        var folder = Path.Combine(_ws.Root, "extra");
        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(repo.GitDir, "config"), Path.Combine(folder, "config"));
        var copy = Path.Combine(folder, "index");
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
