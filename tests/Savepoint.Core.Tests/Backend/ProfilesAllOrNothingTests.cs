using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

// A profile is a branch in each of the game's two folders plus a row. Deleting or duplicating one either finishes or changes nothing (D13.9).
public class ProfilesAllOrNothingTests
{
    [Fact(Timeout = 120_000)]
    public async Task A_delete_that_fails_on_the_second_folder_puts_the_first_folders_branch_back_and_a_retry_finishes()
    {
        var failRemoval = 0;
        using var workspace = new BackendWorkspace(options => options with
        {
            Engine = new GitEngineOptions
            {
                InvocationObserver = invocation =>
                {
                    if (Volatile.Read(ref failRemoval) == 1 && IsRemovalIn(invocation, "external.git"))
                    {
                        throw new IOException("The disk reported an error.");
                    }
                },
            },
        });
        var game = await workspace.AddGameAsync();
        var (spare, _) = await workspace.CreateProfileAsync(game, "Spare");
        await workspace.CreateProfileAsync(game, "Active");
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));

        Volatile.Write(ref failRemoval, 1);
        var failed = await workspace.Profiles.DeleteAsync(game.Id, spare.Id);
        Volatile.Write(ref failRemoval, 0);

        Assert.False(failed.Ok);
        Assert.Contains(spare.Id, (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!.Select(profile => profile.Id));
        foreach (var role in new[] { RepoRole.Install, RepoRole.External })
        {
            Assert.Contains(spare.BranchName, await BranchesAsync(workspace, game, role));
        }

        var retried = await workspace.Profiles.DeleteAsync(game.Id, spare.Id);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));

        Assert.True(retried.Ok, retried.Error?.Detail);
        Assert.DoesNotContain(spare.Id, (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!.Select(profile => profile.Id));
        foreach (var role in new[] { RepoRole.Install, RepoRole.External })
        {
            Assert.DoesNotContain(spare.BranchName, await BranchesAsync(workspace, game, role));
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task A_duplicate_whose_row_cannot_be_saved_leaves_no_branch_in_either_folder()
    {
        FileStream? held = null;
        string? profilesFile = null;
        using var workspace = new BackendWorkspace(options => options with
        {
            Engine = new GitEngineOptions
            {
                InvocationObserver = invocation =>
                {
                    // The saves branch is written, then the row cannot be: the catalog file is held shut from the moment that branch is made.
                    if (Volatile.Read(ref profilesFile) is { } file && held is null && invocation.Arguments[0] == "update-ref" && invocation.GitDir?.Contains("external.git", StringComparison.Ordinal) == true)
                    {
                        held = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                },
            },
        });
        var game = await workspace.AddGameAsync();
        var (source, _) = await workspace.CreateProfileAsync(game, "Source");
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var before = new Dictionary<RepoRole, IReadOnlyList<string>>
        {
            [RepoRole.Install] = await BranchesAsync(workspace, game, RepoRole.Install),
            [RepoRole.External] = await BranchesAsync(workspace, game, RepoRole.External),
        };
        Volatile.Write(ref profilesFile, Path.Combine(workspace.CatalogDirectory, CatalogStore.ProfilesFileName));

        OperationResult<Profile> duplicated;
        try
        {
            duplicated = await workspace.Profiles.DuplicateAsync(game.Id, source.Id, "Copy");
        }
        finally
        {
            held?.Dispose();
        }

        Assert.False(duplicated.Ok);
        Assert.NotNull(held);
        foreach (var role in new[] { RepoRole.Install, RepoRole.External })
        {
            Assert.Equal(before[role].Order(), (await BranchesAsync(workspace, game, role)).Order());
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task A_delete_whose_row_cannot_be_removed_puts_the_branches_back_in_both_folders_and_a_retry_finishes()
    {
        FileStream? held = null;
        string? profilesFile = null;
        using var workspace = new BackendWorkspace(options => options with
        {
            Engine = new GitEngineOptions
            {
                InvocationObserver = invocation =>
                {
                    // Both branches go first and the row is removed after; the catalog file is held shut from the moment the last branch goes.
                    if (Volatile.Read(ref profilesFile) is { } file && held is null && IsRemovalIn(invocation, "external.git"))
                    {
                        held = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                },
            },
        });
        var game = await workspace.AddGameAsync();
        var (spare, _) = await workspace.CreateProfileAsync(game, "Spare");
        await workspace.CreateProfileAsync(game, "Active");
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        Volatile.Write(ref profilesFile, Path.Combine(workspace.CatalogDirectory, CatalogStore.ProfilesFileName));

        OperationResult<Unit> failed;
        try
        {
            failed = await workspace.Profiles.DeleteAsync(game.Id, spare.Id);
        }
        finally
        {
            held?.Dispose();
        }

        Assert.NotNull(held);
        Assert.False(failed.Ok);
        Assert.Contains(spare.Id, (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!.Select(profile => profile.Id));
        foreach (var role in new[] { RepoRole.Install, RepoRole.External })
        {
            Assert.Contains(spare.BranchName, await BranchesAsync(workspace, game, role));
        }

        var retried = await workspace.Profiles.DeleteAsync(game.Id, spare.Id);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));

        Assert.True(retried.Ok, retried.Error?.Detail);
        Assert.DoesNotContain(spare.Id, (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!.Select(profile => profile.Id));
    }

    private static bool IsRemovalIn(GitInvocation invocation, string gitDirName) =>
        invocation.Arguments[0] == "update-ref"
        && invocation.Arguments.Contains("-d")
        && invocation.GitDir?.Contains(gitDirName, StringComparison.Ordinal) == true;

    private static async Task<IReadOnlyList<string>> BranchesAsync(BackendWorkspace workspace, GameDefinition game, RepoRole role)
    {
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        var path = role == RepoRole.Install ? game.Install.Path : game.External!.Path;
        return await storage.Engine.ListBranchesAsync(new RepoRef(storage.GitDirectory(game.Id, role), path, game.Id, role), workspace.Token);
    }
}
