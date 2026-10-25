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
