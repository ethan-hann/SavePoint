using Savepoint.Core.Errors;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Paths;

internal static class PathSafetyAssert
{
    public static void Blocked(PathSafety safety, string path, int tier)
    {
        var error = Record.Exception(() => safety.Check(path));
        if (error is not PathSafetyException blocked)
        {
            Assert.Fail($"'{path}' should be blocked as tier {tier} but {(error is null ? "was accepted" : error.GetType().Name)}");
            return;
        }

        Assert.True(Equals(tier, blocked.Args["tier"]), $"'{path}' was blocked as tier {blocked.Args["tier"]}, expected tier {tier}");
    }

    public static void Accepted(PathSafety safety, string path)
    {
        var error = Record.Exception(() => safety.Check(path));
        Assert.True(error is null, $"'{path}' should be accepted but got {error?.GetType().Name}: {error?.Message}");
    }
}
