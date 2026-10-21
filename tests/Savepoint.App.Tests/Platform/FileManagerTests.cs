using Savepoint.App.Platform;
using Savepoint.App.Tests.Platform.Fakes;
using Savepoint.Core.Platform;

namespace Savepoint.App.Tests.Platform;

public sealed class FileManagerTests : IDisposable
{
    private const string _explorer = "C:\\Windows\\explorer.exe";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "savepoint-filemanager-" + Guid.NewGuid().ToString("N"));
    private readonly FakeProcessRunner _runner = new();
    private readonly FakeShellItemSelector _selector = new();

    public FileManagerTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private FileManager For(OsKind os) => new(os, _runner, _explorer, _selector);

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
    public void Windows_selects_a_file_through_the_shell_without_a_command_line()
    {
        var file = NewFile();

        var started = For(OsKind.Windows).Reveal(file);

        Assert.True(started);
        Assert.Equal([file], _selector.Selected);
        Assert.Empty(_runner.Started);
    }

    [Fact]
    public void A_comma_in_the_path_reaches_the_shell_untouched()
    {
        // Explorer's own /select, switch splits on commas, which is why the shell call is used.
        var file = NewFile("save, copy (2), final.dat");

        Assert.True(For(OsKind.Windows).Reveal(file));

        Assert.Equal([file], _selector.Selected);
        Assert.Empty(_runner.Started);
    }

    [Fact]
    public void A_comma_in_a_folder_name_stays_inside_the_one_argument_Explorer_is_started_with()
    {
        var folder = NewFolder("Saves, backup");

        Assert.True(For(OsKind.Windows).Reveal(folder));

        Assert.Empty(_selector.Selected);
        Assert.Equal([folder], Assert.Single(_runner.Started).Arguments);
    }

    [Fact]
    public void When_the_shell_refuses_Windows_falls_back_to_starting_Explorer_with_an_argument_list()
    {
        _selector.Result = false;
        var file = NewFile();

        var started = For(OsKind.Windows).Reveal(file);

        Assert.True(started);
        Assert.Equal([file], _selector.Selected);
        var command = Assert.Single(_runner.Started);
        Assert.Equal(_explorer, command.FileName);
        Assert.Equal(["/select,", file], command.Arguments);
    }

    [Fact]
    public void When_the_shell_call_throws_Windows_falls_back_to_starting_Explorer()
    {
        _selector.Throws = new InvalidOperationException("boom");
        var file = NewFile();

        var error = Record.Exception(() => For(OsKind.Windows).Reveal(file));

        Assert.Null(error);
        Assert.Equal(["/select,", file], Assert.Single(_runner.Started).Arguments);
    }

    [Fact]
    public void When_the_shell_refuses_and_Explorer_cannot_be_started_the_answer_is_false()
    {
        _selector.Result = false;
        _runner.CanStart = false;

        Assert.False(For(OsKind.Windows).Reveal(NewFile()));
    }

    [Theory]
    [InlineData(OsKind.MacOS)]
    [InlineData(OsKind.Linux)]
    public void The_other_systems_never_ask_the_Windows_shell(OsKind os)
    {
        Assert.True(For(os).Reveal(NewFile()));

        Assert.Empty(_selector.Selected);
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

        if (os == OsKind.Windows)
        {
            Assert.Equal([file], _selector.Selected);
            Assert.Empty(_runner.Started);
            return;
        }

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
        Assert.Empty(_selector.Selected);
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
        _selector.Result = false;
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
