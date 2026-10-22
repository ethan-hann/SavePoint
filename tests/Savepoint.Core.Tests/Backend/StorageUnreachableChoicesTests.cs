using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Backend;

// What the startup screen can do about a storage folder that is gone for good: point SavePoint at where the history is now, or start
// over with an empty folder (D37).
public class StorageUnreachableChoicesTests
{
    // A game with history, then the storage folder carried away, as a drive that now has another letter would be.
    private static async Task<(BackendWorkspace Workspace, GameDefinition Game, string Away)> WithStorageGoneAsync()
    {
        var workspace = new BackendWorkspace();
        try
        {
            await workspace.AppStatus.CheckAsync(workspace.Token);
            var game = await workspace.AddGameAsync();
            await workspace.CreateProfileAsync(game);
            await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
            var away = workspace.DefaultStorageRoot + "-away";
            Directory.Move(workspace.DefaultStorageRoot, away);
            workspace.Restart();
            return (workspace, game, away);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static IReadOnlyDictionary<string, string> WithoutHooks(IReadOnlyDictionary<string, string> snapshot) =>
        snapshot.Where(file => !file.Key.Split(Path.DirectorySeparatorChar).Contains("hooks")).ToDictionary(file => file.Key, file => file.Value);

    private static void CopyFolder(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }

    // The same, but the storage was moved to a folder of its own first, so the default folder is not the one that went missing.
    private static async Task<(BackendWorkspace Workspace, GameDefinition Game, string Custom, string Away)> WithCustomStorageGoneAsync()
    {
        var workspace = new BackendWorkspace();
        try
        {
            await workspace.AppStatus.CheckAsync(workspace.Token);
            var game = await workspace.AddGameAsync();
            await workspace.CreateProfileAsync(game);
            await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
            var custom = Path.Combine(workspace.Root, "custom-storage");
            var started = await workspace.Settings.RelocateStorageAsync(custom);
            var job = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));
            Assert.Equal(JobStatus.Succeeded, job.Status);
            var away = custom + "-away";
            Directory.Move(custom, away);
            workspace.Restart();
            return (workspace, game, custom, away);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task Finding_the_folder_points_the_settings_at_it_without_copying_anything_and_the_history_is_back()
    {
        var (workspace, game, away) = await WithStorageGoneAsync();
        using var _ = workspace;
        var before = BackendWorkspace.Snapshot(away);

        var found = await workspace.AppStatus.UseStorageFolderAsync(away, workspace.Token);
        var check = await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.True(found.Ok, found.Error?.Detail);
        Assert.Equal(away, found.Value);
        Assert.Equal(away, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
        Assert.True(check.Value!.Storage!.Reachable);
        Assert.Single((await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points);

        // Nothing is copied or changed in the history; only the hooks folders are emptied, since a hook is a program the folder could carry.
        Assert.Equal(WithoutHooks(before), WithoutHooks(BackendWorkspace.Snapshot(away)));
        Assert.False(Directory.Exists(workspace.DefaultStorageRoot), "finding the folder must not make an empty one beside it");

        workspace.Restart();
        Assert.True((await workspace.AppStatus.CheckAsync(workspace.Token)).Value!.Storage!.Reachable);
    }

    [Fact(Timeout = 120_000)]
    public async Task Finding_a_folder_someone_else_prepared_runs_none_of_the_programs_it_names()
    {
        var (workspace, game, away) = await WithStorageGoneAsync();
        using var _ = workspace;

        // A copy of the history that carries a hook, a program setting and a stamp that says its config is current.
        var prepared = Path.Combine(workspace.Root, "prepared");
        CopyFolder(away, prepared);
        var hookRan = Path.Combine(workspace.Root, "hook-ran.txt");
        var monitorRan = Path.Combine(workspace.Root, "monitor-ran.txt");
        var gitDirs = new[] { "install.git", "external.git" }.Select(name => Path.Combine(prepared, game.Id, name)).ToList();
        foreach (var gitDir in gitDirs)
        {
            foreach (var name in new[] { "post-commit", "reference-transaction", "post-checkout" })
            {
                var hook = Path.Combine(gitDir, "hooks", name);
                BackendWorkspace.WriteFile(hook, $"#!/bin/sh\necho ran >> '{hookRan.Replace('\\', '/')}'\n");
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }

            File.AppendAllText(Path.Combine(gitDir, "config"), $"[core]\n\tfsmonitor = echo ran >> '{monitorRan.Replace('\\', '/')}' #\n[alias]\n\tst = !echo ran\n");
        }

        var found = await workspace.AppStatus.UseStorageFolderAsync(prepared, workspace.Token);
        var profile = (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!.Single();
        File.WriteAllText(Path.Combine(game.Install.Path, "data", "base.pak"), "what the game wrote after the history was found");
        File.WriteAllText(Path.Combine(game.External!.Path, "slot2.sav"), "a second save");
        var capture = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "After the find");
        var job = await workspace.WaitForJobAsync(capture.Value!.JobId, TimeSpan.FromSeconds(120));

        Assert.True(found.Ok, found.Error?.Detail);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.False(File.Exists(hookRan), "a hook of the adopted folder ran");
        Assert.False(File.Exists(monitorRan), "the program named by core.fsmonitor in the adopted folder ran");

        // The folder's configs are rewritten and its hooks removed before anything else reads it.
        foreach (var gitDir in gitDirs)
        {
            var config = File.ReadAllText(Path.Combine(gitDir, "config"));
            Assert.DoesNotContain("fsmonitor", config, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[alias]", config, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(gitDir, "hooks")));
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task A_folder_without_the_history_of_the_games_is_refused_and_nothing_changes()
    {
        var (workspace, game, _) = await WithStorageGoneAsync();
        using var _ = workspace;
        var empty = Path.Combine(workspace.Root, "empty-folder");
        Directory.CreateDirectory(empty);
        var plain = Path.Combine(workspace.Root, "plain-git");
        BackendWorkspace.WriteFile(Path.Combine(plain, game.Id, "install.git", "HEAD"), "ref: refs/heads/main");

        var results = new[]
        {
            await workspace.AppStatus.UseStorageFolderAsync(empty, workspace.Token),
            await workspace.AppStatus.UseStorageFolderAsync(plain, workspace.Token),
            await workspace.AppStatus.UseStorageFolderAsync(Path.Combine(workspace.Root, "nowhere"), workspace.Token),
        };

        Assert.All(results, result => Assert.Equal("error.storage.noHistoryHere", result.Error?.MessageId));
        Assert.Equal(empty, results[0].Error!.Args["path"]);
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
        Assert.False((await workspace.AppStatus.CheckAsync(workspace.Token)).Value!.Storage!.Reachable);
    }

    [Fact(Timeout = 120_000)]
    public async Task Starting_with_an_empty_folder_uses_the_default_folder_and_keeps_the_games_listed_without_history()
    {
        var (workspace, game, custom, away) = await WithCustomStorageGoneAsync();
        using var _ = workspace;
        var before = BackendWorkspace.Snapshot(away);

        var started = await workspace.AppStatus.StartWithEmptyStorageAsync(workspace.Token);
        var check = await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.True(started.Ok, started.Error?.Detail);
        Assert.Equal(workspace.DefaultStorageRoot, started.Value);
        Assert.NotEqual(custom, started.Value);
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
        Assert.True(check.Value!.Storage!.Reachable);
        Assert.Empty(Directory.GetFileSystemEntries(workspace.DefaultStorageRoot));
        Assert.Equal(game.Id, Assert.Single((await workspace.Games.ListAsync(workspace.Token)).Value!).Id);
        Assert.Empty((await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points);
        Assert.Equal(before, BackendWorkspace.Snapshot(away));
    }

    [Fact(Timeout = 120_000)]
    public async Task Starting_with_an_empty_folder_refuses_a_default_folder_that_already_holds_history_of_its_own()
    {
        var (workspace, _, custom, _) = await WithCustomStorageGoneAsync();
        using var _ = workspace;
        BackendWorkspace.WriteFile(Path.Combine(workspace.DefaultStorageRoot, "some-game", "install.git", "HEAD"), "ref: refs/heads/main");

        var started = await workspace.AppStatus.StartWithEmptyStorageAsync(workspace.Token);

        Assert.Equal("error.storageRelocation.destinationNotEmpty", started.Error?.MessageId);
        Assert.Equal(custom, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
    }
}
