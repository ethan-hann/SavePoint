using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Tests.Services.Storage;

public class DiskSpaceTests
{
    // Free space moves while other tests write; a few megabytes of drift is not a disagreement.
    private const long _drift = 256L * 1024 * 1024;

    private readonly DiskSpace _disk = new();

    [Fact]
    public void An_existing_folder_reports_the_free_space_of_its_volume()
    {
        using var temp = new TempFolder();

        var free = _disk.GetFreeBytes(temp.Path);

        Assert.NotNull(free);
        Assert.InRange(free.Value, 1, TotalSizeOfVolumeHolding(temp.Path));
        Assert.InRange(Math.Abs(free.Value - ReferenceFreeBytes(temp.Path)), 0, _drift);
    }

    [Fact]
    public void A_path_that_does_not_exist_reports_the_volume_of_its_nearest_existing_folder()
    {
        using var temp = new TempFolder();
        var missing = temp.Combine("not", "there", "yet", "file.bin");

        var free = _disk.GetFreeBytes(missing);

        Assert.NotNull(free);
        Assert.InRange(Math.Abs(free.Value - ReferenceFreeBytes(temp.Path)), 0, _drift);
        Assert.False(Directory.Exists(temp.Combine("not")), "asking must not create anything");
    }

    [Fact]
    public void A_file_reports_the_volume_that_holds_it()
    {
        using var temp = new TempFolder();
        var file = temp.WriteBytes("saves/slot1.sav", 10);

        var free = _disk.GetFreeBytes(file);

        Assert.NotNull(free);
        Assert.InRange(Math.Abs(free.Value - ReferenceFreeBytes(temp.Path)), 0, _drift);
    }

    [Fact]
    public void A_relative_path_is_resolved_against_the_current_directory()
    {
        var free = _disk.GetFreeBytes(Path.Combine("no-such-folder-here", "file.bin"));

        Assert.NotNull(free);
        Assert.InRange(Math.Abs(free.Value - ReferenceFreeBytes(Directory.GetCurrentDirectory())), 0, _drift);
    }

    [Fact]
    public void A_trailing_separator_makes_no_difference()
    {
        using var temp = new TempFolder();

        var free = _disk.GetFreeBytes(temp.Path + Path.DirectorySeparatorChar);

        Assert.NotNull(free);
        Assert.InRange(Math.Abs(free.Value - ReferenceFreeBytes(temp.Path)), 0, _drift);
    }

    [Fact]
    public void A_path_that_cannot_be_resolved_reports_unknown_instead_of_throwing()
    {
        Assert.Null(_disk.GetFreeBytes("bad\0path"));
    }

    [Fact]
    public void A_blank_path_is_refused()
    {
        Assert.Throws<ArgumentException>(() => _disk.GetFreeBytes(" "));
    }

    private static long ReferenceFreeBytes(string existingFolder) => DriveHolding(existingFolder).AvailableFreeSpace;

    private static long TotalSizeOfVolumeHolding(string existingFolder) => DriveHolding(existingFolder).TotalSize;

    // On Unix DriveInfo takes any path and asks the file system about it; on Windows it wants the drive root.
    private static DriveInfo DriveHolding(string existingFolder) =>
        new(OperatingSystem.IsWindows() ? Path.GetPathRoot(Path.GetFullPath(existingFolder))! : Path.GetFullPath(existingFolder));
}
