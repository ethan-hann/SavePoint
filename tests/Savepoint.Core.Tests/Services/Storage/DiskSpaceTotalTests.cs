using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Tests.Services.Storage;

public class DiskSpaceTotalTests
{
    private readonly DiskSpace _disk = new();

    [Fact]
    public void An_existing_folder_reports_the_size_of_its_volume()
    {
        using var temp = new TempFolder();

        var total = _disk.GetTotalBytes(temp.Path);

        Assert.NotNull(total);
        Assert.Equal(new DriveInfo(OperatingSystem.IsWindows() ? Path.GetPathRoot(temp.Path)! : temp.Path).TotalSize, total.Value);
    }

    [Fact]
    public void A_volume_is_never_smaller_than_its_free_space()
    {
        using var temp = new TempFolder();

        Assert.True(_disk.GetTotalBytes(temp.Path) >= _disk.GetFreeBytes(temp.Path));
    }

    [Fact]
    public void A_path_that_does_not_exist_reports_the_volume_of_its_nearest_existing_folder()
    {
        using var temp = new TempFolder();

        Assert.Equal(_disk.GetTotalBytes(temp.Path), _disk.GetTotalBytes(temp.Combine("not", "there", "yet")));
        Assert.False(Directory.Exists(temp.Combine("not")), "asking must not create anything");
    }

    [Fact]
    public void A_blank_path_is_a_caller_mistake()
    {
        Assert.Throws<ArgumentException>(() => _disk.GetTotalBytes(" "));
    }
}
