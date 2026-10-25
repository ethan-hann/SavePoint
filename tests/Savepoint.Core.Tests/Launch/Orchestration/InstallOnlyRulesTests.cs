using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A rule for the game's own folder never reaches the saves folder, so a log rule cannot hide a save.
public class InstallOnlyRulesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = 180_000)]
    public async Task An_install_only_rule_keeps_the_log_out_of_the_install_snapshot_and_a_log_in_the_saves_folder_in_the_save()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.EditGameAsync(game => game with { InstallIgnorePaths = ["**/*.log"] });
        rig.WriteInstall("crloader.log", "loader");
        rig.WriteSave("Player.log", "this game saves here");

        var vanilla = await rig.ProfileAsync("Vanilla");

        var install = (await rig.CommitsAsync(RepoRole.Install, vanilla))[0];
        var saves = (await rig.CommitsAsync(RepoRole.External, vanilla))[0];
        var installFiles = await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, install.Id, Ct), RepoRole.Install);
        var saveFiles = await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, saves.Id, Ct), RepoRole.External);
        Assert.DoesNotContain("crloader.log", installFiles);
        Assert.Contains("data/base.pak", installFiles);
        Assert.Contains("Player.log", saveFiles);
        Assert.Empty((await rig.StatusAsync(RepoRole.Install)).UntrackedPaths);
        Assert.Empty((await rig.StatusAsync(RepoRole.External)).UntrackedPaths);
    }

    [Fact(Timeout = 180_000)]
    public async Task A_game_wide_rule_still_applies_to_both_folders()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.EditGameAsync(game => game with { IgnorePaths = ["**/*.log"] });
        rig.WriteInstall("crloader.log", "loader");
        rig.WriteSave("Player.log", "this game saves here");

        var vanilla = await rig.ProfileAsync("Vanilla");

        var install = (await rig.CommitsAsync(RepoRole.Install, vanilla))[0];
        var saves = (await rig.CommitsAsync(RepoRole.External, vanilla))[0];
        Assert.DoesNotContain("crloader.log", await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, install.Id, Ct), RepoRole.Install));
        Assert.DoesNotContain("Player.log", await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, saves.Id, Ct), RepoRole.External));
    }

    private static async Task<IReadOnlyList<string>> ExcludeLinesAsync(LaunchRig rig, RepoRole role) =>
        await rig.WithEngineAsync(
            (_, repo) => Task.FromResult<IReadOnlyList<string>>([.. File.ReadAllLines(Path.Combine(repo.GitDir, "info", "exclude")).Where(line => line.Length > 0 && line[0] != '#')]),
            role);

    [Fact(Timeout = 180_000)]
    public async Task Moving_a_rule_between_the_lists_changes_each_folders_exclude_list_and_the_next_snapshot_follows_per_folder()
    {
        using var rig = await LaunchRig.CreateAsync();
        rig.WriteInstall("crloader.log", "first");
        rig.WriteSave("Player.log", "first");
        var vanilla = await rig.ProfileAsync("Vanilla");
        var installBefore = (await rig.CommitsAsync(RepoRole.Install, vanilla))[0];
        var savesBefore = (await rig.CommitsAsync(RepoRole.External, vanilla))[0];

        await rig.EditGameAsync(game => game with { IgnorePaths = ["**/*.log"] });
        Assert.Contains("**/*.log", await ExcludeLinesAsync(rig, RepoRole.Install));
        Assert.Contains("**/*.log", await ExcludeLinesAsync(rig, RepoRole.External));

        await rig.EditGameAsync(game => game with { IgnorePaths = [], InstallIgnorePaths = ["**/*.log"] });
        Assert.Contains("**/*.log", await ExcludeLinesAsync(rig, RepoRole.Install));
        Assert.DoesNotContain("**/*.log", await ExcludeLinesAsync(rig, RepoRole.External));

        rig.WriteInstall("crloader.log", "second");
        rig.WriteInstall("data/new.pak", "an update");
        rig.WriteSave("Player.log", "second");
        rig.WriteSave("slot2.sav", "a save");
        await rig.CaptureAsync(vanilla, "After the move", RepoRole.Install, RepoRole.External);

        var installAfter = (await rig.CommitsAsync(RepoRole.Install, vanilla))[0];
        var savesAfter = (await rig.CommitsAsync(RepoRole.External, vanilla))[0];
        Assert.NotEqual(installBefore.Id, installAfter.Id);
        Assert.NotEqual(savesBefore.Id, savesAfter.Id);
        var installNow = await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, installAfter.Id, Ct), RepoRole.Install);
        var installThen = await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, installBefore.Id, Ct), RepoRole.Install);
        var savesNow = await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, savesAfter.Id, Ct), RepoRole.External);
        Assert.DoesNotContain("crloader.log", installNow);
        Assert.Contains("data/new.pak", installNow);
        Assert.Contains("crloader.log", installThen);
        Assert.Contains("Player.log", savesNow);
        Assert.Contains("slot2.sav", savesNow);
        Assert.Equal("second", rig.ReadInstall("crloader.log"));
        Assert.Equal("second", rig.ReadSave("Player.log"));
    }

    [Fact(Timeout = 180_000)]
    public async Task A_negation_in_the_both_folders_list_keeps_one_file_the_own_folder_list_leaves_out()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.EditGameAsync(game => game with { InstallIgnorePaths = ["**/*.log"], IgnorePaths = ["!keep.log"] });
        rig.WriteInstall("keep.log", "wanted");
        rig.WriteInstall("noise.log", "not wanted");

        var vanilla = await rig.ProfileAsync("Vanilla");

        var install = (await rig.CommitsAsync(RepoRole.Install, vanilla))[0];
        var files = await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, install.Id, Ct), RepoRole.Install);
        Assert.Contains("keep.log", files);
        Assert.DoesNotContain("noise.log", files);
    }

    [Fact(Timeout = 180_000)]
    public async Task The_capture_preview_counts_each_folder_with_its_own_rules()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.EditGameAsync(game => game with { InstallIgnorePaths = ["**/*.log"] });
        rig.WriteInstall("crloader.log", "loader");
        rig.WriteSave("Player.log", "this game saves here");

        var preview = (await rig.Workspace.Games.GetCapturePreviewAsync(rig.Game.Id, Ct)).Value!;

        var install = preview.Folders.Single(folder => folder.Role == RepoRole.Install);
        var saves = preview.Folders.Single(folder => folder.Role == RepoRole.External);
        var installWithoutLogs = await rig.WithEngineAsync((engine, repo) => engine.ListCaptureCandidatesAsync(repo, ["**/*.log"], Ct), RepoRole.Install);
        var savesAsTheyAre = await rig.WithEngineAsync((engine, repo) => engine.ListCaptureCandidatesAsync(repo, [], Ct), RepoRole.External);
        Assert.Contains(savesAsTheyAre, candidate => candidate.Path == "Player.log");
        Assert.Equal(installWithoutLogs.Count, install.FileCount);
        Assert.Equal(savesAsTheyAre.Count, saves.FileCount);
    }
}
