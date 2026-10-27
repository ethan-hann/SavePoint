using Savepoint.Core.Services.Storage;
using Savepoint.Core.Tests.Git.Engine;

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
    public void Two_folders_in_one_place_are_on_the_same_volume()
    {
        using var temp = new TempFolder();

        var first = _disk.GetVolumeId(temp.Combine("a", "b"));
        var second = _disk.GetVolumeId(temp.Combine("c"));

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(first, _disk.GetVolumeId(temp.Path + Path.DirectorySeparatorChar));
        Assert.Equal(first, _disk.GetVolumeId(temp.WriteBytes("saves/slot1.sav", 10)));
    }

    [Fact]
    public void A_path_that_cannot_be_resolved_has_no_volume_instead_of_throwing()
    {
        Assert.Null(_disk.GetVolumeId("bad\0path"));
    }

    [Fact]
    public void A_blank_path_has_no_volume_to_ask_about()
    {
        Assert.Throws<ArgumentException>(() => _disk.GetVolumeId(" "));
    }

    [Fact]
    public void A_blank_path_is_refused()
    {
        Assert.Throws<ArgumentException>(() => _disk.GetFreeBytes(" "));
    }

    [Fact]
    public void A_folder_reached_through_a_link_is_on_the_volume_the_link_leads_to()
    {
        using var temp = new TempFolder();
        var elsewhere = FolderOnAnotherVolumeThan(temp.Path);
        Assert.SkipWhen(elsewhere is null, "only one volume to be seen from here");
        Assert.SkipUnless(TestLinks.TryCreateFolderLink(temp.Combine("link"), elsewhere!), "this account cannot create folder links");

        var viaLink = _disk.GetVolumeId(temp.Combine("link", "not", "there", "yet"));

        Assert.NotNull(viaLink);
        Assert.Equal(_disk.GetVolumeId(elsewhere!), viaLink);
        Assert.NotEqual(_disk.GetVolumeId(temp.Path), viaLink);
        TestLinks.RemoveFolderLink(temp.Combine("link"));
    }

    [Fact]
    public void A_folder_reached_through_a_link_reports_the_free_space_of_the_volume_the_link_leads_to()
    {
        using var temp = new TempFolder();
        var elsewhere = FolderOnAnotherVolumeThan(temp.Path);
        Assert.SkipWhen(elsewhere is null, "only one volume to be seen from here");
        Assert.SkipUnless(TestLinks.TryCreateFolderLink(temp.Combine("link"), elsewhere!), "this account cannot create folder links");

        var free = _disk.GetFreeBytes(temp.Combine("link"));

        Assert.NotNull(free);
        Assert.InRange(Math.Abs(free.Value - ReferenceFreeBytes(elsewhere!)), 0, _drift);
        TestLinks.RemoveFolderLink(temp.Combine("link"));
    }

    // An existing folder (only read, never written) on a volume other than the one holding <path>; null when none can be found here. The
    // tests keep their temp folder in the workspace, so the system folder (Windows) or the memory-backed one (Linux) is usually elsewhere.
    private string? FolderOnAnotherVolumeThan(string path)
    {
        var here = _disk.GetVolumeId(path);
        return new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows), "/dev/shm" }.FirstOrDefault(candidate => Directory.Exists(candidate) && _disk.GetVolumeId(candidate) is { } volume && volume != here);
    }

    private static long ReferenceFreeBytes(string existingFolder) => DriveHolding(existingFolder).AvailableFreeSpace;

    private static long TotalSizeOfVolumeHolding(string existingFolder) => DriveHolding(existingFolder).TotalSize;

    // On Unix DriveInfo takes any path and asks the file system about it; on Windows it wants the drive root.
    private static DriveInfo DriveHolding(string existingFolder) =>
        new(OperatingSystem.IsWindows() ? Path.GetPathRoot(Path.GetFullPath(existingFolder))! : Path.GetFullPath(existingFolder));
}
