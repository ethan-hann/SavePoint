using System.Text;
using System.Text.RegularExpressions;
using Savepoint.Core.Git;
using Savepoint.Core.Tests.Git;

namespace Savepoint.Core.Tests.Build;

// The bundle copied next to the tests is the pruned one: what SavePoint never runs is gone and what it runs still works.
public sealed partial class GitBundlePruneTests : IDisposable
{
    private readonly GitWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void The_bundle_holds_nothing_the_prune_list_names()
    {
        var layout = LayoutName();
        var patterns = GitManifestFile.PrunePatterns(layout);
        var ignoreCase = _ws.Binary.Layout == GitBundleLayout.MinGit;

        var leftovers = GitManifestFile.RelativeEntries(_ws.Binary.Root)
            .Where(path => patterns.Any(pattern => GitManifestFile.Matches(pattern, path, ignoreCase)))
            .Order()
            .ToList();

        Assert.Empty(leftovers);
    }

    [Fact]
    public void The_stamp_records_the_format_and_the_list_that_was_applied()
    {
        var stamp = File.ReadAllLines(Path.Combine(_ws.Binary.Root, ".savepoint-git-stamp"));

        Assert.Contains("format=2", stamp);
        Assert.Contains("prune=" + GitManifestFile.PruneHash(GitManifestFile.PrunePatterns(LayoutName())), stamp);
    }

    [Fact]
    public async Task The_bundle_still_does_everything_the_app_asks_of_git()
    {
        var ct = TestContext.Current.CancellationToken;

        var version = await _ws.Binary.VerifyAsync(ct);
        await _ws.InitAsync();
        _ws.Write("save.dat", "one");
        await _ws.RunAsync("add", "-A");
        await _ws.RunAsync("commit", "-m", "first");
        var first = (await _ws.RunAsync("rev-parse", "HEAD")).StandardOutput.Trim();
        _ws.Write("save.dat", "two");
        await _ws.RunAsync("add", "-A");
        await _ws.RunAsync("commit", "-m", "second");
        await _ws.RunAsync("repack", "-a", "-d");
        await _ws.RunAsync("checkout", "--detach", first);
        var restored = await File.ReadAllTextAsync(Path.Combine(_ws.WorkTree, "save.dat"), ct);
        var blob = await _ws.RunAsync(new GitInvocation(["hash-object", "-w", "--stdin"]) { Stdin = "loose object" });
        var content = await _ws.RunAsync("cat-file", "-p", blob.StandardOutput.Trim());
        var fsck = await _ws.RunAsync("fsck", "--strict");

        Assert.StartsWith("git version ", version);
        Assert.Equal("one", restored);
        Assert.Equal("loose object", content.StandardOutput);
        Assert.Equal(0, fsck.ExitCode);
    }

    [Fact]
    public async Task Network_transports_are_gone_so_a_remote_url_fails_instead_of_reaching_out()
    {
        await _ws.InitAsync();

        var thrown = await Assert.ThrowsAsync<GitCommandException>(() => _ws.RunAsync("ls-remote", "https://127.0.0.1:9/repo.git"));

        Assert.Matches("remote-https|not a git command", thrown.StandardError);
    }

    [Fact]
    public void Every_bundled_library_a_program_imports_by_name_is_still_in_the_bundle()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only the Windows bundle loads libraries by name from its own folders.");
        var bundledNames = BundledLibraryName();
        var missing = new List<string>();

        // Windows looks for a library beside the program, so only the bin folders (where the bundle keeps its programs) are checked.
        foreach (var file in Directory.EnumerateFiles(_ws.Binary.Root, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == "bin")
            .Where(path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            var folder = Path.GetDirectoryName(file)!;
            var text = Encoding.Latin1.GetString(File.ReadAllBytes(file));
            foreach (var name in bundledNames.Matches(text).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(Path.Combine(folder, name)))
                {
                    missing.Add($"{Path.GetRelativePath(_ws.Binary.Root, file)} -> {name}");
                }
            }
        }

        Assert.Empty(missing);
    }

    private string LayoutName() => _ws.Binary.Layout == GitBundleLayout.MinGit ? "mingit" : "dugite";

    // The toolchain's own naming (libfoo-1.dll, msys-foo.dll, zlib1.dll); Windows system libraries are not the bundle's business.
    [GeneratedRegex(@"\b(?:lib[A-Za-z0-9_+\-]+(?:\.[0-9]+)?|msys-[A-Za-z0-9_+\-]+|zlib1)\.dll\b", RegexOptions.IgnoreCase)]
    private static partial Regex BundledLibraryName();
}
