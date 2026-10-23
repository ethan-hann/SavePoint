using System.Text.RegularExpressions;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Build;

// The GPL asks a program that is passed on to carry its license, so a package without LICENSE must not get made.
// The bundled git and libraries ship LICENSE files of their own, so only the file at the app's root may satisfy the check.
public class PackageLicenseTests
{
    private static string RepoRoot => TestEnvironment.RepoRoot ?? throw new InvalidOperationException("These tests read the repository's build files.");

    private static string Script(string name) => File.ReadAllText(Path.Combine(RepoRoot, "build", name));

    // Entry names as vpk writes them: the full package keeps the app under lib/app/, the portable zip under current/.
    private static readonly string[] _windowsRoots = ["lib/app/LICENSE", "current/LICENSE"];

    private static readonly string[] _windowsNested =
    [
        "lib/app/git/LICENSE.txt",
        "lib/app/git/ucrt64/share/licenses/zlib/LICENSE",
        "lib/app/licenses/LICENSE",
        "current/git/usr/share/licenses/ncurses/LICENSE",
        "current/licenses/LICENSE",
        "LICENSE",
        "lib/LICENSE",
        "lib/app/license",
        "lib/app/LICENSE.md",
        "xlib/app/LICENSE",
    ];

    [Fact]
    public void The_Windows_script_wants_LICENSE_in_the_publish_folder_and_in_every_package()
    {
        var text = Script("package.ps1");

        Assert.Contains("'THIRD-PARTY-NOTICES.md', 'LICENSE', 'git/.savepoint-git-stamp'", text, StringComparison.Ordinal);
        Assert.Contains("Where-Object { $_ -cmatch $needed[$what] }", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Windows_license_check_accepts_only_the_file_at_the_app_root_in_the_nupkg_and_the_zip()
    {
        var pattern = WindowsPattern();

        foreach (var entry in _windowsRoots)
        {
            Assert.Matches(pattern, entry);
        }

        foreach (var entry in _windowsNested)
        {
            Assert.DoesNotMatch(pattern, entry);
        }
    }

    [Fact]
    public void The_Linux_script_wants_LICENSE_in_the_publish_folder_and_in_the_AppImage()
    {
        var text = Script("package.sh");

        Assert.Contains("for expected in SavePoint THIRD-PARTY-NOTICES.md LICENSE git/.savepoint-git-stamp; do", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Linux_license_check_accepts_only_the_file_in_the_apps_folder_of_the_AppImage()
    {
        var pattern = LinuxPattern();

        Assert.Matches(pattern, "./usr/bin/LICENSE");
        foreach (var entry in new[]
        {
            "./usr/bin/git/LICENSE",
            "./usr/bin/licenses/LICENSE",
            "./usr/bin/git/ucrt64/share/licenses/zlib/LICENSE",
            "./usr/share/doc/LICENSE",
            "./LICENSE",
            "./usr/bin/license",
            "./usr/bin/LICENSE.txt",
        })
        {
            Assert.DoesNotMatch(pattern, entry);
        }
    }

    private static Regex WindowsPattern()
    {
        var found = Regex.Match(Script("package.ps1"), @"'the license'\s*=\s*'(?<pattern>[^']+)'");
        Assert.True(found.Success, "package.ps1 has no 'the license' entry in the list of files a package must hold.");
        return new Regex(found.Groups["pattern"].Value, RegexOptions.CultureInvariant);
    }

    private static Regex LinuxPattern()
    {
        var found = Regex.Match(Script("package.sh"), @"grep -qE '(?<pattern>[^']+)' ""\$listing"" \|\| fail ""SavePoint\.AppImage does not hold the license""");
        Assert.True(found.Success, "package.sh has no grep for the license in the AppImage listing.");
        return new Regex(found.Groups["pattern"].Value, RegexOptions.CultureInvariant);
    }
}
