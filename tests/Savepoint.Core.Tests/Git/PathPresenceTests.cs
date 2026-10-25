using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

public sealed class PathPresenceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "savepoint-presence-" + Guid.NewGuid().ToString("N"));

    public PathPresenceTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_file_that_is_there_is_present()
    {
        var file = Path.Combine(_folder, "a.txt");
        File.WriteAllText(file, "x");

        Assert.Equal(Presence.Present, PathPresence.Look(file));
    }

    [Fact]
    public void A_file_that_is_not_there_is_gone()
    {
        Assert.Equal(Presence.Gone, PathPresence.Look(Path.Combine(_folder, "nope.txt")));
    }

    [Fact]
    public void A_file_under_a_name_that_is_now_a_file_is_gone()
    {
        var file = Path.Combine(_folder, "Folder");
        File.WriteAllText(file, "x");

        Assert.Equal(Presence.Gone, PathPresence.Look(Path.Combine(file, "inner.txt")));
    }

    [Fact]
    public void A_file_that_cannot_be_looked_at_is_unknown_not_gone()
    {
        Assert.Equal(Presence.Unknown, PathPresence.Look(Path.Combine(_folder, "held.bin"), _ => throw new UnauthorizedAccessException()));
        Assert.Equal(Presence.Unknown, PathPresence.Look(Path.Combine(_folder, "held.bin"), _ => throw new IOException("locked")));
    }

    [Fact]
    public void A_name_that_git_printed_in_bytes_that_are_not_text_is_unknown_not_gone()
    {
        // Reading git's output as text turns bytes that are not valid UTF-8 into U+FFFD, so the real file cannot be found by that name.
        Assert.Equal(Presence.Unknown, PathPresence.Look(Path.Combine(_folder, "caf�.txt")));
    }
}
