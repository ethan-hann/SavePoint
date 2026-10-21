using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// What a discard would do is counted in the preview, so the confirmation can name it (D31).
public class LaunchPreviewDiscardCountsTests
{
    [Fact(Timeout = 90_000)]
    public async Task A_dirty_folder_counts_the_new_files_a_discard_deletes_apart_from_the_changed_files_it_puts_back()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        rig.WriteSave("slot1.sav", "edited");
        rig.WriteSave("slot2.sav", "new");
        rig.WriteSave("nested/slot3.sav", "also new");

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(vanilla.Id));

        Assert.True(preview.External!.Dirty);
        Assert.Equal(3, preview.External.DirtyFileCount);
        Assert.Equal(2, preview.External.DirtyNewFileCount);
        Assert.Equal(0, preview.Install.DirtyNewFileCount);
    }
}
