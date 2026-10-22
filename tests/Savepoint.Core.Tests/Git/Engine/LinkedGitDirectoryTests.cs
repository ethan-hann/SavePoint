using Savepoint.Core.Git;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Tests.Git.Engine;

// A git directory can gain a link after SavePoint adopted it, and a walk that follows one reads, clears and deletes files that were never
// SavePoint's. Every walk over a git directory stays inside it, whatever "Find the folder" looked at.
[Collection(EngineCollections.A)]
public sealed class LinkedGitDirectoryTests : IDisposable
{
    private readonly EngineWorkspace _ws = new(options => options with { DeleteRetryDelays = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)] });
    private readonly List<string> _links = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var link in _links)
        {
            TestLinks.RemoveFolderLink(link);
        }

        _ws.Dispose();
    }

    // Somebody else's folder, which holds the given files.
    private string Scratch(params string[] files)
    {
        var scratch = Path.Combine(_ws.Root, "somebodys-folder");
        Directory.CreateDirectory(scratch);
        foreach (var file in files)
        {
            var path = Path.Combine(scratch, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "somebody else's file");
        }

        return scratch;
    }

    // Makes a folder of the git directory a link to the scratch folder.
    private void LinkOrSkip(RepoRef repo, string relative, string scratch)
    {
        var link = Path.Combine(repo.GitDir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(link))
        {
            Directory.Delete(link, recursive: true);
        }

        if (!TestLinks.TryCreateFolderLink(link, scratch))
        {
            Assert.Skip("this account cannot create folder links");
        }

        _links.Add(link);
    }

    [Theory]
    [InlineData("refs/heads", "keep.lock;sub/also.lock")]
    [InlineData("refs/tags", "keep.lock;sub/also.lock")]
    [InlineData("objects/pack", "tmp_keep")]
    [InlineData("objects/ab", "tmp_obj_keep")]
    public async Task The_sweep_leaves_what_a_link_in_the_git_directory_leads_to_alone(string linked, string inside)
    {
        var repo = await _ws.InitializedRepoAsync();
        var files = inside.Split(';');
        var scratch = Scratch(files);
        LinkOrSkip(repo, linked, scratch);

        var removed = _ws.Engine.RemoveStaleLocks(repo.GitDir);

        Assert.Equal(0, removed);
        foreach (var file in files)
        {
            Assert.True(File.Exists(Path.Combine(scratch, file.Replace('/', Path.DirectorySeparatorChar))), $"{file} was deleted through the link");
        }
    }

    [Fact]
    public async Task Deleting_the_git_directory_removes_a_link_in_it_as_a_link_and_leaves_what_it_leads_to_as_it_is()
    {
        var repo = await _ws.InitializedRepoAsync();
        var scratch = Scratch("read-only.txt");
        var file = Path.Combine(scratch, "read-only.txt");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        LinkOrSkip(repo, "refs/heads", scratch);

        await _ws.Engine.DeleteRepoAsync(repo, Ct);

        Assert.False(Directory.Exists(repo.GitDir));
        Assert.True(File.Exists(file), "the file the link led to was deleted");
        Assert.True((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0, "the attributes of a file the link led to were changed");
        File.SetAttributes(file, FileAttributes.Normal);
    }

    // The folder of branches cannot be read from outside the git directory, so it is not judged: a link there counts as history.
    [Fact]
    public async Task A_branches_folder_that_is_a_link_is_not_judged_empty()
    {
        var repo = await _ws.InitializedRepoAsync();
        var scratch = Scratch();
        LinkOrSkip(repo, "refs/heads", scratch);

        Assert.True(StorageContent.HasBranch(repo.GitDir));
    }
}
