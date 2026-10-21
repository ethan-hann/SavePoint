using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// Editing a game's ignore list takes effect at once for what the launch dialog reports and what a discard removes (D31).
public class GameIgnoreEditTests
{
    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 120_000)]
    public async Task A_pattern_added_in_the_editor_protects_its_files_from_the_next_discard_and_from_the_unsaved_changes_count()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        rig.WriteInstall("Screenshots/one.png", "a screenshot");

        var updated = await rig.Workspace.Games.UpdateAsync(rig.Game with { IgnorePaths = ["Screenshots/"] });
        var preview = await rig.PreviewAsync(Tip(vanilla));
        var discard = await rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.Install);

        Assert.True(updated.Ok, updated.Error?.Detail);
        Assert.False(preview.Install.Dirty);
        Assert.True(discard.Ok, discard.Error?.Detail);
        Assert.True(rig.InstallHas("Screenshots/one.png"));
    }

    [Fact(Timeout = 120_000)]
    public async Task Saving_a_game_rewrites_each_folders_exclude_list_from_its_ignore_patterns()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.ProfileAsync("Vanilla");

        var updated = await rig.Workspace.Games.UpdateAsync(rig.Game with { IgnorePaths = ["Screenshots/", "*.log"] });

        Assert.True(updated.Ok, updated.Error?.Detail);
        foreach (var role in new[] { RepoRole.Install, RepoRole.External })
        {
            var gitDir = await rig.Workspace.Backend.Services.Storage.ReadAsync(storage => Task.FromResult(storage.GitDirectory(rig.Game.Id, role)), rig.Token);
            var lines = await File.ReadAllLinesAsync(Path.Combine(gitDir, "info", "exclude"), rig.Token);
            Assert.Contains("Screenshots/", lines);
            Assert.Contains("*.log", lines);
        }
    }
}
