using System.Text;
using Savepoint.App.Platform;
using Savepoint.App.Tests.Platform.Fakes;

namespace Savepoint.App.Tests.Platform;

public sealed class AppsMenuEntryTests : IDisposable
{
    private const string _appImage = "/home/sam/Apps/SavePoint.AppImage";
    private static readonly byte[] _icon256 = [2, 5, 6];
    private static readonly byte[] _icon512 = [5, 1, 2];

    private readonly string _dataHome = Directory.CreateTempSubdirectory("savepoint-appsmenu-").FullName;
    private readonly FakeProcessRunner _runner = new();

    public void Dispose() => Directory.Delete(_dataHome, recursive: true);

    private string Entry => Path.Combine(_dataHome, "applications", "savepoint.desktop");

    private string Icon(int size) => Path.Combine(_dataHome, "icons", "hicolor", $"{size}x{size}", "apps", "savepoint.png");

    private AppsMenuEntry Menu(string appImage = _appImage) => new(appImage, _dataHome, OpenIcon, _runner);

    private static Stream? OpenIcon(int size) => size switch
    {
        256 => new MemoryStream(_icon256),
        512 => new MemoryStream(_icon512),
        _ => null,
    };

    [Fact]
    public async Task Adding_writes_a_desktop_entry_with_exactly_these_fields()
    {
        var outcome = await Menu().AddAsync();

        Assert.Equal(AppsMenuOutcome.Done, outcome);
        Assert.Equal(
            [
                "[Desktop Entry]",
                "Type=Application",
                "Name=SavePoint",
                "Comment=Keep a snapshot of your game before you change it",
                "Exec=\"/home/sam/Apps/SavePoint.AppImage\"",
                "Icon=savepoint",
                "Terminal=false",
                "Categories=Utility;",
                "StartupWMClass=SavePointApp",
                "X-SavePoint-Managed=true",
            ],
            File.ReadAllLines(Entry));
    }

