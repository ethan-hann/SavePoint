using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Library;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Library;

// The launch a library game is added with: the template's program when it can run here, the store's own way to start it otherwise.
public class LibraryLaunchTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "savepoint-launch-" + Guid.NewGuid().ToString("N"));

    private static GameTemplate Template() =>
        new("skyrim-se", "Skyrim", new GameDefinition(Id: "skyrim-se", Name: "Skyrim", Launch: new LaunchConfig(LaunchMethod.Exe, "SkyrimSELauncher.exe")));

    private InstalledGame Game() =>
        new(LibrarySource.Steam, "489830", "Skyrim", _folder, "Skyrim Special Edition", new LaunchConfig(LaunchMethod.Uri, Uri: "steam://rungameid/489830"), 489830);

    public LibraryLaunchTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void On_windows_the_templates_program_is_used_when_it_is_in_the_install_folder()
    {
        File.WriteAllText(Path.Combine(_folder, "SkyrimSELauncher.exe"), "x");

        var launch = LibraryScanner.LaunchFor(Template(), Game(), OsKind.Windows);

        Assert.Equal(LaunchMethod.Exe, launch.Method);
        Assert.Equal("SkyrimSELauncher.exe", launch.ExePath);
    }

    [Theory]
    [InlineData(OsKind.Linux)]
    [InlineData(OsKind.MacOS)]
    public void Where_a_windows_program_cannot_run_the_store_launch_is_used(OsKind os)
    {
        File.WriteAllText(Path.Combine(_folder, "SkyrimSELauncher.exe"), "x");

        var launch = LibraryScanner.LaunchFor(Template(), Game(), os);

        Assert.Equal(LaunchMethod.Uri, launch.Method);
        Assert.Equal("steam://rungameid/489830", launch.Uri);
    }

    [Fact]
    public void A_templates_program_that_is_not_in_the_install_folder_gives_way_to_the_store_launch()
    {
        var launch = LibraryScanner.LaunchFor(Template(), Game(), OsKind.Windows);

        Assert.Equal("steam://rungameid/489830", launch.Uri);
    }

    [Fact]
    public void A_template_that_starts_through_a_link_keeps_its_link()
    {
        var template = Template() with { Definition = Template().Definition with { Launch = new LaunchConfig(LaunchMethod.Uri, Uri: "steam://run/489830") } };

        var launch = LibraryScanner.LaunchFor(template, Game(), OsKind.Linux);

        Assert.Equal("steam://run/489830", launch.Uri);
    }
}
