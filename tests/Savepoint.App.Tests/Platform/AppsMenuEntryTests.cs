using System.Text;
using Avalonia.Headless.XUnit;
using Savepoint.App.Platform;
using Savepoint.App.Platform.Processes;
using Savepoint.App.Tests.Platform.Fakes;

namespace Savepoint.App.Tests.Platform;

public sealed class AppsMenuEntryTests : IDisposable
{
    private const string _appImage = "/home/sam/Apps/SavePoint.AppImage";
    private const string _comment = "Keep a snapshot of your game before you change it";
    private const string _appDir = "/tmp/.mount_SavePoabc123";
    private const string _processPath = "/tmp/.mount_SavePoabc123/usr/bin/SavePoint";
    private static readonly byte[] _icon256 = [2, 5, 6];
    private static readonly byte[] _icon512 = [5, 1, 2];

    private readonly string _dataHome = Directory.CreateTempSubdirectory("savepoint-appsmenu-").FullName;
    private readonly FakeProcessRunner _runner = new();

    public void Dispose() => Directory.Delete(_dataHome, recursive: true);

    private string Entry => Path.Combine(_dataHome, "applications", "savepoint.desktop");

    private string Hicolor => Path.Combine(_dataHome, "icons", "hicolor");

    private string Icon(int size) => Path.Combine(Hicolor, $"{size}x{size}", "apps", "savepoint.png");

    private AppsMenuEntry Menu(string appImage = _appImage) => new(appImage, _dataHome, OpenIcon, _runner);

    private static Stream? OpenIcon(int size) => size switch
    {
        256 => new MemoryStream(_icon256),
        512 => new MemoryStream(_icon512),
        _ => null,
    };

    private static string? Environment(string name, string appImage = _appImage, string? appDir = _appDir) => name switch
    {
        "APPIMAGE" => appImage,
        "APPDIR" => appDir,
        _ => null,
    };

    [Fact]
    public async Task Adding_writes_a_desktop_entry_with_exactly_these_fields()
    {
        var outcome = await Menu().AddAsync(_comment);

        Assert.Equal(AppsMenuOutcome.Done, outcome);
        Assert.Equal(
            [
                "[Desktop Entry]",
                "Type=Application",
                "Name=SavePoint",
                "Comment=Keep a snapshot of your game before you change it",
                "Exec=\"/home/sam/Apps/SavePoint.AppImage\"",
                "TryExec=/home/sam/Apps/SavePoint.AppImage",
                "Icon=savepoint",
                "Terminal=false",
                "Categories=Utility;",
                "StartupWMClass=SavePointApp",
                "X-SavePoint-Managed=true",
            ],
            File.ReadAllLines(Entry));
    }

    [Fact]
    public async Task The_comment_is_the_text_the_caller_gives()
    {
        await Menu().AddAsync("Un texte du catalogue");

        Assert.Contains("Comment=Un texte du catalogue", File.ReadAllLines(Entry));
    }

