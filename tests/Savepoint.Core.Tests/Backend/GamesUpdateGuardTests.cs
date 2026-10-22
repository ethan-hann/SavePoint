using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

// Editing a game's folders is refused while a session is open, and cannot overlap another game being added or edited (D31).
public class GamesUpdateGuardTests
{
    private static readonly TimeSpan _held = TimeSpan.FromSeconds(20);

    [Fact(Timeout = 120_000)]
    public async Task A_folder_change_is_refused_while_the_game_has_an_open_session_and_a_name_change_is_not()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        workspace.Sessions.Start(game.Id, "any");
        var elsewhere = Path.Combine(workspace.GamesRoot, "elsewhere");
        BackendWorkspace.WriteFile(Path.Combine(elsewhere, "slot1.sav"), "save one");

        var moved = await workspace.Games.UpdateAsync(game with { External = new ExternalLocation(elsewhere) });
        var renamed = await workspace.Games.UpdateAsync(game with { Name = "Renamed" });

        Assert.Equal("error.gameService.gameRunning", moved.Error?.MessageId);
        Assert.Equal(game.Name, moved.Error!.Args["gameName"]);
        Assert.True(renamed.Ok, renamed.Error?.Detail);
        var stored = (await workspace.Games.ListAsync(workspace.Token)).Value!.Single();
        Assert.Equal(game.External!.Path, stored.External!.Path);
        Assert.Equal("Renamed", stored.Name);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_game_edited_into_the_folder_of_a_game_that_is_being_added_is_refused_as_overlapping()
    {
        using var reachedAdd = new ManualResetEventSlim();
        using var releaseAdd = new ManualResetEventSlim();
        using var reachedEdit = new ManualResetEventSlim();
        string? adding = null;
        string? editing = null;
        using var workspace = new BackendWorkspace(options => options with
        {
            Engine = new GitEngineOptions
            {
                InvocationObserver = invocation =>
                {
                    if (invocation.Arguments[0] != "init")
                    {
                        return;
                    }

                    if (Volatile.Read(ref adding) is { } added && invocation.GitDir?.Contains(added, StringComparison.Ordinal) == true)
                    {
                        reachedAdd.Set();
                        releaseAdd.Wait(_held);
                    }
                    else if (Volatile.Read(ref editing) is { } edited && invocation.GitDir?.Contains(edited, StringComparison.Ordinal) == true)
                    {
                        reachedEdit.Set();
                    }
                },
            },
        });
        var existing = await workspace.AddGameAsync();
        Volatile.Write(ref editing, existing.Id);
        var incoming = workspace.NewGame("Beta");
        Volatile.Write(ref adding, incoming.Id);
        var inside = Path.Combine(incoming.Install.Path, "inner");
        BackendWorkspace.WriteFile(Path.Combine(inside, "game.exe"), "MZ not a real program");
        Task<OperationResult<AddGameOutcome>> add;
        Task<OperationResult<GameDefinition>> edit;
        try
        {
            add = workspace.Games.AddAsync(incoming);
            Assert.True(reachedAdd.Wait(_held, workspace.Token), "the new game never reached its first git call");

            // The edit starts while the new game is not in the catalog yet; it has to wait for the add, or it would not see the game it overlaps.
            edit = workspace.Games.UpdateAsync(existing with { Install = new InstallLocation(inside) });
            await Task.WhenAny(Task.Run(() => reachedEdit.Wait(TimeSpan.FromSeconds(2), workspace.Token), workspace.Token), edit);
        }
        finally
        {
            releaseAdd.Set();
        }

        var added = await add;
        var edited = await edit;

        Assert.True(added.Ok, added.Error?.Detail);
        Assert.Equal("error.gameService.pathOverlap", edited.Error?.MessageId);
        Assert.Equal(incoming.Id, edited.Error!.Args["conflictingGameId"]);
        Assert.Equal(existing.Install.Path, (await workspace.Games.ListAsync(workspace.Token)).Value!.Single(game => game.Id == existing.Id).Install.Path);
    }
}
