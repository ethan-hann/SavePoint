using Savepoint.App.Platform;

namespace Savepoint.App.Tests.Platform;

// These make real shell calls that open nothing: turning a path into an item id, and refusing a path that is not there.
// Opening the Explorer window itself is left to the opt-in smoke test.
public sealed class NativeShellItemSelectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "savepoint-shellitem-" + Guid.NewGuid().ToString("N"));

    public NativeShellItemSelectorTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void The_shell_knows_a_file_whose_name_has_commas_and_spaces()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows shell only.");
        var file = Path.Combine(_root, "save, copy (2), final.dat");
        File.WriteAllText(file, "x");

        var itemId = NativeShellItemSelector.Parse(file);

        Assert.NotEqual(0, itemId);
        NativeShellItemSelector.Free(itemId);
    }

    [Fact]
    public void The_shell_gives_nothing_for_a_file_that_is_not_there()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows shell only.");

        Assert.Equal(0, NativeShellItemSelector.Parse(Path.Combine(_root, "gone, again.dat")));
    }

    [Fact]
    public void Selecting_a_file_that_is_not_there_opens_nothing_and_says_so()
    {
        var selector = new NativeShellItemSelector();

        Assert.False(selector.Select(Path.Combine(_root, "gone, again.dat")));
    }

    [Fact]
    public void Off_Windows_there_is_no_shell_to_ask()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has a shell.");
        var file = Path.Combine(_root, "save.dat");
        File.WriteAllText(file, "x");

        Assert.False(new NativeShellItemSelector().Select(file));
    }
}
