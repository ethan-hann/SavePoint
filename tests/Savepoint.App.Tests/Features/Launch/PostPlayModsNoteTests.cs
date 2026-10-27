using Savepoint.App.Features.Launch;

namespace Savepoint.App.Tests.Features.Launch;

// Files that changed in the mods folder while the game ran are their own note, not the "update" note about the game's folder.
public class PostPlayModsNoteTests : IDisposable
{
    private readonly LaunchRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private async Task<PostPlayDialogViewModel> DialogAsync(int modsFiles, int installFiles = 0)
    {
        var exited = (await _rig.ExitedAsync(LaunchRig.ModdedTip, installFiles: installFiles)) with { ChangedModsFileCount = modsFiles };
        return new PostPlayDialogViewModel(exited, "Modded playthrough - 7-2-26", _rig.Deps);
    }

    [Fact]
    public async Task Changed_mods_files_get_a_note_that_names_the_mods_folder()
    {
        var dialog = await DialogAsync(modsFiles: 7);

        Assert.True(dialog.HasModsNote);
        Assert.Equal("7 files in your mods folder are new or changed since you started.", dialog.ModsNote);
    }

    [Fact]
    public async Task One_changed_mods_file_is_said_in_the_singular()
    {
        var dialog = await DialogAsync(modsFiles: 1);

        Assert.Equal("1 file in your mods folder is new or changed since you started.", dialog.ModsNote);
    }

    [Fact]
    public async Task A_game_with_no_changed_mods_files_has_no_mods_note()
    {
        var dialog = await DialogAsync(modsFiles: 0);

        Assert.False(dialog.HasModsNote);
        Assert.Equal(string.Empty, dialog.ModsNote);
    }

    [Fact]
    public async Task The_mods_note_is_not_the_update_note_and_the_two_can_show_together()
    {
        var dialog = await DialogAsync(modsFiles: 7, installFiles: 14);

        Assert.Equal("14 files in the game's own folder also changed, as an update would.", dialog.InstallNote);
        Assert.DoesNotContain("update", dialog.ModsNote, StringComparison.OrdinalIgnoreCase);
    }
}