    [Fact]
    public async Task The_entry_names_the_window_class_the_app_sets_and_no_stray_icon_theme_category()
    {
        await Menu().AddAsync();
        var text = File.ReadAllText(Entry);

        Assert.Contains($"StartupWMClass={DesktopIdentity.WindowClass}", text);
        Assert.DoesNotContain("hicolor", text);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public async Task Adding_installs_the_icon_at_each_size_the_app_has()
    {
        await Menu().AddAsync();

        Assert.Equal(_icon256, File.ReadAllBytes(Icon(256)));
        Assert.Equal(_icon512, File.ReadAllBytes(Icon(512)));
        Assert.False(File.Exists(Icon(1024)));
    }

    [Theory]
    [InlineData("/home/sam/My Apps/SavePoint.AppImage", "Exec=\"/home/sam/My Apps/SavePoint.AppImage\"")]
    [InlineData("/home/sam/100%/SavePoint.AppImage", "Exec=\"/home/sam/100%%/SavePoint.AppImage\"")]
    [InlineData("/home/sam/$x/SavePoint.AppImage", "Exec=\"/home/sam/\\\\$x/SavePoint.AppImage\"")]
    [InlineData("/home/sam/a\"b/SavePoint.AppImage", "Exec=\"/home/sam/a\\\\\"b/SavePoint.AppImage\"")]
    public async Task The_path_is_quoted_and_escaped_so_the_shell_runs_this_exact_file(string path, string expectedLine)
    {
        await Menu(path).AddAsync();

        Assert.Contains(expectedLine, File.ReadAllLines(Entry));
    }

    [Fact]
    public void Nothing_is_added_until_asked()
    {
        var menu = Menu();

        Assert.Equal(AppsMenuState.NotAdded, menu.GetState());
        Assert.False(Directory.EnumerateFileSystemEntries(_dataHome).Any());
    }

    [Fact]
    public async Task An_added_entry_that_points_at_this_file_is_up_to_date()
    {
        await Menu().AddAsync();

        Assert.Equal(AppsMenuState.Added, Menu().GetState());
    }

    [Fact]
    public async Task An_entry_for_a_file_that_moved_is_out_of_date_and_adding_again_updates_it()
    {
        await Menu().AddAsync();
        var moved = Menu("/home/sam/Downloads/SavePoint.AppImage");

        Assert.Equal(AppsMenuState.OutOfDate, moved.GetState());
        Assert.Equal(AppsMenuOutcome.Done, await moved.AddAsync());
        Assert.Equal(AppsMenuState.Added, moved.GetState());
        Assert.Contains("Exec=\"/home/sam/Downloads/SavePoint.AppImage\"", File.ReadAllLines(Entry));
    }

    [Fact]
    public async Task An_entry_whose_icon_went_missing_is_out_of_date()
    {
        await Menu().AddAsync();
        File.Delete(Icon(256));

        Assert.Equal(AppsMenuState.OutOfDate, Menu().GetState());
    }

    [Fact]
    public async Task An_entry_someone_else_wrote_is_never_overwritten_or_removed()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Entry)!);
        const string theirs = "[Desktop Entry]\nName=Mine\nExec=something\n";
        File.WriteAllText(Entry, theirs);

        var menu = Menu();

        Assert.Equal(AppsMenuState.Foreign, menu.GetState());
        Assert.Equal(AppsMenuOutcome.Foreign, await menu.AddAsync());
        Assert.Equal(AppsMenuOutcome.Foreign, await menu.RemoveAsync());
        Assert.Equal(theirs, File.ReadAllText(Entry));
        Assert.False(File.Exists(Icon(256)));
        Assert.Empty(_runner.Run);
    }

    [Fact]
    public async Task An_icon_someone_else_put_there_stops_the_add_before_anything_is_written()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Icon(256))!);
        File.WriteAllBytes(Icon(256), [9, 9]);

        Assert.Equal(AppsMenuOutcome.Foreign, await Menu().AddAsync());

        Assert.False(File.Exists(Entry));
        Assert.Equal([9, 9], File.ReadAllBytes(Icon(256)));
    }

    [Fact]
    public async Task Removing_deletes_what_adding_wrote_and_nothing_else()
    {
        var neighbor = Path.Combine(_dataHome, "applications", "other.desktop");
        var neighborIcon = Path.Combine(_dataHome, "icons", "hicolor", "256x256", "apps", "other.png");
        Directory.CreateDirectory(Path.GetDirectoryName(neighbor)!);
        Directory.CreateDirectory(Path.GetDirectoryName(neighborIcon)!);
        File.WriteAllText(neighbor, "x");
        File.WriteAllText(neighborIcon, "x");
        await Menu().AddAsync();

        Assert.Equal(AppsMenuOutcome.Done, await Menu().RemoveAsync());

        Assert.False(File.Exists(Entry));
        Assert.False(File.Exists(Icon(256)));
        Assert.False(File.Exists(Icon(512)));
        Assert.True(File.Exists(neighbor));
        Assert.True(File.Exists(neighborIcon));
        Assert.Equal(AppsMenuState.NotAdded, Menu().GetState());
    }

    [Fact]
    public async Task Removing_when_nothing_was_added_is_done_and_changes_nothing()
    {
        Assert.Equal(AppsMenuOutcome.Done, await Menu().RemoveAsync());
        Assert.Empty(_runner.Run);
    }

    [Fact]
    public async Task The_caches_are_refreshed_for_the_users_own_folders_after_an_add_and_after_a_remove()
    {
        await Menu().AddAsync();
        await Menu().RemoveAsync();

        var applications = Path.Combine(_dataHome, "applications");
        var hicolor = Path.Combine(_dataHome, "icons", "hicolor");
        string[] expected = [applications, hicolor];
        Assert.Equal(4, _runner.Run.Count);
        Assert.Equal("update-desktop-database", _runner.Run[0].FileName);
        Assert.Equal([applications], _runner.Run[0].Arguments);
        Assert.Equal("gtk-update-icon-cache", _runner.Run[1].FileName);
        Assert.Equal(["-q", "-t", "-f", hicolor], _runner.Run[1].Arguments);
        Assert.All(_runner.Run, command => Assert.Contains(command.Arguments[^1], expected));
        Assert.DoesNotContain(_runner.Run, command => command.FileName == "sudo");
    }

    [Fact]
    public async Task Cache_tools_that_are_missing_or_fail_do_not_fail_the_add()
    {
        var missing = new FakeProcessRunner(_ => null);
        var failing = new FakeProcessRunner(_ => new Savepoint.App.Platform.Processes.ProcessOutput(1, string.Empty));

        Assert.Equal(AppsMenuOutcome.Done, await new AppsMenuEntry(_appImage, _dataHome, OpenIcon, missing).AddAsync());
        Assert.Equal(AppsMenuOutcome.Done, await new AppsMenuEntry(_appImage, _dataHome, OpenIcon, failing).AddAsync());
        Assert.True(File.Exists(Entry));
    }

    [Fact]
    public async Task A_folder_that_cannot_be_written_reports_a_failure_instead_of_throwing()
    {
        var notAFolder = Path.Combine(_dataHome, "file");
        File.WriteAllText(notAFolder, "x");

        var outcome = await new AppsMenuEntry(_appImage, notAFolder, OpenIcon, _runner).AddAsync();

        Assert.Equal(AppsMenuOutcome.Failed, outcome);
    }

    [Fact]
    public async Task A_failed_add_leaves_no_half_written_entry_behind()
    {
        // An icon that cannot be read stops the add before the entry is written.
        var broken = new AppsMenuEntry(_appImage, _dataHome, size => size == 512 ? throw new IOException("gone") : OpenIcon(size), _runner);

        Assert.Equal(AppsMenuOutcome.Failed, await broken.AddAsync());

        Assert.False(File.Exists(Entry));
        Assert.False(File.Exists(Icon(256)));
    }

    [Fact]
    public async Task A_write_that_fails_part_way_undoes_a_new_entry()
    {
        // A folder where the larger icon belongs makes that last write fail after the entry and the first icon are in place.
        Directory.CreateDirectory(Icon(512));

        Assert.Equal(AppsMenuOutcome.Failed, await Menu().AddAsync());

        Assert.False(File.Exists(Entry));
        Assert.False(File.Exists(Icon(256)));
    }

    [Fact]
    public void The_data_folder_is_XDG_DATA_HOME_when_it_is_an_absolute_path_and_the_home_default_otherwise()
    {
        var home = Path.Combine(Path.GetTempPath(), "home");
        var absolute = Path.Combine(Path.GetTempPath(), "data");

        Assert.Equal(absolute, AppsMenuEntry.ResolveDataHome(name => name == "XDG_DATA_HOME" ? absolute : null, home));
        Assert.Equal(Path.Combine(home, ".local", "share"), AppsMenuEntry.ResolveDataHome(_ => null, home));
        Assert.Equal(Path.Combine(home, ".local", "share"), AppsMenuEntry.ResolveDataHome(name => name == "XDG_DATA_HOME" ? "" : null, home));
        Assert.Equal(Path.Combine(home, ".local", "share"), AppsMenuEntry.ResolveDataHome(name => name == "XDG_DATA_HOME" ? "relative/path" : null, home));
    }

    [Fact]
    public void The_action_exists_only_on_Linux_and_only_when_the_AppImage_names_its_file()
    {
        Func<string, string?> withFile = name => name == "APPIMAGE" ? _appImage : null;

        Assert.NotNull(AppsMenuEntry.For(isLinux: true, withFile, "/home/sam", OpenIcon, _runner));
        Assert.Null(AppsMenuEntry.For(isLinux: false, withFile, "/home/sam", OpenIcon, _runner));
        Assert.Null(AppsMenuEntry.For(isLinux: true, _ => null, "/home/sam", OpenIcon, _runner));
        Assert.Null(AppsMenuEntry.For(isLinux: true, name => name == "APPIMAGE" ? "SavePoint.AppImage" : null, "/home/sam", OpenIcon, _runner));
    }

    [Fact]
    public async Task The_entry_file_is_utf8_without_a_byte_order_mark()
    {
        await Menu().AddAsync();

        var bytes = File.ReadAllBytes(Entry);
        Assert.True(bytes.Length > 3);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
    }
}
