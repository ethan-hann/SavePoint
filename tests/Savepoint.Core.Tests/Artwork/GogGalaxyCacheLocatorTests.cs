using Savepoint.Core.Artwork;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Artwork;

public class GogGalaxyCacheLocatorTests
{
    private const string _data = @"C:\ProgramData";
    private static readonly string _webcache = Path.Combine(_data, "GOG.com", "Galaxy", "webcache");

    [Fact]
    public void Windows_finds_the_webcache_folder_under_program_data()
    {
        var locator = new GogGalaxyCacheLocator(OsKind.Windows, _data, folder => folder == _webcache);

        Assert.Equal(_webcache, locator.FindWebcacheFolder());
    }

    [Fact]
    public void A_folder_that_does_not_exist_is_not_returned()
    {
        var locator = new GogGalaxyCacheLocator(OsKind.Windows, _data, _ => false);

        Assert.Null(locator.FindWebcacheFolder());
    }

    [Theory]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void Other_systems_find_nothing_even_when_a_folder_of_that_name_exists(OsKind os)
    {
        var locator = new GogGalaxyCacheLocator(os, _data, _ => true);

        Assert.Null(locator.FindWebcacheFolder());
    }

    [Fact]
    public void A_system_without_a_program_data_folder_finds_nothing()
    {
        var locator = new GogGalaxyCacheLocator(OsKind.Windows, null, _ => true);

        Assert.Null(locator.FindWebcacheFolder());
    }

    [Fact]
    public void A_file_system_error_is_no_folder_not_a_crash()
    {
        var locator = new GogGalaxyCacheLocator(OsKind.Windows, _data, _ => throw new UnauthorizedAccessException());

        Assert.Null(locator.FindWebcacheFolder());
    }
}
