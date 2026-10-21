using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Two points built once and only read: the second changes one file, deletes one and adds one.
public sealed class ContentFixture : IAsyncLifetime
{
    public static readonly byte[] BinaryBytes = [0xff, 0xfe, 0x00, 0x80, 0xc0, 0x01, 0x4d, 0x5a];

    internal EngineWorkspace Workspace { get; } = new();

    public RepoRef Repo { get; private set; } = null!;

    public string First { get; private set; } = string.Empty;

    public string Second { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        Repo = await Workspace.InitializedRepoAsync();
        Workspace.Write(Repo, "changed.txt", "0123456789");
        Workspace.Write(Repo, "deleted.txt", "going away");
        Workspace.Write(Repo, "same.txt", "never changes");
        Workspace.Write(Repo, "bin.dat", BinaryBytes);
        Workspace.Write(Repo, "mods/sub dir/caf\u00e9.esp", "accent");
        First = (await Workspace.CaptureAsync(Repo, "profile-1", "First")).TipCommitId!;

        Workspace.Write(Repo, "changed.txt", new string('x', 40));
        Workspace.Delete(Repo, "deleted.txt");
        Workspace.Write(Repo, "new.txt", "12345");
        Workspace.Write(Repo, "mods/sub dir/caf\u00e9.esp", "accent, changed");
        Second = (await Workspace.CaptureAsync(Repo, "profile-1", "Second")).TipCommitId!;
    }

    public ValueTask DisposeAsync()
    {
        Workspace.Dispose();
        return ValueTask.CompletedTask;
    }
}

[Collection(EngineCollections.F)]
public sealed class ContentReadTests : IClassFixture<ContentFixture>
{
    private readonly ContentFixture _fx;

