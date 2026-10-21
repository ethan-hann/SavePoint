using Savepoint.App.Platform;
using Savepoint.App.Tests.Platform.Fakes;
using Savepoint.Core.Platform;

namespace Savepoint.App.Tests.Platform;

public sealed class FileManagerTests : IDisposable
{
    private const string _explorer = "C:\\Windows\\explorer.exe";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "savepoint-filemanager-" + Guid.NewGuid().ToString("N"));
    private readonly FakeProcessRunner _runner = new();

    public FileManagerTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private FileManager For(OsKind os) => new(os, _runner, _explorer);

    private string NewFile(string name = "save.dat")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private string NewFolder(string name = "Saves")
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Windows_selects_a_file_in_Explorer()
    {
        var file = NewFile();

        var started = For(OsKind.Windows).Reveal(file);

        Assert.True(started);
        var command = Assert.Single(_runner.Started);
        Assert.Equal(_explorer, command.FileName);
        Assert.Equal(["/select,", file], command.Arguments);
    }

    [Fact]
    public void Windows_opens_a_folder_in_Explorer()
    {
        var folder = NewFolder();

        Assert.True(For(OsKind.Windows).Reveal(folder));

        var command = Assert.Single(_runner.Started);
        Assert.Equal(_explorer, command.FileName);
        Assert.Equal([folder], command.Arguments);
    }

    [Fact]
    public void MacOS_reveals_a_file_in_Finder()
    {
        var file = NewFile();

        Assert.True(For(OsKind.MacOS).Reveal(file));

        var command = Assert.Single(_runner.Started);
        Assert.Equal("/usr/bin/open", command.FileName);
        Assert.Equal(["-R", file], command.Arguments);
    }

    [Fact]
    public void MacOS_opens_a_folder_in_Finder()
    {
        var folder = NewFolder();

        Assert.True(For(OsKind.MacOS).Reveal(folder));

        var command = Assert.Single(_runner.Started);
        Assert.Equal("/usr/bin/open", command.FileName);
        Assert.Equal([folder], command.Arguments);
    }

    [Fact]
    public void Linux_opens_the_folder_that_holds_a_file()
    {
        var file = NewFile();

        Assert.True(For(OsKind.Linux).Reveal(file));

        var command = Assert.Single(_runner.Started);
        Assert.Equal("xdg-open", command.FileName);
        Assert.Equal([_root], command.Arguments);
    }

    [Fact]
    public void Linux_opens_a_folder_itself()
    {
        var folder = NewFolder();

        Assert.True(For(OsKind.Linux).Reveal(folder));

        var command = Assert.Single(_runner.Started);
        Assert.Equal("xdg-open", command.FileName);
        Assert.Equal([folder], command.Arguments);
    }

    [Theory]
    [InlineData(OsKind.Windows)]
    [InlineData(OsKind.MacOS)]
    [InlineData(OsKind.Linux)]
    public void A_path_with_spaces_quotes_and_a_leading_dash_in_its_name_stays_one_argument(OsKind os)
    {
        // Quotes cannot be in a Windows file name, so the hostile name is only possible elsewhere.
        var name = OperatingSystem.IsWindows() ? "-r my save (1)&more.dat" : "-r \"my\" save $(x);&more.dat";
        var file = NewFile(name);

        Assert.True(For(os).Reveal(file));

        var command = Assert.Single(_runner.Started);
        var expectedLast = os == OsKind.Linux ? _root : file;
        Assert.Equal(expectedLast, command.Arguments[^1]);
        Assert.DoesNotContain(command.Arguments, argument => argument.StartsWith('-') && argument != "-R");
    }

    [Theory]
    [InlineData(OsKind.Windows)]
    [InlineData(OsKind.MacOS)]
    [InlineData(OsKind.Linux)]
    public void A_trailing_separator_and_dot_segments_are_tidied_away(OsKind os)
    {
        var folder = NewFolder();
        var untidy = folder + Path.DirectorySeparatorChar + "." + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "Saves" + Path.DirectorySeparatorChar;

        Assert.True(For(os).Reveal(untidy));

        Assert.Equal(folder, Assert.Single(_runner.Started).Arguments[^1]);
    }

    [Theory]
    [InlineData(OsKind.Windows)]
    [InlineData(OsKind.MacOS)]
    [InlineData(OsKind.Linux)]
    public void A_path_that_does_not_exist_starts_nothing(OsKind os)
    {
        var missing = Path.Combine(_root, "gone", "save.dat");

        Assert.False(For(os).Reveal(missing));
        Assert.Empty(_runner.Started);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("save.dat")]
    [InlineData("..")]
    [InlineData("-rf")]
    [InlineData("Saves/save.dat")]
    public void An_empty_or_relative_path_starts_nothing(string path)
    {
        Assert.False(For(OsKind.Linux).Reveal(path));
        Assert.False(For(OsKind.MacOS).Reveal(path));
        Assert.False(For(OsKind.Windows).Reveal(path));
        Assert.Empty(_runner.Started);
    }

    [Theory]
    [InlineData(OsKind.Windows)]
    [InlineData(OsKind.MacOS)]
    [InlineData(OsKind.Linux)]
    public void A_file_manager_that_cannot_be_started_gives_false(OsKind os)
    {
        _runner.CanStart = false;
        var file = NewFile();

        Assert.False(For(os).Reveal(file));
        Assert.Single(_runner.Started);
    }

    [Fact]
    public void The_file_manager_of_this_machine_is_built_without_starting_anything()
    {
        var manager = FileManager.CreateForCurrentOs();

        Assert.NotNull(manager);
    }
}
