using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// The text a failed file step puts in the log and under "Show details": it names what was being done and to which files. The step
// is named by its enum member because the enum is internal and a test method cannot take it.
public sealed class NotUpdatedMessageTests
{
    private static string MessageFor(string step, params string[] paths) =>
        GitEngine.NotUpdatedMessage(Enum.Parse<FileStep>(step), paths);

    [Theory]
    [InlineData("BringToPoint", "Could not write the point's copy of these files: Mods/Foo")]
    [InlineData("RemoveNew", "Could not remove these new files: Mods/Foo")]
    [InlineData("RemoveDisplaced", "Could not remove these files the point being left held: Mods/Foo")]
    [InlineData("RemoveStanding", "Could not remove these files standing where the point being left had a folder: Mods/Foo")]
    public void Each_step_says_what_it_was_doing_to_the_file(string step, string expected)
    {
        Assert.Equal(expected, MessageFor(step, "Mods/Foo"));
    }

    [Theory]
    [InlineData("BringToPoint")]
    [InlineData("RemoveNew")]
    [InlineData("RemoveDisplaced")]
    [InlineData("RemoveStanding")]
    public void No_step_blames_a_checkout(string step)
    {
        Assert.DoesNotContain("checkout", MessageFor(step, "a.txt"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_long_list_names_the_first_twenty_and_counts_the_rest()
    {
        string[] paths = [.. Enumerable.Range(1, 23).Select(i => $"f{i}.txt")];

        var message = MessageFor("RemoveNew", paths);

        Assert.Equal($"Could not remove these new files: {string.Join(", ", paths.Take(20))} and 3 more.", message);
    }
}
