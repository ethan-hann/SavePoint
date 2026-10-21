using System.Diagnostics;
using Savepoint.Core.Tests.Git;

namespace Savepoint.Core.Tests.Build;

// Runs the real fetch script's prune step (no download) against folders that mimic each bundle layout.
public sealed class FetchGitPruneTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "savepoint-prune-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => GitWorkspace.DeleteDirectory(_root);

    [Theory]
    [InlineData("win-x64", "ucrt64")]
    [InlineData("win-arm64", "clangarm64")]
    public async Task A_MinGit_bundle_loses_the_credential_manager_and_the_http_transport_and_keeps_the_rest(string rid, string toolchain)
    {
        var bin = toolchain + "/bin/";
        string[] gone =
        [
            bin + "git-credential-manager.exe", bin + "git-credential-manager.exe.config", bin + "git-credential-helper-selector.exe",
            bin + "gcmcore.dll", bin + "GitHub.dll", bin + "GitLab.dll", bin + "Atlassian.Bitbucket.dll", bin + "MicroCom.Runtime.dll",
            bin + "Microsoft.Identity.Client.dll", bin + "Microsoft.AzureRepos.dll", bin + "System.Text.Json.dll", bin + "System.CommandLine.dll",
            bin + "Avalonia.Base.dll", bin + "Avalonia.dll", bin + "SkiaSharp.dll", bin + "libSkiaSharp.dll", bin + "HarfBuzzSharp.dll",
            bin + "libHarfBuzzSharp.dll", bin + "av_libglesv2.dll", bin + "msalruntime.dll", bin + "msalruntime_arm64.dll",
            toolchain + "/doc/git-credential-manager/README.md",
            bin + "git-remote-http.exe", bin + "git-remote-https.exe", bin + "git-http-fetch.exe", bin + "git-http-push.exe",
            bin + "libcurl-4.dll", bin + "libcurl-openssl-4.dll", bin + "libssh2-1.dll", bin + "libnghttp2-14.dll", bin + "libpsl-5.dll",
            bin + "libidn2-0.dll", bin + "libunistring-5.dll", bin + "libzstd.dll", bin + "libbrotlicommon.dll", bin + "libbrotlidec.dll",
            bin + "libexpat-1.dll", bin + "libcrypto-3-x64.dll", bin + "libcrypto-3.dll", bin + "libssl-3-x64.dll", bin + "libssl-3.dll",
            bin + "psl.exe", bin + "psl-make-dafsa", bin + "brotli.exe", bin + "wcurl", bin + "c_rehash",
            toolchain + "/etc/ssl/openssl.cnf", toolchain + "/etc/ssl/certs/ca-bundle.crt", toolchain + "/lib/ossl-modules/legacy.dll", toolchain + "/lib/engines-3/capi.dll",
        ];
        string[] kept =
        [
            "cmd/git.exe", "cmd/git-upload-pack.exe", bin + "git.exe", bin + "git-receive-pack.exe", bin + "git-upload-pack.exe", bin + "scalar.exe",
            bin + "libiconv-2.dll", bin + "libintl-8.dll", bin + "libpcre2-8-0.dll", bin + "zlib1.dll", bin + "git-sh-i18n--envsubst.exe",
            toolchain + "/libexec/git-core/git-submodule", toolchain + "/libexec/git-core/mergetools/vimdiff", toolchain + "/share/git-core/templates/description",
            toolchain + "/share/licenses/git/COPYING", "usr/bin/sh.exe", "usr/bin/msys-2.0.dll", "usr/bin/ssh.exe", "usr/bin/awk.exe", "usr/ssl/openssl.cnf",
            "etc/gitconfig", "etc/ssh/ssh_config",
        ];
        var bundle = MakeBundle(gone, kept);

        var run = await Prune(rid, bundle);

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(StillThere(bundle, gone));
        Assert.Empty(Lost(bundle, kept));
        Assert.False(Directory.Exists(Path.Combine(bundle, toolchain, "doc")), "folders that the pruning emptied go too");
        Assert.False(Directory.Exists(Path.Combine(bundle, toolchain, "etc")), "folders that the pruning emptied go too");
        Assert.False(Directory.Exists(Path.Combine(bundle, toolchain, "lib")), "folders that the pruning emptied go too");
    }

    [Theory]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    public async Task A_Linux_dugite_bundle_loses_the_credential_manager_lfs_and_the_http_transport_and_keeps_the_rest(string rid)
    {
        string[] gone =
        [
            "libexec/git-core/git-credential-manager", "libexec/git-core/git-lfs", "libexec/git-core/libSkiaSharp.so", "libexec/git-core/libHarfBuzzSharp.so",
            "libexec/git-core/git-remote-http", "libexec/git-core/git-http-fetch", "libexec/git-core/git-http-push", "libexec/git-core/git-http-backend",
            "ssl/cacert.pem",
        ];
        string[] kept =
        [
            "bin/git", "bin/scalar", "libexec/git-core/git", "libexec/git-core/git-submodule", "libexec/git-core/git-sh-setup", "libexec/git-core/git-imap-send",
            "libexec/git-core/git-remote-ext", "libexec/git-core/git-remote-fd", "libexec/git-core/git-credential-store", "libexec/git-core/git-credential-cache",
            "libexec/git-core/git-credential", "libexec/git-core/mergetools/vimdiff", "share/git-core/templates/description", "etc/gitconfig",
        ];
        var bundle = MakeBundle(gone, kept);
        string[] goneLinks = ["git-remote-https", "git-remote-ftp", "git-remote-ftps", "git-lfs-link"];
        string[] keptLinks = ["git-add", "git-cat-file", "git-credential-link"];
        var linksWork = !OperatingSystem.IsWindows();
        if (linksWork)
        {
            foreach (var name in goneLinks)
            {
                File.CreateSymbolicLink(Path.Combine(bundle, "libexec/git-core", name), name == "git-lfs-link" ? "git-lfs" : "git-remote-http");
            }

            foreach (var name in keptLinks)
            {
                File.CreateSymbolicLink(Path.Combine(bundle, "libexec/git-core", name), "git");
            }
        }

        var run = await Prune(rid, bundle);

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(StillThere(bundle, gone));
        Assert.Empty(Lost(bundle, kept));
        Assert.False(Directory.Exists(Path.Combine(bundle, "ssl")), "folders that the pruning emptied go too");
        if (linksWork)
        {
            Assert.Empty(StillThere(bundle, goneLinks.Select(name => "libexec/git-core/" + name), links: true));
            Assert.Empty(Lost(bundle, keptLinks.Select(name => "libexec/git-core/" + name), links: true));
        }
    }

    [Theory]
    [InlineData("osx-x64")]
    [InlineData("osx-arm64")]
    public async Task A_macOS_dugite_bundle_loses_the_whole_dotnet_runtime_the_credential_manager_brings(string rid)
    {
        string[] gone =
        [
            "libexec/git-core/git-credential-manager", "libexec/git-core/git-credential-manager.dll", "libexec/git-core/git-credential-manager.deps.json",
            "libexec/git-core/git-credential-manager.runtimeconfig.json", "libexec/git-core/git-lfs", "libexec/git-core/System.Private.CoreLib.dll",
            "libexec/git-core/Avalonia.Base.dll", "libexec/git-core/libcoreclr.dylib", "libexec/git-core/libSkiaSharp.dylib", "libexec/git-core/createdump",
            "libexec/git-core/uninstall.sh", "libexec/git-core/NOTICE", "libexec/git-core/de/System.CommandLine.resources.dll",
            "libexec/git-core/zh-Hant/System.CommandLine.resources.dll", "libexec/git-core/git-remote-http", "libexec/git-core/git-http-fetch",
            "libexec/git-core/git-http-push", "libexec/git-core/git-http-backend",
        ];
        string[] kept =
        [
            "bin/git", "bin/scalar", "libexec/git-core/git", "libexec/git-core/git-submodule", "libexec/git-core/git-filter-branch", "libexec/git-core/git-send-email",
            "libexec/git-core/git-remote-ext", "libexec/git-core/git-remote-fd", "libexec/git-core/git-credential-osxkeychain", "libexec/git-core/git-credential-store",
            "libexec/git-core/mergetools/vimdiff", "share/git-core/templates/description", "etc/gitconfig",
        ];
        var bundle = MakeBundle(gone, kept);

        var run = await Prune(rid, bundle);

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(StillThere(bundle, gone));
        Assert.Empty(Lost(bundle, kept));
        Assert.False(Directory.Exists(Path.Combine(bundle, "libexec/git-core/de")), "folders that the pruning emptied go too");
        Assert.False(Directory.Exists(Path.Combine(bundle, "libexec/git-core/zh-Hant")), "folders that the pruning emptied go too");
        Assert.True(Directory.Exists(Path.Combine(bundle, "libexec/git-core/mergetools")));
    }

    [Fact]
    public async Task Running_the_prune_twice_changes_nothing_the_second_time()
    {
        var bundle = MakeBundle(["ucrt64/bin/libcurl-4.dll"], ["cmd/git.exe", "ucrt64/bin/git.exe"]);
        await Prune("win-x64", bundle);

        var second = await Prune("win-x64", bundle);

        Assert.Equal(0, second.ExitCode);
        Assert.True(File.Exists(Path.Combine(bundle, "cmd/git.exe")));
        Assert.True(File.Exists(Path.Combine(bundle, "ucrt64/bin/git.exe")));
    }

    [Fact]
    public async Task A_pattern_that_would_remove_the_entry_point_stops_the_script_and_leaves_the_entry_in_place()
    {
        var bundle = MakeBundle([], ["cmd/git.exe", "ucrt64/bin/git.exe"]);
        var manifest = WriteManifest("cmd/git.exe", "mingit", ["ucrt64/bin/git.exe", "cmd"]);

        var run = await Prune("win-x64", bundle, manifest);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("cmd/git.exe", run.Output);
        Assert.True(File.Exists(Path.Combine(bundle, "cmd/git.exe")));
        Assert.True(File.Exists(Path.Combine(bundle, "ucrt64/bin/git.exe")), "nothing is deleted before every pattern has been checked");
    }

    [Theory]
    [InlineData("../victim")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows")]
    [InlineData("ucrt64\\bin\\libcurl-4.dll")]
    [InlineData("ucrt64/./bin")]
    [InlineData("ucrt64//bin")]
    [InlineData("*")]
    public async Task A_pattern_that_could_reach_outside_the_bundle_or_empty_it_is_refused(string pattern)
    {
        var bundle = MakeBundle([], ["aaa/keep.txt", "cmd/git.exe", "ucrt64/bin/git.exe"]);
        var victim = Path.Combine(Path.GetDirectoryName(bundle)!, "victim");
        await File.WriteAllTextAsync(victim, "keep me", TestContext.Current.CancellationToken);
        var manifest = WriteManifest("cmd/git.exe", "mingit", [pattern]);

        var run = await Prune("win-x64", bundle, manifest);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("pattern", run.Output, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(victim));
        Assert.True(File.Exists(Path.Combine(bundle, "aaa/keep.txt")));
        Assert.True(File.Exists(Path.Combine(bundle, "ucrt64/bin/git.exe")));
    }

    [Fact]
    public async Task A_layout_without_a_prune_list_is_an_error_not_a_silent_skip()
    {
        var bundle = MakeBundle([], ["cmd/git.exe"]);
        var manifest = WriteManifest("cmd/git.exe", "other-layout", ["ucrt64/bin/libcurl-4.dll"]);

        var run = await Prune("win-x64", bundle, manifest);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("other-layout", run.Output);
    }

    private static bool Exists(string bundle, string relative, bool link) =>
        link ? new FileInfo(Path.Combine(bundle, relative)).LinkTarget is not null : File.Exists(Path.Combine(bundle, relative)) || Directory.Exists(Path.Combine(bundle, relative));

    private static List<string> StillThere(string bundle, IEnumerable<string> paths, bool links = false) => paths.Where(path => Exists(bundle, path, links)).ToList();

    private static List<string> Lost(string bundle, IEnumerable<string> paths, bool links = false) => paths.Where(path => !Exists(bundle, path, links)).ToList();

    private string MakeBundle(string[] gone, string[] kept)
    {
        var bundle = Path.Combine(_root, "bundle-" + Guid.NewGuid().ToString("N")[..8], "tree");
        Directory.CreateDirectory(bundle);
        foreach (var relative in gone.Concat(kept))
        {
            var path = Path.Combine(bundle, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }

        return bundle;
    }

    private string WriteManifest(string entry, string layoutOfTheRuntime, string[] patterns)
    {
        var list = string.Join(",\n", patterns.Select(pattern => "        \"" + pattern.Replace("\\", "\\\\") + "\""));
        var path = Path.Combine(_root, "manifest-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, $$"""
            {
              "layouts": {
                "mingit": {
                  "prune": [
            {{list}}
                  ]
                }
              },
              "win-x64": {
                "version": "1.0",
                "url": "https://example.invalid/mingit.zip",
                "sha256": "0000000000000000000000000000000000000000000000000000000000000000",
                "archive": "zip",
                "layout": "{{layoutOfTheRuntime}}",
                "entry": "{{entry}}"
              }
            }
            """.Replace("\r\n", "\n"));
        return path;
    }

    private static async Task<(int ExitCode, string Output)> Prune(string rid, string bundle, string? manifest = null)
    {
        var scripts = GitManifestFile.BuildFolder;
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("pwsh") { ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(scripts, "fetch-git.ps1"), "-RuntimeId", rid, "-PruneDirectory", bundle } }
            : new ProcessStartInfo("bash") { ArgumentList = { Path.Combine(scripts, "fetch-git.sh"), rid, "--prune-only", bundle } };
        if (manifest is not null)
        {
            start.ArgumentList.Add(OperatingSystem.IsWindows() ? "-ManifestPath" : "--manifest");
            start.ArgumentList.Add(manifest);
        }

        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await output + await error);
    }
}
