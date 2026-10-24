using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

// What the object store holds right now, including the pack and object files git has open and is still writing.
public sealed class ObjectStoreMeterTests : IDisposable
{
    private readonly string _objects = Path.Combine(Path.GetTempPath(), "savepoint-meter-" + Guid.NewGuid().ToString("N"), "objects");

    public void Dispose() => GitWorkspace.DeleteDirectory(Path.GetDirectoryName(_objects)!);

    private void Write(string relativePath, int bytes)
    {
        var path = Path.Combine(_objects, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    [Fact]
    public void Finished_loose_objects_and_packs_are_summed()
    {
        Write("ab/cdef", 10);
        Write("12/3456", 5);
        Write("pack/pack-x.pack", 20);

        Assert.Equal(35, ObjectStoreMeter.Size(_objects));
    }

    [Fact]
    public void A_store_that_does_not_exist_holds_nothing()
    {
        Assert.Equal(0, ObjectStoreMeter.Size(_objects));
    }

    [Fact]
    public void A_pack_git_still_has_open_counts_what_it_has_written_so_far()
    {
        Directory.CreateDirectory(Path.Combine(_objects, "pack"));
        using var open = new FileStream(Path.Combine(_objects, "pack", "tmp_pack_abc"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
        open.Write(new byte[1_000_000]);

        Assert.Equal(1_000_000, ObjectStoreMeter.Size(_objects));
    }

    [Fact]
    public void A_temporary_file_that_cannot_be_opened_does_not_hide_the_files_after_it()
    {
        // Named to come after the busy file in a directory listing, so a measurement that gave up at the busy file would miss it.
        Write("pack/zz-finished.pack", 20);
        using var busy = new FileStream(Path.Combine(_objects, "pack", "tmp_pack_busy"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        busy.Write(new byte[100]);

        Assert.True(ObjectStoreMeter.Size(_objects) >= 20);
    }
}
