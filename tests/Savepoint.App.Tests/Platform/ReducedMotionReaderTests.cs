using Savepoint.App.Platform.Motion;
using Savepoint.App.Tests.Platform.Fakes;

namespace Savepoint.App.Tests.Platform;

public class ReducedMotionReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Windows_turns_the_animations_switch_around(bool animationsOn, bool expectedReduced)
    {
        var reader = new WindowsReducedMotionReader(() => animationsOn);

        Assert.Equal(expectedReduced, await reader.ReadAsync(Ct));
    }

    [Fact]
    public async Task Windows_says_nothing_when_the_system_call_fails()
    {
        var reader = new WindowsReducedMotionReader(() => null);

        Assert.Null(await reader.ReadAsync(Ct));
    }

    [Theory]
    [InlineData("1\n", true)]
    [InlineData("0\n", false)]
    [InlineData("1", true)]
    [InlineData("  0  \r\n", false)]
    [InlineData("true\n", true)]
    [InlineData("false\n", false)]
    public async Task macOS_reads_the_defaults_value(string output, bool expectedReduced)
    {
        var reader = new MacReducedMotionReader(FakeProcessRunner.Printing(output));

        Assert.Equal(expectedReduced, await reader.ReadAsync(Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("2\n")]
    [InlineData("maybe\n")]
    [InlineData("1 0\n")]
    public async Task macOS_says_nothing_about_a_value_it_does_not_understand(string output)
    {
        var reader = new MacReducedMotionReader(FakeProcessRunner.Printing(output));

        Assert.Null(await reader.ReadAsync(Ct));
    }

    [Fact]
    public async Task macOS_says_nothing_when_the_key_does_not_exist()
    {
        // defaults exits 1 and explains on stderr when the user never touched the switch.
        var reader = new MacReducedMotionReader(FakeProcessRunner.Printing(string.Empty, exitCode: 1));

        Assert.Null(await reader.ReadAsync(Ct));
    }

    [Fact]
    public async Task macOS_ignores_text_on_standard_output_when_defaults_failed()
    {
        var reader = new MacReducedMotionReader(FakeProcessRunner.Printing("1\n", exitCode: 1));

        Assert.Null(await reader.ReadAsync(Ct));
    }

    [Fact]
    public async Task macOS_asks_defaults_with_an_argument_list()
    {
        var runner = FakeProcessRunner.Printing("0\n");
        var reader = new MacReducedMotionReader(runner);

        await reader.ReadAsync(Ct);

        var command = Assert.Single(runner.Run);
        Assert.Equal("/usr/bin/defaults", command.FileName);
        Assert.Equal(["read", "com.apple.universalaccess", "reduceMotion"], command.Arguments);
    }

    [Fact]
    public async Task macOS_says_nothing_when_defaults_cannot_run()
    {
        var reader = new MacReducedMotionReader(FakeProcessRunner.Unavailable());

        Assert.Null(await reader.ReadAsync(Ct));
    }

    [Theory]
    [InlineData("true\n", false)]
    [InlineData("false\n", true)]
    [InlineData("true", false)]
    [InlineData("  false \r\n", true)]
    public async Task GNOME_turns_the_animations_switch_around(string output, bool expectedReduced)
    {
        var reader = new GnomeReducedMotionReader(FakeProcessRunner.Printing(output));

        Assert.Equal(expectedReduced, await reader.ReadAsync(Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("'true'\n")]
    [InlineData("uint32 1\n")]
    [InlineData("1\n")]
    public async Task GNOME_says_nothing_about_a_value_it_does_not_understand(string output)
    {
        var reader = new GnomeReducedMotionReader(FakeProcessRunner.Printing(output));

        Assert.Null(await reader.ReadAsync(Ct));
    }

    [Fact]
    public async Task GNOME_says_nothing_on_a_desktop_without_the_schema()
    {
        // gsettings exits 1 with "No such schema" on stderr, for KDE and others.
        var reader = new GnomeReducedMotionReader(FakeProcessRunner.Printing(string.Empty, exitCode: 1));

        Assert.Null(await reader.ReadAsync(Ct));
    }

    [Fact]
    public async Task GNOME_asks_gsettings_with_an_argument_list()
    {
        var runner = FakeProcessRunner.Printing("true\n");
        var reader = new GnomeReducedMotionReader(runner);

        await reader.ReadAsync(Ct);

        var command = Assert.Single(runner.Run);
        Assert.Equal("gsettings", command.FileName);
        Assert.Equal(["get", "org.gnome.desktop.interface", "enable-animations"], command.Arguments);
    }

    [Fact]
    public async Task GNOME_says_nothing_when_gsettings_is_not_installed()
    {
        var reader = new GnomeReducedMotionReader(FakeProcessRunner.Unavailable());

        Assert.Null(await reader.ReadAsync(Ct));
    }
}
