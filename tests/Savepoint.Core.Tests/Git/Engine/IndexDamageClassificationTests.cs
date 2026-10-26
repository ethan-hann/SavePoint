using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// What git prints when its index file is damaged. The first two come from the bundled git on a file a power loss emptied or zero-filled;
// the others are the messages git has for the same kind of damage.
public sealed class IndexDamageClassificationTests
{
    [Theory]
    [InlineData("fatal: .git/index: index file smaller than expected\n")]
    [InlineData("error: bad signature 0x00000000\nfatal: index file corrupt\n")]
    [InlineData("error: bad index file sha1 signature\nfatal: index file corrupt\n")]
    [InlineData("fatal: bad index file sha1 signature\n")]
    [InlineData("error: unknown index entry format 0x78670000\nfatal: index file corrupt\n")]
    [InlineData("fatal: unknown index entry format 0x78670000\n")]
    [InlineData("Preparing to update\nfatal: C:/Store/game/install.git/index: index file smaller than expected\n")]
    public void Damage_to_the_index_file_is_recognized(string standardError)
    {
        Assert.True(GitFailure.IsIndexDamage(standardError));
    }

    [Theory]
    [InlineData("")]
    [InlineData("fatal: Unable to create 'x/index.lock': File exists.\n")]
    [InlineData("error: index file x/objects/pack/pack-1.idx is too small\n")]
    [InlineData("fatal: unable to read fac580e900000000000000000000000000000000\n")]
    [InlineData("error: object file x/objects/28/8cb2c9 is empty\nfatal: loose object 288cb2 is corrupt\n")]
    [InlineData("fatal: failed to unpack tree object HEAD\n")]
    [InlineData("error: unable to write new index file\n")]
    public void Other_failures_are_not_taken_for_a_damaged_index(string standardError)
    {
        Assert.False(GitFailure.IsIndexDamage(standardError));
    }
}
