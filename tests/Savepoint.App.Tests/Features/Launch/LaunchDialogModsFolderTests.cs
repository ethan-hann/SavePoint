using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Launch;

// A game whose mods live outside its game folder has a third folder in the launch dialog: its changes, its unfinished switch and
// its size all count, in the order the switch writes the folders (install, mods, saves).
public class LaunchDialogModsFolderTests : IDisposable
{
    private const string _modsPath = @"C:\Users\you\AppData\Roaming\7DaysToDie\Mods";

    private readonly LaunchRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private static FolderLaunchState DirtyMods(string? owner = SampleScenario.SurvivalId) =>
        new(RepoRole.Mods, _modsPath, false, true, 5, owner, new SwitchSummary(40, 40, 3_100_000_000, null), DirtyNewFileCount: 2);

    private static FolderLaunchState CleanMods(SwitchSummary? summary = null) =>
        new(RepoRole.Mods, _modsPath, summary is null, false, 0, null, summary);

    private static LaunchPreview WithMods(LaunchPreview preview, FolderLaunchState mods) => preview with { Mods = mods };

    [Fact]
    public async Task A_dirty_mods_folder_gets_a_block_that_names_the_mods_folder_and_offers_both_choices()
    {
        var dialog = await _rig.DialogAsync(change: p => WithMods(p, DirtyMods()));

        var block = dialog.UnsavedBlocks.Single(b => b.Role == RepoRole.Mods);
        Assert.Equal("5 files in your mods folder changed since the last snapshot of \"Survival test - 5-14-26\". Take a snapshot to keep them there, or discard them.", block.Body);
        Assert.Equal(_modsPath, block.Path);
        Assert.Equal(2, block.RemovedFileCount);
        Assert.Equal(3, block.RestoredFileCount);
        Assert.True(block.CanSaveFirst);
        Assert.False(dialog.CanSwitch);
    }

    [Fact]
    public async Task Dirty_folders_get_their_blocks_in_the_order_the_switch_writes_them()
    {
        var dialog = await _rig.DialogAsync(change: p => WithMods(p with { Install = p.Install with { Dirty = true, DirtyFileCount = 12, DirtyOwnerProfileId = SampleScenario.SurvivalId } }, DirtyMods()));

        Assert.Equal([RepoRole.Install, RepoRole.Mods, RepoRole.External], dialog.UnsavedBlocks.Select(b => b.Role));
    }

    [Fact]
    public async Task Changes_in_the_mods_folder_that_belong_to_no_profile_point_at_keeping_them_as_a_new_profile()
    {
        var dialog = await _rig.DialogAsync(change: p => WithMods(p, DirtyMods(owner: null)));

        var block = dialog.UnsavedBlocks.Single(b => b.Role == RepoRole.Mods);
        Assert.Equal("5 files in your mods folder changed since the last snapshot. Keep them as a new profile, or discard them.", block.Body);
        Assert.False(block.CanSaveFirst);
    }

    [Fact]
    public async Task Taking_a_snapshot_first_saves_only_the_mods_folder_into_its_owner_and_clears_its_block()
    {
        var dialog = await _rig.DialogAsync(change: p => WithMods(p, DirtyMods()));
        var block = dialog.UnsavedBlocks.Single(b => b.Role == RepoRole.Mods);

        var run = block.SaveFirstCommand.ExecuteAsync(null);

        var call = Assert.Single(_rig.Backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.CaptureAsync)));
        Assert.Equal(SampleScenario.SurvivalId, call.Args[1]);
        Assert.Equal([RepoRole.Mods], Assert.IsAssignableFrom<IEnumerable<RepoRole>>(call.Args[3]));
        _rig.Backend.Jobs.Complete(_rig.Backend.Jobs.LastStarted(JobKind.Capture).JobId);
        await run;
        Assert.DoesNotContain(dialog.UnsavedBlocks, b => b.Role == RepoRole.Mods);
    }

    [Fact]
    public async Task Discarding_the_mods_changes_names_the_mods_folder_and_discards_that_folder()
    {
        var dialog = await _rig.DialogAsync(change: p => WithMods(p, DirtyMods()));
        _rig.Dialogs.AnswerConfirm(true);

        await dialog.UnsavedBlocks.Single(b => b.Role == RepoRole.Mods).DiscardCommand.ExecuteAsync(null);

        var request = Assert.Single(_rig.Dialogs.Confirmations);
        Assert.StartsWith($"The mods folder ({_modsPath}) goes back to its last snapshot.", request.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Progress you saved in the game", request.Message, StringComparison.Ordinal);
        var call = Assert.Single(_rig.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.DiscardChangesAsync)));
        Assert.Equal([SampleScenario.SkyrimId, RepoRole.Mods], call.Args);
        Assert.DoesNotContain(dialog.UnsavedBlocks, b => b.Role == RepoRole.Mods);
    }

    [Fact]
    public async Task A_mods_folder_left_half_switched_says_so_and_offers_to_finish_or_discard()
    {
        var unfinished = DirtyMods() with { DirtyFileCount = 0, UnfinishedSwitch = true, UnfinishedSwitchTarget = LaunchRig.ModdedTip };
        var dialog = await _rig.DialogAsync(change: p => WithMods(p with { External = p.External! with { Dirty = false, DirtyFileCount = 0 } }, unfinished));

        var block = Assert.Single(dialog.UnsavedBlocks);
        Assert.Equal(RepoRole.Mods, block.Role);
        Assert.True(block.IsUnfinishedSwitch);
        Assert.Equal("The last switch didn't finish", block.Title);
        Assert.Equal("The mods folder has files from two snapshots. Finish the switch, or discard the mix to go back to where it started.", block.Body);
        Assert.True(block.CanFinishSwitch);
    }

    [Fact]
    public async Task A_stopped_switch_in_the_mods_folder_says_how_many_files_the_finish_replaces()
    {
        var replaced = CleanMods() with { UnfinishedSwitch = true, ReplacedFileCount = 4 };
        var dialog = await _rig.DialogAsync(change: p => WithMods(p with { External = p.External! with { Dirty = false, DirtyFileCount = 0 } }, replaced));

        Assert.Equal(
            "4 files in your mods folder that the last switch was changing hold something that's in neither snapshot. Finishing the last switch replaces them with the copies from the snapshot it was heading to, including any change you made since.",
            dialog.ReplacedNote);
    }

    [Fact]
    public async Task A_change_only_in_the_mods_folder_still_shows_the_version_and_mods_card()
    {
        var dialog = await _rig.DialogAsync(
            LaunchRig.ClearedBlackreachPoint,
            change: p => WithMods(
                p with { CurrentInstall = new InstallState("1.6.640", 212), TargetInstall = new InstallState("1.6.640", 189) },
                CleanMods(new SwitchSummary(40, 23, 3_100_000_000, null))));

        Assert.True(dialog.HasVersionSection);
        Assert.Equal("Going from 212 mod files to ", dialog.VersionBefore);
        Assert.Equal("189 mod files", dialog.VersionTarget);
        Assert.Contains("About 3.1 GB of files will be replaced.", dialog.VersionAfter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_size_of_the_mods_switch_is_added_to_the_size_of_the_game_switch()
    {
        var dialog = await _rig.DialogAsync(change: p => WithMods(p, CleanMods(new SwitchSummary(40, 40, 3_100_000_000, null))));

        Assert.Contains("About 9.2 GB of files will be replaced.", dialog.VersionAfter, StringComparison.Ordinal);
    }
}