    public ContentReadTests(ContentFixture fixture)
    {
        _fx = fixture;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GitEngine Engine => _fx.Workspace.Engine;

    [Fact]
    public async Task The_same_revision_twice_changes_nothing()
    {
        var diff = await Engine.DiffStatAsync(_fx.Repo, _fx.First, _fx.First, Ct);

        Assert.Equal(0, diff.FilesChanged);
        Assert.Equal(0, diff.BytesAtTarget);
        Assert.Empty(diff.ChangedPaths);
    }

    [Fact]
    public async Task A_diff_counts_changed_added_and_removed_files_and_sizes_them_at_the_target()
    {
        var diff = await Engine.DiffStatAsync(_fx.Repo, _fx.First, _fx.Second, Ct);

        Assert.Equal(4, diff.FilesChanged);
        Assert.Equal(40 + 5 + "accent, changed".Length, diff.BytesAtTarget);
        Assert.Equal(["changed.txt", "deleted.txt", "mods/sub dir/caf\u00e9.esp", "new.txt"], diff.ChangedPaths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_removed_file_counts_as_changed_with_no_bytes()
    {
        var diff = await Engine.DiffStatAsync(_fx.Repo, _fx.Second, _fx.First, Ct);

        Assert.Contains("new.txt", diff.ChangedPaths);
        Assert.Equal(10 + "going away".Length + "accent".Length, diff.BytesAtTarget);
    }

    [Fact]
    public async Task Revisions_may_be_branches_as_well_as_commit_ids()
    {
        var byBranch = await Engine.DiffStatAsync(_fx.Repo, _fx.First, "profile-1", Ct);
        var byId = await Engine.DiffStatAsync(_fx.Repo, _fx.First, _fx.Second, Ct);

        Assert.Equal(byId.FilesChanged, byBranch.FilesChanged);
        Assert.Equal(byId.BytesAtTarget, byBranch.BytesAtTarget);
        Assert.Equal(byId.ChangedPaths, byBranch.ChangedPaths);
    }

    [Fact]
    public async Task A_revision_that_does_not_exist_is_an_error_for_a_diff()
    {
        await Assert.ThrowsAsync<GitOperationException>(() => Engine.DiffStatAsync(_fx.Repo, "ghost", _fx.First, Ct));
        await Assert.ThrowsAsync<GitOperationException>(() => Engine.DiffStatAsync(_fx.Repo, _fx.First, new string('1', 40), Ct));
    }

    [Fact]
    public async Task Tree_files_are_listed_at_each_point_unaffected_by_later_ones()
    {
        var first = await Engine.ListTreeFilesAsync(_fx.Repo, _fx.First, Ct);
        var second = await Engine.ListTreeFilesAsync(_fx.Repo, "profile-1", Ct);

        Assert.Equal(["bin.dat", "changed.txt", "deleted.txt", "mods/sub dir/caf\u00e9.esp", "same.txt"], first.Order(StringComparer.Ordinal));
        Assert.Equal(["bin.dat", "changed.txt", "mods/sub dir/caf\u00e9.esp", "new.txt", "same.txt"], second.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_revision_that_does_not_exist_has_no_files()
    {
        Assert.Empty(await Engine.ListTreeFilesAsync(_fx.Repo, "ghost", Ct));
        Assert.Empty(await Engine.ListTreeFilesAsync(_fx.Repo, new string('1', 40), Ct));
    }

    [Fact]
    public async Task A_blob_comes_back_byte_for_byte()
    {
        var bytes = await Engine.ReadBlobAsync(_fx.Repo, _fx.First, "bin.dat", Ct);

        Assert.Equal(ContentFixture.BinaryBytes, bytes);
        Assert.Equal("0123456789", System.Text.Encoding.UTF8.GetString((await Engine.ReadBlobAsync(_fx.Repo, _fx.First, "changed.txt", Ct))!));
        Assert.Equal(new string('x', 40), System.Text.Encoding.UTF8.GetString((await Engine.ReadBlobAsync(_fx.Repo, "profile-1", "changed.txt", Ct))!));
    }

    [Fact]
    public async Task A_blob_is_found_through_backslashes_and_non_ascii_names()
    {
        var bytes = await Engine.ReadBlobAsync(_fx.Repo, _fx.First, "mods\\sub dir\\caf\u00e9.esp", Ct);

        Assert.Equal("accent", System.Text.Encoding.UTF8.GetString(bytes!));
    }

    [Theory]
    [InlineData("missing.txt")]
    [InlineData("mods")]
    [InlineData("mods/sub dir")]
    [InlineData("")]
    public async Task A_path_with_no_file_behind_it_reads_as_null(string path)
    {
        Assert.Null(await Engine.ReadBlobAsync(_fx.Repo, _fx.First, path, Ct));
        Assert.Null(await Engine.GetBlobIdAsync(_fx.Repo, _fx.First, path, Ct));
    }

    [Fact]
    public async Task A_revision_that_does_not_exist_reads_as_null()
    {
        Assert.Null(await Engine.ReadBlobAsync(_fx.Repo, "ghost", "same.txt", Ct));
        Assert.Null(await Engine.GetBlobIdAsync(_fx.Repo, new string('1', 40), "same.txt", Ct));
    }

    [Fact]
    public async Task A_blob_id_is_the_same_for_the_same_content_at_any_point_and_differs_when_it_changes()
    {
        var sameFirst = await Engine.GetBlobIdAsync(_fx.Repo, _fx.First, "same.txt", Ct);
        var sameSecond = await Engine.GetBlobIdAsync(_fx.Repo, _fx.Second, "same.txt", Ct);
        var changedFirst = await Engine.GetBlobIdAsync(_fx.Repo, _fx.First, "changed.txt", Ct);
        var changedSecond = await Engine.GetBlobIdAsync(_fx.Repo, _fx.Second, "changed.txt", Ct);

        Assert.Matches("^[0-9a-f]{40}$", sameFirst);
        Assert.Equal(sameFirst, sameSecond);
        Assert.NotEqual(changedFirst, changedSecond);
        Assert.Equal(await _fx.Workspace.GitOutAsync(_fx.Repo, "rev-parse", _fx.First + ":same.txt"), sameFirst);
    }
}
