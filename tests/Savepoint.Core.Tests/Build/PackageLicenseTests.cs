using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// The GPL asks a program that is passed on to carry its license, so a package without LICENSE must not get made.
public class PackageLicenseTests
{
    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    private static string Script(string name) => File.ReadAllText(Path.Combine(RepoRoot, "build", name));

    [Fact]
    public void The_Windows_script_wants_LICENSE_in_the_publish_folder_and_in_every_package()
    {
        var text = Script("package.ps1");

        Assert.Contains("'THIRD-PARTY-NOTICES.md', 'LICENSE', 'git/.savepoint-git-stamp'", text, StringComparison.Ordinal);
        Assert.Contains(@"'the license'        = '(^|[/\\])LICENSE$'", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Linux_script_wants_LICENSE_in_the_publish_folder_and_in_the_AppImage()
    {
        var text = Script("package.sh");

        Assert.Contains("for expected in SavePoint THIRD-PARTY-NOTICES.md LICENSE git/.savepoint-git-stamp; do", text, StringComparison.Ordinal);
        Assert.Contains(@"grep -qE '(^|/)LICENSE$' ""$listing"" || fail ""SavePoint.AppImage does not hold the license""", text, StringComparison.Ordinal);
    }
}
