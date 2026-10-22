using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Git.Engine;

// git reads a pointer file named commondir in a git directory and takes the repository's refs from the folder it names, so a git directory
// that carries one is not the folder SavePoint thinks it is. The environment names the git directory as the shared directory, and
// every operation refuses a git directory that holds such a file, whenever it appeared.
[Collection(EngineCollections.B)]
public sealed class RedirectedGitDirectoryTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_git_directory_that_gained_a_pointer_to_another_is_refused_by_the_operation_and_the_other_is_left_alone()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        var elsewhere = Path.Combine(_ws.Root, "elsewhere.git");
        Directory.CreateDirectory(elsewhere);
        var pointer = Path.Combine(repo.GitDir, "commondir");
        await File.WriteAllTextAsync(pointer, elsewhere, Ct);

        var error = await Assert.ThrowsAsync<GitOperationException>(() => _ws.CaptureAsync(repo, "profile-1"));

        Assert.Equal(pointer, error.Args["path"]);
        Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere));
        Assert.Null(await _ws.TipAsync(repo, "profile-1"));
    }
}