    [Fact]
    public async Task The_entry_names_the_window_class_the_app_sets_and_no_stray_icon_theme_category()
    {
        await Menu().AddAsync(_comment);
        var text = File.ReadAllText(Entry);

        Assert.Contains($"StartupWMClass={DesktopIdentity.WindowClass}", text);
        Assert.DoesNotContain("hicolor", text);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public async Task Adding_installs_the_icon_at_each_size_the_app_has()
    {
        await Menu().AddAsync(_comment);

        Assert.Equal(_icon256, File.ReadAllBytes(Icon(256)));
        Assert.Equal(_icon512, File.ReadAllBytes(Icon(512)));
        Assert.False(File.Exists(Icon(1024)));
    }

    [Theory]
    [InlineData("/home/sam/My Apps/SavePoint.AppImage", "Exec=\"/home/sam/My Apps/SavePoint.AppImage\"")]
    [InlineData("/home/sam/100%/SavePoint.AppImage", "Exec=\"/home/sam/100%%/SavePoint.AppImage\"")]
    [InlineData("/home/sam/$x/SavePoint.AppImage", "Exec=\"/home/sam/\\\\$x/SavePoint.AppImage\"")]
    [InlineData("/home/sam/a\"b/SavePoint.AppImage", "Exec=\"/home/sam/a\\\\\"b/SavePoint.AppImage\"")]
    [InlineData("/home/sam/a`b/SavePoint.AppImage", "Exec=\"/home/sam/a\\\\`b/SavePoint.AppImage\"")]
    [InlineData("/home/sam/a\\b/SavePoint.AppImage", "Exec=\"/home/sam/a\\\\\\\\b/SavePoint.AppImage\"")]
    public async Task The_path_is_quoted_and_escaped_so_the_shell_runs_this_exact_file(string path, string expectedLine)
    {
        await Menu(path).AddAsync(_comment);

        Assert.Contains(expectedLine, File.ReadAllLines(Entry));
    }

    [Fact]
    public async Task The_try_exec_line_is_the_plain_path_with_only_backslashes_doubled()
    {
        await Menu("/home/sam/My Apps/a\\b/SavePoint.AppImage").AddAsync(_comment);

        Assert.Contains("TryExec=/home/sam/My Apps/a\\\\b/SavePoint.AppImage", File.ReadAllLines(Entry));
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
        await Menu().AddAsync(_comment);

        Assert.Equal(AppsMenuState.Added, Menu().GetState());
    }

    [Fact]
    public async Task An_entry_for_a_file_that_moved_is_out_of_date_and_adding_again_updates_it()
    {
        await Menu().AddAsync(_comment);
        var moved = Menu("/home/sam/Downloads/SavePoint.AppImage");

        Assert.Equal(AppsMenuState.OutOfDate, moved.GetState());
        Assert.Equal(AppsMenuOutcome.Done, await moved.AddAsync(_comment));
        Assert.Equal(AppsMenuState.Added, moved.GetState());
        Assert.Contains("Exec=\"/home/sam/Downloads/SavePoint.AppImage\"", File.ReadAllLines(Entry));
    }

    [Fact]
    public async Task An_entry_without_the_try_exec_line_is_out_of_date()
    {
        await Menu().AddAsync(_comment);
        File.WriteAllLines(Entry, File.ReadAllLines(Entry).Where(line => !line.StartsWith("TryExec=", StringComparison.Ordinal)));

        Assert.Equal(AppsMenuState.OutOfDate, Menu().GetState());
    }

    [Fact]
    public async Task An_entry_whose_icon_went_missing_is_out_of_date()
    {
        await Menu().AddAsync(_comment);
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
        Assert.Equal(AppsMenuOutcome.Foreign, await menu.AddAsync(_comment));
        Assert.Equal(AppsMenuOutcome.Foreign, await menu.RemoveAsync());
        Assert.Equal(theirs, File.ReadAllText(Entry));
        Assert.False(File.Exists(Icon(256)));
        Assert.Empty(_runner.Run);
    }

    [Fact]
    public async Task An_entry_file_over_64_KB_is_treated_as_someone_elses_and_not_read()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Entry)!);
        File.WriteAllText(Entry, "X-SavePoint-Managed=true\n" + new string('a', 70 * 1024));

        Assert.Equal(AppsMenuState.Foreign, Menu().GetState());
        Assert.Equal(AppsMenuOutcome.Foreign, await Menu().AddAsync(_comment));
    }

    [Fact]
    public async Task A_folder_where_the_entry_belongs_is_treated_as_someone_elses()
    {
        Directory.CreateDirectory(Entry);

        Assert.Equal(AppsMenuState.Foreign, Menu().GetState());
        Assert.Equal(AppsMenuOutcome.Foreign, await Menu().AddAsync(_comment));
    }

    [Fact]
    public async Task Icons_left_behind_without_an_entry_do_not_lock_the_player_out()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Icon(256))!);
        File.WriteAllBytes(Icon(256), [9, 9]);

        Assert.Equal(AppsMenuState.NotAdded, Menu().GetState());
        Assert.Equal(AppsMenuOutcome.Done, await Menu().AddAsync(_comment));

        Assert.True(File.Exists(Entry));
        Assert.Equal(_icon256, File.ReadAllBytes(Icon(256)));
    }

    [Fact]
    public async Task Removing_cleans_icons_left_behind_without_an_entry()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Icon(256))!);
        File.WriteAllBytes(Icon(256), [9, 9]);

        Assert.Equal(AppsMenuOutcome.Done, await Menu().RemoveAsync());

        Assert.False(File.Exists(Icon(256)));
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
        await Menu().AddAsync(_comment);

        Assert.Equal(AppsMenuOutcome.Done, await Menu().RemoveAsync());

        Assert.False(File.Exists(Entry));
        Assert.False(File.Exists(Icon(256)));
        Assert.False(File.Exists(Icon(512)));
        Assert.True(File.Exists(neighbor));
        Assert.True(File.Exists(neighborIcon));
        Assert.Equal(AppsMenuState.NotAdded, Menu().GetState());
    }

    [Fact]
    public async Task Removing_an_out_of_date_entry_deletes_it_too()
    {
        await Menu("/home/sam/Old/SavePoint.AppImage").AddAsync(_comment);

        Assert.Equal(AppsMenuOutcome.Done, await Menu().RemoveAsync());

        Assert.False(File.Exists(Entry));
        Assert.False(File.Exists(Icon(256)));
    }

    [Fact]
    public async Task A_remove_that_cannot_delete_a_file_reports_a_failure_and_keeps_the_entry()
    {
        await Menu().AddAsync(_comment);
        File.Delete(Icon(512));
        Directory.CreateDirectory(Icon(512));

        Assert.Equal(AppsMenuOutcome.Failed, await Menu().RemoveAsync());

        Assert.True(File.Exists(Entry));
    }

    [Fact]
    public async Task Removing_when_nothing_was_added_is_done_and_changes_nothing()
    {
        Assert.Equal(AppsMenuOutcome.Done, await Menu().RemoveAsync());
        Assert.Empty(_runner.Run);
    }

    [Fact]
    public async Task The_icon_cache_tool_runs_only_when_the_user_already_has_a_cache_file()
    {
        Directory.CreateDirectory(Hicolor);
        File.WriteAllBytes(Path.Combine(Hicolor, "icon-theme.cache"), [1]);

        await Menu().AddAsync(_comment);
        await Menu().RemoveAsync();

        var applications = Path.Combine(_dataHome, "applications");
        Assert.Equal(4, _runner.Run.Count);
        Assert.Equal("update-desktop-database", _runner.Run[0].FileName);
        Assert.Equal([applications], _runner.Run[0].Arguments);
        Assert.Equal("gtk-update-icon-cache", _runner.Run[1].FileName);
        Assert.Equal(["-q", "-t", "-f", Hicolor], _runner.Run[1].Arguments);
        Assert.DoesNotContain(_runner.Run, command => command.FileName == "sudo");
    }

    [Fact]
    public async Task Without_a_cache_file_none_is_created_and_the_icon_folder_is_touched_instead()
    {
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Directory.CreateDirectory(Hicolor);
        Directory.SetLastWriteTimeUtc(Hicolor, old);

        await Menu().AddAsync(_comment);

        Assert.False(File.Exists(Path.Combine(Hicolor, "icon-theme.cache")));
        Assert.DoesNotContain(_runner.Run, command => command.FileName == "gtk-update-icon-cache");
        Assert.Contains(_runner.Run, command => command.FileName == "update-desktop-database");
        Assert.True(Directory.GetLastWriteTimeUtc(Hicolor) > old);
    }

    [Fact]
    public async Task Cache_tools_that_are_missing_or_fail_do_not_fail_the_add()
    {
        Directory.CreateDirectory(Hicolor);
        File.WriteAllBytes(Path.Combine(Hicolor, "icon-theme.cache"), [1]);
        var missing = new FakeProcessRunner(_ => null);
        var failing = new FakeProcessRunner(_ => new ProcessOutput(1, string.Empty));

        Assert.Equal(AppsMenuOutcome.Done, await new AppsMenuEntry(_appImage, _dataHome, OpenIcon, missing).AddAsync(_comment));
        Assert.Equal(AppsMenuOutcome.Done, await new AppsMenuEntry(_appImage, _dataHome, OpenIcon, failing).AddAsync(_comment));
        Assert.True(File.Exists(Entry));
    }

    [Fact]
    public async Task A_folder_that_cannot_be_written_reports_a_failure_instead_of_throwing()
    {
        var notAFolder = Path.Combine(_dataHome, "file");
        File.WriteAllText(notAFolder, "x");

        var outcome = await new AppsMenuEntry(_appImage, notAFolder, OpenIcon, _runner).AddAsync(_comment);

        Assert.Equal(AppsMenuOutcome.Failed, outcome);
    }

    [Fact]
    public async Task A_failed_add_leaves_no_half_written_entry_behind()
    {
        // An icon that cannot be read stops the add before the entry is written.
        var broken = new AppsMenuEntry(_appImage, _dataHome, size => size == 512 ? throw new IOException("gone") : OpenIcon(size), _runner);

        Assert.Equal(AppsMenuOutcome.Failed, await broken.AddAsync(_comment));

        Assert.False(File.Exists(Entry));
        Assert.False(File.Exists(Icon(256)));
    }

    [Fact]
    public async Task An_icon_size_the_app_does_not_carry_fails_the_add_instead_of_leaving_an_entry_that_is_never_current()
    {
        var missingLarge = new AppsMenuEntry(_appImage, _dataHome, size => size == 512 ? null : OpenIcon(size), _runner);

        Assert.Equal(AppsMenuOutcome.Failed, await missingLarge.AddAsync(_comment));

        Assert.False(File.Exists(Entry));
        Assert.False(File.Exists(Icon(256)));
    }

    [Fact]
    public async Task A_write_that_fails_part_way_undoes_a_new_entry_and_leaves_no_temporary_files()
    {
        // A folder where the larger icon belongs makes that last write fail after the first icon is in place.
        Directory.CreateDirectory(Icon(512));

        Assert.Equal(AppsMenuOutcome.Failed, await Menu().AddAsync(_comment));

        Assert.False(File.Exists(Entry));
        Assert.False(File.Exists(Icon(256)));
        Assert.Empty(Directory.EnumerateFiles(_dataHome, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_failed_update_keeps_the_entry_that_was_working()
    {
        await Menu().AddAsync(_comment);
        File.Delete(Icon(512));
        Directory.CreateDirectory(Icon(512));

        Assert.Equal(AppsMenuOutcome.Failed, await Menu("/home/sam/Downloads/SavePoint.AppImage").AddAsync(_comment));

        Assert.Contains("Exec=\"/home/sam/Apps/SavePoint.AppImage\"", File.ReadAllLines(Entry));
        Assert.Empty(Directory.EnumerateFiles(_dataHome, "*.tmp", SearchOption.AllDirectories));
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
        Assert.NotNull(AppsMenuEntry.For(isLinux: true, name => Environment(name), "/home/sam", OpenIcon, _runner, _processPath));
        Assert.Null(AppsMenuEntry.For(isLinux: false, name => Environment(name), "/home/sam", OpenIcon, _runner, _processPath));
        Assert.Null(AppsMenuEntry.For(isLinux: true, _ => null, "/home/sam", OpenIcon, _runner, _processPath));
        Assert.Null(AppsMenuEntry.For(isLinux: true, name => Environment(name, "SavePoint.AppImage"), "/home/sam", OpenIcon, _runner, _processPath));
    }

    [Fact]
    public void A_path_with_a_line_break_gets_no_action()
    {
        Assert.Null(AppsMenuEntry.For(isLinux: true, name => Environment(name, "/home/sam/a\nb/SavePoint.AppImage"), "/home/sam", OpenIcon, _runner, _processPath));
    }

    [Fact]
    public void The_variable_is_trusted_only_when_this_process_runs_from_the_mounted_AppImage_folder()
    {
        // Started from another AppImage's terminal, or from an extracted copy: the variable names a program that is not this one.
        Assert.Null(AppsMenuEntry.For(isLinux: true, name => Environment(name, appDir: null), "/home/sam", OpenIcon, _runner, _processPath));
        Assert.Null(AppsMenuEntry.For(isLinux: true, name => Environment(name), "/home/sam", OpenIcon, _runner, "/home/sam/extracted/usr/bin/SavePoint"));
        Assert.Null(AppsMenuEntry.For(isLinux: true, name => Environment(name), "/home/sam", OpenIcon, _runner, null));
        Assert.Null(AppsMenuEntry.For(isLinux: true, name => Environment(name), "/home/sam", OpenIcon, _runner, "/tmp/.mount_SavePoabc123x/usr/bin/SavePoint"));
        Assert.Null(AppsMenuEntry.For(isLinux: true, name => Environment(name, appDir: "/tmp/.mount_SavePoabc123/../elsewhere"), "/home/sam", OpenIcon, _runner, _processPath));
        Assert.NotNull(AppsMenuEntry.For(isLinux: true, name => Environment(name, appDir: _appDir + "/"), "/home/sam", OpenIcon, _runner, _processPath));
    }

    [Fact]
    public async Task The_entry_file_is_utf8_without_a_byte_order_mark()
    {
        await Menu().AddAsync(_comment);

        var bytes = File.ReadAllBytes(Entry);
        Assert.True(bytes.Length > 3);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
    }

    [AvaloniaFact]
    public void The_bundled_icons_the_entry_installs_resolve_through_the_production_loader()
    {
        foreach (var size in new[] { 256, 512 })
        {
            using var stream = AppsMenuEntry.OpenBundledIcon(size);

            Assert.NotNull(stream);
            var header = new byte[8];
            Assert.Equal(8, stream.Read(header, 0, 8));
            Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], header);
        }
    }
}
