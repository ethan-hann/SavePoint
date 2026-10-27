using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Launch;

// A profile that was made to start with no mods or no saves: the launch dialog is where the player learns the folder will be emptied,
// and nothing but its confirm button empties it.
public class LaunchDialogEmptyFolderTests : IDisposable
{
    private const string _modsPath = @"C:\Users\you\AppData\Roaming\7DaysToDie\Mods";

    private readonly LaunchRig _rig = new();

    public void Dispose() => _rig.Dispose();

    // A target that holds no mods, switched to from a folder with 40 mod files.
    private static LaunchPreview ToNoMods(LaunchPreview preview) => preview with
    {
        CurrentInstall = new InstallState("1.6.640", 40),
        TargetInstall = new InstallState("1.6.640", 0),
        Mods = new FolderLaunchState(RepoRole.Mods, _modsPath, false, false, 0, null, new SwitchSummary(40, 40, 0, null, 3_100_000_000)),
    };

    [Fact]
    public async Task Switching_to_a_profile_with_no_mods_says_the_mods_go_and_how_much_is_removed_before_anything_happens()
    {
        var dialog = await _rig.DialogAsync(LaunchRig.ClearedBlackreachPoint, change: ToNoMods);

        Assert.True(dialog.HasVersionSection);
        Assert.Equal("Going from 40 mod files to ", dialog.VersionBefore);
        Assert.Equal("no mods", dialog.VersionTarget);
        Assert.Contains("About 3.1 GB of files will be removed.", dialog.VersionAfter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_switch_waits_for_the_player_and_asks_nothing_of_the_api_until_the_confirm_button()
    {
        var dialog = await _rig.DialogAsync(LaunchRig.ClearedBlackreachPoint, change: ToNoMods);

        Assert.True(dialog.CanSwitch);
        Assert.Equal("Switch and play", dialog.ConfirmLabel);
        Assert.Empty(_rig.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.StartAsync)));
    }

    [Fact]
    public async Task Switching_to_a_profile_with_no_saves_says_the_saves_folder_is_replaced()
    {
        var dialog = await _rig.DialogAsync(change: preview => preview with { External = preview.External! with { Switch = new SwitchSummary(3, 0, 0, null, 400_000) } });

        Assert.True(dialog.HasSaveSection);
        Assert.Equal("This replaces what's in your saves folder now.", dialog.SaveCaption);
    }
}
