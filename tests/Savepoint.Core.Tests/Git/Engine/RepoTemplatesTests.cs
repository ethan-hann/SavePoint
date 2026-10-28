using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A template that failed to build is built again by the next test that wants it, so one failure shows once and not in every test after it.
public class RepoTemplatesTests
{
    private static Task<(RepoRef Repo, int Data)> Built(EngineWorkspace workspace, int data)
    {
        var repo = workspace.NewRepo();
        Directory.CreateDirectory(repo.GitDir);
        return Task.FromResult((repo, data));
    }

    [Fact]
    public async Task A_template_whose_build_failed_is_built_again_for_the_next_test()
    {
        var name = $"fails-once-{Guid.NewGuid():N}";
        using var into = new EngineWorkspace();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RepoTemplates.CopyAsync<int>(name, into, _ => throw new InvalidOperationException("the first build failed")));
        var (repo, data) = await RepoTemplates.CopyAsync(name, into, workspace => Built(workspace, 7));

        Assert.Equal(7, data);
        Assert.True(Directory.Exists(repo.GitDir));
    }

    [Fact]
    public async Task A_template_that_built_is_not_built_again()
    {
        var name = $"built-once-{Guid.NewGuid():N}";
        using var into = new EngineWorkspace();
        var builds = 0;

        await RepoTemplates.CopyAsync(name, into, workspace => { builds++; return Built(workspace, 1); });
        await RepoTemplates.CopyAsync(name, into, workspace => { builds++; return Built(workspace, 2); });

        Assert.Equal(1, builds);
    }
}
