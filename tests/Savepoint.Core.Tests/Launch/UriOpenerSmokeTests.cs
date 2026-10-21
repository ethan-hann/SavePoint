using Savepoint.Core.Launch;

namespace Savepoint.Core.Tests.Launch;

// The one place a test hands a link to the real OS opener. Opt-in, since it opens whatever handles the link on this machine:
// SAVEPOINT_OS_SMOKE=1 and SAVEPOINT_SMOKE_URI=<a link you are happy to see opened>, for example https://example.com.
public class UriOpenerSmokeTests
{
    private static string? Link => Environment.GetEnvironmentVariable("SAVEPOINT_SMOKE_URI");

    private static bool Enabled => Environment.GetEnvironmentVariable("SAVEPOINT_OS_SMOKE") == "1" && !string.IsNullOrWhiteSpace(Link);

    [Fact]
    public async Task The_real_opener_for_this_os_takes_a_link_without_error()
    {
        Assert.SkipUnless(Enabled, "Set SAVEPOINT_OS_SMOKE=1 and SAVEPOINT_SMOKE_URI=<a link>: this opens it through the real OS opener.");

        var error = await Record.ExceptionAsync(() => GameLaunchers.ForCurrentOs().OpenUriAsync(Link!, TestContext.Current.CancellationToken));

        Assert.Null(error);
    }
}
