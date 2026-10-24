using Avalonia.Input;
using Savepoint.App.Features.Activity;

namespace Savepoint.App.Tests.Features.Activity;

public sealed class CopyKeyTests
{
    [Fact]
    public void Cmd_C_copies_on_macOS()
    {
        Assert.True(CopyKey.Matches(Key.C, KeyModifiers.Meta, isMacOS: true));
    }

    [Fact]
    public void Ctrl_C_does_not_copy_on_macOS()
    {
        Assert.False(CopyKey.Matches(Key.C, KeyModifiers.Control, isMacOS: true));
    }

    [Fact]
    public void Ctrl_C_copies_elsewhere()
    {
        Assert.True(CopyKey.Matches(Key.C, KeyModifiers.Control, isMacOS: false));
    }

    [Fact]
    public void Cmd_C_does_not_copy_elsewhere()
    {
        Assert.False(CopyKey.Matches(Key.C, KeyModifiers.Meta, isMacOS: false));
    }

    [Fact]
    public void Other_keys_do_not_copy()
    {
        Assert.False(CopyKey.Matches(Key.V, KeyModifiers.Control, isMacOS: false));
    }
}
